using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Immutable fragments in one exclusively created, empty app-owned directory.</summary>
/// <remarks>The caller grants ownership of that directory only. Cleanup never
/// recurses or removes unknown files. Cancellation predicates are borrowed for
/// one operation and are never retained. All file access is serialized.</remarks>
internal sealed class AirPlayFragmentCache(string directory)
{
    private const int IoChunkBytes = 64 * 1024;
    private readonly object _gate = new();
    private readonly List<OwnedFragment> _fragments = [];
    private bool _closed;

    private sealed class OwnedFragment(string path)
    {
        internal string Path { get; } = path;
        internal string TemporaryPath { get; } = path + ".partial";
        internal bool OwnsTemporary { get; set; }
        internal bool OwnsFinal { get; set; }
    }

    /// <summary>Seals one whole fragment, refusing duplicate indices and partial publication.</summary>
    internal void Seal(ulong index, byte[] bytes, Func<bool> shouldStop)
    {
        lock (_gate)
        {
            ThrowIfStopped(shouldStop);
            // Allocate tracking before I/O; own a path only after exclusive creation
            // or a successful rename, never just because its name was attempted.
            OwnedFragment fragment = new(FragmentPath(index));
            _fragments.Add(fragment);
            using (FileStream stream = new(fragment.TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fragment.OwnsTemporary = true;
                for (int offset = 0; offset < bytes.Length;)
                {
                    ThrowIfStopped(shouldStop);
                    int count = Math.Min(IoChunkBytes, bytes.Length - offset);
                    stream.Write(bytes, offset, count);
                    offset += count;
                }

                stream.Flush();
            }

            ThrowIfStopped(shouldStop);
            File.Move(fragment.TemporaryPath, fragment.Path);
            fragment.OwnsFinal = true;
            fragment.OwnsTemporary = false;
        }
    }

    /// <summary>Reads an immutable fragment positionally; short reads are allowed.</summary>
    internal int Read(ulong index, ulong offset, byte[] buffer, int count, Func<bool> shouldStop)
    {
        lock (_gate)
        {
            ThrowIfStopped(shouldStop);
            if (offset > long.MaxValue) return 0;
            using FileStream stream = new(FragmentPath(index), FileMode.Open, FileAccess.Read, FileShare.Read);
            stream.Position = (long)offset;
            int read = stream.Read(buffer, 0, count);
            return shouldStop() ? 0 : read;
        }
    }

    /// <summary>Closes storage and attempts every known deletion; release can retry failures.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _closed = true;
            RemoveOwnedFiles();
        }
    }

    /// <summary>Also removes unused empty reservations released without Clear.</summary>
    internal void Release() => Clear();

    /// <summary>Abandons an empty reservation before transferring it to Cast.Create.</summary>
    internal void AbandonBeforeTransfer()
    {
        lock (_gate)
        {
            if (_closed || _fragments.Count != 0) throw new InvalidOperationException("Cache already used.");
            _closed = true;
            RemoveOwnedFiles();
        }
    }

    private string FragmentPath(ulong index) => Path.Combine(directory,
        index.ToString(CultureInfo.InvariantCulture) + ".m4s");

    private void ThrowIfStopped(Func<bool> shouldStop)
    {
        if (_closed || shouldStop()) throw new OperationCanceledException();
    }

    private void RemoveOwnedFiles()
    {
        Exception? failure = null;
        foreach (OwnedFragment fragment in _fragments)
        {
            try
            {
                if (fragment.OwnsTemporary)
                {
                    File.Delete(fragment.TemporaryPath);
                    fragment.OwnsTemporary = false;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failure ??= e;
            }

            try
            {
                if (fragment.OwnsFinal)
                {
                    File.Delete(fragment.Path);
                    fragment.OwnsFinal = false;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failure ??= e;
            }
        }

        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (DirectoryNotFoundException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure ??= e;
        }

        if (failure is not null) throw new IOException("AirPlay fragment cleanup failed.", failure);
    }
}
