using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class OmschrijvingssjabloonTests
{
    private static readonly Factuurvariabelen Gegevens = new("2026-0412", "Zakelijk abonnement", "Belnet B.V.", new(2026, 10, 3), new(2026, 10, 17), "1234567890123456");

    [Theory]
    [InlineData("Telefoon #maand #jaar (#factuurnummer)", "Telefoon oktober 2026 (2026-0412)")]
    [InlineData("#Leverancier: #omschrijving", "Belnet B.V.: Zakelijk abonnement")]
    [InlineData("#kwartaal #jaar, vervalt #vervaldatum", "Q4 2026, vervalt 17-10-2026")]
    [InlineData("Artikel #123 en #onbekend", "Artikel #123 en #onbekend")]
    [InlineData("Kenmerk #kenmerk van #datum", "Kenmerk 1234567890123456 van 03-10-2026")]
    public void Variabelen_worden_gevuld(string sjabloon, string verwacht) =>
        Assert.Equal(verwacht, Omschrijvingssjabloon.Vul(sjabloon, Gegevens));

    [Fact]
    public void Lege_variabele_laat_geen_dubbele_spaties_achter() =>
        Assert.Equal("Telefoon oktober", Omschrijvingssjabloon.Vul("Telefoon #factuurnummer #maand", Gegevens with { Factuurnummer = null }));

    [Fact]
    public void Sjabloon_en_onbekende_variabelen_herkennen()
    {
        Assert.True(Omschrijvingssjabloon.IsSjabloon("Telefoon #maand"));
        Assert.False(Omschrijvingssjabloon.IsSjabloon("Artikel #123"));
        Assert.Equal(["factuurnumer"], Omschrijvingssjabloon.Onbekend("Telefoon #factuurnumer #maand"));
    }
}

public sealed class BedragcontroleTests
{
    [Fact]
    public void Honderd_euro_met_21_procent_is_121()
    {
        var regels = new[] { new Factuurregel("", "4310", "I21", 100m, 21m) };

        Assert.Empty(Bedragcontrole.Controleer(regels, Testadministratie.Btw, new Factuurbedragen(121m, 21m)));
        Assert.Equal(
            ["De boeking (€ 121,00) sluit niet op het totaal van de factuur (€ 125,00); verschil € -4,00."],
            Bedragcontrole.Controleer(regels, Testadministratie.Btw, new Factuurbedragen(125m)));
    }

    [Fact]
    public void Btw_die_niet_bij_het_tarief_past()
    {
        var regels = new[] { new Factuurregel("", "4310", "I21", 100m, 25m), new Factuurregel("", "4310", "I0", 50m, 3m) };

        var signalen = Bedragcontrole.Controleer(regels, Testadministratie.Btw, new Factuurbedragen(178m, 21m));

        Assert.Equal(
            [
                "Regel 1: 21% btw over € 100,00 is € 21,00, niet € 25,00.",
                "Regel 2: bij inkoop 0% of vrijgesteld staat geen btw op de factuur, maar wel € 3,00 in de boeking.",
                "De btw in de boeking (€ 28,00) is niet de btw op de factuur (€ 21,00).",
            ],
            signalen);
    }

    [Fact]
    public void Afronden_per_factuurregel_mag_een_cent_per_regel_schelen() =>
        Assert.Empty(Bedragcontrole.Controleer([new Factuurregel("", "4310", "I21", 100m, 21.02m)], Testadministratie.Btw, new Factuurbedragen(121.02m, 21.02m, Regels: 3)));
}

public sealed class PostcodeTests
{
    [Fact]
    public void Postcodes_in_een_tekst()
    {
        var tekst = "Belnet B.V.\nVerbindingsweg 1, 1234 AB Ergens\nAan: Bakkerij, 5678CD Nergens\nTotaal 2026 EUR 1.250,00\nOrder 1234 AB";

        Assert.Equal(["1234AB", "5678CD"], Postcode.Zoek(tekst));
        Assert.Equal("1234 AB", Postcode.Toon("1234ab"));
        Assert.Empty(Postcode.Zoek("Periode 2026 SA en 0123 AB en 1.234 AB"));
    }

    [Fact]
    public void Tekstfactuur_en_e_factuur_geven_de_postcodes()
    {
        var herkend = Factuurtekst.Analyseer("Belnet B.V.\nVerbindingsweg 1\n1234 AB Ergens\nFactuurnummer 12\nTotaal 24,99");
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "belnet-2026-0411.xml"));

        Assert.Equal(["1234AB"], herkend.Postcodes);
        Assert.Equal(["1234AB", "5678CD"], UblFactuurLezer.Lees(stream).Postcodes);
    }
}
