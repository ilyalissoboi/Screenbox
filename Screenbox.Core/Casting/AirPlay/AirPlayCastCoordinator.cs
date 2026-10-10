using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Events;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using SendAirPlay2;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Owns the AirPlay cast, if any: starts it from the item VLC has loaded, swaps
/// <see cref="PlayerContext.MediaPlayer"/> to an <see cref="AirPlayMediaPlayer"/>
/// while it runs, casts each item the play queue moves to as a new cast on the
/// same receiver (user decision 5), and swaps back to VLC, paused at the last
/// position, however casting ends (user decision 3).
/// </summary>
/// <remarks>
/// <para>
/// Public members are called on the UI thread, and every swap happens there;
/// requests from the AirPlay player (which can arrive on any thread, such as
/// the system media transport controls') are moved to the UI thread first. Casts
/// are stopped and disposed off the UI thread, because a stop waits for the
/// session's teardown. Logs carry result categories only, never names,
/// addresses, paths or profiles.
/// </para>
/// <para>
/// At the end of an item the AirPlay player stays the active player and raises
/// <see cref="IMediaPlayer.MediaEnded"/>. The play queue reacts on a later
/// dispatcher turn: it sets the next item (a new cast), seeks to the start of
/// the same item (repeat one: a new cast), or does nothing (end of the queue).
/// The coordinator queues its own check behind that decision, and ends casting
/// at the end of the item if nothing was requested.
/// </para>
/// </remarks>
public sealed class AirPlayCastCoordinator
{
    // Starting this close to the end would finish at once; start from the beginning instead.
    private static readonly TimeSpan NearEnd = TimeSpan.FromSeconds(1);

    // How long VLC may take to open an item it is handed back after a cast moved on.
    private static readonly TimeSpan LocalOpenTimeout = TimeSpan.FromSeconds(10);

    private readonly PlayerContext _playerContext;
    private readonly CastContext _castContext;
    private readonly IPlayerService _playerService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<AirPlayCastCoordinator> _logger;
    private AirPlayMediaPlayer? _player;
    private Cast? _cast;
    private VlcMediaPlayer? _vlcPlayer;
    private Renderer? _renderer;
    private Cast? _startingCast;
    private bool _swapping;

    // A move to another item is in progress; requests made meanwhile wait here, and only
    // the latest is cast (UI thread only).
    private bool _castingNext;
    private (PlaybackItem Item, TimeSpan Position)? _queuedRequest;

    // Set on the requesting thread, before the request reaches the UI thread, so the
    // end-of-item check can see that the play queue chose a next item.
    private volatile bool _itemRequestPending;

    public AirPlayCastCoordinator(PlayerContext playerContext, CastContext castContext, IPlayerService playerService,
        ILogger<AirPlayCastCoordinator> logger)
    {
        _playerContext = playerContext;
        _castContext = castContext;
        _playerService = playerService;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _playerContext.PropertyChanged += OnPlayerContextPropertyChanged;
    }

    /// <summary>Whether an AirPlay cast is active (including between two queue items).</summary>
    public bool IsCasting => _player is not null;

    /// <summary>
    /// Casts the item VLC has loaded to an AirPlay renderer from its current
    /// position, through the library's HLS remux (user decision 7). VLC is paused
    /// first; the players are swapped only once the TV plays, and on failure VLC
    /// resumes as it was. Call on the UI thread.
    /// </summary>
    public async Task<AirPlayCastResult> StartAsync(Renderer renderer)
    {
        if (_player is not null || _startingCast is not null) return AirPlayCastResult.Failed;
        if (_playerContext.MediaPlayer is not VlcMediaPlayer vlcPlayer || vlcPlayer.PlaybackItem is not { } item)
        {
            return AirPlayCastResult.NothingToCast;
        }

        bool wasPlaying = vlcPlayer.PlaybackState is MediaPlaybackState.Playing;
        TimeSpan duration = vlcPlayer.NaturalDuration;
        TimeSpan position = vlcPlayer.Position;
        if (duration > TimeSpan.Zero && position >= duration - NearEnd) position = TimeSpan.Zero;
        vlcPlayer.Pause();

        (AirPlayCastResult result, Cast? cast) = await StartCastAsync(renderer, item, position);
        if (result is not AirPlayCastResult.Started || cast is null)
        {
            if (wasPlaying && _playerContext.MediaPlayer == vlcPlayer) vlcPlayer.Play();
            return result;
        }

        if (_playerContext.MediaPlayer != vlcPlayer || vlcPlayer.PlaybackItem != item)
        {
            // The local player was replaced, or another item was chosen, while the cast
            // started: the cast is of media no longer wanted, so keep the local choice.
            DisposeInBackground(cast);
            return AirPlayCastResult.Cancelled;
        }

        _vlcPlayer = vlcPlayer;
        _renderer = renderer;
        _castContext.ActiveRenderer = renderer;
        Activate(new AirPlayMediaPlayer(vlcPlayer, cast, item, position, duration, vlcPlayer.NaturalVideoWidth,
            vlcPlayer.NaturalVideoHeight, vlcPlayer.Volume, vlcPlayer.IsMuted, _logger), cast);
        _logger.LogInformation("AirPlay cast started");
        return AirPlayCastResult.Started;
    }

    /// <summary>
    /// Stops the active cast, or cancels one that is starting, and returns to VLC
    /// paused at the last position. Call on the UI thread.
    /// </summary>
    public void Stop()
    {
        if (_startingCast is { } starting)
        {
            // Cast.Stop from another thread cancels a pending Start, which then fails as Cancelled.
            Task.Run(starting.Stop);
            if (_player is null) return;
        }

        End(lastPosition: null, notify: null);
    }

    /// <summary>
    /// Opens the item's file and starts a cast of it on <paramref name="renderer"/>
    /// at <paramref name="position"/>. A cancelled or failed start disposes the cast.
    /// </summary>
    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    private async Task<(AirPlayCastResult Result, Cast? Cast)> StartCastAsync(Renderer renderer, PlaybackItem item,
        TimeSpan position)
    {
        if (renderer.AirPlayReceiver is not { } receiver) return (AirPlayCastResult.Failed, null);

        // Only files on this device: the library serves the media itself, and network streams cannot be remuxed.
        if (item.OriginalSource is not StorageFile file) return (AirPlayCastResult.NotLocalFile, null);

        CastOptions options = new()
        {
            ReceiverAddress = receiver.Address,
            ReceiverPort = receiver.Port,
            Profile = AirPlayProfiles.ForReceiver(receiver.Id),
            CredentialStore = new PasswordVaultCredentialStore(),
            Delivery = CastDelivery.HlsRemux,
            StartPositionSeconds = position.TotalSeconds,
        };

        Cast? cast = null;
        try
        {
            StorageFileMediaSource source = await StorageFileMediaSource.OpenAsync(file);
            cast = Cast.Create(options, source);
            _startingCast = cast;
            // Start blocks until the TV plays: the remux reads the file's index, the TV may need
            // waking (up to 10 s more), and the start position is applied by a seek.
            Cast started = cast;
            await Task.Run(started.Start);
            return (AirPlayCastResult.Started, cast);
        }
        catch (SendAirPlay2Exception e)
        {
            AirPlayCastResult result = e.Result switch
            {
                ResultCode.Cancelled => AirPlayCastResult.Cancelled,
                ResultCode.MediaUnsupported => AirPlayCastResult.FormatUnsupported,
                ResultCode.MediaMalformed => AirPlayCastResult.FileUnreadable,
                ResultCode.Authentication or ResultCode.ProfileNotFound => AirPlayCastResult.PairingInvalid,
                ResultCode.Connection => AirPlayCastResult.ConnectionFailed,
                _ => AirPlayCastResult.Failed,
            };
            _logger.LogInformation("AirPlay cast start failed: {Result} ({ResultCode})", result, e.Result);
            if (cast is not null) DisposeInBackground(cast);
            return (result, null);
        }
        catch (Exception e)
        {
            // For example, the file is no longer accessible.
            _logger.LogWarning("AirPlay cast start failed: {ErrorType}", e.GetType().Name);
            if (cast is not null) DisposeInBackground(cast);
            return (AirPlayCastResult.Failed, null);
        }
        finally
        {
            _startingCast = null;
        }
    }

    /// <summary>Makes <paramref name="player"/> the active player for <paramref name="cast"/>.</summary>
    private void Activate(AirPlayMediaPlayer player, Cast cast)
    {
        player.CastEnded += OnCastEnded;
        player.ItemRequested += OnItemRequested;
        _player = player;
        _cast = cast;
        _itemRequestPending = false;
        Swap(player);
        player.StartWatching();
    }

    /// <summary>
    /// Detaches the active player and disposes its cast; the player stays swapped
    /// in. While moving to another item it keeps taking requests
    /// (<paramref name="keepRequests"/>): it stays the active player for the
    /// seconds the next cast takes to start, and Next, Stop or another item chosen
    /// then must not be lost.
    /// </summary>
    private AirPlayMediaPlayer? Retire(bool keepRequests = false)
    {
        if (_player is not { } player) return null;
        player.CastEnded -= OnCastEnded;
        if (!keepRequests) player.ItemRequested -= OnItemRequested;
        player.StopWatching();
        if (_cast is { } cast) DisposeInBackground(cast);
        _cast = null;
        return player;
    }

    private void OnCastEnded(AirPlayMediaPlayer player, CastStatus status)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (player != _player) return;
            _logger.LogInformation("AirPlay cast ended: {EndReason}", status.EndReason);
            if (status.EndReason is not EndReason.MediaEnd)
            {
                End(lastPosition: null, notify: NotificationKind.AirPlayCastEnded);
                return;
            }

            // Let the play queue choose what comes next (it decides on a later dispatcher
            // turn), then end casting at the end of this item if it chose nothing.
            _itemRequestPending = false;
            player.FinishAtEnd();
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (player == _player && !_itemRequestPending)
                {
                    End(lastPosition: player.NaturalDuration, notify: null);
                }
            });
        });
    }

    /// <summary>Any thread: the active player was asked for another item, none, or a replay.</summary>
    private void OnItemRequested(AirPlayMediaPlayer player, PlaybackItem? item, TimeSpan position)
    {
        _itemRequestPending = true;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (player != _player) return;
            if (item is null)
            {
                // Stop (for example from the system media controls): end casting, and VLC drops the item too.
                VlcMediaPlayer? vlcPlayer = _vlcPlayer;
                End(lastPosition: null, notify: null);
                if (vlcPlayer is not null && _playerContext.MediaPlayer == vlcPlayer) vlcPlayer.PlaybackItem = null;
                return;
            }

            if (_castingNext)
            {
                _queuedRequest = (item, position);
                return;
            }

            _ = CastNextAsync(item, position);
        });
    }

    /// <summary>
    /// Casts <paramref name="item"/> as a new cast on the same receiver, replacing
    /// the current one: the next queue item, a replay, or an item chosen while
    /// casting. The old player stays active until the new cast plays. If the item
    /// cannot be cast, casting ends and VLC takes the item over, paused at its start.
    /// </summary>
    private async Task CastNextAsync(PlaybackItem item, TimeSpan position)
    {
        _castingNext = true;
        try
        {
            while (true)
            {
                await CastOneNextAsync(item, position);
                if (_queuedRequest is not { } next || _player is null) break;
                _queuedRequest = null;
                (item, position) = next;
            }
        }
        finally
        {
            _castingNext = false;
            _queuedRequest = null;
        }
    }

    private async Task CastOneNextAsync(PlaybackItem item, TimeSpan position)
    {
        if (_renderer is not { } renderer || Retire(keepRequests: true) is not { } previous) return;
        _logger.LogInformation("AirPlay cast moves to another item");
        (AirPlayCastResult result, Cast? cast) = await StartCastAsync(renderer, item, position);

        if (previous != _player)
        {
            // Casting ended while the cast started.
            if (cast is not null) DisposeInBackground(cast);
            return;
        }

        if (_queuedRequest is not null)
        {
            // A newer request arrived while this cast started; it supersedes this one.
            if (cast is not null) DisposeInBackground(cast);
            return;
        }

        if (result is not AirPlayCastResult.Started || cast is null)
        {
            End(lastPosition: TimeSpan.Zero, notify: null, localItem: item);
            if (result is not AirPlayCastResult.Cancelled)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(NotificationLevel.Info,
                    NotificationKind.AirPlayNextItemNotCast));
            }

            _logger.LogInformation("AirPlay casting ended: the next item could not be cast ({Result})", result);
            return;
        }

        previous.ItemRequested -= OnItemRequested;
        Activate(new AirPlayMediaPlayer(previous.LocalPlayer, cast, item, position, item.Duration ?? TimeSpan.Zero,
            previous.NaturalVideoWidth, previous.NaturalVideoHeight, previous.Volume, previous.IsMuted, _logger), cast);
    }

    /// <summary>
    /// Ends casting: swaps back to VLC paused at <paramref name="lastPosition"/> (by
    /// default the last cast position) on the item last cast, or on
    /// <paramref name="localItem"/>, and disposes the cast.
    /// </summary>
    private void End(TimeSpan? lastPosition, NotificationKind? notify, PlaybackItem? localItem = null)
    {
        if (_player is not { } player) return;
        Retire();
        TimeSpan position = lastPosition ?? player.Position;
        PlaybackItem? item = localItem ?? player.PlaybackItem;
        VlcMediaPlayer? vlcPlayer = _vlcPlayer;
        _player = null;
        _vlcPlayer = null;
        _renderer = null;
        _castContext.ActiveRenderer = null;
        if (vlcPlayer is not null && _playerContext.MediaPlayer == player)
        {
            Swap(vlcPlayer);
            if (item is not null && vlcPlayer.PlaybackItem != item)
            {
                // Casting moved on from the item VLC had; VLC takes over the last cast item.
                _ = OpenPausedAsync(vlcPlayer, item, position);
            }
            else
            {
                // VLC stayed paused during the cast (user decision 3: do not resume locally).
                vlcPlayer.Position = position;
            }
        }

        if (notify is { } kind)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(NotificationLevel.Info, kind));
        }
    }

    /// <summary>
    /// Loads <paramref name="item"/> into VLC and leaves it paused at
    /// <paramref name="position"/>. VLC can only seek an opened item, so it is
    /// played muted until it opens, then paused, moved and unmuted.
    /// </summary>
    private async Task OpenPausedAsync(VlcMediaPlayer vlcPlayer, PlaybackItem item, TimeSpan position)
    {
        bool wasMuted = vlcPlayer.IsMuted;
        TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(IMediaPlayer sender, ValueChangedEventArgs<MediaPlaybackState> args)
        {
            // Muting before the item opens may not reach its audio output; repeat it as it opens.
            sender.IsMuted = true;
            if (args.NewValue is MediaPlaybackState.Playing) opened.TrySetResult();
        }

        vlcPlayer.PlaybackStateChanged += OnStateChanged;
        try
        {
            vlcPlayer.PlaybackItem = item;
            vlcPlayer.IsMuted = true;
            vlcPlayer.Play();
            await opened.Task.WaitAsync(LocalOpenTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("VLC did not open the item handed back from AirPlay in time");
        }
        finally
        {
            vlcPlayer.PlaybackStateChanged -= OnStateChanged;
        }

        try
        {
            // Another item may have been chosen meanwhile; it plays as chosen, only unmuted.
            if (vlcPlayer.PlaybackItem != item) return;
            vlcPlayer.Pause();
            if (position > TimeSpan.Zero) vlcPlayer.Position = position;
        }
        finally
        {
            vlcPlayer.IsMuted = wasMuted;
        }
    }

    private void Swap(IMediaPlayer player)
    {
        _swapping = true;
        try
        {
            _playerContext.MediaPlayer = player;
        }
        finally
        {
            _swapping = false;
        }
    }

    /// <summary>
    /// The local player was replaced while casting (the video view was recreated):
    /// end the cast without swapping back, and dispose the VLC player this
    /// coordinator was holding, which nothing else references any more.
    /// </summary>
    private void OnPlayerContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_swapping || e.PropertyName != nameof(PlayerContext.MediaPlayer)) return;
        if (_player is null || _playerContext.MediaPlayer == _player) return;
        VlcMediaPlayer? orphan = _vlcPlayer;
        _vlcPlayer = null;
        End(lastPosition: null, notify: null);
        if (orphan is not null)
        {
            Task.Run(() => _playerService.DisposePlayer(orphan));
        }
    }

    private void DisposeInBackground(Cast cast)
    {
        Task.Run(() =>
        {
            try
            {
                cast.Stop();
            }
            finally
            {
                cast.Dispose();
            }
        });
    }
}
