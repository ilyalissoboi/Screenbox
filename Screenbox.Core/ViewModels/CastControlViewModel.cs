using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Events;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class CastControlViewModel : ObservableObject
{
    public ObservableCollection<Renderer> Renderers { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CastCommand))]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    public partial Renderer? SelectedRenderer { get; set; }

    /// <summary>
    /// Whether the selected renderer is an AirPlay receiver without saved
    /// credentials; the flyout then offers Pair instead of Cast.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CastCommand))]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    public partial bool IsSelectedRendererUnpaired { get; set; }

    /// <summary>Whether a pairing is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    [NotifyCanExecuteChangedFor(nameof(CastCommand))]
    public partial bool IsPairing { get; set; }

    /// <summary>Whether an AirPlay cast is starting (the TV may take several seconds).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CastCommand))]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    public partial bool IsCastStarting { get; set; }

    [ObservableProperty] public partial Renderer? CastingDevice { get; set; }
    [ObservableProperty] public partial bool IsCasting { get; set; }

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly PlayerContext _playerContext;
    private readonly CastContext _castContext;
    private readonly ICastService _castService;
    private readonly IAirPlayPairingService _airPlayPairingService;
    private readonly IAirPlayPinDialogService _airPlayPinDialogService;
    private readonly AirPlayCastCoordinator _airPlayCastCoordinator;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<CastControlViewModel> _logger;

    public CastControlViewModel(PlayerContext playerContext, CastContext castContext, ICastService castService,
        IAirPlayPairingService airPlayPairingService, IAirPlayPinDialogService airPlayPinDialogService,
        AirPlayCastCoordinator airPlayCastCoordinator, ILogger<CastControlViewModel> logger)
    {
        _playerContext = playerContext;
        _castContext = castContext;
        _castService = castService;
        _airPlayPairingService = airPlayPairingService;
        _airPlayPinDialogService = airPlayPinDialogService;
        _airPlayCastCoordinator = airPlayCastCoordinator;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _logger = logger;
        Renderers = new ObservableCollection<Renderer>();
        _castContext.PropertyChanged += OnCastContextPropertyChanged;
    }

    public void StartDiscovering()
    {
        if (_airPlayCastCoordinator.IsCasting)
        {
            // This flyout may not have started the AirPlay cast; show it as the active one.
            CastingDevice = _castContext.ActiveRenderer;
            IsCasting = true;
            return;
        }

        // LibVLC discovery needs the VLC player, which an AirPlay cast swaps out.
        if (IsCasting || MediaPlayer is not VlcMediaPlayer) return;

        var watcher = _castService.CreateRendererWatcher(MediaPlayer);
        _castContext.RendererWatcher = watcher;
        watcher.RendererFound += RendererWatcherOnRendererFound;
        watcher.RendererLost += RendererWatcherOnRendererLost;
        watcher.Start();

        // AirPlay receivers come from send-airplay2, listed beside the LibVLC renderers.
        // StopCasting restarts discovery while the flyout is open; keep one AirPlay watcher.
        if (_castContext.AirPlayReceiverWatcher is not null) return;
        var airPlayWatcher = _castService.CreateAirPlayReceiverWatcher();
        _castContext.AirPlayReceiverWatcher = airPlayWatcher;
        airPlayWatcher.RendererFound += RendererWatcherOnRendererFound;
        airPlayWatcher.RendererLost += RendererWatcherOnRendererLost;
        airPlayWatcher.Start();
    }

    public void StopDiscovering()
    {
        var watcher = _castContext.RendererWatcher;
        if (watcher != null)
        {
            watcher.RendererFound -= RendererWatcherOnRendererFound;
            watcher.RendererLost -= RendererWatcherOnRendererLost;
            watcher.Stop();
            watcher.Dispose();
            _castContext.RendererWatcher = null;
        }

        var airPlayWatcher = _castContext.AirPlayReceiverWatcher;
        if (airPlayWatcher is not null)
        {
            airPlayWatcher.RendererFound -= RendererWatcherOnRendererFound;
            airPlayWatcher.RendererLost -= RendererWatcherOnRendererLost;
            airPlayWatcher.Dispose();
            _castContext.AirPlayReceiverWatcher = null;
        }

        SelectedRenderer = null;
        Renderers.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanCast))]
    private async Task CastAsync()
    {
        if (SelectedRenderer == null || MediaPlayer == null) return;
        if (SelectedRenderer.Kind is RendererKind.AirPlay)
        {
            await CastWithAirPlayAsync(SelectedRenderer);
            return;
        }

        _logger.LogInformation("Start casting. {RendererHash} {RendererType} {CanRenderAudio} {CanRenderVideo}",
            SelectedRenderer.Name.GetHashCode(),
            SelectedRenderer.Type,
            SelectedRenderer.CanRenderAudio,
            SelectedRenderer.CanRenderVideo);
        if (_castService.SetActiveRenderer(MediaPlayer, SelectedRenderer))
        {
            _castContext.ActiveRenderer = SelectedRenderer;
            CastingDevice = SelectedRenderer;
            IsCasting = true;
        }
    }

    private bool CanCast() => SelectedRenderer switch
    {
        { IsAvailable: true, Kind: RendererKind.Chromecast } => true,
        // An unpaired AirPlay receiver offers Pair instead.
        { IsAvailable: true, Kind: RendererKind.AirPlay } => !IsSelectedRendererUnpaired && !IsPairing && !IsCastStarting,
        _ => false,
    };

    /// <summary>
    /// Casts the loaded item to an AirPlay receiver, and reports a failed start as
    /// a notification. A pairing the TV no longer accepts is forgotten, so the
    /// flyout offers Pair again.
    /// </summary>
    private async Task CastWithAirPlayAsync(Renderer renderer)
    {
        _logger.LogInformation("Start casting. {RendererType}", renderer.Type);
        IsCastStarting = true;
        AirPlayCastResult result;
        try
        {
            result = await _airPlayCastCoordinator.StartAsync(renderer);
        }
        finally
        {
            IsCastStarting = false;
        }

        if (result is AirPlayCastResult.Started)
        {
            CastingDevice = renderer;
            IsCasting = true;
            return;
        }

        if (result is AirPlayCastResult.PairingInvalid)
        {
            _airPlayPairingService.Forget(renderer);
            if (SelectedRenderer == renderer) IsSelectedRendererUnpaired = true;
        }

        NotificationKind? kind = result switch
        {
            AirPlayCastResult.NothingToCast or AirPlayCastResult.NotLocalFile => NotificationKind.AirPlayCastNotLocalFile,
            AirPlayCastResult.FormatUnsupported => NotificationKind.AirPlayCastFormatUnsupported,
            AirPlayCastResult.FileUnreadable => NotificationKind.AirPlayCastFileUnreadable,
            AirPlayCastResult.ConnectionFailed => NotificationKind.AirPlayCastConnectionFailed,
            AirPlayCastResult.PairingInvalid => NotificationKind.AirPlayCastPairingInvalid,
            AirPlayCastResult.Failed => NotificationKind.AirPlayCastFailed,
            _ => null, // Cancelled: nothing to report.
        };
        if (kind is { } notificationKind)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(NotificationLevel.Error, notificationKind));
        }
    }

    /// <summary>
    /// An AirPlay cast can end without this flyout (the TV stopped it, or the
    /// connection dropped); return the flyout to its discovering state then.
    /// </summary>
    private void OnCastContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CastContext.ActiveRenderer) || _castContext.ActiveRenderer is not null) return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (IsCasting && CastingDevice is { Kind: RendererKind.AirPlay } && !_airPlayCastCoordinator.IsCasting)
            {
                IsCasting = false;
                CastingDevice = null;
            }
        });
    }

    partial void OnSelectedRendererChanged(Renderer? value)
    {
        IsSelectedRendererUnpaired = value is { Kind: RendererKind.AirPlay } && !_airPlayPairingService.IsPaired(value);
    }

    /// <summary>
    /// Pairs with the selected AirPlay receiver: the TV shows a PIN, the PIN
    /// dialog asks for it, and the result is reported as a notification. The
    /// pairing does not depend on the flyout staying open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPair))]
    private async Task PairAsync()
    {
        if (SelectedRenderer is not { Kind: RendererKind.AirPlay } renderer) return;
        IsPairing = true;
        AirPlayPairingResult result;
        try
        {
            result = await _airPlayPairingService.PairAsync(renderer, () => RequestPinOnUiThread(renderer.Name));
        }
        finally
        {
            IsPairing = false;
        }

        if (result is AirPlayPairingResult.Paired or AirPlayPairingResult.AlreadyPaired
            && SelectedRenderer == renderer)
        {
            IsSelectedRendererUnpaired = false;
        }

        NotificationMessage? notification = result switch
        {
            AirPlayPairingResult.Paired or AirPlayPairingResult.AlreadyPaired =>
                new(NotificationLevel.Success, NotificationKind.AirPlayPaired, title: renderer.Name),
            AirPlayPairingResult.PinRejected => new(NotificationLevel.Error, NotificationKind.AirPlayPinRejected),
            AirPlayPairingResult.ConnectionFailed =>
                new(NotificationLevel.Error, NotificationKind.AirPlayPairingConnectionFailed),
            AirPlayPairingResult.Failed => new(NotificationLevel.Error, NotificationKind.AirPlayPairingFailed),
            _ => null, // Cancelled by the user: nothing to report.
        };
        if (notification is not null)
        {
            WeakReferenceMessenger.Default.Send(notification);
        }
    }

    private bool CanPair() => !IsPairing && IsSelectedRendererUnpaired && SelectedRenderer is { IsAvailable: true };

    /// <summary>
    /// Runs on the pairing thread: shows the PIN dialog on the UI thread and
    /// returns its result, or <see langword="null"/> if the dialog cannot be shown.
    /// </summary>
    private Task<string?> RequestPinOnUiThread(string receiverName)
    {
        TaskCompletionSource<string?> pin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                pin.TrySetResult(await _airPlayPinDialogService.RequestPinAsync(receiverName));
            }
            catch (Exception e)
            {
                // For example, another dialog is already open; pairing then ends as cancelled.
                _logger.LogWarning("AirPlay PIN dialog failed: {ErrorType}", e.GetType().Name);
                pin.TrySetResult(null);
            }
        });
        if (!queued)
        {
            pin.TrySetResult(null);
        }

        return pin.Task;
    }

    [RelayCommand]
    private void StopCasting()
    {
        if (MediaPlayer == null) return;
        _logger.LogInformation("Stop casting.");
        if (_airPlayCastCoordinator.IsCasting)
        {
            // Swaps back to VLC, paused at the last position, before discovery restarts.
            _airPlayCastCoordinator.Stop();
            CastingDevice = null;
            IsCasting = false;
            StartDiscovering();
            return;
        }

        _castService.SetActiveRenderer(MediaPlayer, null);
        _castContext.ActiveRenderer = null;
        IsCasting = false;
        StartDiscovering();
    }

    private void RendererWatcherOnRendererLost(object? sender, RendererLostEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            Renderers.Remove(e.Renderer);
            if (SelectedRenderer == e.Renderer) SelectedRenderer = null;
        });
    }

    private void RendererWatcherOnRendererFound(object? sender, RendererFoundEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // A watcher stopped after queuing this event has made the renderer unavailable;
            // StopDiscovering has cleared the list by then, so adding it would leave a stale row.
            if (e.Renderer.IsAvailable && !Renderers.Contains(e.Renderer))
            {
                Renderers.Add(e.Renderer);
            }
        });
    }
}
