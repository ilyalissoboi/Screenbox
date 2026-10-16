using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Tests.Casting;

public partial class AirPlayFragmentCacheTests
{
    [Test]
    public async Task Seal_Read_ShouldBeImmutableAndPositional()
    {
        using var reservation = new Reservation();
        var cache = reservation.Cache;
        cache.Seal(7, [10, 20, 30, 40], NeverStop);
        byte[] buffer = new byte[8];
        await Assert.That(cache.Read(7, 1, buffer, buffer.Length, NeverStop)).IsEqualTo(3);
        await Assert.That(buffer[..3].SequenceEqual(new byte[] { 20, 30, 40 })).IsTrue();
        await Assert.That(cache.Read(7, ulong.MaxValue, buffer, 8, NeverStop)).IsEqualTo(0);
        Expect<IOException>(() => cache.Seal(7, [99], NeverStop));
        await Assert.That(File.ReadAllBytes(Path.Combine(reservation.Path, "7.m4s"))
            .SequenceEqual(new byte[] { 10, 20, 30, 40 })).IsTrue();
        cache.Clear();
        cache.Release();
        await Assert.That(Directory.Exists(reservation.Path)).IsFalse();
        Expect<OperationCanceledException>(() => cache.Read(7, 0, buffer, 1, NeverStop));
    }

    [Test]
    public async Task Seal_WhenCancelledDuringWrite_ShouldNeverPublishAndShouldCleanPartial()
    {
        using var reservation = new Reservation();
        int polls = 0;
        Expect<OperationCanceledException>(() => reservation.Cache.Seal(3, new byte[256 * 1024], () => ++polls == 3));
        await Assert.That(File.Exists(Path.Combine(reservation.Path, "3.m4s"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(reservation.Path, "3.m4s.partial"))).IsTrue();
        reservation.Cache.Release();
        await Assert.That(Directory.Exists(reservation.Path)).IsFalse();
    }

    [Test]
    [Arguments("4.m4s")]
    [Arguments("4.m4s.partial")]
    public async Task Seal_WhenPathExists_ShouldNeverAdoptOrDeleteUnknownFile(string name)
    {
        using var reservation = new Reservation();
        string unknown = Path.Combine(reservation.Path, name);
        File.WriteAllText(unknown, "unowned");
        try
        {
            Expect<IOException>(() => reservation.Cache.Seal(4, [1, 2], NeverStop));
            Expect<IOException>(reservation.Cache.Clear);
            await Assert.That(File.ReadAllText(unknown)).IsEqualTo("unowned");
        }
        finally
        {
            File.Delete(unknown);
            reservation.Cache.Release();
        }
        await Assert.That(Directory.Exists(reservation.Path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyReservation_ShouldBeRemovedWithoutTouchingSibling(bool abandon)
    {
        using var reservation = new Reservation();
        string sibling = reservation.Path + "-sibling";
        File.WriteAllText(sibling, "keep");
        try
        {
            if (abandon) reservation.Cache.AbandonBeforeTransfer();
            else reservation.Cache.Release();
            await Assert.That(Directory.Exists(reservation.Path)).IsFalse();
            await Assert.That(File.ReadAllText(sibling)).IsEqualTo("keep");
        }
        finally { File.Delete(sibling); }
    }

    [Test]
    public async Task Cleanup_WhenFileIsBusy_ShouldRetryOnRelease()
    {
        using var reservation = new Reservation();
        reservation.Cache.Seal(1, [1], NeverStop);
        using (FileStream reader = new(Path.Combine(reservation.Path, "1.m4s"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Expect<IOException>(reservation.Cache.Clear);
            await Assert.That(Directory.Exists(reservation.Path)).IsTrue();
        }
        reservation.Cache.Release();
        await Assert.That(Directory.Exists(reservation.Path)).IsFalse();
    }

    private static bool NeverStop() => false;

    private static void Expect<T>(Action operation) where T : Exception
    {
        try { operation(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed partial class Reservation : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "screenbox-cache-test-" + Guid.NewGuid().ToString("N"));
        internal AirPlayFragmentCache Cache { get; }

        internal Reservation()
        {
            Directory.CreateDirectory(Path);
            Cache = new AirPlayFragmentCache(Path);
        }

        public void Dispose() => Cache.Release();
    }
}
