namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// One AirPlay receiver from a discovery scan. All fields are unauthenticated
/// multicast DNS data: pairing, not discovery, pins a receiver's identity.
/// </summary>
/// <param name="Id">Advertised identity, stable across scans; the tracking key.</param>
/// <param name="Name">Friendly name. Network-supplied text: display it as plain text and never log it.</param>
/// <param name="Address">Numeric receiver address, or empty when the receiver has no castable address.</param>
/// <param name="Port">AirPlay control port for <paramref name="Address"/>.</param>
/// <param name="Model">Advertised model identifier (for example "AppleTV14,1"), or empty.</param>
internal sealed record AirPlayReceiverInfo(string Id, string Name, string Address, ushort Port, string Model = "");
