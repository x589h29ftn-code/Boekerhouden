using System.Globalization;
using System.Text;
using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Domain.Rubricering;

/// <summary>Een voorstel voor een koppeling. Wordt nooit automatisch toegepast: de gebruiker bevestigt.</summary>
public sealed record Voorstel(RgsCode Code, double Score);

/// <summary>
/// Stelt een RGS-code voor op basis van de omschrijving van een grootboekrekening: woorden vergelijken met de
/// omschrijving van de code en van zijn ouder, met een kleine lijst vaktermen ("aanschaf" ≈ "verkrijgingsprijs").
/// </summary>
public sealed class Voorsteller
{
    public const double Drempel = 0.6;

    private static readonly HashSet<string> Stopwoorden =
    [
        "en", "van", "de", "het", "een", "op", "in", "voor", "met", "te", "uit", "aan", "of", "ivm", "inzake", "bv", "nv", "t", "b", "v",
    ];

    private static readonly Dictionary<string, string> Vaktermen = new(StringComparer.Ordinal)
    {
        ["aanschaf"] = "verkrijgings",
        ["aanschafwaarde"] = "verkrijgings",
        ["aanschafprijs"] = "verkrijgings",
        ["afschr"] = "afschrijvingen",
        ["afs"] = "afschrijvingen",
        ["afschrijving"] = "afschrijvingen",
        ["cum"] = "cumulatieve",
        ["btw"] = "omzetbelasting",
        ["auto"] = "automobielen",
        ["bedrijfsauto"] = "automobielen",
        ["vervoermiddelen"] = "transportmiddelen",
        ["debiteuren"] = "handelsdebiteuren",
        ["crediteuren"] = "handelscrediteuren",
        ["brutolonen"] = "lonen",
        ["loon"] = "lonen",
        ["salarissen"] = "lonen",
        ["verzekeringen"] = "assurantiepremies",
        ["verzekering"] = "assurantiepremies",
        ["vpb"] = "vennootschapsbelasting",
        ["rc"] = "courant",
        ["telefoon"] = "telefoonkosten",
    };

    private readonly List<(RgsCode Code, string[] Woorden, string[] Ouder)> kandidaten;

    /// <param name="kandidaten">De codes waaruit gekozen mag worden (bijv. de selectie voor de rechtsvorm, niveau 3 en 4).</param>
    public Voorsteller(RgsSchema schema, IEnumerable<RgsCode> kandidaten)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(kandidaten);
        this.kandidaten =
        [
            .. kandidaten
                .Where(c => c.Niveau is 3 or 4 && !c.Inactief)
                .Select(c => (c, Woorden(c.Omschrijving + " " + c.OmschrijvingLang).Distinct().ToArray(),
                    Woorden(schema.Zoek(c.Ouder)?.Omschrijving ?? string.Empty))),
        ];
    }

    public Voorstel? StelVoor(string omschrijving)
    {
        var woorden = Woorden(omschrijving);
        if (woorden.Length == 0)
        {
            return null;
        }

        Voorstel? beste = null;
        foreach (var (code, eigen, ouder) in kandidaten)
        {
            var score = 0.0;
            foreach (var woord in woorden)
            {
                var opEigen = eigen.Length == 0 ? 0 : eigen.Max(w => Gelijkenis(woord, w));
                var opOuder = ouder.Length == 0 ? 0 : 0.5 * ouder.Max(w => Gelijkenis(woord, w));
                score += Math.Max(opEigen, opOuder);
            }

            // Korter en specifieker (niveau 4) wint bij gelijke woorden.
            score = (score / woorden.Length) - (0.02 * eigen.Length) + (code.Niveau == 4 ? 0.03 : 0);
            if (beste is null || score > beste.Score)
            {
                beste = new Voorstel(code, score);
            }
        }

        return beste is { Score: >= Drempel } ? beste : null;
    }

    /// <summary>Lijken twee omschrijvingen op elkaar? (Dezelfde woorden, of de ene bevat de andere.)</summary>
    public static bool LijktOp(string a, string b)
    {
        var x = Woorden(a).ToHashSet();
        var y = Woorden(b).ToHashSet();
        if (x.Count == 0 || y.Count == 0)
        {
            return false;
        }

        var gedeeld = x.Count(w => y.Any(v => Gelijkenis(w, v) >= 0.8));
        return gedeeld >= Math.Min(x.Count, y.Count) * 0.5;
    }

    public static string[] Woorden(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var zonderAccenten = new StringBuilder();
        foreach (var teken in tekst.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(teken) != UnicodeCategory.NonSpacingMark)
            {
                zonderAccenten.Append(char.IsLetterOrDigit(teken) ? teken : ' ');
            }
        }

        return
        [
            .. zonderAccenten.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !Stopwoorden.Contains(w) && !w.All(char.IsDigit))
                .Select(w => Vaktermen.GetValueOrDefault(w, w)),
        ];
    }

    private static double Gelijkenis(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }

        if (a.Length >= 4 && b.Length >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)))
        {
            return 0.8;
        }

        return a.Length >= 5 && b.Length >= 5 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) ? 0.6 : 0;
    }
}
