using System.Globalization;
using System.Security.Cryptography;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Rekent een code uit zoals een authenticator-app (RFC 6238: HMAC-SHA1, 30 seconden, 6 cijfers).</summary>
internal static class Totp
{
    private const string Alfabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Code(string sleutel, DateTimeOffset? op = null)
    {
        var teller = (op ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var bytes = BitConverter.GetBytes(teller);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

#pragma warning disable CA5350 // TOTP (RFC 6238) schrijft HMAC-SHA1 voor; zo werken alle authenticator-apps.
        var hash = HMACSHA1.HashData(Base32(sleutel), bytes);
#pragma warning restore CA5350
        var plek = hash[^1] & 0x0f;
        var getal = ((hash[plek] & 0x7f) << 24) | (hash[plek + 1] << 16) | (hash[plek + 2] << 8) | hash[plek + 3];
        return (getal % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string tekst)
    {
        var schoon = tekst.ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=');
        var uit = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in schoon)
        {
            buffer = (buffer << 5) | Alfabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                uit.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. uit];
    }
}
