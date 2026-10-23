using System.Globalization;
using System.Text;
using Boekerhouden.Domain;
using Boekerhouden.Import;

Console.OutputEncoding = Encoding.UTF8;

return args switch
{
    ["sjabloon", var pad, .. var opties] => MaakSjabloon(pad, opties),
    ["controleer", var pad] => Controleer(pad, null),
    ["controleer", var pad, "--boekjaar", var jaar] when int.TryParse(jaar, NumberStyles.None, CultureInfo.InvariantCulture, out var boekjaar)
        => Controleer(pad, boekjaar),
    _ => Gebruik(),
};

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
        """);
    return 2;
}
