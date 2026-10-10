using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Casting;

public class AirPlayReceiverTrackerTests
{
    private static AirPlayReceiverInfo LivingRoom(string address = "192.0.2.10") =>
        new("AA:BB:CC:DD:EE:01", "Living Room", address, 7000);

    private static AirPlayReceiverInfo Bedroom() =>
        new("AA:BB:CC:DD:EE:02", "Bedroom", "192.0.2.11", 7000);

    [Test]
    public async Task Apply_NewReceiver_IsFoundAsAirPlayRenderer()
    {
        var tracker = new AirPlayReceiverTracker();

        var (found, lost) = tracker.Apply(new[] { LivingRoom() });

        await Assert.That(found).HasSingleItem();
        await Assert.That(lost).IsEmpty();
        await Assert.That(found[0].Kind).IsEqualTo(RendererKind.AirPlay);
        await Assert.That(found[0].Name).IsEqualTo("Living Room");
        await Assert.That(found[0].IsAvailable).IsTrue();
        await Assert.That(found[0].Target is null).IsTrue();
    }

    [Test]
    public async Task Apply_ReceiverSeenAgain_ReportsNothingNew()
    {
        var tracker = new AirPlayReceiverTracker();
        tracker.Apply(new[] { LivingRoom() });

        var (found, lost) = tracker.Apply(new[] { LivingRoom() });

        await Assert.That(found).IsEmpty();
        await Assert.That(lost).IsEmpty();
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_OneMissedScan_KeepsReceiver()
    {
        var tracker = new AirPlayReceiverTracker();
        tracker.Apply(new[] { LivingRoom() });

        var (_, lost) = tracker.Apply(Array.Empty<AirPlayReceiverInfo>());

        await Assert.That(lost).IsEmpty();
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_TwoConsecutiveMissedScans_LosesReceiver()
    {
        var tracker = new AirPlayReceiverTracker();
        var (found, _) = tracker.Apply(new[] { LivingRoom(), Bedroom() });
        tracker.Apply(new[] { Bedroom() });

        var (_, lost) = tracker.Apply(new[] { Bedroom() });

        await Assert.That(lost).HasSingleItem();
        await Assert.That(lost[0]).IsEqualTo(found[0]);
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_ReceiverBackAfterOneMiss_ResetsMissCount()
    {
        var tracker = new AirPlayReceiverTracker();
        tracker.Apply(new[] { LivingRoom() });
        tracker.Apply(Array.Empty<AirPlayReceiverInfo>());
        tracker.Apply(new[] { LivingRoom() });

        var (_, lost) = tracker.Apply(Array.Empty<AirPlayReceiverInfo>());

        await Assert.That(lost).IsEmpty();
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_ReceiverWithoutAddress_IsNotListed()
    {
        var tracker = new AirPlayReceiverTracker();

        var (found, _) = tracker.Apply(new[] { LivingRoom(address: string.Empty) });

        await Assert.That(found).IsEmpty();
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_AddressChange_UpdatesRendererWithoutEvents()
    {
        var tracker = new AirPlayReceiverTracker();
        var (found, _) = tracker.Apply(new[] { LivingRoom() });

        var (newlyFound, lost) = tracker.Apply(new[] { LivingRoom(address: "192.0.2.20") });

        await Assert.That(newlyFound).IsEmpty();
        await Assert.That(lost).IsEmpty();
        await Assert.That(found[0].AirPlayReceiver!.Address).IsEqualTo("192.0.2.20");
    }

    [Test]
    public async Task Apply_DuplicateIdInOneScan_ListsReceiverOnce()
    {
        var tracker = new AirPlayReceiverTracker();

        var (found, _) = tracker.Apply(new[] { LivingRoom(), LivingRoom(address: "192.0.2.20") });

        await Assert.That(found).HasSingleItem();
        await Assert.That(found[0].AirPlayReceiver!.Address).IsEqualTo("192.0.2.10");
    }

    [Test]
    public async Task Clear_ReturnsAndRemovesAllRenderers()
    {
        var tracker = new AirPlayReceiverTracker();
        tracker.Apply(new[] { LivingRoom(), Bedroom() });

        var cleared = tracker.Clear();

        await Assert.That(cleared.Count).IsEqualTo(2);
        await Assert.That(tracker.Renderers.Count()).IsEqualTo(0);
    }
}
