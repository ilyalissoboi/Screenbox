namespace Screenbox.Core.Models;

/// <summary>
/// The product family of a <see cref="Renderer"/>, shown under its name. The view
/// maps each value to a localized resource.
/// </summary>
public enum RendererDeviceFamily
{
    /// <summary>Not known (LibVLC renderers and receivers from other makers).</summary>
    Unknown,
    AppleTV,
    AppleTVHD,
    AppleTV4K,
    HomePod,
    HomePodMini,
    MacBook,
    MacBookAir,
    MacBookPro,
    IMac,
    IMacPro,
    MacMini,
    MacStudio,
    MacPro,

    /// <summary>A Mac whose identifier is not in the known table.</summary>
    Mac,
}
