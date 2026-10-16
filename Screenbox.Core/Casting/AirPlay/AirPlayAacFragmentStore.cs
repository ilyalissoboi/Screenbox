#if SCREENBOX_AIRPLAY_STEREO_AAC
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SendAirPlay2;
using Windows.Storage;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>One cast's callback context over an exclusively reserved UWP temporary child.</summary>
/// <remarks>Cast.Create consumes this context on failure too. Abandon is only for
/// source-open failure before that invocation. Clear/release run after native
/// workers drain; neither the UI nor the next cast deletes this directory.</remarks>
internal sealed class AirPlayAacFragmentStore(AirPlayFragmentCache cache, ILogger logger) : AacFragmentStore
{
    internal static async Task<AirPlayAacFragmentStore> CreateAsync(ILogger logger)
    {
        StorageFolder folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
            "screenbox-airplay-" + Guid.NewGuid().ToString("N"), CreationCollisionOption.FailIfExists);
        try
        {
            return new AirPlayAacFragmentStore(new AirPlayFragmentCache(folder.Path), logger);
        }
        catch
        {
            try { Directory.Delete(folder.Path, recursive: false); }
            catch (Exception e) { logger.LogWarning("AirPlay cache cleanup failed: {ErrorType}", e.GetType().Name); }
            throw;
        }
    }

    internal void AbandonBeforeTransfer() => TryCleanup(cache.AbandonBeforeTransfer);

    protected override void Seal(ulong index, byte[] bytes, ReadControl control) =>
        cache.Seal(index, bytes, () => control.ShouldStop);

    protected override int Read(ulong index, ulong offset, byte[] buffer, int count, ReadControl control) =>
        cache.Read(index, offset, buffer, count, () => control.ShouldStop);

    protected override void Clear() => TryCleanup(cache.Clear);
    protected override void OnReleased() => TryCleanup(cache.Release);

    private void TryCleanup(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception e) { logger.LogWarning("AirPlay cache cleanup failed: {ErrorType}", e.GetType().Name); }
    }
}
#endif
