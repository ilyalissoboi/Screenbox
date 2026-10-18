using Screenbox.Core.Casting.AirPlay;
using SendAirPlay2;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayMediaDeliveryTests
{
    /// <summary>The M4A route works before VLC parses a queued item and in both build profiles.</summary>
    [Test]
    [Arguments(".m4a")]
    [Arguments(".M4A")]
    public async Task M4a_ShouldUseProgressiveAudioMime(string fileType)
    {
        var options = new CastOptions();
        await Assert.That(AirPlayMediaDelivery.Configure(options, fileType)).IsTrue();
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.Progressive);
        await Assert.That(options.ContentType).IsEqualTo("audio/mp4");
    }

    /// <summary>Video and unimplemented audio extensions must not silently gain the direct-audio route.</summary>
    [Test]
    [Arguments(".mp4")]
    [Arguments(".mkv")]
    [Arguments(".aac")]
    [Arguments(".flac")]
    [Arguments(".opus")]
    [Arguments(".mp3")]
    [Arguments("")]
    public async Task OtherExtension_ShouldRetainVideoRemux(string fileType)
    {
        var options = new CastOptions { Delivery = CastDelivery.Progressive, ContentType = "audio/mp4" };
        await Assert.That(AirPlayMediaDelivery.Configure(options, fileType)).IsFalse();
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.HlsRemux);
        await Assert.That(options.ContentType).IsNull();
    }

#if SCREENBOX_AIRPLAY_STEREO_AAC
    /// <summary>A mixed queue can return from M4A to the existing opt-in video copy policy.</summary>
    [Test]
    public async Task VideoAfterAudio_ShouldKeepVideoPolicyAndConversionPermission()
    {
        var options = new CastOptions();
        AirPlayMediaDelivery.Configure(options, ".m4a");
        bool audio = AirPlayMediaDelivery.Configure(options, ".mkv");
        if (!audio) AirPlayAudioPolicy.Configure(options, null, allowStereo: false);
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.AutomaticAudioHls);
        await Assert.That(options.ContentType).IsNull();
        await Assert.That(options.ConversionPermission).IsEqualTo(AudioConversionPermission.PreserveOriginal);
        await Assert.That(options.FragmentStore).IsNull();
    }
#endif
}
