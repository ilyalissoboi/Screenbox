using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayCastLifetimeTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Admission stays pending until the test releases cleanup, then checks the request ticket.</summary>
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
            // Only the test releases cleanup. A timed wait can expire while a busy
            // runner delays the assertion, falsely making admission complete.
            release.Wait();
        });
        long request = lifetime.BeginRequest();
        Task<bool> admission = lifetime.WaitForCleanupAsync(request);
        try
        {
            await entered.Task.WaitAsync(CompletionTimeout);
            await Assert.That(admission.IsCompleted).IsFalse();
            if (cancel)
            {
                lifetime.InvalidateRequests();
            }
        }
        finally
        {
            release.Set();
            // Drain the worker before disposing its gate, including assertion failures.
            await admission.WaitAsync(CompletionTimeout);
        }
        await Assert.That(await admission).IsEqualTo(!cancel);
    }

    /// <summary>Later cleanup remains ordered and logger failures cannot strand the admission barrier.</summary>
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
            release.Wait();
            order.Add(1);
            throw new IOException("Simulated disposal failure.");
        });
        long first = lifetime.BeginRequest();
        Task<bool> stale = lifetime.WaitForCleanupAsync(first);
        lifetime.ScheduleCleanup(() => order.Add(2));
        long latest = lifetime.BeginRequest();
        try
        {
            release.Set();
            await Assert.That(await stale.WaitAsync(CompletionTimeout)).IsFalse();
            await Assert.That(await lifetime.WaitForCleanupAsync(latest).WaitAsync(CompletionTimeout)).IsTrue();
            await Assert.That(order.SequenceEqual(new[] { 1, 2 })).IsTrue();
            await Assert.That(failures).IsEqualTo(1);
        }
        finally
        {
            release.Set();
            await lifetime.WaitForCleanupAsync(latest).WaitAsync(CompletionTimeout);
        }
    }
}
