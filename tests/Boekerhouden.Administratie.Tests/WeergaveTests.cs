using Boekerhouden.Administratie.Components.Gedeeld;

namespace Boekerhouden.Administratie.Tests;

public sealed class WeergaveTests
{
    private static readonly string[] Alle = ["werkvoorraad", "viewer", "btwbedrag", "verval"];
    private static readonly string[] Eerder = ["viewer", "btwbedrag", "verval"];

    [Fact]
    public void Zonder_keuze_de_standaard() =>
        Assert.Equal(Alle.ToHashSet(), Weergavekeuze.Lees(null, Alle, Alle, Eerder));

    [Fact]
    public void Een_nieuw_onderdeel_volgt_de_standaard_bij_een_oude_keuze() =>
        Assert.Equal(new HashSet<string> { "viewer", "werkvoorraad" }, Weergavekeuze.Lees("viewer", Alle, Alle, Eerder));

    [Fact]
    public void Een_bewaarde_keuze_blijft_zoals_hij_is_en_onbekende_onderdelen_vallen_weg()
    {
        var tekst = Weergavekeuze.Tekst(["verval", "oud"], Alle);

        Assert.Equal(new HashSet<string> { "verval" }, Weergavekeuze.Lees(tekst, Alle, Alle, Eerder));
    }

    [Fact]
    public void Bedragen_op_het_scherm_met_een_echt_minteken()
    {
        Assert.Equal("-1.234,56", Weergave.Bedrag(-1234.56m));
        Assert.Equal("1.234,56", Weergave.Bedrag(1234.56m));
        Assert.Equal("-81", Weergave.BedragHeel(-80.5m));
        Assert.True(Domain.BedragParser.TryParse(Weergave.Bedrag(-25m), out var terug));
        Assert.Equal(-25m, terug);
    }

    [Fact]
    public void Een_tijdstip_zoals_je_het_leest()
    {
        var nu = new DateTimeOffset(2026, 10, 31, 17, 0, 0, TimeSpan.Zero);
        Assert.Equal("vandaag 18:32", Weergave.Moment(new DateTimeOffset(2026, 10, 31, 17, 32, 0, TimeSpan.Zero), nu));
        Assert.Equal("gisteren 09:05", Weergave.Moment(new DateTimeOffset(2026, 10, 30, 8, 5, 0, TimeSpan.Zero), nu));
        Assert.Equal("di 14:10", Weergave.Moment(new DateTimeOffset(2026, 10, 27, 13, 10, 0, TimeSpan.Zero), nu));
        Assert.Equal("01-10-2026 12:00", Weergave.Moment(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), nu));
    }

    [Fact]
    public void Bedragen_onder_elkaar_in_hele_euros_tellen_op_tot_het_afgeronde_totaal()
    {
        decimal[] bedragen = [10.40m, 10.40m, 10.40m, -0.60m];

        var heel = Weergave.HeleEuros(bedragen);

        Assert.Equal(Domain.Afronding.OpHeleEuro(bedragen.Sum()), heel.Sum());
        Assert.Equal([11m, 11m, 10m, -1m], heel);
        Assert.Empty(Weergave.HeleEuros([]));
    }

    [Fact]
    public void Tegenpartij_zonder_naam_is_het_begin_van_de_omschrijving()
    {
        Assert.Equal("Belnet B.V.", Weergave.Tegenpartij(" Belnet B.V. ", "Factuur 12"));
        Assert.Equal("Kosten Zakelijk Betalingsverkeer", Weergave.Tegenpartij(null, "Kosten Zakelijk Betalingsverkeer"));
        Assert.Equal(50, Weergave.Tegenpartij("", new string('x', 80)).Length);
        Assert.EndsWith("…", Weergave.Tegenpartij(null, new string('x', 80)), StringComparison.Ordinal);
        Assert.Equal("Onbekende tegenpartij", Weergave.Tegenpartij(null, " "));
    }
}
