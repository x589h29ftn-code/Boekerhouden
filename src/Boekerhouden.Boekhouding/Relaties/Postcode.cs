using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Relaties;

/// <summary>Nederlandse postcodes vinden en vergelijken, om een ander adres van een leverancier op te merken.</summary>
public static partial class Postcode
{
    /// <summary>Zonder spatie en in hoofdletters, bijv. <c>1234AB</c>.</summary>
    public static string Normaliseer(string postcode)
    {
        ArgumentNullException.ThrowIfNull(postcode);
        return string.Concat(postcode.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    }

    /// <summary>Met een spatie, bijv. <c>1234 AB</c>.</summary>
    public static string Toon(string postcode)
    {
        var n = Normaliseer(postcode);
        return n.Length == 6 ? $"{n[..4]} {n[4..]}" : n;
    }

    /// <summary>Alle postcodes in een tekst, genormaliseerd en zonder dubbele, in volgorde van voorkomen.</summary>
    public static IReadOnlyList<string> Zoek(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        return [.. Patroon().Matches(tekst)
            .Select(m => m.Groups[1].Value + m.Groups[2].Value)
            .Where(p => p[4..] is not ("SA" or "SD" or "SS"))
            .Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"(?<![\d.,])\b([1-9]\d{3}) ?([A-Z]{2})\b(?![.\-])")]
    private static partial Regex Patroon();
}
