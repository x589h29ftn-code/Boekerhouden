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
        ["kapitaal"] = "aandelenkapitaal",
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
        ["rabobank"] = "bank",
        ["ing"] = "bank",
        ["abn"] = "bank",
        ["amro"] = "bank",
        ["asn"] = "bank",
        ["sns"] = "bank",
        ["knab"] = "bank",
        ["bunq"] = "bank",
        ["triodos"] = "bank",
        ["regiobank"] = "bank",
        ["bankrekening"] = "bank",
    };

    private readonly List<(RgsCode Code, string[] Kort, string[] Lang, string[] Ouder)> kandidaten;

    /// <param name="kandidaten">De codes waaruit gekozen mag worden (bijv. de selectie voor de rechtsvorm, niveau 3 en 4).</param>
    public Voorsteller(RgsSchema schema, IEnumerable<RgsCode> kandidaten)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(kandidaten);
        this.kandidaten =
        [
            .. kandidaten
                .Where(c => c.Niveau is 3 or 4 && !c.Inactief && !IsFunctioneleIndeling(c.Code))
                .OrderBy(c => c.Volgorde)
                .Select(c => (c, Woorden(c.Omschrijving).Distinct().ToArray(), Woorden(c.OmschrijvingLang).Distinct().ToArray(),
                    Woorden(schema.Zoek(c.Ouder)?.Omschrijving ?? string.Empty))),
        ];
    }

    /// <param name="zijde">'B' of 'W' als bekend is of de rekening op de balans of in de W&amp;V hoort; anders null.</param>
    public Voorstel? StelVoor(string omschrijving, char? zijde = null)
    {
        var woorden = Woorden(omschrijving);
        if (woorden.Length == 0)
        {
            return null;
        }

        Voorstel? beste = null;
        foreach (var (code, kort, lang, ouder) in kandidaten)
        {
            if (zijde is { } z && code.Code[0] != z)
            {
                continue;
            }

            // Per woord de beste treffer: in de korte omschrijving telt volledig, in de lange (die vaak de context
            // van de ouder herhaalt) iets minder, en in de omschrijving van de ouder half.
            var score = woorden.Sum(woord => Math.Max(
                Math.Max(Beste(woord, kort), 0.8 * Beste(woord, lang)),
                0.5 * Beste(woord, ouder)));

            // Een code met veel woorden die niet in de omschrijving voorkomen ("Vooruitbetaalde autokosten" bij
            // "Autokosten") past minder goed; een specifieke code (niveau 4) wint bij gelijke stand.
            var ongebruikt = kort.Length == 0 ? 0 : kort.Count(w => woorden.All(v => Gelijkenis(v, w) < 0.6)) / (double)kort.Length;
            score = (score / woorden.Length) - (0.02 * kort.Length) - (0.1 * ongebruikt) + (code.Niveau == 4 ? 0.03 : 0);
            if (beste is null || score > beste.Score)
            {
                beste = new Voorstel(code, score);
            }
        }

        return beste is { Score: >= Drempel } ? beste : null;
    }

    /// <summary>
    /// Codes van de functionele W&amp;V-indeling (verkoopkosten <c>WVkf</c>, algemene beheerskosten <c>WAkf</c>) stellen we
    /// niet voor: het MKB presenteert de W&amp;V categoriaal. Neerzetten op die posten kan wel.
    /// </summary>
    private static bool IsFunctioneleIndeling(string code) =>
        code.StartsWith("WVkf", StringComparison.Ordinal) || code.StartsWith("WAkf", StringComparison.Ordinal);

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

    private static double Beste(string woord, string[] woorden) => woorden.Length == 0 ? 0 : woorden.Max(w => Gelijkenis(woord, w));

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

        return Math.Min(a.Length, b.Length) >= 4 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) ? 0.6 : 0;
    }
}
