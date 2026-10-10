using System;
using System.Threading.Tasks;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

/// <summary>
/// Pairs Screenbox with AirPlay receivers and reports which ones are paired.
/// Credentials are kept per receiver in the app's PasswordVault.
/// </summary>
public interface IAirPlayPairingService
{
    /// <summary>
    /// Whether local credentials exist for an AirPlay renderer. This does not
    /// prove the receiver still trusts them; a cast finds that out.
    /// </summary>
    bool IsPaired(Renderer renderer);

    /// <summary>
    /// Pairs with an AirPlay renderer by PIN. The receiver shows a PIN, then
    /// <paramref name="requestPin"/> is awaited for it. Runs off the calling
    /// thread and never throws for pairing failures.
    /// </summary>
    /// <param name="renderer">An AirPlay renderer with a discovered address.</param>
    /// <param name="requestPin">
    /// Called once, on a background thread, after the receiver shows its PIN;
    /// returns the PIN or <see langword="null"/> to cancel. It must marshal any
    /// UI work itself.
    /// </param>
    Task<AirPlayPairingResult> PairAsync(Renderer renderer, Func<Task<string?>> requestPin);
}
