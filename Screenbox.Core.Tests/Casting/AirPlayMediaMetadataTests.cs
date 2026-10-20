#if SCREENBOX_AIRPLAY_STEREO_AAC
using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayMediaMetadataTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Opening/partial/empty current items wait instead of failing or prematurely choosing audio.</summary>
    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(false, false, true)]
    public async Task UnreadyTracks_ShouldWaitForIndependentMetadata(bool complete, bool hasAudio, bool parsedVideo)
    {
        var parsed = new TaskCompletionSource<(bool, bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<bool> classification = AirPlayMediaDelivery.ResolveAudioOnlyAsync(".mp4", complete,
            hasAudio, hasVideo: false, _ =>
            {
                calls++;
                return parsed.Task;
            }, CancellationToken.None);

        await Assert.That(classification.IsCompleted).IsFalse();
        await Assert.That(calls).IsEqualTo(1);
        parsed.SetResult((true, true, parsedVideo));
        await Assert.That(await classification.WaitAsync(CompletionTimeout)).IsEqualTo(!parsedVideo);
    }

    /// <summary>Complete current/queued track inventories need no independent descriptor.</summary>
    [Test]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(false, true, false)]
    public async Task ReadyTracks_ShouldAvoidReparsing(bool hasAudio, bool hasVideo, bool audioOnly)
    {
        bool result = await AirPlayMediaDelivery.ResolveAudioOnlyAsync(".m4a", metadataComplete: true,
            hasAudio, hasVideo, _ => throw new InvalidOperationException("Complete metadata must not reparse."),
            CancellationToken.None);
        await Assert.That(result).IsEqualTo(audioOnly);
    }

    /// <summary>Timeout/failed/empty parse results must not become an audio-only policy.</summary>
    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task IncompleteOrEmptyParse_ShouldRefuse(bool complete, bool hasAudio)
    {
        try
        {
            await AirPlayMediaDelivery.ResolveAudioOnlyAsync(".m4a", false, false, false,
                _ => Task.FromResult((complete, hasAudio, false)), CancellationToken.None);
        }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"Invalid metadata admitted: complete={complete}, hasAudio={hasAudio}.");
    }

    /// <summary>A cancelled request never starts metadata work.</summary>
    [Test]
    public async Task CancelledRequest_ShouldNotParse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        Task<bool> classification = AirPlayMediaDelivery.ResolveAudioOnlyAsync(".mp3", false, false, false,
            _ =>
            {
                calls++;
                return Task.FromResult((true, true, false));
            }, cancellation.Token);
        await ExpectCancellationAsync(classification);
        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>Stop propagates to a cooperative parser without waiting for its full timeout.</summary>
    [Test]
    public async Task CancellationDuringParse_ShouldStopWaiting()
    {
        using var cancellation = new CancellationTokenSource();
        Task<bool> classification = AirPlayMediaDelivery.ResolveAudioOnlyAsync(".flac", false, false, false,
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return (true, true, false);
            }, cancellation.Token);
        cancellation.Cancel();
        await ExpectCancellationAsync(classification);
    }

    /// <summary>A parser completing concurrently with Stop must not admit a late policy result.</summary>
    [Test]
    public async Task CancellationBeforeLateParseResult_ShouldWin()
    {
        using var cancellation = new CancellationTokenSource();
        var parsed = new TaskCompletionSource<(bool, bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> classification = AirPlayMediaDelivery.ResolveAudioOnlyAsync(".mp4", false, false, false,
            _ => parsed.Task, cancellation.Token);
        cancellation.Cancel();
        parsed.SetResult((true, true, true));
        await ExpectCancellationAsync(classification);
    }

    /// <summary>The caller receives parsing failures and remains responsible for descriptor cleanup.</summary>
    [Test]
    public async Task ParseFailure_ShouldPropagate()
    {
        var expected = new IOException("Owned fixture metadata failure.");
        try
        {
            await AirPlayMediaDelivery.ResolveAudioOnlyAsync(".mp4", false, false, false,
                _ => Task.FromException<(bool, bool, bool)>(expected), CancellationToken.None);
        }
        catch (IOException actual)
        {
            await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
            return;
        }
        throw new InvalidOperationException("Parser failure must propagate.");
    }

    /// <summary>Requires bounded cancellation completion and rejects a successful classification.</summary>
    private static async Task ExpectCancellationAsync(Task<bool> classification)
    {
        try { await classification.WaitAsync(CompletionTimeout); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancelled metadata must not produce a policy result.");
    }
}
#endif
