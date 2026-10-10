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
/// The player never changes or ends the cast itself; <see cref="AirPlayCastCoordinator"/>
/// owns the cast. When the cast ends on the receiver's side the player raises
/// <see cref="CastEnded"/> once. At the end of the media the coordinator then
/// calls <see cref="FinishAtEnd"/>, which raises <see cref="MediaEnded"/> for the
/// play queue. Setting another <see cref="PlaybackItem"/>, calling
/// <see cref="Close"/>, or setting <see cref="Position"/> once finished (repeat
/// one) raises <see cref="ItemRequested"/>: each item is a new cast.
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

    // The receiver's duration of the remuxed HLS differs from VLC's by container
    // timing (a fraction of a second). Reporting that as a change makes the seek
    // bar reset its length and chapters, so only a larger difference is taken.
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(1);

    /// <summary>Raised by <see cref="FinishAtEnd"/>, on the UI thread.</summary>
    public event TypedEventHandler<IMediaPlayer, EventArgs>? MediaEnded;

    // Not raised while casting: this player's item never changes (each item is a new
    // cast), and the inert members never change.
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
    /// Raised on the calling thread, which may be any thread, when Screenbox asks
    /// for another item (or none, or closes the player), or seeks once the media
    /// has finished (repeat one): the requested item and where to start it.
    /// </summary>
    internal event Action<AirPlayMediaPlayer, PlaybackItem?, TimeSpan>? ItemRequested;

    private readonly Cast _cast;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TimeSpan _position;
    private TimeSpan _naturalDuration;
    private MediaPlaybackState _playbackState = MediaPlaybackState.Opening;
    private bool _ended;
    private bool _finished;
    private PlaybackItem? _requestedItem;
    private bool _hasRequestedItem;
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
            if (IsFinished)
            {
                // The cast has ended; a seek now (repeat one) plays the item again as a new cast.
                ItemRequested?.Invoke(this, _item, target);
                return;
            }

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
    /// this cast; it raises <see cref="ItemRequested"/> for the coordinator.
    /// </summary>
    public PlaybackItem? PlaybackItem
    {
        get => _item;
        set
        {
            // The play queue sets the same item twice when it moves on (PlaySingle via
            // SetCurrentItem, then directly); one request per item is enough.
            lock (_gate)
            {
                if (value == _item || (_hasRequestedItem && value == _requestedItem)) return;
                _requestedItem = value;
                _hasRequestedItem = true;
            }

            ItemRequested?.Invoke(this, value, TimeSpan.Zero);
        }
    }

    /// <summary>Whether the media has finished (<see cref="FinishAtEnd"/>).</summary>
    internal bool IsFinished
    {
        get
        {
            lock (_gate)
            {
                return _finished;
            }
        }
    }

    public void Close()
    {
        ItemRequested?.Invoke(this, null, TimeSpan.Zero);
    }

    // Once finished the cast is gone: the play queue calls Play right after choosing the
    // next item, which then starts as its own cast, so these do nothing.
    public void Play()
    {
        if (IsFinished) return;
        SendCommand(nameof(Cast.Play), _cast.Play);
    }

    public void Pause()
    {
        if (IsFinished) return;
        SendCommand(nameof(Cast.Pause), _cast.Pause);
    }

    /// <summary>
    /// Marks the media as finished at its end and raises <see cref="MediaEnded"/>,
    /// so the play queue can choose what comes next. Call on the UI thread after
    /// <see cref="CastEnded"/> reported the end of the media.
    /// </summary>
    internal void FinishAtEnd()
    {
        MediaPlaybackState oldState;
        lock (_gate)
        {
            _finished = true;
            oldState = _playbackState;
            _playbackState = MediaPlaybackState.Paused;
        }

        SetPosition(NaturalDuration);
        if (oldState != MediaPlaybackState.Paused)
        {
            PlaybackStateChanged?.Invoke(this,
                new ValueChangedEventArgs<MediaPlaybackState>(MediaPlaybackState.Paused, oldState));
        }

        MediaEnded?.Invoke(this, EventArgs.Empty);
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
        // Subscribers read the state of a swapped-in player only on a change; report the
        // start position now rather than after the first status.
        TimeSpan position = Position;
        PositionChanged?.Invoke(this, new ValueChangedEventArgs<TimeSpan>(position, position));
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
            bool changed;
            lock (_gate)
            {
                oldDuration = _naturalDuration;
                changed = oldDuration <= TimeSpan.Zero || (duration - oldDuration).Duration() > DurationTolerance;
                if (changed) _naturalDuration = duration;
            }

            if (changed)
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
            // Loading appears only mid-playback (the cast starts once the TV plays), briefly
            // after each seek; reporting it would flip the play/pause button to "play".
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
