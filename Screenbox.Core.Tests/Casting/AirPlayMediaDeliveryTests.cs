using Screenbox.Core.Casting.AirPlay;
using SendAirPlay2;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayMediaDeliveryTests
{
#if SCREENBOX_AIRPLAY_STEREO_AAC
    /// <summary>Track presence selects a policy; the native library validates actual content.</summary>
    [Test]
    [Arguments(".m4a")]
    [Arguments(".MP3")]
    [Arguments(".flac")]
    [Arguments(".ogg")]
    [Arguments(".wav")]
    [Arguments(".mp4")]
    [Arguments(".mov")]
    [Arguments(".mkv")]
    [Arguments("")]
    public async Task ParsedAudio_ShouldUseAudioPolicyRegardlessOfExtension(string fileType)
    {
        bool audioOnly = AirPlayMediaDelivery.IsAudioOnly(fileType, hasAudio: true, hasVideo: false);
        var options = new CastOptions { ContentType = "video/mp4" };
        AirPlayMediaDelivery.Configure(options, audioOnly);
        await Assert.That(audioOnly).IsTrue();
        await Assert.That(options.AudioProfile).IsEqualTo(AudioDeliveryProfile.AppleAudioV1);
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.AutomaticAudioHls);
        await Assert.That(options.ContentType).IsNull();
    }

    /// <summary>A video track prevents audio-only policy even with an audio-looking extension.</summary>
    [Test]
    [Arguments(".m4a", true)]
    [Arguments(".mp4", true)]
    [Arguments(".mkv", false)]
    public async Task ParsedVideo_ShouldUseVideoPolicy(string fileType, bool hasAudio)
    {
        bool audioOnly = AirPlayMediaDelivery.IsAudioOnly(fileType, hasAudio, hasVideo: true);
        var options = new CastOptions();
        AirPlayMediaDelivery.Configure(options, audioOnly);
        await Assert.That(audioOnly).IsFalse();
        await Assert.That(options.AudioProfile).IsEqualTo(AudioDeliveryProfile.AppleHlsV1);
    }

    /// <summary>Missing metadata must not accidentally select the audio policy.</summary>
    [Test]
    public void MissingTracks_ShouldFailClassification()
    {
        try { AirPlayMediaDelivery.IsAudioOnly(".m4a", hasAudio: false, hasVideo: false); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Missing tracks must fail before native casting.");
    }

    /// <summary>Every queue item resets its policy/MIME independently of conversion permission.</summary>
    [Test]
    public async Task MixedQueue_ShouldNotCarryOverProfileMimeOrPermission()
    {
        var options = new CastOptions { ContentType = "audio/mp4" };
        foreach (bool audioOnly in new[] { true, false, true, false })
        {
            AirPlayMediaDelivery.Configure(options, audioOnly);
            AirPlayAudioPolicy.Configure(options, null, allowStereo: false);
            await Assert.That(options.AudioProfile).IsEqualTo(audioOnly
                ? AudioDeliveryProfile.AppleAudioV1 : AudioDeliveryProfile.AppleHlsV1);
            await Assert.That(options.ContentType).IsNull();
            await Assert.That(options.ConversionPermission).IsEqualTo(AudioConversionPermission.PreserveOriginal);
            await Assert.That(options.FragmentStore).IsNull();
            await Assert.That(options.CacheQuotaBytes).IsEqualTo(0ul);
        }
    }
#else
    /// <summary>Ordinary builds retain their M4A route without a metadata prerequisite.</summary>
    [Test]
    [Arguments(".m4a")]
    [Arguments(".M4A")]
    public async Task M4a_ShouldUseProgressiveAudioMime(string fileType)
    {
        var options = new CastOptions();
        bool audioOnly = AirPlayMediaDelivery.IsAudioOnly(fileType, hasAudio: false, hasVideo: false);
        AirPlayMediaDelivery.Configure(options, audioOnly);
        await Assert.That(audioOnly).IsTrue();
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
        bool audioOnly = AirPlayMediaDelivery.IsAudioOnly(fileType, hasAudio: true, hasVideo: false);
        AirPlayMediaDelivery.Configure(options, audioOnly);
        await Assert.That(audioOnly).IsFalse();
        await Assert.That(options.Delivery).IsEqualTo(CastDelivery.HlsRemux);
        await Assert.That(options.ContentType).IsNull();
    }

#endif
}
