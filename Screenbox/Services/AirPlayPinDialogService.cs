using System.Threading.Tasks;
using Screenbox.Core.Services;
using Screenbox.Dialogs;

namespace Screenbox.Services;

/// <summary>
/// Shows <see cref="AirPlayPinDialog"/> for AirPlay pairing.
/// </summary>
public sealed class AirPlayPinDialogService : IAirPlayPinDialogService
{
    /// <inheritdoc/>
    public Task<string?> RequestPinAsync(string receiverName)
    {
        return new AirPlayPinDialog(receiverName).GetPinAsync();
    }
}
