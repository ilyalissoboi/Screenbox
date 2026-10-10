using System;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Services;

public sealed class CastService : ICastService
{
    private readonly ILogger<AirPlayReceiverWatcher> _airPlayLogger;

    public CastService(ILogger<AirPlayReceiverWatcher> airPlayLogger)
    {
        _airPlayLogger = airPlayLogger;
    }

    public RendererWatcher CreateRendererWatcher(IMediaPlayer player)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer)
            throw new NotSupportedException("RendererWatcher only supports VlcMediaPlayer.");

        return new RendererWatcher(vlcMediaPlayer);
    }

    public AirPlayReceiverWatcher CreateAirPlayReceiverWatcher()
    {
        return new AirPlayReceiverWatcher(_airPlayLogger);
    }

    public bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer) return false;
        // An AirPlay renderer has no LibVLC target; passing null would end Chromecast output instead.
        if (renderer is { Kind: RendererKind.AirPlay }) return false;
        return vlcMediaPlayer.VlcPlayer.SetRenderer(renderer?.Target);
    }
}
