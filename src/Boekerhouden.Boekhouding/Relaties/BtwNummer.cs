using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Relaties;

/// <summary>Btw-identificatienummers (Nederlands: <c>NL</c> + 9 cijfers + <c>B</c> + 2 cijfers).</summary>
public static partial class BtwNummer
{
    public static string Normaliseer(string nummer)
    {
        ArgumentNullException.ThrowIfNull(nummer);
        return string.Concat(nummer.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    }

    /// <summary>
    /// Heeft het nummer de juiste vorm? Bij een Nederlands nummer ook de controle: de elfproef op de negen cijfers
    /// (oude nummers) of modulus 97 over het hele nummer (nummers sinds 2020).
    /// </summary>
    public static bool IsGeldig(string? nummer)
    {
        if (nummer is null)
        {
            return false;
        }

        var n = Normaliseer(nummer);
        if (!n.StartsWith("NL", StringComparison.Ordinal))
        {
            return BuitenlandsPatroon().IsMatch(n);
        }

        if (!NederlandsPatroon().IsMatch(n))
        {
            return false;
        }

        var cijfers = n[2..11];
        var elfproef = cijfers.Select((c, i) => (c - '0') * (i == 8 ? -1 : 9 - i)).Sum() % 11 == 0;
        var mod97 = Mod97("2321" + cijfers + "11" + n[12..]) == 1;
        return elfproef || mod97;
    }

    private static int Mod97(string cijfers) => cijfers.Aggregate(0, (rest, c) => ((rest * 10) + (c - '0')) % 97);

    [GeneratedRegex("^NL[0-9]{9}B[0-9]{2}$")]
    private static partial Regex NederlandsPatroon();

    [GeneratedRegex("^[A-Z]{2}[0-9A-Z]{2,13}$")]
    private static partial Regex BuitenlandsPatroon();
}
