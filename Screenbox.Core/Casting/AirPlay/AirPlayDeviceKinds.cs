using System;
using System.Collections.Generic;
using System.Globalization;
using Screenbox.Core.Models;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Describes an AirPlay receiver from its advertised model identifier (for
/// example "AppleTV14,1" or "MacBookPro18,1"): the kind of device, for its icon,
/// and its product family, shown under the receiver's own name.
/// </summary>
/// <remarks>
/// Families are Apple's product names, which Apple does not translate, so they
/// are not localized. Receivers from other makers get no family.
/// </remarks>
internal static class AirPlayDeviceKinds
{
    private const string MacBookAir = "MacBook Air";
    private const string MacBookPro = "MacBook Pro";
    private const string IMac = "iMac";
    private const string MacMini = "Mac mini";
    private const string MacStudio = "Mac Studio";
    private const string MacPro = "Mac Pro";

    // Apple-silicon Macs from 2022 on advertise "MacNN,M" for laptops and desktops
    // alike, so the name alone tells neither the family nor the kind. These are
    // Apple's published identifiers; any other "MacNN,M" is a "Mac" laptop, which
    // most Macs are.
    private static readonly Dictionary<string, (RendererDeviceKind Kind, string Family)> AppleSiliconMacs =
        new(StringComparer.Ordinal)
        {
            ["Mac13,1"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac13,2"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac14,2"] = (RendererDeviceKind.Laptop, MacBookAir),
            ["Mac14,3"] = (RendererDeviceKind.Desktop, MacMini),
            ["Mac14,5"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac14,6"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac14,7"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac14,8"] = (RendererDeviceKind.Desktop, MacPro),
            ["Mac14,9"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac14,10"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac14,12"] = (RendererDeviceKind.Desktop, MacMini),
            ["Mac14,13"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac14,14"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac14,15"] = (RendererDeviceKind.Laptop, MacBookAir),
            ["Mac15,3"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,4"] = (RendererDeviceKind.Desktop, IMac),
            ["Mac15,5"] = (RendererDeviceKind.Desktop, IMac),
            ["Mac15,6"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,7"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,8"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,9"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,10"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,11"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac15,12"] = (RendererDeviceKind.Laptop, MacBookAir),
            ["Mac15,13"] = (RendererDeviceKind.Laptop, MacBookAir),
            ["Mac15,14"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac16,1"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac16,2"] = (RendererDeviceKind.Desktop, IMac),
            ["Mac16,3"] = (RendererDeviceKind.Desktop, IMac),
            ["Mac16,5"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac16,6"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac16,7"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac16,8"] = (RendererDeviceKind.Laptop, MacBookPro),
            ["Mac16,9"] = (RendererDeviceKind.Desktop, MacStudio),
            ["Mac16,10"] = (RendererDeviceKind.Desktop, MacMini),
            ["Mac16,11"] = (RendererDeviceKind.Desktop, MacMini),
            ["Mac16,12"] = (RendererDeviceKind.Laptop, MacBookAir),
            ["Mac16,13"] = (RendererDeviceKind.Laptop, MacBookAir),
        };

    /// <summary>The device kind for <paramref name="model"/>; see <see cref="Identify"/>.</summary>
    internal static RendererDeviceKind FromModel(string model) => Identify(model).Kind;

    /// <summary>
    /// The device kind and product family for <paramref name="model"/>. Apple TVs,
    /// HomePods ("AudioAccessory") and Macs are recognized. Anything else is a
    /// television without a family, because discovery lists only receivers that
    /// advertise AirPlay video.
    /// </summary>
    internal static (RendererDeviceKind Kind, string? Family) Identify(string model)
    {
        if (model.StartsWith("AppleTV", StringComparison.Ordinal))
        {
            // AppleTV5,3 is the Apple TV HD; AppleTV6 and later are 4K models; earlier ones
            // are plain Apple TVs.
            int generation = MajorVersion(model, "AppleTV".Length);
            string family = generation >= 6 ? "Apple TV 4K" : generation == 5 ? "Apple TV HD" : "Apple TV";
            return (RendererDeviceKind.Television, family);
        }

        if (model.StartsWith("AudioAccessory", StringComparison.Ordinal))
        {
            // AudioAccessory5 is the HomePod mini; 1 and 6 are the HomePod.
            bool isMini = MajorVersion(model, "AudioAccessory".Length) == 5;
            return (RendererDeviceKind.Speaker, isMini ? "HomePod mini" : "HomePod");
        }

        if (model.StartsWith("MacBookAir", StringComparison.Ordinal)) return (RendererDeviceKind.Laptop, MacBookAir);
        if (model.StartsWith("MacBookPro", StringComparison.Ordinal)) return (RendererDeviceKind.Laptop, MacBookPro);
        if (model.StartsWith("MacBook", StringComparison.Ordinal)) return (RendererDeviceKind.Laptop, "MacBook");
        if (model.StartsWith("iMacPro", StringComparison.Ordinal)) return (RendererDeviceKind.Desktop, "iMac Pro");
        if (model.StartsWith("iMac", StringComparison.Ordinal)) return (RendererDeviceKind.Desktop, IMac);
        if (model.StartsWith("Macmini", StringComparison.Ordinal)) return (RendererDeviceKind.Desktop, MacMini);
        if (model.StartsWith("MacPro", StringComparison.Ordinal)) return (RendererDeviceKind.Desktop, MacPro);
        if (model.StartsWith("Mac", StringComparison.Ordinal))
        {
            return AppleSiliconMacs.TryGetValue(model, out var mac) ? mac : (RendererDeviceKind.Laptop, "Mac");
        }

        return (RendererDeviceKind.Television, null);
    }

    /// <summary>The number before the comma in "NameNN,M", from <paramref name="start"/>; 0 if none.</summary>
    private static int MajorVersion(string model, int start)
    {
        int comma = model.IndexOf(',', start);
        string digits = comma < 0 ? model[start..] : model[start..comma];
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
