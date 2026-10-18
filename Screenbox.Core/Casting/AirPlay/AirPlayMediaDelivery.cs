using System;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Selects direct M4A delivery without involving the video-only HLS pipeline.</summary>
internal static class AirPlayMediaDelivery
{
    /// <summary>
    /// M4A is a container hint, not a codec validation result. The initial audio-file
    /// slice validates AAC/M4A only; other extensions retain the existing remux route.
    /// This does not retry failed casts or convert an unsupported audio-only codec.
    /// </summary>
    internal static bool IsProgressiveAudio(string fileType) =>
        fileType.Equals(".m4a", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets delivery/MIME for each item and returns whether it bypasses HLS and its cache.</summary>
    internal static bool Configure(CastOptions options, string fileType)
    {
        bool audio = IsProgressiveAudio(fileType);
        options.Delivery = audio ? CastDelivery.Progressive : CastDelivery.HlsRemux;
        options.ContentType = audio ? "audio/mp4" : null;
        return audio;
    }
}
