using System.Diagnostics;
using System.Globalization;
using static Boekerhouden.Documenten.Tests.FictiefLoonjournaal;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Stresstest van het lezen van een loonjournaal: grote en negatieve bedragen, honderden rekeningen over meerdere
/// pagina's, beide opbouwen, rekeningnummers van 3 tot 8 cijfers, namen met cijfers, nul-bedragen, niet sluitend en
/// geen journaal.
/// </summary>
public sealed class PdfLoonjournaalStressTests
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private static string B(decimal bedrag) => bedrag.ToString("#,##0.00", Nl);

    /// <summary>Een sluitend journaal met <paramref name="aantal"/> debetrekeningen en evenveel creditrekeningen, met grote en kleine bedragen.</summary>
    private static (List<Rekening> Rekeningen, decimal Totaal) Journaal(int aantal)
    {
        var rekeningen = new List<Rekening>();
        var totaal = 0m;
        for (var i = 0; i < aantal; i++)
        {
            // Van centen tot honderden miljoenen.
            var bedrag = i % 5 == 0 ? 123_456_789.01m + i : (i * 7919 % 1_000_000) + (i % 100 / 100m) + 0.01m;
            totaal += bedrag;
            rekeningen.Add(new Rekening($"{4000 + i}", $"Looncomponent {i} premie WIA 2026", B(bedrag), Credit: false));
            rekeningen.Add(new Rekening($"{(i % 3 == 0 ? 10_000_000 + i : 1000 + i)}", $"Af te dragen post {i}", B(bedrag), Credit: true));
        }

        return (rekeningen, totaal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Honderden_rekeningen_over_meerdere_paginas_met_grote_bedragen(bool tweeKolommen)
    {
        var (rekeningen, totaal) = Journaal(150);
        var pdf = Maak(rekeningen, B(totaal), B(totaal), tweeKolommen);
        var klok = Stopwatch.StartNew();

        var journaal = PdfLoonjournaal.Lees(pdf)!;

        Assert.True(klok.ElapsedMilliseconds < 10_000, $"{klok.ElapsedMilliseconds} ms");
        Assert.Equal(300, journaal.Regels.Count);
        Assert.Equal((totaal, totaal), (journaal.Debet, journaal.Credit));
        Assert.Empty(journaal.Controle());
        Assert.Equal(123_456_789.01m, journaal.Regels.Single(r => r.Nummer == "4000").Bedrag);
        Assert.Equal(-123_456_789.01m, journaal.Regels.Single(r => r.Nummer == "10000000").Bedrag);
        Assert.Equal("Looncomponent 7 premie WIA 2026", journaal.Regels.Single(r => r.Nummer == "4007").Naam);
    }

    [Fact]
    public void Negatieve_correcties_in_beide_kolommen_houden_hun_teken()
    {
        Rekening[] rekeningen =
        [
            new("4000", "Brutolonen", "10.000,00", false),
            new("4005", "Correctie lonen vorige periode", "-1.250,00", false),
            new("1500", "Af te dragen loonheffing", "3.000,00", true),
            new("1505", "Correctie loonheffing", "-250,00", true),
            new("2100", "Netto loon", "6.000,00", true),
        ];

        foreach (var tweeKolommen in new[] { false, true })
        {
            var journaal = PdfLoonjournaal.Lees(Maak(rekeningen, "8.750,00", "8.750,00", tweeKolommen))!;

            Assert.Equal([10_000m, -1_250m, -3_000m, 250m, -6_000m], journaal.Regels.OrderBy(r => r.Nummer == "4000" ? 0 : r.Nummer == "4005" ? 1 : r.Nummer == "1500" ? 2 : r.Nummer == "1505" ? 3 : 4).Select(r => r.Bedrag));
            Assert.Equal(0m, journaal.Regels.Sum(r => r.Bedrag));

            // Het vermelde kolomtotaal (met de correcties erin) klopt: geen melding.
            Assert.Empty(journaal.Controle());
        }
    }

    [Fact]
    public void Nul_bedragen_vallen_weg_en_niet_sluitend_wordt_gemeld()
    {
        Rekening[] rekeningen =
        [
            new("4000", "Brutolonen", "1.000,00", false),
            new("4010", "Sociale lasten", "0,00", false),
            new("2100", "Netto loon", "999,99", true),
        ];

        var journaal = PdfLoonjournaal.Lees(Maak(rekeningen, "1.000,00", "1.000,00"))!;

        Assert.DoesNotContain(journaal.Regels, r => r.Nummer == "4010");
        Assert.Contains(journaal.Controle(), c => c.StartsWith("Niet in balans: debet 1.000,00, credit 999,99.", StringComparison.Ordinal));
        Assert.Contains(journaal.Controle(), c => c.Contains("vermeldt 1.000,00", StringComparison.Ordinal));
    }

    [Fact]
    public void Geen_loonjournaal_geeft_niets()
    {
        Assert.Null(PdfLoonjournaal.Lees(FictieveJaarrekening.Pdf()));
        Assert.Null(PdfLoonjournaal.Lees(Maak([], "0,00", "0,00")));
    }
}
