using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Events;
using Screenbox.Core.Models;
using SendAirPlay2;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Discovers AirPlay receivers with repeated send-airplay2 scans while started,
/// and reports them as <see cref="Renderer"/>s of kind <see cref="RendererKind.AirPlay"/>,
/// with the same events as <see cref="Helpers.RendererWatcher"/>.
/// </summary>
/// <remarks>
/// Each scan blocks for <see cref="Receivers.DefaultDuration"/>, so scans run on a
/// background thread and the events are raised there; subscribers marshal to the
/// UI thread. After <see cref="Stop"/> returns, no further events are raised, even
/// if a scan was still running. Receiver names are never logged.
/// </remarks>
public sealed partial class AirPlayReceiverWatcher : IDisposable
{
    /// <summary>Pause between the end of one scan and the start of the next.</summary>
    internal static readonly TimeSpan ScanPause = TimeSpan.FromSeconds(2);

    public event EventHandler<RendererFoundEventArgs>? RendererFound;
    public event EventHandler<RendererLostEventArgs>? RendererLost;

    public bool IsStarted { get; private set; }

    private readonly Func<IReadOnlyList<AirPlayReceiverInfo>> _scan;
    private readonly TimeSpan _pause;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly AirPlayReceiverTracker _tracker = new();
    private CancellationTokenSource? _stop;

    internal AirPlayReceiverWatcher(ILogger logger)
        : this(ScanReceivers, ScanPause, logger)
    {
    }

    /// <param name="scan">One blocking discovery scan; may throw.</param>
    /// <param name="pause">Pause between scans.</param>
    /// <param name="logger">Receives fixed-field diagnostics only.</param>
    internal AirPlayReceiverWatcher(Func<IReadOnlyList<AirPlayReceiverInfo>> scan, TimeSpan pause, ILogger logger)
    {
        _scan = scan;
        _pause = pause;
        _logger = logger;
    }

    /// <summary>The AirPlay renderers currently listed.</summary>
    public IReadOnlyList<Renderer> GetRenderers()
    {
        lock (_gate)
        {
            return _tracker.Renderers.ToList();
        }
    }

    /// <summary>Starts scanning; returns immediately. Starting twice has no effect.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (IsStarted)
            {
                return true;
            }

            _stop = new CancellationTokenSource();
            IsStarted = true;
            CancellationToken token = _stop.Token;
            Task.Factory.StartNew(() => RunAsync(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return true;
        }
    }

    /// <summary>
    /// Stops scanning and makes every listed renderer unavailable. Returns without
    /// waiting for a running scan, which then finishes without raising events.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!IsStarted)
            {
                return;
            }

            _stop?.Cancel();
            _stop?.Dispose();
            _stop = null;
            IsStarted = false;
            foreach (Renderer renderer in _tracker.Clear())
            {
                renderer.Dispose();
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IReadOnlyList<AirPlayReceiverInfo>? scan = null;
            try
            {
                scan = _scan();
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
                                          or NotSupportedException)
            {
                // The native library is missing, for another architecture, or too old: retrying cannot help.
                _logger.LogWarning("AirPlay discovery is unavailable: {ErrorType}", e.GetType().Name);
                return;
            }
            catch (Exception e)
            {
                // A failed scan (for example, no usable network) says nothing about which receivers are present.
                _logger.LogInformation("AirPlay discovery scan failed: {ErrorType}", e.GetType().Name);
            }

            if (scan is not null)
            {
                Publish(scan, token);
            }

            try
            {
                await Task.Delay(_pause, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Publish(IReadOnlyList<AirPlayReceiverInfo> scan, CancellationToken token)
    {
        // Events are raised under the lock so that Stop cannot interleave: once Stop
        // has returned, the token is cancelled and nothing more is published.
        lock (_gate)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            (List<Renderer> found, List<Renderer> lost) = _tracker.Apply(scan);
            foreach (Renderer renderer in lost)
            {
                renderer.Dispose();
                RendererLost?.Invoke(this, new RendererLostEventArgs(renderer));
            }

            foreach (Renderer renderer in found)
            {
                RendererFound?.Invoke(this, new RendererFoundEventArgs(renderer));
            }
        }
    }

    private static IReadOnlyList<AirPlayReceiverInfo> ScanReceivers()
    {
        return Receivers.Discover(Receivers.DefaultDuration)
            .Select(receiver => new AirPlayReceiverInfo(receiver.Id, receiver.Name, receiver.Address, receiver.Port,
                receiver.Model))
            .ToList();
    }
}
