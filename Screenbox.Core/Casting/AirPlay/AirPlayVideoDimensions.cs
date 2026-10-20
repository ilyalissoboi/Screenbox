using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Preserves current display dimensions and resolves each queued item's own dimensions.</summary>
internal static class AirPlayVideoDimensions
{
    /// <summary>VLC's open-player dimensions already account for display metadata; do not replace them.</summary>
    internal static (uint Width, uint Height) Current(bool audioOnly, uint naturalWidth, uint naturalHeight) =>
        audioOnly ? (0, 0) : (naturalWidth, naturalHeight);

    /// <summary>
    /// Audio needs no metadata. A queued video must supply its own nonzero display size;
    /// the cancellable metadata reader owns its parse timeout and must honor the token.
    /// Failure leaves the coordinator's normal paused local handoff in charge.
    /// </summary>
    internal static async Task<(uint Width, uint Height)> QueuedAsync(bool audioOnly,
        Func<CancellationToken, Task<(uint Width, uint Height)>> readMetadata, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (audioOnly) return (0, 0);
        var size = await readMetadata(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (size.Width == 0 || size.Height == 0) throw new InvalidDataException("Queued video has no display dimensions.");
        return size;
    }

    /// <summary>Converts coded track size to display pixels using sample aspect ratio, then orientation.</summary>
    internal static (uint Width, uint Height) FromTrack(uint width, uint height, uint sarNumerator,
        uint sarDenominator, VideoOrientation orientation)
    {
        if (width == 0 || height == 0) throw new InvalidDataException("Video track has no coded dimensions.");
        // VLC uses zero for unspecified SAR; square pixels are the display fallback.
        if (sarNumerator == 0 || sarDenominator == 0) sarNumerator = sarDenominator = 1;
        ulong displayWidth = ((ulong)width * sarNumerator + sarDenominator / 2) / sarDenominator;
        if (displayWidth == 0 || displayWidth > uint.MaxValue) throw new InvalidDataException("Video display width is out of range.");
        bool swapsAxes = orientation is VideoOrientation.LeftTop or VideoOrientation.LeftBottom or
            VideoOrientation.RightTop or VideoOrientation.RightBottom;
        return swapsAxes ? (height, (uint)displayWidth) : ((uint)displayWidth, height);
    }
}
