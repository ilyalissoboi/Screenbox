using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayDeviceKindsTests
{
    [Test]
    [Arguments("AppleTV14,1", RendererDeviceKind.Television)]
    [Arguments("AppleTV5,3", RendererDeviceKind.Television)]
    [Arguments("AudioAccessory1,1", RendererDeviceKind.Speaker)]
    [Arguments("AudioAccessory5,1", RendererDeviceKind.Speaker)]
    [Arguments("MacBookPro18,1", RendererDeviceKind.Laptop)]
    [Arguments("MacBookAir10,1", RendererDeviceKind.Laptop)]
    [Arguments("iMac21,1", RendererDeviceKind.Desktop)]
    [Arguments("Macmini9,1", RendererDeviceKind.Desktop)]
    [Arguments("MacPro7,1", RendererDeviceKind.Desktop)]
    [Arguments("Mac14,3", RendererDeviceKind.Desktop)]
    [Arguments("SomeVendorTV2024", RendererDeviceKind.Television)]
    [Arguments("", RendererDeviceKind.Television)]
    public async Task FromModel_MapsAdvertisedModel(string model, RendererDeviceKind expected)
    {
        await Assert.That(AirPlayDeviceKinds.FromModel(model)).IsEqualTo(expected);
    }

    [Test]
    public async Task AirPlayRenderer_TakesDeviceKindFromModel()
    {
        var tracker = new AirPlayReceiverTracker();

        var (found, _) = tracker.Apply(new[]
        {
            new AirPlayReceiverInfo("AA:BB:CC:DD:EE:01", "Office", "192.0.2.10", 7000, "MacBookPro18,1"),
        });

        await Assert.That(found[0].DeviceKind).IsEqualTo(RendererDeviceKind.Laptop);
    }
}
