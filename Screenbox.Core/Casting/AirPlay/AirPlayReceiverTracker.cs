using System.Collections.Generic;
using Screenbox.Core.Models;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Turns successive discovery scans into found and lost renderers, keyed by
/// receiver id. Not thread-safe: <see cref="AirPlayReceiverWatcher"/> calls it
/// from its scan loop under its own lock.
/// </summary>
/// <remarks>
/// A listed receiver is removed only after it is missing from
/// <see cref="MissesBeforeLost"/> consecutive scans, because a single scan can
/// miss a receiver that is present (seen right after a cast ended). Receivers
/// without a castable address are not listed at all.
/// </remarks>
internal sealed class AirPlayReceiverTracker
{
    /// <summary>Consecutive scans a listed receiver may be missing from before it is lost.</summary>
    internal const int MissesBeforeLost = 2;

    private readonly Dictionary<string, Entry> _entries = new();

    /// <summary>The renderers currently listed.</summary>
    internal IEnumerable<Renderer> Renderers
    {
        get
        {
            foreach (Entry entry in _entries.Values)
            {
                yield return entry.Renderer;
            }
        }
    }

    /// <summary>
    /// Applies one completed scan. A failed scan must not be applied: it says
    /// nothing about which receivers are present.
    /// </summary>
    /// <returns>The renderers first listed by this scan, and those it removed.</returns>
    internal (List<Renderer> Found, List<Renderer> Lost) Apply(IReadOnlyList<AirPlayReceiverInfo> scan)
    {
        List<Renderer> found = new();
        List<Renderer> lost = new();
        HashSet<string> seen = new();
        foreach (AirPlayReceiverInfo receiver in scan)
        {
            if (receiver.Id.Length == 0 || receiver.Address.Length == 0 || !seen.Add(receiver.Id))
            {
                continue;
            }

            if (_entries.TryGetValue(receiver.Id, out Entry? entry))
            {
                entry.Misses = 0;
                // The address can change between scans (DHCP); later casts use the latest.
                entry.Renderer.UpdateAirPlayReceiver(receiver);
            }
            else
            {
                Renderer renderer = new(receiver);
                _entries.Add(receiver.Id, new Entry(renderer));
                found.Add(renderer);
            }
        }

        List<string> removed = new();
        foreach ((string id, Entry entry) in _entries)
        {
            if (!seen.Contains(id) && ++entry.Misses >= MissesBeforeLost)
            {
                removed.Add(id);
                lost.Add(entry.Renderer);
            }
        }

        foreach (string id in removed)
        {
            _entries.Remove(id);
        }

        return (found, lost);
    }

    /// <summary>Removes every listed renderer and returns them.</summary>
    internal List<Renderer> Clear()
    {
        List<Renderer> all = new(Renderers);
        _entries.Clear();
        return all;
    }

    private sealed class Entry
    {
        public Renderer Renderer { get; }

        public int Misses { get; set; }

        public Entry(Renderer renderer)
        {
            Renderer = renderer;
        }
    }
}
