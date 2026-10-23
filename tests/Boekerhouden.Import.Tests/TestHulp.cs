using System.Text;
using Boekerhouden.Import;

namespace Boekerhouden.Import.Tests;

internal static class TestHulp
{
    /// <summary>Leest CSV-tekst (regels gescheiden door \n) in alsof het een bestand "test.csv" is.</summary>
    public static ImportResultaat LeesCsv(string inhoud, int? boekjaar = null) =>
        ImportSjabloonLezer.Lees(new MemoryStream(Encoding.UTF8.GetBytes(inhoud)), "test.csv", new ImportOpties { Boekjaar = boekjaar });

    /// <summary>Een geldig minimaal sjabloon met de opgegeven dataregels.</summary>
    public static string Sjabloon(string boekjaar, params string[] regels) =>
        $"Boekjaar;{boekjaar}\nKlant;Test B.V.\n\nGrootboeknummer;Omschrijving;Bedrag\n" + string.Join('\n', regels) + "\n";

    public static string FictiefPad(params string[] delen) =>
        Path.Combine([AppContext.BaseDirectory, "Fictief", .. delen]);

    public static ImportMelding EnigeFout(ImportResultaat resultaat, string code)
    {
        Assert.False(resultaat.IsGeslaagd);
        Assert.Null(resultaat.Saldibalans);
        return Assert.Single(resultaat.Fouten, m => m.Code == code);
    }
}
