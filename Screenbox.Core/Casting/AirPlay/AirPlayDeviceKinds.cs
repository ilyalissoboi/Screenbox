using System;
using System.Collections.Generic;
using Screenbox.Core.Models;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Maps an AirPlay receiver's advertised model identifier (for example
/// "AppleTV14,1" or "MacBookPro18,1") to the kind of device, for its icon.
/// </summary>
internal static class AirPlayDeviceKinds
{
    // Apple-silicon Macs from 2022 on advertise "MacNN,M" whether they are laptops or
    // desktops, so the name alone cannot tell them apart. These are the desktops
    // (Mac Studio, Mac mini, Mac Pro, iMac), from Apple's published identifiers; any
    // other "MacNN,M" is treated as a laptop, which most Macs are.
    private static readonly HashSet<string> DesktopMacModels = new(StringComparer.Ordinal)
    {
        "Mac13,1", "Mac13,2", // Mac Studio (M1 Max, M1 Ultra)
        "Mac14,3", "Mac14,12", // Mac mini (M2, M2 Pro)
        "Mac14,8", // Mac Pro (M2 Ultra)
        "Mac14,13", "Mac14,14", // Mac Studio (M2 Max, M2 Ultra)
        "Mac15,4", "Mac15,5", // iMac (M3)
        "Mac15,14", // Mac Studio (M3 Ultra)
        "Mac16,2", "Mac16,3", // iMac (M4)
        "Mac16,9", // Mac Studio (M4 Max)
        "Mac16,10", "Mac16,11", // Mac mini (M4, M4 Pro)
    };

    /// <summary>
    /// The device kind for <paramref name="model"/>: Apple TVs and other TVs,
    /// HomePods ("AudioAccessory"), laptops (MacBook names, and "MacNN,M" Macs not
    /// known as desktops) and desktops (iMac, Mac mini, Mac Pro, Mac Studio).
    /// Anything unrecognized is a television, because discovery lists only
    /// receivers that advertise AirPlay video.
    /// </summary>
    internal static RendererDeviceKind FromModel(string model)
    {
        if (model.StartsWith("AudioAccessory", StringComparison.Ordinal)) return RendererDeviceKind.Speaker;
        if (model.StartsWith("MacBook", StringComparison.Ordinal)) return RendererDeviceKind.Laptop;
        if (model.StartsWith("iMac", StringComparison.Ordinal) || model.StartsWith("Macmini", StringComparison.Ordinal) ||
            model.StartsWith("MacPro", StringComparison.Ordinal))
        {
            return RendererDeviceKind.Desktop;
        }

        if (model.StartsWith("Mac", StringComparison.Ordinal))
        {
            return DesktopMacModels.Contains(model) ? RendererDeviceKind.Desktop : RendererDeviceKind.Laptop;
        }

        return RendererDeviceKind.Television;
    }
}
