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
        Func<DateOnly, bool>? periodeOpen = null,
        decimal? limietAutomatisch = null)
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
        else if (leverancier is not null && Gelijkend(herkend, leverancier.Relatie.Code, bestaand) is { } gelijkend)
        {
            belemmeringen.Add($"Zelfde bedrag als factuur {gelijkend.Factuurnummer} van {gelijkend.Datum:dd-MM-yyyy}, met een ander nummer: een herinnering of kopie?");
        }

        // Geleerd, maar deze factuur past niet in het patroon. Het IBAN controleert de relatie al (hieronder) als die er een kent.
        var afwijkingen = advies.Afwijkingen ?? [];
        foreach (var afwijking in afwijkingen.Where(a => a.Soort != Afwijkingssoort.Iban || leverancier?.Relatie.Ibans.Count is null or 0))
        {
            belemmeringen.Add(afwijking.Melding);
        }

        if (leverancier is not null && herkend.TotaalInclusief is { } nu && afwijkingen.All(a => a.Soort != Afwijkingssoort.Bedrag)
            && Afwijkend(nu, leverancier.Relatie.Code, bestaand) is { } hoogste)
        {
            belemmeringen.Add($"Het bedrag is veel hoger dan gebruikelijk bij {leverancier.Relatie.Naam} (tot nu toe hoogstens {Domain.Bedragen.Euro(hoogste)}).");
        }

        // Factuurfraude: een bekende leverancier met ineens een ander rekeningnummer.
        if (leverancier is { Op: not "IBAN" } && herkend.Iban is { } iban && leverancier.Relatie.Ibans.Count > 0 && !leverancier.Relatie.HeeftIban(iban))
        {
            belemmeringen.Add($"Het IBAN op de factuur ({Relaties.Iban.Toon(iban)}) is niet het bekende IBAN van {leverancier.Relatie.Naam}. Controleer dit bij de leverancier via een bekend telefoonnummer.");
        }

        if (advies.Automatisch && limietAutomatisch is { } limiet && herkend.TotaalInclusief is { } bedrag && Math.Abs(bedrag) > limiet)
        {
            belemmeringen.Add($"Het bedrag is hoger dan de limiet voor automatisch verwerken ({Domain.Bedragen.Euro(limiet)}).");
        }

        // Of de factuur op naam van de onderneming staat, houdt automatisch verwerken niet tegen: de naam op de factuur
        // wijkt in de praktijk vaak af (handelsnaam, ander kantoor, afkorting) zonder dat er iets mis is.

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
                    Omschrijving(herkend, leverancier.Relatie, advies.Omschrijving, betalingstermijn),
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

    /// <summary>Minstens zoveel eerdere facturen van de leverancier voordat een bedrag "ongebruikelijk" kan zijn.</summary>
    public const int EerdereFacturenVoorAfwijking = 3;

    /// <summary>
    /// Het hoogste eerdere bedrag als dit bedrag meer dan twee keer zo hoog is; anders <c>null</c>. Pas vanaf
    /// <see cref="EerdereFacturenVoorAfwijking"/> eerdere facturen, zodat een nieuwe leverancier geen signaal geeft.
    /// </summary>
    public static decimal? Afwijkend(decimal bedrag, string relatie, IEnumerable<BestaandeFactuur> bestaand)
    {
        ArgumentNullException.ThrowIfNull(bestaand);
        var eerder = bestaand.Where(f => f.Relatie == relatie).Select(f => Math.Abs(f.Totaal)).ToList();
        if (eerder.Count < EerdereFacturenVoorAfwijking)
        {
            return null;
        }

        var hoogste = eerder.Max();
        return Math.Abs(bedrag) > 2 * hoogste ? hoogste : null;
    }

    /// <summary>De gegevens waarop regels en leergeheugen werken.</summary>
    public static Verwerkingsgegevens Gegevens(HerkendeFactuur herkend, Relatie? relatie)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        return new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, relatie?.Code, relatie?.Naam ?? herkend.Naam,
            herkend.TotaalInclusief ?? 0m, herkend.Omschrijving, herkend.Iban, herkend.Verlegd ? null : herkend.EnigTarief, herkend.Postcodes);
    }

    /// <summary>
    /// De btw-code bij een factuur met verlegde btw: binnenland (2a), binnen de EU (4b) of buiten de EU (4a), naar het
    /// land van het btw-nummer van de leverancier, of anders van zijn IBAN. Leeg als de btw niet verlegd is.
    /// </summary>
    public static BtwCode? VerlegdeCode(HerkendeFactuur herkend, IReadOnlyList<BtwCode> btwCodes)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        ArgumentNullException.ThrowIfNull(btwCodes);
        if (!herkend.Verlegd)
        {
            return null;
        }

        var land = (herkend.BtwNummer ?? herkend.Iban) is { Length: >= 2 } nummer ? nummer[..2].ToUpperInvariant() : null;
        var soort = land switch
        {
            "NL" => Btwsoort.InkoopVerlegd,
            not null when BtwNummer.IsEuLand(land) => Btwsoort.InkoopBinnenEu,
            not null => Btwsoort.InkoopBuitenEu,
            _ => Btwsoort.InkoopVerlegd,
        };
        return btwCodes.FirstOrDefault(c => c.Soort == soort && c.Tarief == Tarief.Hoog && c.Actief) ?? btwCodes.FirstOrDefault(c => c.Soort == soort);
    }

    private static List<Factuurregel>? Factuurregels(HerkendeFactuur herkend, string rekening, string? adviesCode, IReadOnlyList<BtwCode> btwCodes, decimal totaal)
    {
        var omschrijving = herkend.Omschrijving ?? herkend.Naam ?? "Inkoop";
        var advies = BtwCodes.Zoek(btwCodes, adviesCode);

        // Btw verlegd: geen btw op de factuur; één regel met de verlegde code (die van het advies, als die verlegd is).
        if (VerlegdeCode(herkend, btwCodes) is { } verlegd)
        {
            return [new Factuurregel(omschrijving, rekening, (advies is { Verlegd: true } ? advies : verlegd).Code, herkend.TotaalExclusief ?? totaal)];
        }

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

    /// <summary>
    /// De tweede controle: dezelfde leverancier en hetzelfde bedrag, binnen zes dagen, maar een ander factuurnummer (een
    /// herinnering, een kopie of een opnieuw verstuurde factuur). Een maand- of weekfactuur valt daar buiten.
    /// </summary>
    private static BestaandeFactuur? Gelijkend(HerkendeFactuur herkend, string relatie, IEnumerable<BestaandeFactuur> bestaand) =>
        herkend is { TotaalInclusief: { } totaal, Factuurdatum: { } datum } && totaal != 0
            ? bestaand.Where(b => b.Relatie == relatie && b.Totaal == totaal && Math.Abs(b.Datum.DayNumber - datum.DayNumber) <= 6)
                .OrderBy(b => Math.Abs(b.Datum.DayNumber - datum.DayNumber)).FirstOrDefault()
            : null;

    private static string Omschrijving(HerkendeFactuur herkend, Relatie relatie, string? sjabloon, int betalingstermijn)
    {
        var tekst = sjabloon is { Length: > 0 }
            ? Omschrijvingssjabloon.Vul(sjabloon, Variabelen(herkend, relatie.Naam, betalingstermijn))
            : herkend.Omschrijving is { Length: > 0 } o ? o : $"{relatie.Naam} {herkend.Factuurnummer}".Trim();
        return tekst.Length > 200 ? tekst[..200] : tekst;
    }

    /// <summary>De gegevens van de herkende factuur voor de variabelen in een omschrijving.</summary>
    public static Factuurvariabelen Variabelen(HerkendeFactuur herkend, string? leverancier, int betalingstermijn = 30)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        return new Factuurvariabelen(herkend.Factuurnummer, herkend.Omschrijving, leverancier ?? herkend.Naam, herkend.Factuurdatum,
            herkend.Vervaldatum ?? herkend.Factuurdatum?.AddDays(betalingstermijn), herkend.Betalingskenmerk);
    }
}
