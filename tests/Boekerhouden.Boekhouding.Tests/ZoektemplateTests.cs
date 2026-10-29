using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class ZoektemplateTests
{
    private static readonly HerkendeFactuur Zonder = new(Herkenningsbron.Tekst, Naam: "Belnet B.V.", Factuurdatum: new(2026, 4, 1), TotaalInclusief: 24.99m);

    [Fact]
    public void Template_vult_aan_wat_de_herkenning_niet_vond()
    {
        var (factuur, ingevuld) = Zoektemplate.Pas(Zonder,
        [
            (new Zoekgebied(Factuurveld.Factuurnummer, 1, 400, 700, 560, 712), "Factuurnummer: BN-2026-0411"),
            (new Zoekgebied(Factuurveld.Factuurdatum, 1, 400, 680, 560, 692), "Datum 15-04-2026"),
            (new Zoekgebied(Factuurveld.Betalingskenmerk, 1, 40, 100, 300, 112), "Kenmerk 1234 5678 9012 3456"),
            (new Zoekgebied(Factuurveld.Omschrijving, 1, 40, 500, 300, 512), "Zakelijk   abonnement  april"),
        ]);

        Assert.Equal("BN-2026-0411", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 4, 1), factuur.Factuurdatum);
        Assert.Equal("1234567890123456", factuur.Betalingskenmerk);
        Assert.Equal("Zakelijk abonnement april", factuur.Omschrijving);
        Assert.Equal([Factuurveld.Factuurnummer, Factuurveld.Betalingskenmerk, Factuurveld.Omschrijving], ingevuld);
    }

    [Fact]
    public void Met_voorrang_gaat_het_aangewezen_gebied_voor()
    {
        var (factuur, ingevuld) = Zoektemplate.Pas(Zonder with { Factuurnummer = "KLANT-77" },
            [(new Zoekgebied(Factuurveld.Factuurnummer, 1, 0, 0, 1, 1, Voorrang: true), "2026-0412"), (new Zoekgebied(Factuurveld.Factuurdatum, 1, 0, 0, 1, 1, Voorrang: true), "15-04-2026")]);

        Assert.Equal(("2026-0412", new DateOnly(2026, 4, 15)), (factuur.Factuurnummer, factuur.Factuurdatum));
        Assert.Equal([Factuurveld.Factuurnummer, Factuurveld.Factuurdatum], ingevuld);
    }

    [Theory]
    [InlineData("Factuurnummer: 32401847603", "32401847603")]
    [InlineData("  BN-2026-0411 ", "BN-2026-0411")]
    [InlineData("Ref. DV/77/2026", "DV/77/2026")]
    [InlineData("Nr. BN-2026 0411", "BN-2026 0411")]
    [InlineData("Factuurnummer:", null)]
    [InlineData("Totaal", null)]
    public void Factuurnummer_uit_aangewezen_tekst(string tekst, string? verwacht) =>
        Assert.Equal(verwacht, Zoektemplate.Factuurnummer(tekst));

    [Fact]
    public void Niets_bruikbaars_in_het_gebied_laat_de_factuur_zoals_hij_is()
    {
        var (factuur, ingevuld) = Zoektemplate.Pas(Zonder, [(new Zoekgebied(Factuurveld.Vervaldatum, 1, 0, 0, 1, 1), "geen datum")]);

        Assert.Equal(Zonder, factuur);
        Assert.Empty(ingevuld);
    }
}
