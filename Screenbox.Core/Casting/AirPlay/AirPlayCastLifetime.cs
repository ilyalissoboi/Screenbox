using System;
using System.Threading;
using System.Threading.Tasks;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>Serializes retired-cast cleanup and rejects superseded start requests.</summary>
/// <remarks>Cleanup actions run off the caller's thread in order. A failed cleanup
/// is reported without faulting the barrier or preventing later cleanup. Callers
/// must recheck their ticket after asynchronous source/cache creation too.</remarks>
internal sealed class AirPlayCastLifetime(Action<Exception> reportCleanupFailure)
{
    private readonly object _gate = new();
    private Task _cleanup = Task.CompletedTask;
    private long _generation;

    internal long BeginRequest() => Interlocked.Increment(ref _generation);
    internal void InvalidateRequests() => Interlocked.Increment(ref _generation);
    internal bool IsCurrent(long ticket) => Volatile.Read(ref _generation) == ticket;

    /// <summary>Queues exactly one cast's Stop/Dispose action, in that order.</summary>
    internal void ScheduleCleanup(Action cleanup)
    {
        lock (_gate)
        {
            Task previous = _cleanup;
            _cleanup = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                try
                {
                    cleanup();
                }
                catch (Exception e)
                {
                    try { reportCleanupFailure(e); }
                    catch { /* A logger failure must not strand later cleanup. */ }
                }
            });
        }
    }

    /// <summary>Waits for all queued cleanup before admitting one current request.</summary>
    internal async Task<bool> WaitForCleanupAsync(long ticket)
    {
        while (IsCurrent(ticket))
        {
            Task pending;
            lock (_gate) pending = _cleanup;
            await pending.ConfigureAwait(false);
            lock (_gate)
            {
                if (pending == _cleanup) return IsCurrent(ticket);
            }
        }

        return false;
    }
}
