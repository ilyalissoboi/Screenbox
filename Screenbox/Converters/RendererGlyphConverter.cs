using System;
using Screenbox.Core.Models;
using Windows.UI.Xaml.Data;

namespace Screenbox.Converters;

internal sealed partial class RendererGlyphConverter : IValueConverter
{
    // Segoe Fluent Icons / Segoe MDL2 Assets.
    private const string TelevisionGlyph = ""; // TVMonitor
    private const string SpeakerGlyph = ""; // Speakers
    private const string LaptopGlyph = ""; // DeviceLaptopNoPic
    private const string DesktopGlyph = ""; // PC1

    public object? Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) return null;
        Renderer renderer = (Renderer)value;
        return renderer.DeviceKind switch
        {
            RendererDeviceKind.Television => TelevisionGlyph,
            RendererDeviceKind.Speaker => SpeakerGlyph,
            RendererDeviceKind.Laptop => LaptopGlyph,
            RendererDeviceKind.Desktop => DesktopGlyph,
            // LibVLC renderers (Chromecast) report only whether they play video.
            _ => renderer.CanRenderVideo ? TelevisionGlyph : SpeakerGlyph,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
