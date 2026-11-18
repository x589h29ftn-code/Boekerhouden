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

    [Fact]
    public void Het_sjabloon_ingevuld_wordt_helemaal_gelezen()
    {
        var sjabloon = ExcelActivastaat.Sjabloon(2025, [("0200", "Inventaris", "activa"), ("0210", "Afschrijving inventaris", "afschrijving")]);
        using var boek = new XLWorkbook(new MemoryStream(sjabloon));
        Assert.Equal(["Vaste activa", "Uitleg", "Rekeningen"], boek.Worksheets.Select(w => w.Name));
        Assert.Equal("0200", boek.Worksheet("Rekeningen").Cell(2, 1).GetString());
        var blad = boek.Worksheet("Vaste activa");
        object[] bus = ["Bestelbus", "Vervoermiddelen", new DateTime(2024, 7, 1), 24000, 2000, 20, 2200, 21800, "0300", "0310", "4810", "bestelauto", null!, "Grijs kenteken"];
        object[] auto = ["Personenauto", "Vervoermiddelen", new DateTime(2023, 3, 1), 40000, 5000, 20, 15750, 24250, "0300", "0310", "4810", "ja", 45000, null!];
        foreach (var (rij, waarden) in new[] { (2, bus), (3, auto) })
        {
            for (var c = 0; c < waarden.Length; c++)
            {
                if (waarden[c] is { } w)
                {
                    blad.Cell(rij, c + 1).Value = XLCellValue.FromObject(w, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        using var ingevuld = new MemoryStream();
        boek.SaveAs(ingevuld);
        ingevuld.Position = 0;

        var staat = ExcelActivastaat.Lees(ingevuld);

        Assert.True(staat.Sluit, string.Join(" | ", staat.Signalen));
        Assert.Equal(2025, staat.Jaar);
        var (b, a) = (staat.Regels.First(), staat.Regels.Last());
        Assert.Equal(("Vervoermiddelen", 24000m, 2000m, 20m, 2200m, 21800m), (b.Groep, b.AanschafEind, b.Restwaarde, b.Percentage!.Value, b.AfschrijvingEind, b.Boekwaarde));
        Assert.Equal(("0300", "0310", "4810", Boekhouding.Activa.Privegebruik.NeeBestelauto, "Grijs kenteken"), (b.Activarekening, b.Afschrijvingsrekening, b.Kostenrekening, b.Privegebruik, b.Notitie));
        Assert.Equal((Boekhouding.Activa.Privegebruik.JaForfait, (decimal?)45000m, new DateOnly(2023, 3, 1)), (a.Privegebruik, a.Cataloguswaarde, a.Datum!.Value));
    }
}
