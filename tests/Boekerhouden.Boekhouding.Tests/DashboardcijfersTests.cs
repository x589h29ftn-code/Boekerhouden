using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De cijfers achter het dashboard: de btw-periode en omzet, inkoop en resultaat per maand.</summary>
public sealed class DashboardcijfersTests
{
    [Theory]
    [InlineData(2026, 10, 15, Aangiftefrequentie.Kwartaal, "2026-07-01", "2026-09-30", "3e kwartaal 2026", "2026-10-01")]
    [InlineData(2026, 10, 1, Aangiftefrequentie.Kwartaal, "2026-07-01", "2026-09-30", "3e kwartaal 2026", "2026-10-01")]
    [InlineData(2026, 1, 15, Aangiftefrequentie.Kwartaal, "2025-10-01", "2025-12-31", "4e kwartaal 2025", "2026-01-01")]
    [InlineData(2026, 3, 1, Aangiftefrequentie.Maand, "2026-02-01", "2026-02-28", "februari 2026", "2026-03-01")]
    [InlineData(2026, 6, 30, Aangiftefrequentie.Jaar, "2025-01-01", "2025-12-31", "2025", "2026-01-01")]
    public void De_afgelopen_aangifteperiode_en_de_lopende(int jaar, int maand, int dag, Aangiftefrequentie frequentie, string van, string tot, string naam, string lopend)
    {
        var afgelopen = Aangifteperiode.Afgelopen(new DateOnly(jaar, maand, dag), frequentie);

        Assert.Equal((DateOnly.Parse(van, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(tot, System.Globalization.CultureInfo.InvariantCulture), naam), (afgelopen.Van, afgelopen.Tot, afgelopen.Naam));
        Assert.Equal(DateOnly.Parse(lopend, System.Globalization.CultureInfo.InvariantCulture), afgelopen.Volgende.Van);
    }

    [Fact]
    public void Omzet_inkoop_overige_kosten_en_resultaat_per_maand()
    {
        var journaal = new Journaal(new Rekeningschema(
        [
            .. Testadministratie.Schema().Alle,
            new Rekening("7000", "Inkoopwaarde", Rekeningsoort.Resultaat, "WKprInhInh"),
        ]), [Testadministratie.Memoriaal]);
        journaal.Boek(JournaalTests.Memoriaal("1", new(2026, 2, 10), ("1300", 1000m), ("8000", -1000m)));
        journaal.Boek(JournaalTests.Memoriaal("2", new(2026, 2, 20), ("7000", 400m), ("1600", -400m)));
        journaal.Boek(JournaalTests.Memoriaal("3", new(2026, 2, 28), ("4300", 25m), ("1100", -25m)));
        journaal.Boek(JournaalTests.Memoriaal("4", new(2026, 3, 5), ("8000", 100m), ("1300", -100m)));

        var maanden = Maandcijfers.Uit(journaal.Maandoverzicht(new(2026, 1, 1), new(2026, 3, 31)));

        Assert.Equal(3, maanden.Count);
        Assert.Equal(new Maandcijfer(new(2026, 1, 1), 0m, 0m, 0m, 0m), maanden[0]);
        Assert.Equal(new Maandcijfer(new(2026, 2, 1), 1000m, 400m, 25m, 575m), maanden[1]);
        Assert.Equal(425m, maanden[1].Kosten);
        Assert.Equal((-100m, -100m), (maanden[2].Omzet, maanden[2].Resultaat));
    }
}
