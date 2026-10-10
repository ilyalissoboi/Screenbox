using System;
using System.Security.Cryptography;
using System.Text;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Names the send-airplay2 credential profile of each AirPlay receiver, so every
/// Apple TV has its own pairing.
/// </summary>
/// <remarks>
/// A profile is 1-64 characters of lower-case letters, digits and <c>._-</c>,
/// starting with a letter or digit. The receiver id is unauthenticated discovery
/// data, but the stored credentials pin the receiver's key: a different device
/// answering under the same id fails authentication.
/// </remarks>
internal static class AirPlayProfiles
{
    private const string Prefix = "airplay-";
    private const int MaxProfileLength = 64;
    private const int HashHexLength = 32;

    /// <summary>
    /// The profile for a receiver id: <c>airplay-</c> plus the id's hexadecimal
    /// digits in lower case (a MAC-style id "AA:BB:CC:DD:EE:FF" becomes
    /// "airplay-aabbccddeeff"). An id without hexadecimal digits, or with too many,
    /// uses the first 32 hexadecimal digits of its SHA-256 instead.
    /// </summary>
    internal static string ForReceiver(string receiverId)
    {
        StringBuilder digits = new(receiverId.Length);
        foreach (char c in receiverId)
        {
            if (Uri.IsHexDigit(c))
            {
                digits.Append(char.ToLowerInvariant(c));
            }
        }

        if (digits.Length == 0 || Prefix.Length + digits.Length > MaxProfileLength)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(receiverId));
            return Prefix + Convert.ToHexString(hash).ToLowerInvariant()[..HashHexLength];
        }

        return Prefix + digits;
    }
}
