using System;
using System.IO;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Selects an explicit per-item policy; native preparation validates the content.</summary>
internal static class AirPlayMediaDelivery
{
    /// <summary>
    /// The opt-in runtime uses parsed track presence, including unplayed queued items.
    /// Missing metadata must fail rather than choose the audio policy by accident.
    /// Ordinary builds preserve their historical M4A hint and video-remux route.
    /// </summary>
    internal static bool IsAudioOnly(string fileType, bool hasAudio, bool hasVideo)
    {
#if SCREENBOX_AIRPLAY_STEREO_AAC
        if (hasVideo) return false;
        if (hasAudio) return true;
        throw new InvalidDataException("Media has no parsed audio or video tracks.");
#else
        return fileType.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
#endif
    }

    /// <summary>Resets delivery/MIME per item without retrying a failed representation.</summary>
    internal static void Configure(CastOptions options, bool audioOnly)
    {
#if SCREENBOX_AIRPLAY_STEREO_AAC
        options.Delivery = CastDelivery.AutomaticAudioHls;
        options.AudioProfile = audioOnly ? AudioDeliveryProfile.AppleAudioV1 : AudioDeliveryProfile.AppleHlsV1;
        // The native profile determines original AAC/MP3 MIME or generated HLS from content.
        options.ContentType = null;
#else
        options.Delivery = audioOnly ? CastDelivery.Progressive : CastDelivery.HlsRemux;
        options.ContentType = audioOnly ? "audio/mp4" : null;
#endif
    }
}
