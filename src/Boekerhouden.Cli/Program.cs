using System.Globalization;
using System.Text;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Import;

Console.OutputEncoding = Encoding.UTF8;

return args switch
{
    ["sjabloon", var pad, .. var opties] => MaakSjabloon(pad, opties),
    ["controleer", var pad] => Controleer(pad, null),
    ["controleer", var pad, "--boekjaar", var jaar] when int.TryParse(jaar, NumberStyles.None, CultureInfo.InvariantCulture, out var boekjaar)
        => Controleer(pad, boekjaar),
    ["rgs", var pad] => ControleerRgs(pad),
    ["herken", var pad, .. var opties] => await Herkennen.Voer(pad, opties),
    _ => Gebruik(),
};

static int ControleerRgs(string pad)
{
    if (!File.Exists(pad))
    {
        Console.Error.WriteLine($"Bestand niet gevonden: {pad}");
        return 2;
    }

    using var stream = File.OpenRead(pad);
    var werkboek = RgsWerkboekLezer.Lees(stream, Path.GetFileName(pad));
    foreach (var melding in werkboek.Meldingen)
    {
        var soort = melding.Ernst == MeldingErnst.Fout ? "FOUT  " : "LET OP";
        Console.WriteLine($"{soort} {(melding.Rij is { } rij ? $"rij {rij}: " : string.Empty)}{melding.Tekst}");
    }

    if (!werkboek.IsGeslaagd)
    {
        return 1;
    }

    var schema = new RgsSchema(werkboek.Versie ?? "?", werkboek.Codes);
    Console.WriteLine($"RGS-versie:    {werkboek.Versie ?? "(onbekend)"} (werkblad {werkboek.Werkblad})");
    Console.WriteLine($"Codes:         {schema.Aantal}, per niveau: {string.Join(", ", werkboek.Codes.GroupBy(c => c.Niveau).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"))}");
    Console.WriteLine($"Inactief:      {werkboek.Codes.Count(c => c.Inactief)}; met omslagcode: {werkboek.Codes.Count(c => c.Omslagcode is not null)}");
    Console.WriteLine($"Kiesfilters:   {string.Join(", ", werkboek.Kiesfilters)}");
    Console.WriteLine($"Wegfilters:    {string.Join(", ", werkboek.Wegfilters)}");
    foreach (var selectie in new[] { RgsSelectie.BV, RgsSelectie.Eenmanszaak })
    {
        var gekozen = schema.Selecteer(selectie).Select(schema.Zoek).OfType<RgsCode>().ToList();
        Console.WriteLine($"{selectie.Naam,-14} {gekozen.Count(c => c.Niveau == 2)} hoofdposten, {gekozen.Count(c => c.Niveau == 3)} subposten, {gekozen.Count(c => c.Niveau == 4)} grootboekcodes");
    }

    return 0;
}

static int MaakSjabloon(string pad, string[] opties)
{
    int? boekjaar = null;
    string? klant = null;
    for (var i = 0; i < opties.Length - 1; i += 2)
    {
        switch (opties[i])
        {
            case "--boekjaar" when int.TryParse(opties[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var jaar):
                boekjaar = jaar;
                break;
            case "--klant":
                klant = opties[i + 1];
                break;
            default:
                return Gebruik();
        }
    }

    ImportSjabloonGenerator.Schrijf(pad, boekjaar, klant);
    Console.WriteLine($"Importsjabloon aangemaakt: {pad}");
    return 0;
}

static int Controleer(string pad, int? boekjaar)
{
    if (!File.Exists(pad))
    {
        Console.Error.WriteLine($"Bestand niet gevonden: {pad}");
        return 2;
    }

    var resultaat = ImportSjabloonLezer.Lees(pad, new ImportOpties { Boekjaar = boekjaar });
    foreach (var melding in resultaat.Meldingen)
    {
        var soort = melding.Ernst == MeldingErnst.Fout ? "FOUT  " : "LET OP";
        Console.WriteLine($"{soort} [{melding.Code}] {melding.Tekst}");
    }

    if (resultaat.Saldibalans is not { } saldibalans)
    {
        Console.WriteLine($"Niet geïmporteerd: {resultaat.Fouten.Count()} fout(en). Herstel het bestand en probeer het opnieuw.");
        return 1;
    }

    Console.WriteLine($"Boekjaar:             {saldibalans.Boekjaar}");
    Console.WriteLine($"Klant:                {saldibalans.Klant ?? "(niet ingevuld)"}");
    Console.WriteLine($"Grootboekrekeningen:  {saldibalans.Regels.Count}");
    Console.WriteLine($"Totaal debet:         {Bedragen.Euro(saldibalans.TotaalDebet)}");
    Console.WriteLine($"Totaal credit:        {Bedragen.Euro(saldibalans.TotaalCredit)}");
    Console.WriteLine("Saldo nihil: de saldibalans kan worden geïmporteerd.");
    return 0;
}

static int Gebruik()
{
    Console.Error.WriteLine("""
        Gebruik:
          boekerhouden sjabloon <uitvoer.xlsx> [--boekjaar 2025] [--klant "Naam"]
              Maakt een leeg importsjabloon.
          boekerhouden controleer <bestand.xlsx|bestand.csv> [--boekjaar 2025]
              Leest een ingevuld importsjabloon (of een export met de kolommen Rekening, Omschr, Saldo)
              in en toont de uitkomst van de controles. --boekjaar is nodig als het bestand geen boekjaar bevat.
          boekerhouden rgs <RGS-werkboek.xlsx>
              Leest het officiële RGS-werkboek in en toont versie, aantallen en de selectie per rechtsvorm.
          boekerhouden herken <factuur.pdf|factuur.xml> [--eigen-naam "Naam"] [--eigen-iban NL..] [--eigen-btw NL..B01] [--tekst]
                              [--leer <rekening> <btw-code>]
              Herkent een inkoopfactuur zoals het administratiepakket dat doet en toont het boekingsvoorstel.
              --tekst toont ook de tekst uit de PDF; --leer speelt drie keer dezelfde leverancier af (kiezen,
              voorstel, automatisch) met de gekozen rekening en btw-code (bijv. I21).
        """);
    return 2;
}
