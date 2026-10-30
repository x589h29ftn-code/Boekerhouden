using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class PeriodeverdelingTests
{
    [Fact]
    public void Elke_maand_die_de_periode_raakt_telt_mee()
    {
        var verdeling = new Periodeverdeling(new(2026, 9, 1), new(2027, 6, 1), "1410");

        Assert.Equal(10, verdeling.Maanden.Count);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 1)), (verdeling.Maanden[0], verdeling.Maanden[^1]));
        Assert.Single(new Periodeverdeling(new(2026, 9, 1), new(2026, 9, 30), "1410").Maanden);
    }

    [Fact]
    public void Gelijke_delen_op_de_cent_en_het_restje_in_de_laatste_maand()
    {
        Assert.Equal([33.33m, 33.33m, 33.34m], Periodeverdeling.Verdeel(100m, 3));
        Assert.Equal([-33.33m, -33.33m, -33.34m], Periodeverdeling.Verdeel(-100m, 3));
        Assert.Equal(1000m, Periodeverdeling.Verdeel(1000m, 12).Sum());
    }

    [Fact]
    public void Per_maand_naar_de_kosten_en_af_van_de_verdeelrekening()
    {
        var verdeling = new Periodeverdeling(new(2026, 11, 15), new(2027, 1, 14), "1410");

        var maanden = verdeling.PerMaand([new Verdeelregel("4300", 100m), new Verdeelregel("4310", 30m)]);

        Assert.Equal(3, maanden.Count);
        Assert.All(maanden, m => Assert.Equal(0m, m.Regels.Sum(r => r.Bedrag)));
        Assert.Equal(-130m, maanden.Sum(m => m.Regels.Single(r => r.Rekeningnummer == "1410").Bedrag));
        Assert.Equal(100m, maanden.Sum(m => m.Regels.Single(r => r.Rekeningnummer == "4300").Bedrag));
        Assert.Equal(new DateOnly(2027, 1, 1), maanden[^1].Maand);
    }

    [Theory]
    [InlineData("2027-06-01", "2026-09-01", "De periode eindigt vóór het begin.")]
    [InlineData("2026-09-01", "2032-09-01", "Een verdeling over meer dan 60 maanden kan niet.")]
    public void Onmogelijke_periode(string van, string tot, string fout) =>
        Assert.Contains(fout, new Periodeverdeling(DateOnly.Parse(van, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(tot, System.Globalization.CultureInfo.InvariantCulture), "1410").Controleer());

    [Theory]
    [InlineData("Betreft: Verleende diensten in periode 01-09-2026 t/m 30-09-2026", "2026-09-01", "2026-09-30")]
    [InlineData("Jaarabonnement 1 september 2026 tot en met 31 augustus 2027", "2026-09-01", "2027-08-31")]
    [InlineData("Licentie   01-09-2026 - 31-08-2027   1.200,00", "2026-09-01", "2027-08-31")]
    [InlineData("Subscription period: May 1, 2026 to April 30, 2027", "2026-05-01", "2027-04-30")]
    public void Periode_van_de_factuur_herkennen(string regel, string van, string tot)
    {
        var factuur = Factuurtekst.Analyseer($"Softwarehuis B.V.\nFactuurnummer: 1\nFactuurdatum: 01-09-2026\n{regel}\nTotaal 10,00");

        Assert.Equal(new Factuurperiode(DateOnly.Parse(van, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(tot, System.Globalization.CultureInfo.InvariantCulture)), factuur.Periode);
    }

    [Fact]
    public void Geen_periode_bij_losse_datums()
    {
        Assert.Null(Factuurtekst.Analyseer("Iets B.V.\nFactuurdatum: 01-09-2026\nVervaldatum: 15-09-2026\nTotaal 10,00").Periode);
    }
}
