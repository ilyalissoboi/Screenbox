using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
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
/// while it runs, and swaps back to VLC, paused at the last position, however it
/// ends (user decision 3 of docs/AIRPLAY_INTEGRATION.md).
/// </summary>
/// <remarks>
/// Public members are called on the UI thread, and every swap happens there.
/// The cast is stopped and disposed off the UI thread, because a stop waits for
/// the session's teardown. Logs carry result categories only, never names,
/// addresses, paths or profiles.
/// </remarks>
public sealed class AirPlayCastCoordinator
{
    // Starting this close to the end would finish at once; start from the beginning instead.
    private static readonly TimeSpan NearEnd = TimeSpan.FromSeconds(1);

    private readonly PlayerContext _playerContext;
    private readonly CastContext _castContext;
    private readonly IPlayerService _playerService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<AirPlayCastCoordinator> _logger;
    private AirPlayMediaPlayer? _player;
    private Cast? _cast;
    private VlcMediaPlayer? _vlcPlayer;
    private Cast? _startingCast;
    private bool _swapping;

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

    /// <summary>Whether an AirPlay cast is active.</summary>
    public bool IsCasting => _player is not null;

    /// <summary>
    /// Casts the item VLC has loaded to an AirPlay renderer from its current
    /// position, through the library's HLS remux (user decision 7). VLC is paused
    /// first; the players are swapped only once the TV plays, and on failure VLC
    /// resumes as it was. Call on the UI thread.
    /// </summary>
    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    public async Task<AirPlayCastResult> StartAsync(Renderer renderer)
    {
        if (_player is not null || _startingCast is not null) return AirPlayCastResult.Failed;
        if (renderer.AirPlayReceiver is not { } receiver) return AirPlayCastResult.Failed;
        if (_playerContext.MediaPlayer is not VlcMediaPlayer vlcPlayer || vlcPlayer.PlaybackItem is not { } item)
        {
            return AirPlayCastResult.NothingToCast;
        }

        // Only files on this device: the library serves the media itself, and network streams cannot be remuxed.
        if (item.OriginalSource is not StorageFile file) return AirPlayCastResult.NotLocalFile;

        bool wasPlaying = vlcPlayer.PlaybackState is MediaPlaybackState.Playing;
        TimeSpan duration = vlcPlayer.NaturalDuration;
        TimeSpan position = vlcPlayer.Position;
        if (duration > TimeSpan.Zero && position >= duration - NearEnd) position = TimeSpan.Zero;
        vlcPlayer.Pause();

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
        AirPlayCastResult result = AirPlayCastResult.Failed;
        try
        {
            StorageFileMediaSource source = await StorageFileMediaSource.OpenAsync(file);
            cast = Cast.Create(options, source);
            _startingCast = cast;
            // Start blocks until the TV plays: the remux reads the file's index, the TV may need
            // waking (up to 10 s more), and the start position is applied by a seek.
            Cast started = cast;
            await Task.Run(started.Start);
            result = AirPlayCastResult.Started;
        }
        catch (SendAirPlay2Exception e)
        {
            result = e.Result switch
            {
                ResultCode.Cancelled => AirPlayCastResult.Cancelled,
                ResultCode.MediaUnsupported => AirPlayCastResult.FormatUnsupported,
                ResultCode.MediaMalformed => AirPlayCastResult.FileUnreadable,
                ResultCode.Authentication or ResultCode.ProfileNotFound => AirPlayCastResult.PairingInvalid,
                ResultCode.Connection => AirPlayCastResult.ConnectionFailed,
                _ => AirPlayCastResult.Failed,
            };
            _logger.LogInformation("AirPlay cast start failed: {Result} ({ResultCode})", result, e.Result);
        }
        catch (Exception e)
        {
            // For example, the file is no longer accessible.
            _logger.LogWarning("AirPlay cast start failed: {ErrorType}", e.GetType().Name);
        }
        finally
        {
            _startingCast = null;
        }

        if (result is not AirPlayCastResult.Started || cast is null)
        {
            if (cast is not null) DisposeInBackground(cast);
            if (wasPlaying && _playerContext.MediaPlayer == vlcPlayer) vlcPlayer.Play();
            return result;
        }

        if (_playerContext.MediaPlayer != vlcPlayer)
        {
            // The local player was replaced while the cast started; keep the new one.
            DisposeInBackground(cast);
            return AirPlayCastResult.Failed;
        }

        AirPlayMediaPlayer player = new(cast, item, position, duration, vlcPlayer.NaturalVideoWidth,
            vlcPlayer.NaturalVideoHeight, vlcPlayer.Volume, vlcPlayer.IsMuted, _logger);
        player.CastEnded += OnCastEnded;
        player.EndRequested += OnEndRequested;
        _player = player;
        _cast = cast;
        _vlcPlayer = vlcPlayer;
        _castContext.ActiveRenderer = renderer;
        Swap(player);
        player.StartWatching();
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
            return;
        }

        End(lastPosition: null, notify: false);
    }

    private void OnCastEnded(AirPlayMediaPlayer player, CastStatus status)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (player != _player) return;
            _logger.LogInformation("AirPlay cast ended: {EndReason}", status.EndReason);
            // At the end of the media, VLC is left paused at the end; otherwise at the last position.
            TimeSpan? end = status.EndReason is EndReason.MediaEnd ? player.NaturalDuration : null;
            End(end, notify: status.EndReason is not EndReason.MediaEnd);
        });
    }

    private void OnEndRequested(AirPlayMediaPlayer player, PlaybackItem? requested)
    {
        if (player != _player) return;
        VlcMediaPlayer? vlcPlayer = _vlcPlayer;
        End(lastPosition: null, notify: false);
        // Another item (or none) was asked for while casting: VLC takes it over locally.
        if (vlcPlayer is not null && _playerContext.MediaPlayer == vlcPlayer)
        {
            vlcPlayer.PlaybackItem = requested;
        }
    }

    /// <summary>Swaps back to VLC, paused at the last cast position, and disposes the cast.</summary>
    private void End(TimeSpan? lastPosition, bool notify)
    {
        if (_player is not { } player || _cast is not { } cast) return;
        player.CastEnded -= OnCastEnded;
        player.EndRequested -= OnEndRequested;
        player.StopWatching();
        TimeSpan position = lastPosition ?? player.Position;
        VlcMediaPlayer? vlcPlayer = _vlcPlayer;
        _player = null;
        _cast = null;
        _vlcPlayer = null;
        _castContext.ActiveRenderer = null;
        if (vlcPlayer is not null && _playerContext.MediaPlayer == player)
        {
            Swap(vlcPlayer);
            // VLC stayed paused during the cast (user decision 3: do not resume locally).
            vlcPlayer.Position = position;
        }

        DisposeInBackground(cast);
        if (notify)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(NotificationLevel.Info,
                NotificationKind.AirPlayCastEnded));
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
        End(lastPosition: null, notify: false);
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
