using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>
/// Een verkoopfactuur uit een ander pakket (PDF, e-factuur of scan) omzetten in een verkoopfactuur van de administratie:
/// de klant zoeken, de regels per btw-tarief opstellen en nagaan of hij vanzelf geboekt kan worden. Het nummer van het
/// andere pakket blijft het factuurnummer.
/// </summary>
public static class Verkoopimport
{
    /// <summary>
    /// De klant van de factuur: bij een e-factuur de afnemer (btw-nummer, KvK, naam), anders wat de tekstanalyse vond
    /// (de eigen naam en het eigen btw-nummer tellen daar niet mee, dus wat overblijft is meestal de klant).
    /// </summary>
    public static Herkendeleverancier? Klant(HerkendeFactuur factuur, IEnumerable<Relatie> klanten)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(klanten);
        var zoek = factuur.Afnemer is { } a
            ? new HerkendeFactuur(factuur.Bron, Naam: a.Naam, BtwNummer: a.BtwNummer, KvkNummer: a.KvkNummer)
            : factuur with { Iban = null };
        return Leverancierherkenning.Zoek(zoek, klanten);
    }

    /// <summary>De naam van de klant zoals op de factuur (om een nieuwe klant voor te stellen).</summary>
    public static string? Klantnaam(HerkendeFactuur factuur) => factuur?.Afnemer?.Naam ?? factuur?.Naam;

    /// <summary>
    /// De regels: bij een e-factuur de regels van de factuur, anders één regel per btw-tarief (de grondslag), of één regel
    /// met het bedrag exclusief btw. Verlegd wordt <c>VEU</c> bij een klant uit een ander EU-land, anders <c>VVL</c>.
    /// </summary>
    public static IReadOnlyList<Verkoopregel> Regels(HerkendeFactuur factuur, IReadOnlyList<BtwCode> codes, string omzetrekening, string? klantland = null)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(codes);
        var buitenland = klantland is { Length: 2 } land && !land.Equals("NL", StringComparison.OrdinalIgnoreCase) && BtwNummer.IsEuLand(land);
        string Code(decimal? percentage) => factuur.Verlegd
            ? (buitenland ? "VEU" : "VVL")
            : BtwCodes.Bij(codes, BtwRichting.Verkoop, percentage ?? 21m)?.Code ?? "V21";
        var omschrijving = factuur.Omschrijving is { Length: > 0 } o ? o : $"Factuur {factuur.Factuurnummer}".Trim();

        if (factuur.Bron == Herkenningsbron.EFactuur && factuur.Regels is { Count: > 0 } regels)
        {
            return [.. regels.Select(r => new Verkoopregel(r.Omschrijving.Length > 0 ? r.Omschrijving : omschrijving, 1m, r.Bedrag, omzetrekening, Code(r.Percentage)))];
        }

        if (factuur.Btwregels.Count > 0)
        {
            return
            [
                .. factuur.Btwregels.Select(b => new Verkoopregel(factuur.Btwregels.Count == 1 ? omschrijving : $"{omschrijving} ({Btwpercentage.Tekst(b.Percentage)})", 1m,
                    b.Grondslag ?? (b.Percentage == 0 ? 0m : Math.Round(b.Btw * 100m / b.Percentage, 2, MidpointRounding.AwayFromZero)), omzetrekening, Code(b.Percentage))),
            ];
        }

        if (factuur.TotaalExclusief is { } exclusief)
        {
            return [new Verkoopregel(omschrijving, 1m, exclusief, omzetrekening, Code(factuur.EnigTarief ?? (factuur.TotaalInclusief == exclusief ? 0m : 21m)))];
        }

        return factuur.TotaalInclusief is { } inclusief
            ? [new Verkoopregel(omschrijving, 1m, BtwBerekening.UitInclusief(inclusief, 21m).Grondslag, omzetrekening, "V21")]
            : [];
    }

    /// <summary>Wat er ontbreekt of niet klopt om de factuur vanzelf te boeken; leeg = boeken.</summary>
    public static IReadOnlyList<string> Belemmeringen(HerkendeFactuur factuur, Verkoopfactuur verkoop, IReadOnlyList<BtwCode> codes, bool klantHerkend)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(verkoop);
        ArgumentNullException.ThrowIfNull(codes);
        var redenen = new List<string>();
        if (!klantHerkend)
        {
            redenen.Add(Klantnaam(factuur) is { } naam ? $"klant {naam} is (nog) niet bekend" : "de klant is niet herkend");
        }

        if (factuur.Valuta is { } valuta)
        {
            redenen.Add($"de factuur is in {valuta}");
        }

        redenen.AddRange(factuur.Controleer().Select(f => f.TrimEnd('.').ToLowerInvariant()));
        if (verkoop.Regels.Count == 0)
        {
            redenen.Add("geen bedragen gevonden");
        }
        else if (factuur.TotaalInclusief is { } totaal)
        {
            try
            {
                var berekend = verkoop.Totaal(codes);
                if (Math.Abs(berekend - totaal) > 0.01m * Math.Max(1, verkoop.Regels.Count))
                {
                    redenen.Add($"het totaal ({totaal:0.00}) sluit niet op de regels ({berekend:0.00})");
                }
            }
            catch (ArgumentException fout)
            {
                redenen.Add(fout.Message.TrimEnd('.'));
            }
        }

        return [.. redenen.Distinct()];
    }
}
