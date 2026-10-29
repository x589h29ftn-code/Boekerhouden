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
}
