using System.Data.Common;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

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
    public async Task Overzichten_over_een_periode_zijn_gelijk_aan_die_uit_het_hele_journaal()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        var nummer = 0;
        foreach (var jaar in new[] { 2024, 2025, 2026 })
        {
            for (var maand = 1; maand <= 12; maand++)
            {
                var d = new DateOnly(jaar, maand, 10);
                Assert.True((await grootboek.Boek(wie, id, Post($"{jaar}-{++nummer:0000}", d, ("1100", 1210m + maand), ("8000", -1000m - maand), ("1510", -210m)))).IsGelukt);
                Assert.True((await grootboek.Boek(wie, id, Post($"{jaar}-{++nummer:0000}", d, ("4310", 30.25m * maand), ("1100", -30.25m * maand)))).IsGelukt);
            }
        }

        // Het hele journaal, zoals het domein het rekent.
        var alles = (await grootboek.Posten(wie, id)).OrderBy(p => p.Volgnummer).Select(p => p.Post);
        var journaal = Journaal.Herstel(await grootboek.Schema(wie, id), await grootboek.Dagboeken(wie, id), alles);

        Assert.Equal(journaal.Proefbalans(2026).Regels, (await grootboek.Proefbalans(wie, id, 2026)).Regels);
        Assert.Equal(journaal.Kolommenbalans(new(2025, 4, 1), new(2025, 9, 30)).Regels, (await grootboek.Kolommenbalans(wie, id, new(2025, 4, 1), new(2025, 9, 30))).Regels);
        var maandenVerwacht = journaal.Maandoverzicht(new(2025, 4, 1), new(2026, 3, 31));
        var maanden = await grootboek.Maandoverzicht(wie, id, new(2025, 4, 1), new(2026, 3, 31));
        Assert.Equal(maandenVerwacht.Regels.Select(r => (r.Rekening, r.Begin, r.Eind, string.Join(";", r.PerMaand))), maanden.Regels.Select(r => (r.Rekening, r.Begin, r.Eind, string.Join(";", r.PerMaand))));
        foreach (var rekening in new[] { "1100", "4310", "8000" })
        {
            var verwacht = journaal.Kaart(rekening, new(2025, 7, 1), new(2026, 2, 28));
            var kaart = await grootboek.Kaart(wie, id, rekening, new(2025, 7, 1), new(2026, 2, 28));
            Assert.Equal((verwacht.Beginsaldo, verwacht.Mutaties.Count), (kaart.Beginsaldo, kaart.Mutaties.Count));
            Assert.Equal(verwacht.Mutaties, kaart.Mutaties);
        }

        Assert.Equal(24, (await grootboek.Posten(wie, id, van: new(2026, 1, 1), tot: new(2026, 12, 31))).Count);
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

        var wijzigen = await Assert.ThrowsAnyAsync<DbException>(() => app.VoerSqlUit("UPDATE \"Boekingsregels\" SET \"Bedrag\" = 99 WHERE \"Rekeningnummer\" = '1100'"));
        var verwijderen = await Assert.ThrowsAnyAsync<DbException>(() => app.VoerSqlUit("DELETE FROM \"Boekingen\""));
        var log = await Assert.ThrowsAnyAsync<DbException>(() => app.VoerSqlUit("DELETE FROM \"AuditRegels\""));

        Assert.Contains("kunnen niet worden gewijzigd of verwijderd", wijzigen.Message, StringComparison.Ordinal);
        Assert.Contains("Boekingen", verwijderen.Message, StringComparison.Ordinal);
        Assert.Contains("AuditRegels", log.Message, StringComparison.Ordinal);
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
        var wijziging = $"""
            UPDATE "Boekingsregels" SET "Bedrag" = "Bedrag" * 10
                WHERE "BoekingId" = (SELECT "Id" FROM "Boekingen" WHERE "AdministratieId" = '{AdministratieFixture.SqlId(id)}' AND "Volgnummer" = 2);
            """;
        await app.VoerSqlUit(AdministratieFixture.IsSqlite
            ? $"DROP TRIGGER boekingsregels_onwijzigbaar_update; {wijziging}"
            : $"""ALTER TABLE "Boekingsregels" DISABLE TRIGGER boekingsregels_onwijzigbaar; {wijziging} ALTER TABLE "Boekingsregels" ENABLE TRIGGER boekingsregels_onwijzigbaar;""");

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

        // Een btw-code bij de rekening (of leeg = variabel); een onbekende code kan niet.
        Assert.Equal("Btw-code X99 bestaat niet.", (await grootboek.VoegRekeningToe(wie, id, new("4370", "Drukwerk", Rekeningsoort.Resultaat, null, "X99"))).Fouten[0]);
        Assert.True((await grootboek.VoegRekeningToe(wie, id, new("4370", "Drukwerk", Rekeningsoort.Resultaat, null, "I21"))).IsGelukt);
        var schema = await grootboek.Schema(wie, id);
        Assert.Equal("I21", schema.Zoek("4370")!.BtwCode);
        Assert.Null(schema.Zoek("4340")!.BtwCode);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.RekeningToegevoegd && r.Onderwerp == "4370 Drukwerk" && r.Details == "btw I21");
        Assert.Equal("Hosting", (await grootboek.Schema(wie, id)).Zoek("4340")?.Naam);
    }

    [Fact]
    public async Task Boeking_aanpassen_is_een_nieuwe_versie_met_hetzelfde_boekstuk()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 3, 31), ("4300", 100m), ("1400", -100m)))).IsGelukt);

        var nieuw = Post("x", new(2026, 4, 30), ("4320", 80m), ("4300", 20m), ("1400", -100m)) with { Omschrijving = "Kleine aanschaffing" };
        var uitkomst = await grootboek.PasAan(wie, id, "MEM", "2026-0001", nieuw);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal(2, uitkomst.Waarde);
        var proef = await grootboek.Proefbalans(wie, id, 2026);
        Assert.Equal((20m, 80m, -100m), (Saldo(proef, "4300"), Saldo(proef, "4320"), Saldo(proef, "1400")));
        var posten = await grootboek.Posten(wie, id);
        Assert.Equal(("2026-0001", "Kleine aanschaffing", new DateOnly(2026, 4, 30)), (Assert.Single(posten).Post.Boekstuk, posten[0].Post.Omschrijving, posten[0].Post.Datum));
        var detail = (await grootboek.Boeking(wie, id, "MEM", "2026-0001"))!;
        Assert.Equal((2, "Kleine aanschaffing", (string?)null), (detail.Versie, detail.Boeking.Post.Omschrijving, detail.Vast));
        var eerder = Assert.Single(detail.Eerder!);
        Assert.Equal((1, 100m), (eerder.Versie, eerder.Boeking.Post.Regels[0].Bedrag));
        Assert.Equal("2026-0002", await grootboek.VolgendBoekstuk(wie, id, "MEM", 2026));
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BoekingAangepast && r.Onderwerp == "MEM 2026-0001"
            && r.Details!.StartsWith("versie 2; Kleine aanschaffing; 100.00", StringComparison.Ordinal));

        // Nog eens aanpassen geeft versie 3; een ongewijzigde of onevenwichtige aanpassing niet.
        Assert.Equal(["Er is niets gewijzigd."], (await grootboek.PasAan(wie, id, "MEM", "2026-0001", detail.Boeking.Post)).Fouten);
        Assert.Contains("niet in evenwicht", (await grootboek.PasAan(wie, id, "MEM", "2026-0001", Post("x", new(2026, 4, 30), ("4320", 80m), ("1400", -100m)))).Fouten[0], StringComparison.Ordinal);
        Assert.Equal(3, (await grootboek.PasAan(wie, id, "MEM", "2026-0001", nieuw with { Omschrijving = "Toch anders" })).Waarde);
        Assert.Equal(2, (await grootboek.Boeking(wie, id, "MEM", "2026-0001"))!.Eerder!.Count);
        Assert.False((await grootboek.PasAan(wie, id, "MEM", "2099-0001", nieuw)).IsGelukt);

        // Het memoriaal is voor het kantoor; buiten de administratie geen toegang.
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Data.Lidmaatschapsrol.Ondernemer, wie);
        Assert.Equal(["Je hebt in deze administratie niet het recht om hier te boeken."], (await grootboek.PasAan(ondernemer, id, "MEM", "2026-0001", nieuw)).Fouten);
        var buitenstaander = await app.Gebruiker("Bart Buiten");
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.PasAan(buitenstaander, id, "MEM", "2026-0001", nieuw));
    }

    [Fact]
    public async Task Rekening_wijzigen_koppelt_aan_de_uitgebreide_indeling()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.VoegRekeningToe(wie, id, new("4390", "Diversen", Rekeningsoort.Resultaat, null))).IsGelukt);
        Assert.Equal("Niet ingedeeld (W&V)", Rekeningindeling.Groep((await grootboek.Schema(wie, id)).Zoek("4390")!).Naam);

        Assert.True((await grootboek.WijzigRekening(wie, id, "4390", "Diverse kosten", "WBedKanKan", "I21")).IsGelukt);

        var rekening = (await grootboek.Schema(wie, id)).Zoek("4390")!;
        Assert.Equal(("Diverse kosten", "WBedKanKan", "I21", "Kantoorkosten"), (rekening.Naam, rekening.RgsCode, rekening.BtwCode, Rekeningindeling.Groep(rekening).Naam));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.RekeningGewijzigd && r.Onderwerp == "4390"
            && r.Details == "Diversen; -; btw variabel → Diverse kosten; WBedKanKan; btw I21");
        Assert.Contains("resultaatrekening", (await grootboek.WijzigRekening(wie, id, "4390", "X", "BLimBanRba", null)).Fouten[0], StringComparison.Ordinal);
        Assert.Equal(["Btw-code X99 bestaat niet."], (await grootboek.WijzigRekening(wie, id, "4390", "X", null, "X99")).Fouten);
        Assert.Equal(["Rekening 9999 bestaat niet."], (await grootboek.WijzigRekening(wie, id, "9999", "X", null, null)).Fouten);
        var ondernemer = await app.Gebruiker("Oscar Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Data.Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.WijzigRekening(ondernemer, id, "4390", "X", null, null));
    }

    [Fact]
    public async Task Herboeken_vanaf_de_grootboekkaart_maakt_per_boeking_een_nieuwe_versie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 2, 1), ("4300", 100m), ("4310", 20m), ("1100", -120m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 3, 1), ("4300", 50m), ("1100", -50m)))).IsGelukt);
        var kaart = await grootboek.Kaart(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31));
        var regels = kaart.Mutaties.Select(m => new Herboekregel(m.Dagboek, m.Boekstuk, m.Regel)).ToList();

        Assert.Contains("hoort bij de bank", (await grootboek.Herboek(wie, id, regels, "1100")).Fouten[0], StringComparison.Ordinal);
        Assert.Contains("blijft staan", (await grootboek.Herboek(wie, id, [new Herboekregel("MEM", "2026-0001", 2)], "4320")).Fouten[0], StringComparison.Ordinal);
        var uitkomst = await grootboek.Herboek(wie, id, regels, "4320");

        Assert.Equal(2, uitkomst.Waarde);
        Assert.Empty((await grootboek.Kaart(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31))).Mutaties);
        Assert.Equal(150m, (await grootboek.Kaart(wie, id, "4320", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
        Assert.Equal(20m, (await grootboek.Kaart(wie, id, "4310", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
        Assert.Equal(2, (await grootboek.Boeking(wie, id, "MEM", "2026-0001"))!.Versie);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        var ondernemer = await app.Gebruiker("Herman Herboek");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Data.Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Herboek(ondernemer, id, regels, "4320"));
    }

    [Fact]
    public async Task Grootboekkaart_en_kolommenbalans_naar_Excel_en_html()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Export Overzicht B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 2, 1), ("4300", 100.40m), ("1100", -100.40m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 3, 1), ("4300", 49.60m), ("1100", -49.60m)))).IsGelukt);
        var overzicht = app.Dienst<OverzichtDienst>();

        using (var boek = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await overzicht.KaartExcel(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31)))))
        {
            var blad = boek.Worksheets.First();
            var cellen = blad.CellsUsed().Select(c => c.GetFormattedString()).ToList();
            Assert.Contains(cellen, c => c.Contains("Export Overzicht B.V.", StringComparison.Ordinal));
            Assert.Contains(blad.CellsUsed(), c => c.DataType == ClosedXML.Excel.XLDataType.Number && c.GetValue<decimal>() == 150m);
        }

        using (var boek = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await overzicht.KolommenbalansExcel(wie, id, new(2026, 1, 1), new(2026, 12, 31)))))
        {
            Assert.Contains(boek.Worksheets.First().CellsUsed(), c => c.GetFormattedString().Contains("Totaal generaal", StringComparison.Ordinal));
        }

        var html = Export.OverzichtExport.KaartHtml(await grootboek.Kaart(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31)), "Export Overzicht B.V.");
        Assert.Contains("100,40", html, StringComparison.Ordinal);
        Assert.Contains("150,00", html, StringComparison.Ordinal);
        var buiten = await app.Gebruiker("Buiten Export");
        await Assert.ThrowsAsync<GeenToegangException>(() => overzicht.KaartExcel(buiten, id, "4300", new(2026, 1, 1), new(2026, 12, 31)));
    }

    [Fact]
    public async Task Een_boeking_elke_maand_herhalen_met_eigen_boekstukken()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Herhaal B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.Contains("1 tot 24", (await grootboek.BoekPerMaand(wie, id, Post("2026-0001", new(2026, 1, 31), ("4300", 1000m), ("1100", -1000m)), 25)).Fouten[0], StringComparison.Ordinal);
        var uitkomst = await grootboek.BoekPerMaand(wie, id, Post("2026-0001", new(2026, 1, 31), ("4300", 1000m), ("1100", -1000m)), 4);

        Assert.Equal(4, uitkomst.Waarde);
        var posten = (await grootboek.Posten(wie, id, "MEM")).Select(p => p.Post).OrderBy(p => p.Datum).ToList();
        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], posten.Select(p => p.Datum));
        Assert.Equal(4, posten.Select(p => p.Boekstuk).Distinct().Count());
        Assert.Equal(4000m, (await grootboek.Kaart(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
        var dubbel = await grootboek.BoekPerMaand(wie, id, Post(posten[0].Boekstuk, new(2026, 5, 1), ("4300", 1m), ("1100", -1m)), 2);
        Assert.False(dubbel.IsGelukt);
        var ondernemer = await app.Gebruiker("Olga Herhaal");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Data.Lidmaatschapsrol.Ondernemer, wie);
        Assert.False((await grootboek.BoekPerMaand(ondernemer, id, Post("2026-0099", new(2026, 6, 1), ("4300", 1m), ("1100", -1m)), 2)).IsGelukt);
    }

    [Fact]
    public async Task Meerdere_memorialen_voor_lonen_en_accountant()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.True((await grootboek.VoegMemoriaalToe(wie, id, " mlon ", "Memoriaal lonen")).IsGelukt);
        Assert.True((await grootboek.VoegMemoriaalToe(wie, id, "MACC", "Memoriaal accountant")).IsGelukt);
        Assert.Equal(["Dagboek MLON bestaat al."], (await grootboek.VoegMemoriaalToe(wie, id, "MLON", "Nog eens")).Fouten);
        Assert.Equal(["Een dagboekcode is 2 tot 10 letters of cijfers, bijv. MLON."], (await grootboek.VoegMemoriaalToe(wie, id, "M LON", "X")).Fouten);
        Assert.Equal(["Geef het dagboek een naam, bijv. Memoriaal lonen."], (await grootboek.VoegMemoriaalToe(wie, id, "MX", " ")).Fouten);
        var memorialen = (await grootboek.Dagboeken(wie, id)).Where(d => d.Soort == Dagboeksoort.Memoriaal).Select(d => d.Code).ToList();
        Assert.Equal(["MACC", "MEM", "MLON"], memorialen);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.DagboekToegevoegd && r.Onderwerp == "MLON Memoriaal lonen");

        // Elk memoriaal nummert zelf; boeken en aanpassen werkt er net zo.
        var loon = Post("2026-0001", new(2026, 1, 31), ("4000", 3000m), ("1100", -3000m)) with { Dagboek = "MLON", Omschrijving = "Lonen januari" };
        Assert.True((await grootboek.Boek(wie, id, loon)).IsGelukt);
        Assert.Equal("2026-0002", await grootboek.VolgendBoekstuk(wie, id, "MLON", 2026));
        Assert.Equal("2026-0001", await grootboek.VolgendBoekstuk(wie, id, "MACC", 2026));
        Assert.Equal(2, (await grootboek.PasAan(wie, id, "MLON", "2026-0001", loon with { Regels = [new("4000", 3000m), new("4010", 500m), new("1100", -3000m), new("1400", -500m)] })).Waarde);
        Assert.Equal(500m, Saldo(await grootboek.Proefbalans(wie, id, 2026), "4010"));
        Assert.Single(await grootboek.Posten(wie, id, "MLON"));

        // Een ondernemer of buitenstaander voegt geen dagboek toe.
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Data.Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.VoegMemoriaalToe(ondernemer, id, "MOND", "Van de ondernemer"));
    }

    private static decimal Saldo(Proefbalans proef, string rekening) => proef.Regels.Single(r => r.Rekening.Nummer == rekening).Saldo;

    private static Journaalpost Post(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal " + boekstuk, [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag))]);
}
