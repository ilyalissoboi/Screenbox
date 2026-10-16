using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayCastLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Admission_WhenCleanupIsPending_ShouldWaitAndRejectSupersededRequest(bool cancel)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new AirPlayCastLifetime(_ => throw new InvalidOperationException("Unexpected cleanup failure."));
        lifetime.ScheduleCleanup(() =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Cleanup release timed out.");
        });
        long request = lifetime.BeginRequest();
        Task<bool> admission = lifetime.WaitForCleanupAsync(request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(admission.IsCompleted).IsFalse();
            if (cancel) lifetime.InvalidateRequests();
        }
        finally { release.Set(); }
        await Assert.That(await admission.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(!cancel);
    }

    [Test]
    public async Task Admission_ShouldIncludeLaterCleanupAndContinueAfterFailure()
    {
        using var release = new ManualResetEventSlim();
        List<int> order = [];
        int failures = 0;
        var lifetime = new AirPlayCastLifetime(_ =>
        {
            Interlocked.Increment(ref failures);
            throw new InvalidOperationException("Logger failure must not strand cleanup.");
        });
        lifetime.ScheduleCleanup(() =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Cleanup release timed out.");
            order.Add(1);
            throw new IOException("Simulated disposal failure.");
        });
        long first = lifetime.BeginRequest();
        Task<bool> stale = lifetime.WaitForCleanupAsync(first);
        lifetime.ScheduleCleanup(() => order.Add(2));
        long latest = lifetime.BeginRequest();
        release.Set();
        await Assert.That(await stale.WaitAsync(TimeSpan.FromSeconds(5))).IsFalse();
        await Assert.That(await lifetime.WaitForCleanupAsync(latest).WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
        await Assert.That(order.SequenceEqual(new[] { 1, 2 })).IsTrue();
        await Assert.That(failures).IsEqualTo(1);
    }
}
