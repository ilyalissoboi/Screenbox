using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayReceiverWatcherTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private static readonly AirPlayReceiverInfo LivingRoom = new("AA:BB:CC:DD:EE:01", "Living Room", "192.0.2.10", 7000);

    [Test]
    public async Task Start_RaisesRendererFoundFromScan()
    {
        var found = new TaskCompletionSource<Renderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new AirPlayReceiverWatcher(() => new[] { LivingRoom }, TimeSpan.FromMilliseconds(10),
            NullLogger.Instance);
        watcher.RendererFound += (_, e) => found.TrySetResult(e.Renderer);

        watcher.Start();
        var renderer = await found.Task.WaitAsync(EventTimeout);

        await Assert.That(renderer.Kind).IsEqualTo(RendererKind.AirPlay);
        await Assert.That(watcher.GetRenderers()).HasSingleItem();
    }

    [Test]
    public async Task Stop_MakesRenderersUnavailableAndSilencesEvents()
    {
        var found = new TaskCompletionSource<Renderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        int eventsAfterStop = 0;
        bool stopped = false;
        using var watcher = new AirPlayReceiverWatcher(() => new[] { LivingRoom }, TimeSpan.FromMilliseconds(10),
            NullLogger.Instance);
        watcher.RendererFound += (_, e) =>
        {
            if (stopped) Interlocked.Increment(ref eventsAfterStop);
            found.TrySetResult(e.Renderer);
        };
        watcher.RendererLost += (_, _) =>
        {
            if (stopped) Interlocked.Increment(ref eventsAfterStop);
        };
        watcher.Start();
        var renderer = await found.Task.WaitAsync(EventTimeout);

        watcher.Stop();
        stopped = true;
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        await Assert.That(watcher.IsStarted).IsFalse();
        await Assert.That(renderer.IsAvailable).IsFalse();
        await Assert.That(watcher.GetRenderers()).IsEmpty();
        await Assert.That(eventsAfterStop).IsEqualTo(0);
    }

    [Test]
    public async Task FailedScan_KeepsListedReceivers()
    {
        int scans = 0;
        var threeFailedScans = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int lostEvents = 0;
        using var watcher = new AirPlayReceiverWatcher(() =>
        {
            int scan = Interlocked.Increment(ref scans);
            if (scan >= 4) threeFailedScans.TrySetResult();
            // The first scan finds the receiver; the next ones fail, which must not count as misses.
            return scan == 1 ? new[] { LivingRoom } : throw new InvalidOperationException("network down");
        }, TimeSpan.FromMilliseconds(10), NullLogger.Instance);
        watcher.RendererLost += (_, _) => Interlocked.Increment(ref lostEvents);

        watcher.Start();
        await threeFailedScans.Task.WaitAsync(EventTimeout);
        watcher.Stop();

        await Assert.That(lostEvents).IsEqualTo(0);
    }
}
