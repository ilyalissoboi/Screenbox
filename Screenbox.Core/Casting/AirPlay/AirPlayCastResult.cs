namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// The outcome of starting an AirPlay cast.
/// </summary>
public enum AirPlayCastResult
{
    /// <summary>The TV is playing the item.</summary>
    Started,

    /// <summary>No item is loaded in the local player.</summary>
    NothingToCast,

    /// <summary>The item is not a file on this device (for example, a network stream).</summary>
    NotLocalFile,

    /// <summary>The remux cannot serve this container or codec.</summary>
    FormatUnsupported,

    /// <summary>The file's structure could not be read.</summary>
    FileUnreadable,

    /// <summary>No saved pairing, or the TV no longer accepts it.</summary>
    PairingInvalid,

    /// <summary>A network failure; often the TV cannot connect back on a Public network.</summary>
    ConnectionFailed,

    /// <summary>The start was cancelled by a stop.</summary>
    Cancelled,

    /// <summary>Any other failure.</summary>
    Failed,
}
