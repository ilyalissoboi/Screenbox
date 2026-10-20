using LibVLCSharp.Shared;
using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayVideoDimensionsTests
{
    /// <summary>Already-open rotated/anamorphic videos keep VLC's effective dimensions.</summary>
    [Test]
    [Arguments(1080u, 1920u)]
    [Arguments(768u, 576u)]
    public async Task CurrentVideo_ShouldPreserveNaturalDisplaySize(uint width, uint height)
    {
        await Assert.That(AirPlayVideoDimensions.Current(false, width, height)).IsEqualTo((width, height));
        await Assert.That(AirPlayVideoDimensions.Current(true, width, height)).IsEqualTo((0u, 0u));
    }

    /// <summary>A fresh queued video waits for its own metadata, not the preceding audio's zero size.</summary>
    [Test]
    public async Task QueuedVideo_ShouldWaitForIndependentMetadata()
    {
        var metadata = new TaskCompletionSource<(uint, uint)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(uint, uint)> size = AirPlayVideoDimensions.QueuedAsync(false, _ => metadata.Task, CancellationToken.None);
        await Assert.That(size.IsCompleted).IsFalse();
        metadata.SetResult((1920, 1080));
        await Assert.That(await size.WaitAsync(TimeSpan.FromSeconds(30))).IsEqualTo((1920u, 1080u));
    }

    /// <summary>Audio skips metadata entirely, including not-yet-parsed queue items.</summary>
    [Test]
    public async Task QueuedAudio_ShouldNotReadVideoMetadata()
    {
        var size = await AirPlayVideoDimensions.QueuedAsync(true,
            _ => throw new InvalidOperationException("Audio must not parse video metadata."), CancellationToken.None);
        await Assert.That(size).IsEqualTo((0u, 0u));
    }

    /// <summary>Stop cancels a pending metadata read rather than admitting a later native start.</summary>
    [Test]
    public async Task QueuedMetadata_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        Task<(uint, uint)> size = AirPlayVideoDimensions.QueuedAsync(false, async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return (1920u, 1080u);
        }, cancellation.Token);
        cancellation.Cancel();
        try { await size.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancelled metadata must not return a video size.");
    }

    /// <summary>Failed/missing metadata must not silently classify a queued video as audio.</summary>
    [Test]
    public async Task QueuedVideo_WithMissingSize_ShouldFail()
    {
        try
        {
            await AirPlayVideoDimensions.QueuedAsync(false, _ => Task.FromResult((0u, 0u)), CancellationToken.None);
        }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Queued video without dimensions must be rejected.");
    }

    /// <summary>Sample aspect ratio precedes orientation, including transposed/flipped rotations.</summary>
    [Test]
    [Arguments(VideoOrientation.TopLeft, 768u, 576u)]
    [Arguments(VideoOrientation.TopRight, 768u, 576u)]
    [Arguments(VideoOrientation.BottomLeft, 768u, 576u)]
    [Arguments(VideoOrientation.BottomRight, 768u, 576u)]
    [Arguments(VideoOrientation.LeftTop, 576u, 768u)]
    [Arguments(VideoOrientation.LeftBottom, 576u, 768u)]
    [Arguments(VideoOrientation.RightTop, 576u, 768u)]
    [Arguments(VideoOrientation.RightBottom, 576u, 768u)]
    public async Task QueuedTrack_ShouldApplyDisplayMetadata(VideoOrientation orientation, uint width, uint height)
    {
        await Assert.That(AirPlayVideoDimensions.FromTrack(720, 576, 16, 15, orientation)).IsEqualTo((width, height));
    }

    /// <summary>Unspecified pixel aspect ratio uses square pixels, not a divide by zero.</summary>
    [Test]
    [Arguments(0u, 1u)]
    [Arguments(1u, 0u)]
    public async Task QueuedTrack_WithUnspecifiedSar_ShouldUseSquarePixels(uint numerator, uint denominator)
    {
        await Assert.That(AirPlayVideoDimensions.FromTrack(720, 576, numerator, denominator,
            VideoOrientation.TopLeft)).IsEqualTo((720u, 576u));
    }

    /// <summary>Malformed/overflowing metadata must not produce a zero or wrapped display size.</summary>
    [Test]
    [Arguments(0u, 576u, 1u, 1u)]
    [Arguments(720u, 0u, 1u, 1u)]
    [Arguments(uint.MaxValue, 1u, uint.MaxValue, 1u)]
    [Arguments(1u, 1u, 1u, uint.MaxValue)]
    public async Task QueuedTrack_WithInvalidSize_ShouldFail(uint width, uint height, uint numerator, uint denominator)
    {
        try { AirPlayVideoDimensions.FromTrack(width, height, numerator, denominator, VideoOrientation.TopLeft); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid display dimensions must be rejected.");
    }
}
