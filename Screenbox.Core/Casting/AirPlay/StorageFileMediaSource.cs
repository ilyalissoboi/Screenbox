using System;
using System.IO;
using System.Threading.Tasks;
using SendAirPlay2;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// A send-airplay2 media source over a brokered <see cref="StorageFile"/>, the
/// access a packaged app has to the user's files.
/// </summary>
/// <remarks>
/// The library reads positionally from several of its own threads at once, so
/// reads are serialized under one lock. The stream is closed when the library
/// releases the source, which happens exactly once. One source serves one cast.
/// </remarks>
internal sealed class StorageFileMediaSource : MediaSource
{
    private readonly object _gate = new();
    private readonly IRandomAccessStreamWithContentType _stream;
    private readonly Stream _reader;

    private StorageFileMediaSource(IRandomAccessStreamWithContentType stream)
    {
        _stream = stream;
        // No read-ahead buffer: the receiver's range requests jump around the file.
        _reader = stream.AsStreamForRead(0);
        Size = checked((long)stream.Size);
    }

    /// <summary>Opens <paramref name="file"/> for reading.</summary>
    internal static async Task<StorageFileMediaSource> OpenAsync(StorageFile file)
    {
        return new StorageFileMediaSource(await file.OpenReadAsync());
    }

    public override long Size { get; }

    protected override int Read(long offset, byte[] buffer, int count, ReadControl control)
    {
        if (control.ShouldStop)
        {
            return 0;
        }

        lock (_gate)
        {
            _reader.Position = offset;
            return _reader.Read(buffer, 0, count);
        }
    }

    protected override void OnReleased()
    {
        lock (_gate)
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
