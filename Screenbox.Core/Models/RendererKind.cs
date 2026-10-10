namespace Screenbox.Core.Models;

/// <summary>
/// The casting pipeline a <see cref="Renderer"/> belongs to.
/// </summary>
public enum RendererKind
{
    /// <summary>A LibVLC renderer (Chromecast); VLC itself streams to it.</summary>
    Chromecast,

    /// <summary>An AirPlay receiver discovered through send-airplay2.</summary>
    AirPlay,
}
