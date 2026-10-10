using System;
using Screenbox.Core.Models;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Maps an AirPlay receiver's advertised model identifier (for example
/// "AppleTV14,1" or "MacBookPro18,1") to the kind of device, for its icon.
/// </summary>
internal static class AirPlayDeviceKinds
{
    /// <summary>
    /// The device kind for <paramref name="model"/>. Apple TVs, HomePods
    /// ("AudioAccessory"), MacBooks and other Macs are recognized; anything else,
    /// such as a TV from another maker, is a television, because discovery lists
    /// only receivers that advertise AirPlay video.
    /// </summary>
    internal static RendererDeviceKind FromModel(string model)
    {
        if (model.StartsWith("AudioAccessory", StringComparison.Ordinal)) return RendererDeviceKind.Speaker;
        if (model.StartsWith("MacBook", StringComparison.Ordinal)) return RendererDeviceKind.Laptop;
        // Mac (Mac Studio, recent Mac mini and iMac identifiers), iMac, Macmini and MacPro.
        if (model.StartsWith("Mac", StringComparison.Ordinal) || model.StartsWith("iMac", StringComparison.Ordinal))
        {
            return RendererDeviceKind.Desktop;
        }

        return RendererDeviceKind.Television;
    }
}
