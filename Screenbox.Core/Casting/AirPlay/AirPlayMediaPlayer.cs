using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Events;
using Screenbox.Core.Playback;
using SendAirPlay2;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using CastPlaybackState = SendAirPlay2.PlaybackState;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// The <see cref="IMediaPlayer"/> that stands in for VLC while an item is cast
/// with AirPlay, so the seek bar, play/pause and other controls follow the TV.
/// </summary>
/// <remarks>
/// <para>
/// Position, state and duration come from a background loop over the cast's
/// status, and its events are raised on that thread, as VLC raises its own;
/// subscribers marshal to the UI thread. Play, pause and seek become cast
/// commands, sent off the calling thread. What a cast cannot do is inert:
/// volume, mute, rate, tracks, chapters, subtitles and frame stepping.
/// </para>
/// <para>
/// The player never ends the cast itself. When the cast ends on the receiver's
/// side it raises <see cref="CastEnded"/> once; setting another
/// <see cref="PlaybackItem"/> or calling <see cref="Close"/> raises
/// <see cref="EndRequested"/>. <see cref="AirPlayCastCoordinator"/> owns the
/// cast and handles both.
/// </para>
/// </remarks>
public sealed partial class AirPlayMediaPlayer : IMediaPlayer
{
    // How long a status wait may block before the position is read again. The
    // status is local state, so a short interval keeps the seek bar smooth.
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(250);

    // After a seek the receiver keeps reporting the old position for a moment.
    // Reported positions farther than SeekTolerance from the target are ignored
    // until one comes close or SeekSettleTime passes, so the seek bar does not
    // jump back.
    private static readonly TimeSpan SeekSettleTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(2);

    // Not raised while casting: the item does not change, end of media ends the
    // cast, and the inert members never change.
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaEnded { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaFailed { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaOpened { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? IsMutedChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? VolumeChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<PlaybackItem?>>? PlaybackItemChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingProgressChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingStarted { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? BufferingEnded { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? NaturalDurationChanged;
    public event TypedEventHandler<IMediaPlayer, EventArgs>? NaturalVideoSizeChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, EventArgs>? CanSeekChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<TimeSpan>>? PositionChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<ChapterCue?>>? ChapterChanged { add { } remove { } }
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<MediaPlaybackState>>? PlaybackStateChanged;
    public event TypedEventHandler<IMediaPlayer, ValueChangedEventArgs<double>>? PlaybackRateChanged { add { } remove { } }

    /// <summary>Raised once, on the status thread, when the cast ends on the receiver's side.</summary>
    internal event Action<AirPlayMediaPlayer, CastStatus>? CastEnded;

    /// <summary>
    /// Raised on the calling thread when Screenbox asks to play another item (or
    /// none) or closes the player; the argument is the requested item.
    /// </summary>
    internal event Action<AirPlayMediaPlayer, PlaybackItem?>? EndRequested;

    private readonly Cast _cast;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TimeSpan _position;
    private TimeSpan _naturalDuration;
    private MediaPlaybackState _playbackState = MediaPlaybackState.Opening;
    private bool _ended;
    private TimeSpan? _seekTarget;
    private DateTimeOffset _seekDeadline;
    private readonly PlaybackItem _item;

    /// <param name="cast">A started cast; the coordinator keeps ownership.</param>
    /// <param name="item">The item being cast.</param>
    /// <param name="startPosition">Where the cast started.</param>
    /// <param name="naturalDuration">VLC's duration of the item, until the receiver reports one.</param>
    /// <param name="naturalVideoWidth">The item's video width, kept for the player layout.</param>
    /// <param name="naturalVideoHeight">The item's video height, kept for the player layout.</param>
    /// <param name="volume">The local volume, reported unchanged while casting.</param>
    /// <param name="isMuted">The local mute state, reported unchanged while casting.</param>
    /// <param name="logger">Receives fixed-field diagnostics only.</param>
    internal AirPlayMediaPlayer(Cast cast, PlaybackItem item, TimeSpan startPosition, TimeSpan naturalDuration,
        uint naturalVideoWidth, uint naturalVideoHeight, double volume, bool isMuted, ILogger logger)
    {
        _cast = cast;
        _logger = logger;
        _item = item;
        _position = startPosition;
        // The library seeks to the start position as the cast starts, and the receiver
        // briefly reports the position before that seek, so treat it like a seek.
        _seekTarget = startPosition;
        _seekDeadline = DateTimeOffset.UtcNow + SeekSettleTime;
        _naturalDuration = naturalDuration;
        NaturalVideoWidth = naturalVideoWidth;
        NaturalVideoHeight = naturalVideoHeight;
        Volume = volume;
        IsMuted = isMuted;
    }

    public bool CanPause => true;

    public bool CanSeek => true;

    /// <summary>The local mute state; the TV's own volume is not controlled.</summary>
    public bool IsMuted { get; set; }

    public bool IsLoopingEnabled { get; set; }

    public DeviceInformation? AudioDevice { get; set; }

    public MediaPlaybackState PlaybackState
    {
        get
        {
            lock (_gate)
            {
                return _playbackState;
            }
        }
    }

    public double BufferingProgress => 1;

    public uint NaturalVideoHeight { get; }

    public uint NaturalVideoWidth { get; }

    /// <summary>
    /// The receiver's position. Setting it seeks the TV; the new value is reported
    /// right away and corrected by the next status.
    /// </summary>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _position;
            }
        }
        set
        {
            TimeSpan target = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            lock (_gate)
            {
                _seekTarget = target;
                _seekDeadline = DateTimeOffset.UtcNow + SeekSettleTime;
            }

            SetPosition(target);
            SendCommand(nameof(Cast.Seek), () => _cast.Seek(target.TotalSeconds));
        }
    }

    public TimeSpan NaturalDuration
    {
        get
        {
            lock (_gate)
            {
                return _naturalDuration;
            }
        }
    }

    public ChapterCue? Chapter => null;

    /// <summary>Always 1; the receiver plays at normal speed.</summary>
    public double PlaybackRate
    {
        get => 1;
        set { }
    }

    public Rect NormalizedSourceRect { get; set; } = new(0, 0, 1, 1);

    /// <summary>The local volume; the TV's own volume is not controlled.</summary>
    public double Volume { get; set; }

    /// <summary>
    /// The item being cast. Setting a different item (or none) does not change
    /// the cast; it raises <see cref="EndRequested"/> for the coordinator.
    /// </summary>
    public PlaybackItem? PlaybackItem
    {
        get => _item;
        set
        {
            if (value == _item) return;
            EndRequested?.Invoke(this, value);
        }
    }

    public void Close()
    {
        EndRequested?.Invoke(this, null);
    }

    public void Play()
    {
        SendCommand(nameof(Cast.Play), _cast.Play);
    }

    public void Pause()
    {
        SendCommand(nameof(Cast.Pause), _cast.Pause);
    }

    public void StepForwardOneFrame()
    {
    }

    public void StepBackwardOneFrame()
    {
    }

    public void AddSubtitle(IStorageFile file, bool select = true)
    {
    }

    /// <summary>Starts following the cast's status on a background thread.</summary>
    internal void StartWatching()
    {
        CancellationToken token = _stop.Token;
        Task.Factory.StartNew(() => Watch(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Stops following the cast. No events are raised afterwards, except by a
    /// status already being applied. The coordinator stops and disposes the cast.
    /// </summary>
    internal void StopWatching()
    {
        _stop.Cancel();
    }

    private void Watch(CancellationToken token)
    {
        try
        {
            CastStatus status = _cast.GetStatus();
            Apply(status);
            while (!token.IsCancellationRequested)
            {
                status = _cast.WaitForChange(status.PlaybackState, StatusPollInterval);
                if (token.IsCancellationRequested) return;
                Apply(status);
                if (status.Phase is CastPhase.Ended)
                {
                    lock (_gate)
                    {
                        if (_ended) return;
                        _ended = true;
                    }

                    CastEnded?.Invoke(this, status);
                    return;
                }

                if (status.Phase is CastPhase.Stopped) return;
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or SendAirPlay2Exception)
        {
            // The coordinator stopped and disposed the cast while a status wait was pending.
            _logger.LogDebug("AirPlay status loop ended: {ErrorType}", e.GetType().Name);
        }
    }

    private void Apply(CastStatus status)
    {
        if (status.DurationSeconds is double seconds and > 0)
        {
            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            TimeSpan oldDuration;
            lock (_gate)
            {
                oldDuration = _naturalDuration;
                _naturalDuration = duration;
            }

            // The receiver's duration can differ from VLC's by a few frames; ignore that jitter.
            if (Math.Abs((duration - oldDuration).TotalMilliseconds) > 50)
            {
                NaturalDurationChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(duration, oldDuration));
            }
        }

        if (status.PositionSeconds is { } positionSeconds && !IsStaleAfterSeek(TimeSpan.FromSeconds(positionSeconds)))
        {
            SetPosition(TimeSpan.FromSeconds(positionSeconds));
        }

        MediaPlaybackState? state = status.PlaybackState switch
        {
            CastPlaybackState.Playing => MediaPlaybackState.Playing,
            CastPlaybackState.Paused => MediaPlaybackState.Paused,
            CastPlaybackState.Loading => MediaPlaybackState.Buffering,
            // Idle, stopped and ended are followed by the end of the cast.
            _ => null,
        };
        if (state is { } newState)
        {
            MediaPlaybackState oldState;
            lock (_gate)
            {
                oldState = _playbackState;
                _playbackState = newState;
            }

            if (newState != oldState)
            {
                PlaybackStateChanged?.Invoke(this, new ValueChangedEventArgs<MediaPlaybackState>(newState, oldState));
            }
        }
    }

    /// <summary>
    /// Whether a reported position predates the last seek: far from its target
    /// while the seek is settling. A position near the target ends the settling.
    /// </summary>
    private bool IsStaleAfterSeek(TimeSpan reported)
    {
        lock (_gate)
        {
            if (_seekTarget is not { } target) return false;
            if (DateTimeOffset.UtcNow > _seekDeadline || (reported - target).Duration() <= SeekTolerance)
            {
                _seekTarget = null;
                return false;
            }

            return true;
        }
    }

    private void SetPosition(TimeSpan position)
    {
        TimeSpan oldPosition;
        lock (_gate)
        {
            oldPosition = _position;
            _position = position;
        }

        if (position != oldPosition)
        {
            PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(position, oldPosition));
        }
    }

    /// <summary>Sends a cast command off the calling thread; a failure is logged, not thrown.</summary>
    private void SendCommand(string name, Action command)
    {
        Task.Run(() =>
        {
            try
            {
                command();
            }
            catch (Exception e) when (e is SendAirPlay2Exception or ObjectDisposedException)
            {
                // The receiver rejected the command, or the cast is ending.
                _logger.LogInformation("AirPlay {Command} failed: {ErrorType} {Result}", name, e.GetType().Name,
                    (e as SendAirPlay2Exception)?.Result);
            }
        });
    }
}
