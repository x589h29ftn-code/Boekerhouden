using System.Numerics;

namespace Boekerhouden.Boekhouding.Relaties;

/// <summary>IBAN's normaliseren en controleren (ISO 13616, modulus 97).</summary>
public static class Iban
{
    /// <summary>Zonder spaties en in hoofdletters, bijv. <c>NL91ABNA0417164300</c>.</summary>
    public static string Normaliseer(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        return string.Concat(iban.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    }

    public static bool IsGeldig(string? iban)
    {
        if (iban is null)
        {
            return false;
        }

        var n = Normaliseer(iban);
        if (n.Length is < 15 or > 34 || !char.IsAsciiLetter(n[0]) || !char.IsAsciiLetter(n[1]) || !char.IsAsciiDigit(n[2]) || !char.IsAsciiDigit(n[3]))
        {
            return false;
        }

        if (n.StartsWith("NL", StringComparison.Ordinal) && n.Length != 18)
        {
            return false;
        }

        var omgezet = string.Concat((n[4..] + n[..4]).Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return BigInteger.Parse(omgezet, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }

    /// <summary>In groepen van vier, bijv. <c>NL91 ABNA 0417 1643 00</c>.</summary>
    public static string Toon(string iban)
    {
        var n = Normaliseer(iban);
        return string.Join(' ', Enumerable.Range(0, (n.Length + 3) / 4).Select(i => n.Substring(i * 4, Math.Min(4, n.Length - (i * 4)))));
    }
}
