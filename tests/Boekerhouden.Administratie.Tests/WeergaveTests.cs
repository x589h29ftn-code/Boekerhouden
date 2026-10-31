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
        Assert.Equal("\u22121.234,56", Weergave.Bedrag(-1234.56m));
        Assert.Equal("1.234,56", Weergave.Bedrag(1234.56m));
        Assert.Equal("\u221281", Weergave.BedragHeel(-80.5m));
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
}
