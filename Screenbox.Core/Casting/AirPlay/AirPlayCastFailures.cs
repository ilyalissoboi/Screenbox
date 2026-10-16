using Screenbox.Core.Enums;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Maps fixed native categories without exposing exception text or changing delivery routes.</summary>
internal static class AirPlayCastFailures
{
    internal static AirPlayCastResult FromStart(ResultCode result) => result switch
    {
        ResultCode.Cancelled => AirPlayCastResult.Cancelled,
        ResultCode.MediaUnsupported => AirPlayCastResult.FormatUnsupported,
        ResultCode.MediaMalformed => AirPlayCastResult.FileUnreadable,
        ResultCode.Authentication or ResultCode.ProfileNotFound => AirPlayCastResult.PairingInvalid,
        ResultCode.Connection => AirPlayCastResult.ConnectionFailed,
#if SCREENBOX_AIRPLAY_STEREO_AAC
        ResultCode.AudioSelectionRequired or ResultCode.AudioTrackInvalid => AirPlayCastResult.AudioSelectionFailed,
        ResultCode.MediaSource => AirPlayCastResult.FileUnreadable,
        ResultCode.MediaCodec => AirPlayCastResult.AudioCodecFailed,
        ResultCode.MediaCache => AirPlayCastResult.AudioCacheFailed,
        ResultCode.MediaLimit => AirPlayCastResult.AudioLimitReached,
        ResultCode.MediaGenerationTimeout => AirPlayCastResult.AudioGenerationTimedOut,
#endif
        _ => AirPlayCastResult.Failed,
    };

    internal static NotificationKind? ForStart(AirPlayCastResult result) => result switch
    {
        AirPlayCastResult.NothingToCast or AirPlayCastResult.NotLocalFile => NotificationKind.AirPlayCastNotLocalFile,
        AirPlayCastResult.FormatUnsupported => NotificationKind.AirPlayCastFormatUnsupported,
        AirPlayCastResult.FileUnreadable => NotificationKind.AirPlayCastFileUnreadable,
        AirPlayCastResult.ConnectionFailed => NotificationKind.AirPlayCastConnectionFailed,
        AirPlayCastResult.PairingInvalid => NotificationKind.AirPlayCastPairingInvalid,
        AirPlayCastResult.AudioSelectionFailed => NotificationKind.AirPlayAudioSelectionFailed,
        AirPlayCastResult.AudioCodecFailed => NotificationKind.AirPlayAudioCodecFailed,
        AirPlayCastResult.AudioCacheFailed => NotificationKind.AirPlayAudioCacheFailed,
        AirPlayCastResult.AudioLimitReached => NotificationKind.AirPlayAudioLimitReached,
        AirPlayCastResult.AudioGenerationTimedOut => NotificationKind.AirPlayAudioGenerationTimedOut,
        AirPlayCastResult.Failed => NotificationKind.AirPlayCastFailed,
        _ => null,
    };

    internal static NotificationKind ForTerminal(CastStatus status)
    {
#if SCREENBOX_AIRPLAY_STEREO_AAC
        return ForTerminal(status.EndReason, status.MediaFailure);
#else
        return NotificationKind.AirPlayCastEnded;
#endif
    }

    internal static NotificationKind ForNextItem(AirPlayCastResult result) => result switch
    {
        AirPlayCastResult.AudioSelectionFailed or AirPlayCastResult.AudioCodecFailed or
            AirPlayCastResult.AudioCacheFailed or AirPlayCastResult.AudioLimitReached or
            AirPlayCastResult.AudioGenerationTimedOut => ForStart(result)!.Value,
        _ => NotificationKind.AirPlayNextItemNotCast,
    };

#if SCREENBOX_AIRPLAY_STEREO_AAC
    internal static NotificationKind ForTerminal(EndReason reason, MediaFailure failure) =>
        reason != EndReason.MediaFailure ? NotificationKind.AirPlayCastEnded : failure switch
        {
            MediaFailure.Malformed or MediaFailure.Source => NotificationKind.AirPlayCastFileUnreadable,
            MediaFailure.Unsupported => NotificationKind.AirPlayCastFormatUnsupported,
            MediaFailure.Codec => NotificationKind.AirPlayAudioCodecFailed,
            MediaFailure.Cache => NotificationKind.AirPlayAudioCacheFailed,
            MediaFailure.Limit => NotificationKind.AirPlayAudioLimitReached,
            MediaFailure.Timeout => NotificationKind.AirPlayAudioGenerationTimedOut,
            _ => NotificationKind.AirPlayCastEnded,
        };
#endif
}
