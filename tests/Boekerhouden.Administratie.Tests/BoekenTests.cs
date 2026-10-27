using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Npgsql;

namespace Boekerhouden.Administratie.Tests;

public sealed class BoekenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Boeken_vult_journaal_kaart_proefbalans_en_balans()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 2, 1), ("4310", 30.25m), ("1100", -30.25m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0003", new(2026, 3, 1), ("1100", 1210m), ("8000", -1000m), ("1510", -210m)))).IsGelukt);

        var kaart = await grootboek.Kaart(wie, id, "1100", new(2026, 1, 1), new(2026, 12, 31));
        var proefbalans = await grootboek.Proefbalans(wie, id, 2026);
        var overzicht = await grootboek.Jaaroverzicht(wie, id, 2026);

        Assert.Equal([5000m, 4969.75m, 6179.75m], kaart.Mutaties.Select(m => m.Saldo));
        Assert.Equal(0m, proefbalans.Saldo);
        Assert.Equal(969.75m, overzicht.Resultaat);
        Assert.Equal(overzicht.TotaalActiva, overzicht.TotaalPassiva);
        Assert.Equal(["WOmz", "WBed"], overzicht.Resultaatposten.Select(p => p.Code));
        Assert.Equal("2026-0004", await grootboek.VolgendBoekstuk(wie, id, "MEM", 2026));
        Assert.Equal([2026], await grootboek.Boekjaren(wie, id));
    }

    [Fact]
    public async Task Fouten_worden_gemeld_en_er_wordt_niets_geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 1, 1), ("1100", 10m), ("0500", -10m)));

        var uitkomst = await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 1, 2), ("1100", 10m), ("9999", -9m)));

        Assert.Contains("Regel 2 heeft een onbekende rekening.", uitkomst.Fouten);
        Assert.Contains(uitkomst.Fouten, f => f.StartsWith("De boeking is niet in evenwicht", StringComparison.Ordinal));
        Assert.Contains("Boekstuk 2026-0001 bestaat al in dagboek MEM.", uitkomst.Fouten);
        Assert.Single(await grootboek.Posten(wie, id));
    }

    [Fact]
    public async Task Keten_heeft_volgnummers_en_is_heel()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        var eerste = await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 1, 1), ("1100", 10m), ("0500", -10m)));
        var tweede = await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 1, 2), ("4300", 5m), ("1100", -5m)));

        Assert.Equal(1L, eerste.Waarde);
        Assert.Equal(2L, tweede.Waarde);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Gelijktijdig_boeken_houdt_de_keten_heel()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        var uitkomsten = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            grootboek.Boek(wie, id, Post($"2026-{i:0000}", new(2026, 1, i), ("4300", i), ("1100", -i)))));

        Assert.All(uitkomsten, u => Assert.True(u.IsGelukt, string.Join("; ", u.Fouten)));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), uitkomsten.Select(u => u.Waarde).Order());
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Database_weigert_wijzigen_en_verwijderen_van_boekingen_en_log()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await app.Dienst<GrootboekDienst>().Boek(wie, id, Post("2026-0001", new(2026, 1, 1), ("1100", 10m), ("0500", -10m)));

        var wijzigen = await Assert.ThrowsAsync<PostgresException>(() => app.VoerSqlUit("UPDATE \"Boekingsregels\" SET \"Bedrag\" = 99 WHERE \"Rekeningnummer\" = '1100'"));
        var verwijderen = await Assert.ThrowsAsync<PostgresException>(() => app.VoerSqlUit("DELETE FROM \"Boekingen\""));
        var log = await Assert.ThrowsAsync<PostgresException>(() => app.VoerSqlUit("DELETE FROM \"AuditRegels\""));

        Assert.Contains("kunnen niet worden gewijzigd of verwijderd", wijzigen.MessageText, StringComparison.Ordinal);
        Assert.Contains("Boekingen", verwijderen.MessageText, StringComparison.Ordinal);
        Assert.Contains("AuditRegels", log.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wie_de_triggers_omzeilt_breekt_de_keten_en_dat_is_te_zien()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 1, 1), ("1100", 10m), ("0500", -10m)));
        await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 1, 2), ("4300", 5m), ("1100", -5m)));

        // De eigenaar van de tabellen kan de trigger uitzetten; de keten laat het dan alsnog zien.
        await app.VoerSqlUit($"""
            ALTER TABLE "Boekingsregels" DISABLE TRIGGER boekingsregels_onwijzigbaar;
            UPDATE "Boekingsregels" SET "Bedrag" = "Bedrag" * 10
                WHERE "BoekingId" = (SELECT "Id" FROM "Boekingen" WHERE "AdministratieId" = '{id}' AND "Volgnummer" = 2);
            ALTER TABLE "Boekingsregels" ENABLE TRIGGER boekingsregels_onwijzigbaar;
            """);

        Assert.Equal([2L], await grootboek.ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Rekening_toevoegen_met_controles()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.True((await grootboek.VoegRekeningToe(wie, id, new("4320", "Hosting", Rekeningsoort.Resultaat, "WBedKanCea"))).IsGelukt is false);
        Assert.True((await grootboek.VoegRekeningToe(wie, id, new("4340", "Hosting", Rekeningsoort.Resultaat, "WBedKanCea"))).IsGelukt);
        Assert.Equal("Een RGS-code begint met B of W, gevolgd door letters, bijv. WBedKanTef.", (await grootboek.VoegRekeningToe(wie, id, new("4350", "X", Rekeningsoort.Resultaat, "kosten"))).Fouten[0]);
        Assert.Contains("resultaatrekening", (await grootboek.VoegRekeningToe(wie, id, new("4360", "X", Rekeningsoort.Resultaat, "BLimKasKas"))).Fouten[0], StringComparison.Ordinal);
        Assert.Equal("Hosting", (await grootboek.Schema(wie, id)).Zoek("4340")?.Naam);
    }

    private static Journaalpost Post(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal " + boekstuk, [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag))]);
}
