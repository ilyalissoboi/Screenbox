using System;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Build availability and explicit permission for the finite Apple HLS profile.</summary>
internal static class AirPlayAudioPolicy
{
#if SCREENBOX_AIRPLAY_STEREO_AAC
    internal static bool IsAvailable => true;
    internal const ulong CacheQuotaBytes = 512ul * 1024 * 1024;

    internal static void Configure(CastOptions options, AacFragmentStore? store, bool allowStereo)
    {
        if (allowStereo && store is null) throw new ArgumentNullException(nameof(store));
        options.Delivery = CastDelivery.AutomaticAudioHls;
        options.AudioProfile = AudioDeliveryProfile.AppleHlsV1;
        options.ConversionPermission = allowStereo
            ? AudioConversionPermission.AllowStereo
            : AudioConversionPermission.PreserveOriginal;
        options.CacheQuotaBytes = allowStereo ? CacheQuotaBytes : 0;
        options.FragmentStore = store;
    }
#else
    internal static bool IsAvailable => false;
#endif
}
