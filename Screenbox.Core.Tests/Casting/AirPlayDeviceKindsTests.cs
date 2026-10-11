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
    [Arguments("Mac14,3", RendererDeviceKind.Desktop)] // Mac mini (M2)
    [Arguments("Mac13,1", RendererDeviceKind.Desktop)] // Mac Studio (M1 Max)
    [Arguments("Mac16,2", RendererDeviceKind.Desktop)] // iMac (M4)
    [Arguments("Mac14,9", RendererDeviceKind.Laptop)] // MacBook Pro 14" (M2 Pro)
    [Arguments("Mac15,3", RendererDeviceKind.Laptop)] // MacBook Pro 14" (M3)
    [Arguments("Mac14,2", RendererDeviceKind.Laptop)] // MacBook Air (M2)
    [Arguments("Mac99,1", RendererDeviceKind.Laptop)] // a future identifier
    [Arguments("SomeVendorTV2024", RendererDeviceKind.Television)]
    [Arguments("", RendererDeviceKind.Television)]
    public async Task FromModel_MapsAdvertisedModel(string model, RendererDeviceKind expected)
    {
        await Assert.That(AirPlayDeviceKinds.FromModel(model)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("AppleTV14,1", RendererDeviceFamily.AppleTV4K)]
    [Arguments("AppleTV6,2", RendererDeviceFamily.AppleTV4K)]
    [Arguments("AppleTV5,3", RendererDeviceFamily.AppleTVHD)]
    [Arguments("AppleTV3,2", RendererDeviceFamily.AppleTV)]
    [Arguments("AudioAccessory5,1", RendererDeviceFamily.HomePodMini)]
    [Arguments("AudioAccessory6,1", RendererDeviceFamily.HomePod)]
    [Arguments("MacBookPro18,1", RendererDeviceFamily.MacBookPro)]
    [Arguments("MacBookAir10,1", RendererDeviceFamily.MacBookAir)]
    [Arguments("MacBook10,1", RendererDeviceFamily.MacBook)]
    [Arguments("iMacPro1,1", RendererDeviceFamily.IMacPro)]
    [Arguments("iMac21,1", RendererDeviceFamily.IMac)]
    [Arguments("Macmini9,1", RendererDeviceFamily.MacMini)]
    [Arguments("MacPro7,1", RendererDeviceFamily.MacPro)]
    [Arguments("Mac14,9", RendererDeviceFamily.MacBookPro)]
    [Arguments("Mac15,13", RendererDeviceFamily.MacBookAir)]
    [Arguments("Mac13,1", RendererDeviceFamily.MacStudio)]
    [Arguments("Mac16,10", RendererDeviceFamily.MacMini)]
    [Arguments("Mac99,1", RendererDeviceFamily.Mac)]
    public async Task Identify_GivesProductFamily(string model, RendererDeviceFamily family)
    {
        await Assert.That(AirPlayDeviceKinds.Identify(model).Family).IsEqualTo(family);
    }

    [Test]
    [Arguments("SomeVendorTV2024")]
    [Arguments("")]
    public async Task Identify_OtherMakers_HaveNoFamily(string model)
    {
        await Assert.That(AirPlayDeviceKinds.Identify(model).Family).IsEqualTo(RendererDeviceFamily.Unknown);
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
        await Assert.That(found[0].DeviceFamily).IsEqualTo(RendererDeviceFamily.MacBookPro);
    }
}
