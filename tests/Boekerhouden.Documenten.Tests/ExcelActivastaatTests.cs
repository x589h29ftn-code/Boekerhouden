using Boekerhouden.Boekhouding.Activa;
using ClosedXML.Excel;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een verzonnen mva-staat in Excel: een tweede blad met de staat, samengevoegde koppen, datums en formules.</summary>
public sealed class ExcelActivastaatTests
{
    internal static byte[] Staat()
    {
        using var boek = new XLWorkbook();
        boek.AddWorksheet("Toelichting").Cell(1, 1).Value = "Bakkerij De Korst B.V.";
        var blad = boek.AddWorksheet("Staat vaste activa");
        blad.Range("D2:G2").Merge().Value = "Aanschafwaarde";
        blad.Range("H2:K2").Merge().Value = "Afschrijvingen";
        string[] kop = ["", "Omschrijving", "Datum", "Aanschaf-\nfingen tot\n01-01-2025", "Inves-\nteringen\n2025", "Desinves-\nteringen\n2025", "Aanschaf-\nfingen t/m\n31-12-2025",
            "Afschrij-\nvingen tot\n01-01-2025", "Afschrij-\nvingen \n2025", "Afschrij-\nving desin-\nvesteringen", "Afschrij-\nvingen t/m\n31-12-2025", "Boekwaarde per \n31-12-2025", "Residu-\nwaarde", "Afschrij-\nvingsper-\ncentage"];
        for (var c = 0; c < kop.Length; c++)
        {
            blad.Cell(3, c + 1).Value = kop[c];
        }

        blad.Cell(5, 2).Value = "Inventaris";
        Regel(blad, 6, "Oven", new DateTime(2022, 2, 11), 3000, 0, 1450, 600, 0, 20);
        Regel(blad, 7, "Kassa", null, 800, 0, 800, 0, 0, 20);
        blad.Cell(8, 7).FormulaA1 = "SUM(G6:G7)";
        blad.Cell(8, 11).FormulaA1 = "SUM(K6:K7)";
        blad.Cell(8, 12).FormulaA1 = "SUM(L6:L7)";
        blad.Cell(10, 2).Value = "Vervoermiddelen";
        Regel(blad, 11, "Bestelbus", new DateTime(2024, 7, 1), 0, 24000, 0, 2200, 2000, 20);
        using var stream = new MemoryStream();
        boek.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Regel(IXLWorksheet blad, int r, string omschrijving, DateTime? datum, decimal begin, decimal investering, decimal afschrijvingBegin, decimal afschrijvingJaar, decimal rest, decimal procent)
    {
        blad.Cell(r, 2).Value = omschrijving;
        if (datum is { } d)
        {
            blad.Cell(r, 3).Value = d;
        }

        blad.Cell(r, 4).Value = begin;
        blad.Cell(r, 5).Value = investering;
        blad.Cell(r, 7).FormulaA1 = $"D{r}+E{r}-F{r}";
        blad.Cell(r, 8).Value = afschrijvingBegin;
        blad.Cell(r, 9).Value = afschrijvingJaar;
        blad.Cell(r, 11).FormulaA1 = $"H{r}+I{r}-J{r}";
        blad.Cell(r, 12).FormulaA1 = $"G{r}-K{r}";
        blad.Cell(r, 13).Value = rest;
        blad.Cell(r, 14).Value = procent;
    }

    [Fact]
    public void Een_staat_op_een_tweede_blad_met_formules_en_samengevoegde_koppen()
    {
        var staat = ExcelActivastaat.Lees(new MemoryStream(Staat()));

        Assert.True(staat.Sluit, string.Join(" | ", staat.Signalen));
        Assert.Equal(2025, staat.Jaar);
        Assert.Equal([("Inventaris", "Oven", 3000m, 2050m), ("Inventaris", "Kassa", 800m, 800m), ("Vervoermiddelen", "Bestelbus", 24000m, 2200m)],
            staat.Regels.Select(r => (r.Groep, r.Omschrijving, r.AanschafEind, r.AfschrijvingEind)));
        Assert.Equal(new DateOnly(2022, 2, 11), staat.Regels.First().Datum);
        Assert.Equal(3800m, staat.Groepen[0].Subtotaal!.AanschafEind);
    }

    [Fact]
    public void Geen_excelbestand_geeft_een_duidelijke_fout() =>
        Assert.Contains(".xlsx", Assert.Throws<DocumentException>(() => ExcelActivastaat.Lees(new MemoryStream("geen excel"u8.ToArray()))).Message, StringComparison.Ordinal);
}
