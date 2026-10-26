using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Relaties;

/// <summary>Namen van bedrijven vergelijken, ongeacht rechtsvorm, hoofdletters, accenten en leestekens.</summary>
public static partial class Namen
{
    private static readonly HashSet<string> Rechtsvormen = new(StringComparer.Ordinal)
    {
        "bv", "nv", "vof", "cv", "holding", "bvba", "gmbh", "ltd", "limited", "inc", "sa", "ag", "eenmanszaak", "stichting", "the", "de", "het",
    };

    /// <summary>De betekenisvolle woorden van een naam, bijv. "KPN B.V." → <c>kpn</c>.</summary>
    public static IReadOnlyList<string> Woorden(string? naam)
    {
        if (string.IsNullOrWhiteSpace(naam))
        {
            return [];
        }

        var zonderAccenten = new StringBuilder();
        foreach (var c in naam.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                zonderAccenten.Append(char.ToLowerInvariant(c));
            }
        }

        // "B.V." en "V.O.F." eerst samenvoegen tot één woord, daarna splitsen op alles wat geen letter of cijfer is.
        var tekst = zonderAccenten.ToString().Replace(".", string.Empty, StringComparison.Ordinal);
        return GeenLetterOfCijfer().Split(tekst)
            .Where(w => w.Length > 0 && !Rechtsvormen.Contains(w))
            .ToList();
    }

    public static string Normaliseer(string? naam) => string.Join(' ', Woorden(naam));

    /// <summary>
    /// Lijken twee namen op elkaar? Gelijk na normaliseren, of alle woorden van de kortste naam komen in de langste
    /// voor (bijv. "KPN" en "KPN Zakelijk B.V."), met minstens drie letters.
    /// </summary>
    public static bool LijktOp(string? a, string? b)
    {
        var x = Woorden(a);
        var y = Woorden(b);
        if (x.Count == 0 || y.Count == 0)
        {
            return false;
        }

        var (kort, lang) = x.Count <= y.Count ? (x, y) : (y, x);
        return kort.Sum(w => w.Length) >= 3 && kort.All(lang.Contains);
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex GeenLetterOfCijfer();
}
