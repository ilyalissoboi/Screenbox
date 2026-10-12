using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayPairingServiceTests
{
    [Test]
    public async Task Forget_ShouldRemoveOnlyTheSelectedProfile()
    {
        HashSet<string> profiles = ["airplay-aabbccddee01", "airplay-aabbccddee02"];
        var service = new AirPlayPairingService(NullLogger<AirPlayPairingService>.Instance, profiles.Remove);
        var renderer = new Renderer(new AirPlayReceiverInfo("AA:BB:CC:DD:EE:01", "First", "192.0.2.1", 7000));

        await Assert.That(service.Forget(renderer)).IsTrue();
        await Assert.That(profiles.Contains("airplay-aabbccddee01")).IsFalse();
        await Assert.That(profiles.Contains("airplay-aabbccddee02")).IsTrue();
    }

    [Test]
    public async Task Forget_WhenAlreadyAbsent_ShouldSucceed()
    {
        var service = new AirPlayPairingService(NullLogger<AirPlayPairingService>.Instance, _ => false);
        var renderer = new Renderer(new AirPlayReceiverInfo("AA:BB:CC:DD:EE:01", "First", "", 7000));
        renderer.Dispose();

        await Assert.That(service.Forget(renderer)).IsTrue();
    }

    [Test]
    public async Task Forget_WhenStorageFails_ShouldReportFailure()
    {
        var service = new AirPlayPairingService(NullLogger<AirPlayPairingService>.Instance,
            _ => throw new InvalidOperationException());
        var renderer = new Renderer(new AirPlayReceiverInfo("AA:BB:CC:DD:EE:01", "First", "192.0.2.1", 7000));

        await Assert.That(service.Forget(renderer)).IsFalse();
    }
}
