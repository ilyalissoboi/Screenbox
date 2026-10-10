using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Names the send-airplay2 credential profile of each AirPlay receiver, so every
/// Apple TV has its own pairing.
/// </summary>
/// <remarks>
/// A profile is 1-64 characters of lower-case letters, digits and <c>._-</c>,
/// starting with a letter or digit. The receiver id is unauthenticated discovery
/// data, but the stored credentials pin the receiver's key: a different device
/// answering under the same id fails authentication. Distinct ids must therefore
/// never share a profile, so only ids whose whole text is a recognized
/// hexadecimal format are shortened to their digits; any other id is hashed.
/// </remarks>
internal static partial class AirPlayProfiles
{
    private const string Prefix = "airplay-";
    private const int HashHexLength = 32;

    /// <summary>
    /// The profile for a receiver id. A MAC-style id ("AA:BB:CC:DD:EE:FF" or with
    /// dashes), a UUID-style id, or 1-56 plain hexadecimal digits becomes
    /// <c>airplay-</c> plus its hexadecimal digits in lower case
    /// ("airplay-aabbccddeeff"). Any other id becomes <c>airplay-</c> plus the
    /// first 32 hexadecimal digits of the SHA-256 of its UTF-8 text.
    /// </summary>
    internal static string ForReceiver(string receiverId)
    {
        if (MacStyleId().IsMatch(receiverId) || UuidStyleId().IsMatch(receiverId) ||
            PlainHexId().IsMatch(receiverId))
        {
            StringBuilder digits = new(receiverId.Length);
            foreach (char c in receiverId)
            {
                if (Uri.IsHexDigit(c))
                {
                    digits.Append(char.ToLowerInvariant(c));
                }
            }

            return Prefix + digits;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(receiverId));
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant()[..HashHexLength];
    }

    // Six hexadecimal pairs separated by colons or dashes.
    [GeneratedRegex("^[0-9A-Fa-f]{2}([:-][0-9A-Fa-f]{2}){5}$")]
    private static partial Regex MacStyleId();

    // 8-4-4-4-12 hexadecimal digits.
    [GeneratedRegex("^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")]
    private static partial Regex UuidStyleId();

    // At most 56 digits, so the profile stays within 64 characters.
    [GeneratedRegex("^[0-9A-Fa-f]{1,56}$")]
    private static partial Regex PlainHexId();
}
