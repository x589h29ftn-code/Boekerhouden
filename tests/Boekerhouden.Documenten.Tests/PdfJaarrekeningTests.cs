using Boekerhouden.Boekhouding.Beginbalans;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Test die alleen draait als BOEKERHOUDEN_JAARREKENING naar een echte jaarrekening wijst (die staat niet in git).</summary>
internal sealed class EchteJaarrekeningFactAttribute : FactAttribute
{
    public EchteJaarrekeningFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_JAARREKENING") is not { Length: > 0 } pad || !File.Exists(pad))
        {
            Skip = "Zet BOEKERHOUDEN_JAARREKENING op een jaarrekening als PDF om deze test te draaien.";
        }
    }
}

/// <summary>De balans uit een jaarrekening als PDF: een verzonnen jaarrekening met inhoudsopgave, activa en passiva.</summary>
public sealed class PdfJaarrekeningTests
{
    [Fact]
    public void De_balans_wordt_gelezen_met_datum_posten_en_totalen()
    {
        var balans = PdfJaarrekening.Lees(FictieveJaarrekening.Pdf());

        Assert.NotNull(balans);
        Assert.Equal(new DateOnly(2025, 12, 31), balans.Balansdatum);
        Assert.Equal(
            [
                (Balanszijde.Activa, "Inventaris", 4000m), (Balanszijde.Activa, "Debiteuren", 2500m), (Balanszijde.Activa, "Rekening-courant directie", 750m),
                (Balanszijde.Activa, "Liquide middelen", 1250m), (Balanszijde.Passiva, "Aandelenkapitaal", 1000m), (Balanszijde.Passiva, "Overige reserves", 3300m),
                (Balanszijde.Passiva, "Overige schulden", 2000m), (Balanszijde.Passiva, "Crediteuren", 1700m), (Balanszijde.Passiva, "Overlopende passiva", 500m),
            ],
            balans.Posten.Select(p => (p.Zijde, p.Naam, p.Bedrag)));
        Assert.Equal("Langlopende schulden", balans.Posten.Single(p => p.Naam == "Overige schulden").Rubriek);
        Assert.Equal((8500m, 8500m), (balans.TotaalActiva, balans.TotaalPassiva));
        Assert.Empty(balans.Controle());
        Assert.Equal(0m, balans.Posten.Sum(p => p.Boekbedrag));
    }

    [Fact]
    public void Een_balans_die_niet_sluit_wordt_gemeld_en_een_gewone_pdf_heeft_geen_balans()
    {
        var balans = PdfJaarrekening.Lees(FictieveJaarrekening.Pdf(liquide: "1.240"))!;

        Assert.Equal(2, balans.Controle().Count);
        Assert.Contains(balans.Controle(), s => s.Contains("niet gelijk", StringComparison.Ordinal));

        var gewoon = new PdfDocumentBuilder();
        gewoon.AddPage(PageSize.A4).AddText("Factuur 2026-001 totaal 1.250", 10, new PdfPoint(50, 700), gewoon.AddStandard14Font(Standard14Font.Helvetica));
        Assert.Null(PdfJaarrekening.Lees(gewoon.Build()));
    }

    [EchteJaarrekeningFact]
    public void Een_echte_jaarrekening_sluit()
    {
        var balans = PdfJaarrekening.Lees(File.ReadAllBytes(Environment.GetEnvironmentVariable("BOEKERHOUDEN_JAARREKENING")!));

        Assert.NotNull(balans);
        Assert.True(balans.Controle().Count == 0, string.Join(" | ", balans.Posten.Select(p => $"{p.Zijde} [{p.Rubriek}] {p.Naam} {p.Bedrag}")) + " || " + string.Join(" ", balans.Controle()));
    }
}
