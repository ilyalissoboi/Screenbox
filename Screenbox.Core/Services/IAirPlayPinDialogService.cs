using System.Threading.Tasks;

namespace Screenbox.Core.Services;

/// <summary>
/// Asks the user for the PIN an AirPlay receiver shows while pairing.
/// Implemented in the app project, which owns the dialog.
/// </summary>
public interface IAirPlayPinDialogService
{
    /// <summary>
    /// Shows the PIN dialog for <paramref name="receiverName"/>. Call on the UI thread.
    /// </summary>
    /// <param name="receiverName">The receiver's advertised name; network-supplied text, shown as plain text.</param>
    /// <returns>The 4-8 digit PIN, or <see langword="null"/> when the user cancels.</returns>
    Task<string?> RequestPinAsync(string receiverName);
}
