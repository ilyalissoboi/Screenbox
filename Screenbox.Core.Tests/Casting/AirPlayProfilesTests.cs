using System.Text.RegularExpressions;
using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public partial class AirPlayProfilesTests
{
    // send-airplay2's profile format: 1-64 of a-z, 0-9 and ._-, starting with a letter or digit.
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex ProfileFormat();

    [Test]
    public async Task ForReceiver_MacStyleId_UsesLowerCaseHexDigits()
    {
        string profile = AirPlayProfiles.ForReceiver("AA:BB:CC:DD:EE:0F");

        await Assert.That(profile).IsEqualTo("airplay-aabbccddee0f");
    }

    [Test]
    public async Task ForReceiver_SameIdInOtherCase_GivesSameProfile()
    {
        await Assert.That(AirPlayProfiles.ForReceiver("aa:bb:cc:dd:ee:0f"))
            .IsEqualTo(AirPlayProfiles.ForReceiver("AA:BB:CC:DD:EE:0F"));
    }

    [Test]
    public async Task ForReceiver_IdWithoutHexDigits_UsesHashOfId()
    {
        string profile = AirPlayProfiles.ForReceiver("living-room");

        // Neither "living-room" nor "wiz" contains a hexadecimal digit, so both use the hash.
        await Assert.That(profile.StartsWith("airplay-", StringComparison.Ordinal)).IsTrue();
        await Assert.That(profile.Length).IsEqualTo("airplay-".Length + 32);
        await Assert.That(ProfileFormat().IsMatch(profile)).IsTrue();
        await Assert.That(AirPlayProfiles.ForReceiver("wiz")).IsNotEqualTo(profile);
    }

    [Test]
    public async Task ForReceiver_VeryLongId_StaysWithinProfileLimit()
    {
        string profile = AirPlayProfiles.ForReceiver(new string('a', 100));

        await Assert.That(profile.Length).IsLessThanOrEqualTo(64);
        await Assert.That(ProfileFormat().IsMatch(profile)).IsTrue();
    }

    [Test]
    public async Task ForReceiver_UuidId_MatchesProfileFormat()
    {
        string profile = AirPlayProfiles.ForReceiver("6F1E2A0B-1C2D-4E5F-8A9B-0C1D2E3F4A5B");

        await Assert.That(profile).IsEqualTo("airplay-6f1e2a0b1c2d4e5f8a9b0c1d2e3f4a5b");
        await Assert.That(ProfileFormat().IsMatch(profile)).IsTrue();
    }
}
