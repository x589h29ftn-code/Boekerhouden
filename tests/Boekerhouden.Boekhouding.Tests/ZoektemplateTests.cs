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

public sealed class ZoektemplateBedragenTests
{
    // De herkenning las het totaal uit een specificatie: exclusief plus btw sluit niet.
    private static readonly HerkendeFactuur Fout = new(Herkenningsbron.Tekst, Naam: "Softwarehuis Delta B.V.", TotaalExclusief: -25.71m, TotaalInclusief: 3.96m,
        Btw: [new Btwregel(21m, null, 29.67m)]);

    [Fact]
    public void Aangewezen_exclusief_bedrag_gaat_voor_als_de_bedragen_niet_sluiten_en_het_totaal_volgt()
    {
        var (factuur, ingevuld) = Zoektemplate.Pas(Fout, [(new Zoekgebied(Factuurveld.Exclusief, 1, 0, 0, 1, 1, Voorrang: true), "€141,28")]);

        Assert.Equal((141.28m, 170.95m, 141.28m), (factuur.TotaalExclusief, factuur.TotaalInclusief, factuur.Btwregels.Single().Grondslag));
        Assert.Equal([Factuurveld.Exclusief], ingevuld);
    }

    [Fact]
    public void Aangewezen_totaal_vult_aan_en_het_exclusieve_bedrag_volgt_uit_de_btw()
    {
        var (factuur, _) = Zoektemplate.Pas(Fout with { TotaalExclusief = null, TotaalInclusief = null },
            [(new Zoekgebied(Factuurveld.Totaal, 1, 0, 0, 1, 1), "Totaal (incl. BTW)  €170,95")]);

        Assert.Equal((141.28m, 170.95m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
    }

    [Fact]
    public void Sluitende_bedragen_blijven_staan_ook_met_voorrang()
    {
        var goed = Fout with { TotaalExclusief = 135.52m, TotaalInclusief = 163.98m, Btw = [new Btwregel(21m, 135.52m, 28.46m)] };

        var (factuur, ingevuld) = Zoektemplate.Pas(goed, [(new Zoekgebied(Factuurveld.Exclusief, 1, 0, 0, 1, 1, Voorrang: true), "€ 0,18")]);

        Assert.Equal(goed, factuur);
        Assert.Empty(ingevuld);
    }

    [Fact]
    public void Aangewezen_btw_zonder_herkende_btw_krijgt_het_passende_tarief()
    {
        var (factuur, _) = Zoektemplate.Pas(Fout with { Btw = null, TotaalExclusief = 100m, TotaalInclusief = 109m },
            [(new Zoekgebied(Factuurveld.Btw, 1, 0, 0, 1, 1), "9,00")]);

        Assert.Equal(new Btwregel(9m, 100m, 9m), factuur.Btwregels.Single());
    }
}
