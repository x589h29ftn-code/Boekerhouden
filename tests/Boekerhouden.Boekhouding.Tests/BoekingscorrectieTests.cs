using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class BoekingscorrectieTests
{
    private static readonly Rekeningschema Schema = new(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

    private static readonly Journaalpost Memoriaal = new("MEM", "2026-0001", new(2026, 3, 31), "Afschrijving",
        [new("4300", 100m), new("1500", 21m), new("4310", -121m)]);

    private static readonly Journaalpost Bank = new("BNK", "2026-0004", new(2026, 3, 2), "Telefoon",
        [new("4310", 50m, BtwCode: "I21"), new("1500", 10.50m), new("1100", -60.50m)]);

    [Fact]
    public void Een_memoriaalboeking_mag_helemaal_anders()
    {
        var nieuw = Memoriaal with { Datum = new(2026, 4, 30), Regels = [new("4320", 100m), new("1500", 21m), new("4310", -121m)] };

        Assert.Empty(Boekingscorrectie.Controleer(Memoriaal, nieuw, Schema));
    }

    [Fact]
    public void Zonder_wijziging_is_er_niets_aan_te_passen()
    {
        var zelfde = Memoriaal with { Omschrijving = " Afschrijving ", Regels = [.. Memoriaal.Regels.Select(r => r with { Omschrijving = "" })] };

        Assert.Equal(["Er is niets gewijzigd."], Boekingscorrectie.Controleer(Memoriaal, zelfde, Schema));
    }

    [Fact]
    public void Bij_een_bankboeking_blijven_de_bankregel_en_de_datum_gelijk()
    {
        var andereKosten = Bank with { Regels = [new("4300", 50m, BtwCode: "I21"), new("1500", 10.50m), new("1100", -60.50m)] };
        Assert.Empty(Boekingscorrectie.Controleer(Bank, andereKosten, Schema));

        var anderBedrag = Bank with { Regels = [new("4310", 50m), new("1500", 10m), new("1100", -60m)] };
        Assert.Equal(["De regels op 1100 horen bij het bankafschrift of de openstaande posten en kunnen niet veranderen."], Boekingscorrectie.Controleer(Bank, anderBedrag, Schema));

        var andereDatum = andereKosten with { Datum = new(2026, 3, 3) };
        Assert.Equal(["De datum kan niet veranderen: de boeking hoort bij het bankafschrift of de openstaande posten."], Boekingscorrectie.Controleer(Bank, andereDatum, Schema));
    }

    [Fact]
    public void Afgeletterde_crediteurregel_blijft_met_relatie_en_factuur_gelijk()
    {
        var betaling = new Journaalpost("BNK", "2026-0005", new(2026, 3, 9), "Betaling",
            [new("1600", 121m, Relatie: "JANSEN", Factuurnummer: "F-1"), new("1100", -121m)]);
        var andereFactuur = betaling with { Regels = [new("1600", 121m, Relatie: "JANSEN", Factuurnummer: "F-2"), new("1100", -121m)] };

        Assert.Single(Boekingscorrectie.Controleer(betaling, andereFactuur, Schema));
        Assert.Equal(["Het dagboek kan bij een aanpassing niet veranderen."], Boekingscorrectie.Controleer(Memoriaal, Memoriaal with { Dagboek = "INK", Omschrijving = "x" }, Schema));
        Assert.True(Boekingscorrectie.IsVast(Schema.Zoek("1300")));
        Assert.False(Boekingscorrectie.IsVast(Schema.Zoek("1500")));
    }

    [Fact]
    public void In_het_memoriaal_staan_alleen_regels_met_een_openstaande_post_vast()
    {
        var oud = new Journaalpost("MEM", "2026-0002", new(2026, 3, 15), "Correctie", [new("4300", 250m), new("1100", -250m)]);
        var anders = oud with { Datum = new(2026, 3, 16), Regels = [new("4300", 200m), new("1100", -200m)] };
        Assert.Empty(Boekingscorrectie.Controleer(oud, anders, Schema, memoriaal: true));
        Assert.NotEmpty(Boekingscorrectie.Controleer(oud, anders, Schema));

        var factuur = new Journaalpost("MEM", "2026-0003", new(2026, 1, 1), "Factuur vorig jaar",
            [new("4300", 100m), new("1600", -100m, Relatie: "JANSEN", Factuurnummer: "F-9")]);
        Assert.Empty(Boekingscorrectie.Controleer(factuur, factuur with { Regels = [new("4320", 100m), factuur.Regels[1]] }, Schema, memoriaal: true));
        Assert.Equal(["De regels op 1600 met een factuurnummer horen bij de openstaande posten en kunnen niet veranderen."],
            Boekingscorrectie.Controleer(factuur, factuur with { Regels = [new("4300", 90m), new("1600", -90m, Relatie: "JANSEN", Factuurnummer: "F-9")] }, Schema, memoriaal: true));
        Assert.False(Boekingscorrectie.IsVast(new Journaalregel("1600", -100m), Schema, memoriaal: true));
        Assert.True(Boekingscorrectie.IsVast(factuur.Regels[1], Schema, memoriaal: true));
    }
}
