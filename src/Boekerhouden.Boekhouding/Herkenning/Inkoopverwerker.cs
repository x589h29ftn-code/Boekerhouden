using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>Een factuur die al in de administratie staat, om dubbel boeken te voorkomen.</summary>
public sealed record BestaandeFactuur(string Relatie, string Factuurnummer, DateOnly Datum, decimal Totaal);

/// <summary>
/// Het voorstel voor een ingelezen inkoopfactuur: de herkende leverancier, het advies (rekening en btw-code), de
/// factuur zoals die geboekt kan worden, en wat automatisch verwerken in de weg staat.
/// </summary>
/// <param name="Factuur">De te boeken factuur; leeg als leverancier, rekening of bedragen ontbreken.</param>
/// <param name="Belemmeringen">Waarom de factuur in "Te verwerken" blijft; leeg als alles compleet is.</param>
public sealed record Inkoopvoorstel(
    HerkendeFactuur Herkend,
    Herkendeleverancier? Leverancier,
    Verwerkingsadvies Advies,
    Factuur? Factuur,
    IReadOnlyList<string> Belemmeringen)
{
    /// <summary>Mag het pakket deze factuur zelf boeken? Alleen als het advies dat zegt én er niets in de weg staat.</summary>
    public bool Automatisch => Advies.Automatisch && Factuur is not null && Belemmeringen.Count == 0;
}

/// <summary>
/// Van herkende factuur naar boekingsvoorstel: leverancier zoeken, advies vragen (regel &gt; geleerd &gt; relatie),
/// de factuurregels per btw-tarief opbouwen en controleren of automatisch verwerken veilig is: compleet, sluitend,
/// niet eerder geboekt en in een open periode.
/// </summary>
public static class Inkoopverwerker
{
    public static Inkoopvoorstel Verwerk(
        HerkendeFactuur herkend,
        IReadOnlyList<Relatie> relaties,
        IEnumerable<Verwerkingsregel> regels,
        Leergeheugen geheugen,
        IReadOnlyList<BtwCode> btwCodes,
        IEnumerable<BestaandeFactuur> bestaand,
        int betalingstermijn = 30,
        Func<DateOnly, bool>? periodeOpen = null)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        ArgumentNullException.ThrowIfNull(bestaand);
        var leverancier = Leverancierherkenning.Zoek(herkend, relaties);
        var gegevens = Gegevens(herkend, leverancier?.Relatie);
        var advies = Verwerkingsadviseur.Advies(gegevens, regels, geheugen, leverancier?.Relatie, btwCodes);

        var belemmeringen = herkend.Controleer().ToList();
        if (leverancier is null)
        {
            belemmeringen.Add("Leverancier niet herkend.");
        }

        if (advies.Rekeningnummer is null)
        {
            belemmeringen.Add("Kies een grootboekrekening.");
        }

        if (leverancier is not null && Dubbel(herkend, leverancier.Relatie.Code, bestaand) is { } dubbel)
        {
            belemmeringen.Add($"Lijkt al geboekt: factuur {dubbel.Factuurnummer} van {dubbel.Datum:dd-MM-yyyy}.");
        }

        if (herkend.Factuurdatum is { } datum && periodeOpen is not null && !periodeOpen(datum))
        {
            belemmeringen.Add($"De periode van {datum:dd-MM-yyyy} is afgesloten.");
        }

        Factuur? factuur = null;
        if (leverancier is not null && advies.Rekeningnummer is { } rekening && herkend.TotaalInclusief is { } totaal && herkend.Factuurdatum is { } factuurdatum)
        {
            var factuurregels = Factuurregels(herkend, rekening, advies.BtwCode, btwCodes, totaal);
            if (factuurregels is null)
            {
                belemmeringen.Add("Kies een btw-code.");
            }
            else
            {
                factuur = new Factuur(
                    Factuursoort.Inkoop,
                    leverancier.Relatie.Code,
                    herkend.Factuurnummer ?? string.Empty,
                    factuurdatum,
                    herkend.Vervaldatum is { } verval && verval >= factuurdatum ? verval : factuurdatum.AddDays(betalingstermijn),
                    Omschrijving(herkend, leverancier.Relatie),
                    factuurregels,
                    herkend.Betalingskenmerk);
                if (factuur.Totaal(btwCodes) != totaal)
                {
                    belemmeringen.Add($"De regels ({factuur.Totaal(btwCodes):0.00}) sluiten niet op het totaal ({totaal:0.00}).");
                }
            }
        }

        return new Inkoopvoorstel(herkend, leverancier, advies, factuur, belemmeringen);
    }

    /// <summary>De gegevens waarop regels en leergeheugen werken.</summary>
    public static Verwerkingsgegevens Gegevens(HerkendeFactuur herkend, Relatie? relatie)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        return new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, relatie?.Code, relatie?.Naam ?? herkend.Naam,
            herkend.TotaalInclusief ?? 0m, herkend.Omschrijving, herkend.Iban, herkend.EnigTarief);
    }

    private static List<Factuurregel>? Factuurregels(HerkendeFactuur herkend, string rekening, string? adviesCode, IReadOnlyList<BtwCode> btwCodes, decimal totaal)
    {
        var omschrijving = herkend.Omschrijving ?? herkend.Naam ?? "Inkoop";
        var advies = BtwCodes.Zoek(btwCodes, adviesCode);

        // Btw per tarief bekend: één regel per tarief, met de btw zoals op de factuur.
        if (herkend.Btwregels.Count > 0)
        {
            var regels = new List<Factuurregel>();
            foreach (var b in herkend.Btwregels)
            {
                var grondslag = b.Grondslag
                                ?? (herkend.Btwregels.Count == 1 ? herkend.TotaalExclusief ?? totaal - b.Btw : (decimal?)null);
                if (grondslag is null)
                {
                    return null;
                }

                var code = advies is { Richting: BtwRichting.Inkoop } && advies.Percentage == b.Percentage && !advies.Verlegd
                    ? advies
                    : BtwCodes.Bij(btwCodes, BtwRichting.Inkoop, b.Percentage);
                if (code is null)
                {
                    return null;
                }

                regels.Add(new Factuurregel(omschrijving, rekening, code.Code, grondslag.Value, b.Btw));
            }

            return regels;
        }

        // Geen btw op de factuur gevonden: de btw-code van het advies, met de btw uit het totaal.
        if (advies is null)
        {
            return null;
        }

        if (!advies.BtwOpFactuur)
        {
            return [new Factuurregel(omschrijving, rekening, advies.Code, totaal)];
        }

        var (excl, btw) = BtwBerekening.UitInclusief(totaal, advies.Percentage);
        return [new Factuurregel(omschrijving, rekening, advies.Code, excl, btw)];
    }

    private static BestaandeFactuur? Dubbel(HerkendeFactuur herkend, string relatie, IEnumerable<BestaandeFactuur> bestaand) =>
        bestaand.FirstOrDefault(b => b.Relatie == relatie
                                     && ((herkend.Factuurnummer is { } nummer && Factuurnummers.Normaliseer(nummer) == Factuurnummers.Normaliseer(b.Factuurnummer))
                                         || (herkend.Factuurdatum == b.Datum && herkend.TotaalInclusief == b.Totaal)));

    private static string Omschrijving(HerkendeFactuur herkend, Relatie relatie)
    {
        var tekst = herkend.Omschrijving is { Length: > 0 } o ? o : $"{relatie.Naam} {herkend.Factuurnummer}".Trim();
        return tekst.Length > 200 ? tekst[..200] : tekst;
    }
}
