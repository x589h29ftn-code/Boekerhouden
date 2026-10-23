using Boekerhouden.Import;
using ClosedXML.Excel;

namespace Boekerhouden.Import.Tests;

/// <summary>Inlezen van Excel-bestanden, waaronder het zelf gegenereerde sjabloon.</summary>
public class ExcelImportTests
{
    [Fact]
    public void Ingevuld_sjabloon_wordt_ingelezen_met_tekstnummers_getallen_en_formules()
    {
        var bestand = VulSjabloon(werkblad =>
        {
            werkblad.Cell(5, 1).Value = "0100";
            werkblad.Cell(5, 2).Value = "Inventaris";
            werkblad.Cell(5, 3).Value = 1_500.25;
            werkblad.Cell(6, 1).Value = 1300; // Grootboeknummer als getal ingevoerd.
            werkblad.Cell(6, 2).Value = "Debiteuren";
            werkblad.Cell(6, 3).FormulaA1 = "100+0.1+0.2";
            werkblad.Cell(7, 1).Value = "0500";
            werkblad.Cell(7, 2).Value = "Kapitaal";
            werkblad.Cell(7, 3).Value = -1_600.55;
            werkblad.Cell(7, 4).Value = "BEivGokGea";
        }, boekjaar: 2025);

        var resultaat = ImportSjabloonLezer.Lees(bestand, "sjabloon.xlsx");

        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
        var regels = resultaat.Saldibalans!.Regels;
        Assert.Equal(["0100", "1300", "0500"], regels.Select(r => r.Grootboeknummer));
        Assert.Equal([1_500.25m, 100.30m, -1_600.55m], regels.Select(r => r.Bedrag));
        Assert.Equal("BEivGokGea", regels[2].RgsCode);
        Assert.Equal(2025, resultaat.Saldibalans.Boekjaar);
    }

    [Fact]
    public void Leeg_boekjaar_in_sjabloon_wordt_niet_verward_met_de_controlecellen()
    {
        var bestand = VulSjabloon(werkblad =>
        {
            werkblad.Cell(5, 1).Value = "1100";
            werkblad.Cell(5, 2).Value = "Bank";
            werkblad.Cell(5, 3).Value = 0;
        }, boekjaar: null);

        var resultaat = ImportSjabloonLezer.Lees(bestand, "sjabloon.xlsx");

        Assert.Single(resultaat.Fouten, f => f.Code == ImportCodes.Boekjaar);
    }

    [Fact]
    public void Leeg_sjabloon_heeft_geen_regels()
    {
        var resultaat = ImportSjabloonLezer.Lees(VulSjabloon(_ => { }, boekjaar: 2025), "sjabloon.xlsx");
        Assert.Single(resultaat.Fouten, f => f.Code == ImportCodes.VerplichteVelden);
    }

    [Fact]
    public void Meer_dan_twee_decimalen_in_een_Excel_getal_is_een_fout()
    {
        var bestand = VulSjabloon(werkblad =>
        {
            werkblad.Cell(5, 1).Value = "1100";
            werkblad.Cell(5, 2).Value = "Bank";
            werkblad.Cell(5, 3).Value = 10.125;
            werkblad.Cell(6, 1).Value = "0500";
            werkblad.Cell(6, 2).Value = "Kapitaal";
            werkblad.Cell(6, 3).Value = -10.125;
        }, boekjaar: 2025);

        var resultaat = ImportSjabloonLezer.Lees(bestand, "sjabloon.xlsx");
        Assert.Equal(2, resultaat.Fouten.Count(f => f.Code == ImportCodes.GeldigeBedragen));
    }

    [Fact]
    public void Export_met_ander_werkblad_en_kolomnamen_wordt_ingelezen_met_opgegeven_boekjaar()
    {
        // Opbouw zoals een gangbare export: werkblad heet naar het jaar, kopregel op rij 1, geen boekjaarcel.
        using var werkmap = new XLWorkbook();
        var blad = werkmap.Worksheets.Add("2025");
        blad.Cell(1, 1).Value = "Rekening";
        blad.Cell(1, 2).Value = "Omschr";
        blad.Cell(1, 3).Value = "Saldo";
        blad.Cell(2, 1).Value = "0210.01";
        blad.Cell(2, 2).Value = "Inventaris aanschafwaarde";
        blad.Cell(2, 3).Value = 3_250.80;
        blad.Cell(3, 1).Value = "1600";
        blad.Cell(3, 2).Value = "Rekening-courant directie";
        blad.Cell(3, 3).Value = -3_250.80;
        var stream = new MemoryStream();
        werkmap.SaveAs(stream);
        stream.Position = 0;

        var resultaat = ImportSjabloonLezer.Lees(stream, "kolommenbalans.xlsx", new ImportOpties { Boekjaar = 2025 });

        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
        Assert.Equal("0210.01", resultaat.Saldibalans!.Regels[0].Grootboeknummer);
    }

    [Fact]
    public void Gegenereerd_sjabloon_bevat_toelichting_en_voorgevulde_gegevens()
    {
        var stream = new MemoryStream();
        ImportSjabloonGenerator.Schrijf(stream, boekjaar: 2025, klant: "Test B.V.");
        stream.Position = 0;

        using var werkmap = new XLWorkbook(stream);
        var blad = werkmap.Worksheet(ImportSjabloon.WerkbladNaam);
        Assert.Equal(2025, blad.Cell(1, 2).GetValue<int>());
        Assert.Equal("Test B.V.", blad.Cell(2, 2).GetString());
        Assert.Equal(ImportSjabloon.KolomGrootboeknummer, blad.Cell(ImportSjabloon.KopRij, 1).GetString());
        Assert.True(werkmap.TryGetWorksheet(ImportSjabloon.ToelichtingWerkbladNaam, out _));
    }

    private static MemoryStream VulSjabloon(Action<IXLWorksheet> vul, int? boekjaar)
    {
        var sjabloon = new MemoryStream();
        ImportSjabloonGenerator.Schrijf(sjabloon, boekjaar);
        sjabloon.Position = 0;

        using var werkmap = new XLWorkbook(sjabloon);
        vul(werkmap.Worksheet(ImportSjabloon.WerkbladNaam));
        var resultaat = new MemoryStream();
        werkmap.SaveAs(resultaat);
        resultaat.Position = 0;
        return resultaat;
    }
}
