using LibVLCSharp.Shared;
using Screenbox.Core.Casting.AirPlay;

namespace Screenbox.Core.Models;

public sealed partial class Renderer
{
    public bool IsAvailable { get; private set; }

    /// <summary>The casting pipeline this renderer belongs to.</summary>
    public RendererKind Kind { get; }

    /// <summary>The kind of device, for its icon; unknown for LibVLC renderers.</summary>
    public RendererDeviceKind DeviceKind { get; }

    public string Name { get; }

    public string Type { get; }

    public string? IconUri { get; }

    public bool CanRenderVideo { get; }

    public bool CanRenderAudio { get; }

    /// <summary>The LibVLC renderer item; null for AirPlay renderers and once disposed.</summary>
    internal RendererItem? Target => IsAvailable ? _item : null;

    /// <summary>The latest discovery data of an AirPlay renderer; null for LibVLC renderers.</summary>
    internal AirPlayReceiverInfo? AirPlayReceiver { get; private set; }

    private readonly RendererItem? _item;

    internal Renderer(RendererItem item)
    {
        _item = item;
        Kind = RendererKind.Chromecast;
        Name = item.Name;
        Type = item.Type;
        IconUri = item.IconUri;
        CanRenderVideo = item.CanRenderVideo;
        CanRenderAudio = item.CanRenderAudio;
        IsAvailable = true;
    }

    internal Renderer(AirPlayReceiverInfo receiver)
    {
        Kind = RendererKind.AirPlay;
        DeviceKind = AirPlayDeviceKinds.FromModel(receiver.Model);
        Name = receiver.Name;
        Type = "airplay";
        // Discovery lists only receivers that advertise AirPlay video, which carries audio too.
        CanRenderVideo = true;
        CanRenderAudio = true;
        AirPlayReceiver = receiver;
        IsAvailable = true;
    }

    /// <summary>Keeps an AirPlay renderer's address and port current between scans.</summary>
    internal void UpdateAirPlayReceiver(AirPlayReceiverInfo receiver)
    {
        if (Kind is RendererKind.AirPlay)
        {
            AirPlayReceiver = receiver;
        }
    }

    internal void Dispose()
    {
        IsAvailable = false;
        _item?.Dispose();
    }

    public override string ToString()
    {
        return $"{Name}, {Type}";
    }
}
