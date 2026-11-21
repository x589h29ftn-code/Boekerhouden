using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Facturen;
using UglyToad.PdfPig;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De PDF's met veel kolommen of lange lijsten: de kolommenbalans per maand past op A4 liggend (niets buiten het
/// papier), en de openstaande posten van een relatie met meer posten dan op een pagina passen beginnen op de eerste
/// pagina in plaats van een lege eerste pagina met alleen de kop.
/// </summary>
public sealed class AfdrukopmaakTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static Exporttabel PerMaand(decimal groot)
    {
        string[] koppen = ["Beginbalans", .. Enumerable.Range(1, 12).Select(m => $"{new DateOnly(2025, m, 1):MMM} 25"), "Eindbalans"];
        var regels = Enumerable.Range(0, 12).Select(i => new Exportregel(Exportsoort.Rekening, $"{4000 + (i * 10)}", $"Kostenrekening {i}", 0,
            [null, .. Enumerable.Range(1, 12).Select(m => (decimal?)(-38_765.43m - m)), groot])).ToList();
        return new Exporttabel("Kolommenbalans per maand", new(2025, 1, 1), new(2025, 12, 31), koppen, regels);
    }

    [Fact]
    public void Per_maand_kleiner_gezet_en_vanaf_een_miljoen_in_hele_euros()
    {
        var gewoon = OverzichtExport.TabelHtml(PerMaand(-372_342.17m), "Afdruk B.V.");
        Assert.Contains("class=\"breed\"", gewoon, StringComparison.Ordinal);
        Assert.Contains("-372.342,17", gewoon, StringComparison.Ordinal);
        Assert.DoesNotContain("hele euro", gewoon, StringComparison.Ordinal);

        var miljoen = OverzichtExport.TabelHtml(PerMaand(-12_345_678.91m), "Afdruk B.V.");
        Assert.Contains("-12.345.679", miljoen, StringComparison.Ordinal);
        Assert.Contains("-38.766<", miljoen, StringComparison.Ordinal);
        Assert.Contains("Bedragen in hele euro's", miljoen, StringComparison.Ordinal);

        // Een gewone kolommenbalans (weinig kolommen) blijft zoals hij was.
        var smal = new Exporttabel("Kolommenbalans", new(2025, 1, 1), new(2025, 12, 31), ["Debet", "Credit"], [new(Exportsoort.Rekening, "4000", "Huur", 0, [1_234_567.89m, null])]);
        var smalHtml = OverzichtExport.TabelHtml(smal, "Afdruk B.V.");
        Assert.DoesNotContain("class=\"breed\"", smalHtml, StringComparison.Ordinal);
        Assert.Contains("1.234.567,89", smalHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Een_lange_relatie_mag_over_pagina_s_lopen_een_korte_blijft_bij_elkaar()
    {
        var lijst = Lijst(60, 3);
        var html = OpenpostenHtml.Maak(lijst);
        Assert.Contains("<div class=\"relatie\"><h2>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"relatie kort\"><h2>", html, StringComparison.Ordinal);
    }

    [PdfBrowserFact]
    public async Task Per_maand_past_op_het_papier_en_de_eerste_pagina_is_niet_leeg()
    {
        var pdf = app.Dienst<PdfDienst>();
        using (var document = PdfDocument.Open((await pdf.Maak(OverzichtExport.TabelHtml(PerMaand(-372_342.17m), "Afdruk B.V."))).Waarde!))
        {
            Assert.Equal(1, document.NumberOfPages);
            var pagina = document.GetPage(1);
            Assert.All(pagina.Letters, l => Assert.True(l.BoundingBox.Right <= pagina.Width, $"'{l.Value}' valt buiten het papier."));
        }

        using (var document = PdfDocument.Open((await pdf.Maak(OpenpostenHtml.Maak(Lijst(60, 3)))).Waarde!))
        {
            Assert.Contains("Lange Klant", document.GetPage(1).Text, StringComparison.Ordinal);
        }
    }

    private static Openpostenlijst Lijst(int lang, int kort)
    {
        Openpost Post(int i) => new(Guid.NewGuid(), new DateOnly(2025, 1, 1).AddDays(i), new DateOnly(2025, 1, 31).AddDays(i), "Levering", $"F-{i}", 100m + i, 0m, 300, "VK", $"2025-{i:0000}", null);
        return new("Afdruk B.V.", new(2025, 12, 31), Factuursoort.Verkoop,
        [
            new("LANG", "Lange Klant B.V.", Enumerable.Range(1, lang).Select(Post).ToList()),
            new("KORT", "Korte Klant B.V.", Enumerable.Range(1, kort).Select(Post).ToList()),
        ]);
    }
}
