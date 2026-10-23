using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class CorrectieDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private CorrectieDienst Correcties => server.Dienst<CorrectieDienst>();

    private RubriceerDienst Rubriceer => server.Dienst<RubriceerDienst>();

    /// <summary>De fictieve bakkerij met saldibalansen 2024 en 2025, dossiers voor beide jaren en enkele koppelingen.</summary>
    private async Task<(Guid KlantId, Guid Dossier2024, Guid Dossier2025, Dictionary<string, Guid> Rekeningen)> Bakkerij()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            var inhoud = TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv");
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, inhoud, $"{jaar}.csv", Tester)).IsGelukt);
        }

        var dossiers = server.Dienst<DossierDienst>();
        var d2024 = (await dossiers.Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var d2025 = (await dossiers.Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var o = (await Rubriceer.Overzicht(d2025, metVoorstellen: false))!;
        var rekeningen = o.Rekeningen.ToDictionary(r => r.Nummer, r => r.Id);
        await Rubriceer.Koppel(klantId, [rekeningen["0210"]], "BMvaBeiCae", false, Tester);
        await Rubriceer.Koppel(klantId, [rekeningen["4800"]], "WAfsAmvBei", false, Tester);
        await Rubriceer.Koppel(klantId, [rekeningen["0550"]], "BEivOvrOrs", false, Tester);
        return (klantId, d2024, d2025, rekeningen);
    }

    private static CorrectieInvoer Afschrijving(Dictionary<string, Guid> r, decimal bedrag, bool terugkerend = false, bool verwerkt = false) =>
        new("Extra afschrijving inventaris", CorrectieSoort.Correctie, terugkerend, verwerkt,
            [new(r["4800"], bedrag, "Afschrijving tweede halfjaar"), new(r["0210"], -bedrag)]);

    [Fact]
    public async Task Correctie_verandert_de_saldi_in_het_dossier_en_het_resultaat()
    {
        var (_, _, dossier, r) = await Bakkerij();
        var voor = (await Rubriceer.Overzicht(dossier, metVoorstellen: false))!;

        var uitkomst = await Correcties.Bewaar(dossier, null, Afschrijving(r, 2_340m), Tester);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var overzicht = (await Correcties.Overzicht(dossier))!;
        var boeking = Assert.Single(overzicht.Boekingen);
        Assert.Equal(1, boeking.Nummer);
        Assert.Equal(2_340m, boeking.Totaal);
        Assert.Equal(-2_340m, boeking.Effect.Resultaat);
        Assert.Equal(-2_340m, overzicht.Totaal.EigenVermogen);
        Assert.Equal("Afschrijving tweede halfjaar", boeking.Regels[0].Toelichting);

        var na = (await Rubriceer.Overzicht(dossier, metVoorstellen: false))!;
        var cumAfschrijving = na.Rekeningen.Single(x => x.Nummer == "0210");
        Assert.Equal(voor.Rekeningen.Single(x => x.Nummer == "0210").Saldo - 2_340m, cumAfschrijving.Saldo);
        Assert.Equal(-2_340m, cumAfschrijving.Correcties);
        Assert.Equal(voor.Balans!.ResultaatHuidig - 2_340m, na.Balans!.ResultaatHuidig);
        Assert.Equal(-2_340m, na.Balans.Totaal("BMva").Correcties);
        Assert.Equal(voor.Balans.Totaal("BMva").Huidig, na.Balans.Totaal("BMva").Administratie);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.CorrectieToegevoegd),
            a => a.Onderwerp!.EndsWith("correctie 1", StringComparison.Ordinal) && a.Details!.Contains("4800 € 2.340,00 debet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Boeking_niet_in_evenwicht_of_met_rekening_van_een_andere_klant_wordt_geweigerd()
    {
        var (_, _, dossier, r) = await Bakkerij();
        var (_, _, _, ander) = await Bakkerij();

        var scheef = await Correcties.Bewaar(dossier, null,
            new CorrectieInvoer("Scheef", CorrectieSoort.Correctie, false, false, [new(r["4800"], 100m), new(r["0210"], -90m)]), Tester);
        var vreemd = await Correcties.Bewaar(dossier, null,
            new CorrectieInvoer("Vreemd", CorrectieSoort.Correctie, false, false, [new(r["4800"], 100m), new(ander["0210"], -100m)]), Tester);

        Assert.Contains(scheef.Fouten, f => f.Contains("niet in evenwicht", StringComparison.Ordinal));
        Assert.Contains(vreemd.Fouten, f => f.Contains("van deze klant", StringComparison.Ordinal));
        Assert.Empty((await Correcties.Overzicht(dossier))!.Boekingen);
    }

    [Fact]
    public async Task Wijzigen_en_verwijderen_zetten_de_saldi_terug()
    {
        var (_, _, dossier, r) = await Bakkerij();
        var id = (await Correcties.Bewaar(dossier, null, Afschrijving(r, 1_000m), Tester)).Waarde;

        var wijziging = await Correcties.Bewaar(dossier, id, Afschrijving(r, 1_500m, verwerkt: true), Tester);
        Assert.True(wijziging.IsGelukt, string.Join("; ", wijziging.Fouten));
        var gewijzigd = Assert.Single((await Correcties.Overzicht(dossier))!.Boekingen);
        Assert.Equal(1_500m, gewijzigd.Totaal);
        Assert.True(gewijzigd.VerwerktBijKlant);

        Assert.True((await Correcties.Verwijder(dossier, id, Tester)).IsGelukt);
        Assert.Empty((await Correcties.Overzicht(dossier))!.Boekingen);
        Assert.Equal(0m, (await Rubriceer.Overzicht(dossier, metVoorstellen: false))!.Rekeningen.Single(x => x.Nummer == "0210").Correcties);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.CorrectieVerwijderd), a => a.Details!.Contains("1.500,00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Twee_regels_op_dezelfde_rekening_kan_en_is_te_verwijderen()
    {
        var (_, _, dossier, r) = await Bakkerij();
        var uitkomst = await Correcties.Bewaar(dossier, null,
            new CorrectieInvoer("Verdeling", CorrectieSoort.Herrubricering, false, false,
                [new(r["4800"], 300m), new(r["4800"], 200m), new(r["0210"], -500m)]), Tester);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal(500m, (await Rubriceer.Overzicht(dossier, metVoorstellen: false))!.Rekeningen.Single(x => x.Nummer == "4800").Correcties);
        Assert.True((await Correcties.Verwijder(dossier, uitkomst.Waarde, Tester)).IsGelukt);
    }

    [Fact]
    public async Task Nieuwe_rekening_voor_een_correctie_verschijnt_bij_het_rubriceren()
    {
        var (_, _, dossier, r) = await Bakkerij();

        var nieuw = await Correcties.MaakRekening(dossier, "1650", "Te betalen accountantskosten", Tester);
        var dubbel = await Correcties.MaakRekening(dossier, "1650", "Nogmaals", Tester);
        Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
        Assert.Contains(dubbel.Fouten, f => f.Contains("bestaat al", StringComparison.Ordinal));

        await Correcties.Bewaar(dossier, null,
            new CorrectieInvoer("Nog te betalen accountantskosten", CorrectieSoort.Correctie, false, false, [new(r["4500"], 1_500m), new(nieuw.Waarde, -1_500m)]), Tester);

        var o = (await Rubriceer.Overzicht(dossier, metVoorstellen: false))!;
        var rekening = o.Rekeningen.Single(x => x.Nummer == "1650");
        Assert.Equal(-1_500m, rekening.Saldo);
        Assert.Null(rekening.Koppeling);
        Assert.True((await Correcties.Overzicht(dossier))!.Rekeningen.Single(x => x.Nummer == "1650").AangemaaktBijCorrectie);
    }

    [Fact]
    public async Task Correcties_van_vorig_jaar_terugkerend_overnemen_en_vergelijkende_cijfers()
    {
        var (_, d2024, d2025, r) = await Bakkerij();
        var vorigVoor = (await Rubriceer.Overzicht(d2025, metVoorstellen: false))!.Rekeningen.Single(x => x.Nummer == "0210").SaldoVorig;
        await Correcties.Bewaar(d2024, null, Afschrijving(r, 900m), Tester);
        await Correcties.Bewaar(d2024, null,
            new CorrectieInvoer("Privégebruik auto", CorrectieSoort.Correctie, true, false, [new(r["0550"], 2_000m), new(r["4200"], -2_000m)]), Tester);

        var overzicht = (await Correcties.Overzicht(d2025))!;
        Assert.Equal("Extra afschrijving inventaris", Assert.Single(overzicht.VorigJaarOnverwerkt).Omschrijving);
        Assert.Equal(1, overzicht.TerugkerendOverTeNemen);

        var over = await Correcties.NeemTerugkerendeOver(d2025, Tester);
        Assert.Equal(1, over.Waarde);
        Assert.False((await Correcties.NeemTerugkerendeOver(d2025, Tester)).IsGelukt);
        var overgenomen = Assert.Single((await Correcties.Overzicht(d2025))!.Boekingen);
        Assert.True(overgenomen.Overgenomen);
        Assert.Equal(2_000m, overgenomen.Totaal);
        Assert.Equal(0, (await Correcties.Overzicht(d2025))!.TerugkerendOverTeNemen);

        // De vergelijkende cijfers in 2025 zijn de gecorrigeerde cijfers van 2024.
        var o = (await Rubriceer.Overzicht(d2025, metVoorstellen: false))!;
        Assert.Equal(vorigVoor - 900m, o.Rekeningen.Single(x => x.Nummer == "0210").SaldoVorig);
    }
}
