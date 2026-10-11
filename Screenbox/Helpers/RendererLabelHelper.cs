using Screenbox.Core.Models;
using Windows.UI.Xaml;

namespace Screenbox.Helpers;

/// <summary>
/// Localized labels for cast renderers, for <c>x:Bind</c> in the cast flyout.
/// </summary>
public static class RendererLabelHelper
{
    /// <summary>
    /// The localized product family of a renderer (for example "MacBook Pro"), or
    /// an empty string when it is not known.
    /// </summary>
    public static string GetDeviceFamilyName(RendererDeviceFamily family)
    {
        return family switch
        {
            RendererDeviceFamily.AppleTV => Strings.Resources.AirPlayFamilyAppleTV,
            RendererDeviceFamily.AppleTVHD => Strings.Resources.AirPlayFamilyAppleTVHD,
            RendererDeviceFamily.AppleTV4K => Strings.Resources.AirPlayFamilyAppleTV4K,
            RendererDeviceFamily.HomePod => Strings.Resources.AirPlayFamilyHomePod,
            RendererDeviceFamily.HomePodMini => Strings.Resources.AirPlayFamilyHomePodMini,
            RendererDeviceFamily.MacBook => Strings.Resources.AirPlayFamilyMacBook,
            RendererDeviceFamily.MacBookAir => Strings.Resources.AirPlayFamilyMacBookAir,
            RendererDeviceFamily.MacBookPro => Strings.Resources.AirPlayFamilyMacBookPro,
            RendererDeviceFamily.IMac => Strings.Resources.AirPlayFamilyIMac,
            RendererDeviceFamily.IMacPro => Strings.Resources.AirPlayFamilyIMacPro,
            RendererDeviceFamily.MacMini => Strings.Resources.AirPlayFamilyMacMini,
            RendererDeviceFamily.MacStudio => Strings.Resources.AirPlayFamilyMacStudio,
            RendererDeviceFamily.MacPro => Strings.Resources.AirPlayFamilyMacPro,
            RendererDeviceFamily.Mac => Strings.Resources.AirPlayFamilyMac,
            _ => string.Empty,
        };
    }

    /// <summary>Visible when the renderer's product family is known.</summary>
    public static Visibility GetDeviceFamilyVisibility(RendererDeviceFamily family)
    {
        return family is RendererDeviceFamily.Unknown ? Visibility.Collapsed : Visibility.Visible;
    }
}
