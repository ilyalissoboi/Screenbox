namespace Screenbox.Core.Models;

/// <summary>
/// The kind of device a <see cref="Renderer"/> is, for its icon.
/// </summary>
public enum RendererDeviceKind
{
    /// <summary>Not known; the icon follows whether the renderer plays video.</summary>
    Unknown,

    /// <summary>A TV or TV box, such as an Apple TV.</summary>
    Television,

    /// <summary>A speaker, such as a HomePod.</summary>
    Speaker,

    /// <summary>A laptop, such as a MacBook.</summary>
    Laptop,

    /// <summary>A desktop computer, such as an iMac or a Mac mini.</summary>
    Desktop,
}
