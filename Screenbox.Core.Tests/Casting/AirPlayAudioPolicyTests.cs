using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Enums;
using Screenbox.Core.Tests.Helpers;
using SendAirPlay2;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayAudioPolicyTests
{
    [Test]
    public async Task ConversionPermission_ShouldDefaultOff()
    {
        await Assert.That(new TestSettingsService().AirPlayAllowStereoConversion).IsFalse();
#if SCREENBOX_AIRPLAY_STEREO_AAC
        await Assert.That(AirPlayAudioPolicy.IsAvailable).IsTrue();
#else
        await Assert.That(AirPlayAudioPolicy.IsAvailable).IsFalse();
#endif
    }

    [Test]
    public async Task CancelledStart_ShouldHaveNoErrorNotification()
    {
        await Assert.That(AirPlayCastFailures.FromStart(ResultCode.Cancelled)).IsEqualTo(AirPlayCastResult.Cancelled);
        await Assert.That(AirPlayCastFailures.ForStart(AirPlayCastResult.Cancelled)).IsNull();
    }

#if SCREENBOX_AIRPLAY_STEREO_AAC
    [Test]
    public async Task PreserveOriginal_ShouldNotRequireWritableStorage()
    {
        var options = new CastOptions();
        AirPlayAudioPolicy.Configure(options, null, allowStereo: false);
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.AutomaticAudioHls);
        await Assert.That(options.AudioProfile).IsEqualTo(AudioDeliveryProfile.AppleHlsV1);
        await Assert.That(options.ConversionPermission).IsEqualTo(AudioConversionPermission.PreserveOriginal);
        await Assert.That(options.FragmentStore).IsNull();
        await Assert.That(options.CacheQuotaBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task AllowStereo_ShouldUseExplicitPermissionAndFiniteQuota()
    {
        var options = new CastOptions();
        var store = new UnusedStore();
        AirPlayAudioPolicy.Configure(options, store, allowStereo: true);
        await Assert.That(options.ConversionPermission).IsEqualTo(AudioConversionPermission.AllowStereo);
        await Assert.That(options.FragmentStore).IsEqualTo(store);
        await Assert.That(options.CacheQuotaBytes).IsEqualTo(536870912ul);
        try { AirPlayAudioPolicy.Configure(options, null, allowStereo: true); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("Conversion without a store must be rejected.");
    }

    [Test]
    [Arguments(ResultCode.AudioSelectionRequired, AirPlayCastResult.AudioSelectionFailed, NotificationKind.AirPlayAudioSelectionFailed)]
    [Arguments(ResultCode.AudioTrackInvalid, AirPlayCastResult.AudioSelectionFailed, NotificationKind.AirPlayAudioSelectionFailed)]
    [Arguments(ResultCode.MediaSource, AirPlayCastResult.FileUnreadable, NotificationKind.AirPlayCastFileUnreadable)]
    [Arguments(ResultCode.MediaCodec, AirPlayCastResult.AudioCodecFailed, NotificationKind.AirPlayAudioCodecFailed)]
    [Arguments(ResultCode.MediaCache, AirPlayCastResult.AudioCacheFailed, NotificationKind.AirPlayAudioCacheFailed)]
    [Arguments(ResultCode.MediaLimit, AirPlayCastResult.AudioLimitReached, NotificationKind.AirPlayAudioLimitReached)]
    [Arguments(ResultCode.MediaGenerationTimeout, AirPlayCastResult.AudioGenerationTimedOut, NotificationKind.AirPlayAudioGenerationTimedOut)]
    public async Task StartFailure_ShouldKeepSpecificCategory(ResultCode native, AirPlayCastResult host, NotificationKind notification)
    {
        await Assert.That(AirPlayCastFailures.FromStart(native)).IsEqualTo(host);
        await Assert.That(AirPlayCastFailures.ForStart(host)).IsEqualTo(notification);
        if (host is not AirPlayCastResult.FileUnreadable)
        {
            await Assert.That(AirPlayCastFailures.ForNextItem(host)).IsEqualTo(notification);
        }
    }

    [Test]
    [Arguments(MediaFailure.Codec, NotificationKind.AirPlayAudioCodecFailed)]
    [Arguments(MediaFailure.Cache, NotificationKind.AirPlayAudioCacheFailed)]
    [Arguments(MediaFailure.Limit, NotificationKind.AirPlayAudioLimitReached)]
    [Arguments(MediaFailure.Timeout, NotificationKind.AirPlayAudioGenerationTimedOut)]
    [Arguments(MediaFailure.Source, NotificationKind.AirPlayCastFileUnreadable)]
    public async Task TerminalFailure_ShouldRespectFirstReason(MediaFailure failure, NotificationKind notification)
    {
        await Assert.That(AirPlayCastFailures.ForTerminal(EndReason.MediaFailure, failure)).IsEqualTo(notification);
        await Assert.That(AirPlayCastFailures.ForTerminal(EndReason.ConnectionLost, failure)).IsEqualTo(NotificationKind.AirPlayCastEnded);
    }

    private sealed class UnusedStore : AacFragmentStore
    {
        protected override void Seal(ulong index, byte[] bytes, ReadControl control) => throw new InvalidOperationException();
        protected override int Read(ulong index, ulong offset, byte[] buffer, int count, ReadControl control) => throw new InvalidOperationException();
        protected override void Clear() => throw new InvalidOperationException();
    }
#endif
}
