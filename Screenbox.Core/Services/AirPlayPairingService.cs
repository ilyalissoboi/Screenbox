using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Models;
using SendAirPlay2;

namespace Screenbox.Core.Services;

/// <inheritdoc cref="IAirPlayPairingService"/>
/// <remarks>Logs result categories only, never PINs, names, addresses or profiles.</remarks>
public sealed class AirPlayPairingService : IAirPlayPairingService
{
    private readonly ILogger<AirPlayPairingService> _logger;

    public AirPlayPairingService(ILogger<AirPlayPairingService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsPaired(Renderer renderer)
    {
        if (renderer.AirPlayReceiver is not { } receiver)
        {
            return false;
        }

        try
        {
            return new PasswordVaultCredentialStore().HasProfile(AirPlayProfiles.ForReceiver(receiver.Id));
        }
        catch (Exception e)
        {
            // An unreadable vault means pairing is offered again, which then reports the failure.
            _logger.LogWarning("AirPlay credential lookup failed: {ErrorType}", e.GetType().Name);
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<AirPlayPairingResult> PairAsync(Renderer renderer, Func<Task<string?>> requestPin)
    {
        if (renderer.AirPlayReceiver is not { } receiver)
        {
            return Task.FromResult(AirPlayPairingResult.Failed);
        }

        PairOptions options = new()
        {
            ReceiverAddress = receiver.Address,
            ReceiverPort = receiver.Port,
            Profile = AirPlayProfiles.ForReceiver(receiver.Id),
            CredentialStore = new PasswordVaultCredentialStore(),
        };

        // Pairing.Pair blocks on network work and on the PIN reader, so it runs on a pool thread.
        return Task.Run(() =>
        {
            AirPlayPairingResult result;
            try
            {
                Pairing.Pair(options, (char[] digits, out int length) => ReadPin(requestPin, digits, out length));
                result = AirPlayPairingResult.Paired;
            }
            catch (SendAirPlay2Exception e)
            {
                result = e.Result switch
                {
                    ResultCode.ProfileExists => AirPlayPairingResult.AlreadyPaired,
                    ResultCode.Cancelled => AirPlayPairingResult.Cancelled,
                    ResultCode.Authentication or ResultCode.PinTimeout => AirPlayPairingResult.PinRejected,
                    ResultCode.Connection or ResultCode.ReceiverRejected => AirPlayPairingResult.ConnectionFailed,
                    _ => AirPlayPairingResult.Failed,
                };
                _logger.LogInformation("AirPlay pairing ended: {Result} ({ResultCode})", result, e.Result);
                return result;
            }
            catch (Exception e)
            {
                _logger.LogWarning("AirPlay pairing failed: {ErrorType}", e.GetType().Name);
                return AirPlayPairingResult.Failed;
            }

            _logger.LogInformation("AirPlay pairing ended: {Result}", result);
            return result;
        });
    }

    /// <summary>
    /// The binding's PIN reader: waits on this pairing thread for the PIN and
    /// copies it into the binding's buffer, which the binding wipes afterwards.
    /// </summary>
    private static bool ReadPin(Func<Task<string?>> requestPin, char[] digits, out int length)
    {
        string? pin = requestPin().GetAwaiter().GetResult()?.Trim();
        if (pin is null)
        {
            length = 0;
            return false;
        }

        int count = Math.Min(pin.Length, digits.Length);
        pin.CopyTo(0, digits, 0, count);
        // A PIN longer than the buffer is passed on as too long, so the library rejects it.
        length = pin.Length;
        return true;
    }
}
