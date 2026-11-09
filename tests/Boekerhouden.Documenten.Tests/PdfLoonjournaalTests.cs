namespace Boekerhouden.Documenten.Tests;

/// <summary>Het journaal van een salarispakket uit een PDF lezen: debet links, credit rechts, bedragen per kostenplaats opgeteld.</summary>
public sealed class PdfLoonjournaalTests
{
    [Fact]
    public void Rekeningen_met_opgetelde_bedragen_periode_en_totalen()
    {
        var journaal = PdfLoonjournaal.Lees(FictiefLoonjournaal.Pdf())!;

        Assert.Equal(
            [("4000", "Brutolonen", 7_000m), ("1500", "Af te dragen loonheffing", -2_500m), ("4010", "Sociale lasten", 3_000m), ("2100", "Netto loon", -7_500m)],
            journaal.Regels.Select(r => (r.Nummer, r.Naam, r.Bedrag)));
        Assert.Equal(("Voorbeeld Bouw B.V.", "2026-1", "2026-3"), (journaal.Werkgever, journaal.PeriodeVan, journaal.PeriodeTot));
        Assert.Equal((10_000m, 10_000m), (journaal.TotaalDebet, journaal.TotaalCredit));
        Assert.Empty(journaal.Controle());
    }

    [Fact]
    public void Afwijkend_totaal_geeft_een_signaal()
    {
        var journaal = PdfLoonjournaal.Lees(FictiefLoonjournaal.Pdf("10.100,00"))!;

        Assert.Equal(2, journaal.Controle().Count());
        Assert.Contains("vermeldt 10.100,00", journaal.Controle().First(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FictiefLoonjournaal.Lijstsoort.Kolommen)]
    [InlineData(FictiefLoonjournaal.Lijstsoort.DebetCredit)]
    [InlineData(FictiefLoonjournaal.Lijstsoort.Teken)]
    public void Een_lijst_met_debet_en_credit_als_kolom_als_DC_of_als_teken(FictiefLoonjournaal.Lijstsoort soort)
    {
        var journaal = PdfLoonjournaal.Lees(FictiefLoonjournaal.Lijst(soort))!;

        Assert.Equal(
            [("4000", "Brutolonen", 6_000m), ("4010", "Sociale lasten", 1_000m), ("1500", "Af te dragen loonheffing", -2_500m), ("2100", "Netto loon", -4_500m)],
            journaal.Regels.Select(r => (r.Nummer, r.Naam, r.Bedrag)));
        Assert.Equal((7_000m, 7_000m), (journaal.Debet, journaal.Credit));
        Assert.Empty(journaal.Controle());
        Assert.Equal(("2026-3", "2026-3"), (journaal.PeriodeVan, journaal.PeriodeTot));
    }
}
