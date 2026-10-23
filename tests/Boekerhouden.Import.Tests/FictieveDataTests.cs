using System.Globalization;
using Boekerhouden.Import;
using ClosedXML.Excel;
using static Boekerhouden.Import.Tests.TestHulp;

namespace Boekerhouden.Import.Tests;

/// <summary>
/// De fictieve testklanten in tests/Fictief (verzonnen namen en bedragen): elke saldibalans
/// moet foutloos inlezen, als CSV én als Excel.
/// </summary>
public class FictieveDataTests
{
    public static TheoryData<string, int> Bestanden()
    {
        var data = new TheoryData<string, int>();
        foreach (var klant in new[] { "bv-voorbeeld-bakkerij", "ez-schildersbedrijf" })
        {
            for (var jaar = 2021; jaar <= 2025; jaar++)
            {
                data.Add(klant, jaar);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Bestanden))]
    public void Fictieve_saldibalans_wordt_foutloos_ingelezen(string klant, int jaar)
    {
        var resultaat = ImportSjabloonLezer.Lees(FictiefPad(klant, $"saldibalans-{jaar}.csv"));

        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
        Assert.Empty(resultaat.Meldingen);
        var saldibalans = resultaat.Saldibalans!;
        Assert.Equal(jaar, saldibalans.Boekjaar);
        Assert.True(saldibalans.IsInEvenwicht);
        Assert.Equal(Verwachting(klant, jaar).AantalRekeningen, saldibalans.Regels.Count);
    }

    [Theory]
    [MemberData(nameof(Bestanden))]
    public void Dezelfde_saldibalans_als_Excel_geeft_dezelfde_uitkomst(string klant, int jaar)
    {
        var csv = ImportSjabloonLezer.Lees(FictiefPad(klant, $"saldibalans-{jaar}.csv")).Saldibalans!;

        var sjabloon = new MemoryStream();
        ImportSjabloonGenerator.Schrijf(sjabloon, csv.Boekjaar, csv.Klant);
        sjabloon.Position = 0;
        using var werkmap = new XLWorkbook(sjabloon);
        var blad = werkmap.Worksheet(ImportSjabloon.WerkbladNaam);
        for (var i = 0; i < csv.Regels.Count; i++)
        {
            var rij = ImportSjabloon.KopRij + 1 + i;
            blad.Cell(rij, 1).Value = csv.Regels[i].Grootboeknummer;
            blad.Cell(rij, 2).Value = csv.Regels[i].Omschrijving;
            blad.Cell(rij, 3).Value = csv.Regels[i].Bedrag;
        }

        var excel = new MemoryStream();
        werkmap.SaveAs(excel);
        excel.Position = 0;

        var resultaat = ImportSjabloonLezer.Lees(excel, "saldibalans.xlsx");

        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
        Assert.Equal(csv.Regels, resultaat.Saldibalans!.Regels);
        Assert.Equal(csv.Klant, resultaat.Saldibalans.Klant);
    }

    [Theory]
    [InlineData("saldo-niet-nihil.csv", ImportCodes.SaldoNihil)]
    [InlineData("dubbel-grootboeknummer.csv", ImportCodes.UniekeNummers)]
    public void Foutieve_voorbeeldbestanden_worden_geweigerd(string bestand, string code)
    {
        EnigeFout(ImportSjabloonLezer.Lees(FictiefPad("fouten", bestand)), code);
    }

    private static (int AantalRekeningen, decimal Resultaat) Verwachting(string klant, int jaar)
    {
        var regel = File.ReadAllLines(FictiefPad(klant, "verwachting.csv"))
            .Skip(1)
            .Select(r => r.Split(';'))
            .Single(d => d[0] == jaar.ToString(CultureInfo.InvariantCulture));
        return (int.Parse(regel[1], CultureInfo.InvariantCulture), decimal.Parse(regel[2], CultureInfo.GetCultureInfo("nl-NL")));
    }
}
