using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Selects an explicit per-item policy; native preparation validates the content.</summary>
internal static class AirPlayMediaDelivery
{
#if SCREENBOX_AIRPLAY_STEREO_AAC
    /// <summary>
    /// Resolves incomplete or empty track metadata before choosing an opt-in policy.
    /// The caller owns the independent parser/descriptor and its timeout; the supplied
    /// token must cancel that work. No native cast or local-player mutation occurs here.
    /// </summary>
    internal static async Task<bool> ResolveAudioOnlyAsync(string fileType, bool metadataComplete,
        bool hasAudio, bool hasVideo,
        Func<CancellationToken, Task<(bool Complete, bool HasAudio, bool HasVideo)>> parseMetadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!metadataComplete || (!hasAudio && !hasVideo))
        {
            var metadata = await parseMetadata(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!metadata.Complete) throw new InvalidDataException("Media metadata is incomplete.");
            hasAudio = metadata.HasAudio;
            hasVideo = metadata.HasVideo;
        }

        return IsAudioOnly(fileType, hasAudio, hasVideo);
    }
#endif

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
