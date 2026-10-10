using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Services;

public interface ICastService
{
    /// <summary>
    /// Create a new renderer watcher for the specified media player
    /// </summary>
    RendererWatcher CreateRendererWatcher(IMediaPlayer player);

    /// <summary>
    /// Create a new watcher for AirPlay receivers, which are discovered by the
    /// send-airplay2 library rather than LibVLC
    /// </summary>
    AirPlayReceiverWatcher CreateAirPlayReceiverWatcher();

    /// <summary>
    /// Set the active renderer for the media player. Only LibVLC (Chromecast)
    /// renderers can be set; returns false for an AirPlay renderer.
    /// </summary>
    bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer);
}
