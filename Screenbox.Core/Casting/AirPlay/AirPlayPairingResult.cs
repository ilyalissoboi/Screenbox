namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// The outcome of pairing with an AirPlay receiver.
/// </summary>
public enum AirPlayPairingResult
{
    /// <summary>Paired; the credentials are saved.</summary>
    Paired,

    /// <summary>Credentials for this receiver were already saved; nothing changed.</summary>
    AlreadyPaired,

    /// <summary>The user cancelled the PIN dialog.</summary>
    Cancelled,

    /// <summary>The receiver did not accept the PIN, or the PIN came too late.</summary>
    PinRejected,

    /// <summary>The receiver could not be reached or stopped answering.</summary>
    ConnectionFailed,

    /// <summary>Any other failure, such as an unusable credential store.</summary>
    Failed,
}
