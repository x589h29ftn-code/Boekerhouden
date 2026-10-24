using System.Net;
using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Server.Tests;

public sealed class RapportDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    /// <summary>Alle rekeningen van de voorbeeldbakkerij met een passende code uit het RGS-uittreksel.</summary>
    private static readonly Dictionary<string, string> Rubricering = new()
    {
        ["0200"] = "BMvaObeVvp",
        ["0210"] = "BMvaObeCae",
        ["0300"] = "BMvaTevVvp",
        ["0310"] = "BMvaTevCae",
        ["0500"] = "BEivGokGea",
        ["0550"] = "BEivOvrOrs",
        ["0700"] = "BLasSakLvl",
        ["1000"] = "BLimKasKas",
        ["1100"] = "BLimBanRba",
        ["1110"] = "BLimBanSpa",
        ["1300"] = "BVorDebHad",
        ["1310"] = "BVorDebVdd",
        ["1400"] = "BVorOvrOvk",
        ["1500"] = "BSchCreHac",
        ["1600"] = "BSchBepBtw",
        ["1610"] = "BSchBepLhe",
        ["1620"] = "BSchOvsOvs",
        ["1800"] = "BSchOpaNtb",
        ["3000"] = "BVrdGehVoo",
        ["4000"] = "WPerLesLon",
        ["4010"] = "WPerSolPsv",
        ["4020"] = "WPerPenPen",
        ["4100"] = "WBedHuiBeh",
        ["4110"] = "WBedHuiGwe",
        ["4200"] = "WBedAutBra",
        ["4300"] = "WBedKanKan",
        ["4400"] = "WBedVkkRea",
        ["4500"] = "WBedAeaAea",
        ["4800"] = "WAfsAmvObe",
        ["4810"] = "WAfsAmvTev",
        ["7000"] = "WKprInhInh",
        ["8000"] = "WOmzNopOlh",
        ["9100"] = "WFbeRlmRlm",
        ["9500"] = "WBelBgrLab",
    };

    private RapportDienst Rapporten => server.Dienst<RapportDienst>();

    /// <summary>De voorbeeldbakkerij met 2024 en 2025, helemaal gerubriceerd (op 4300 na als <paramref name="alles"/> onwaar is).</summary>
    private async Task<(Guid KlantId, Guid DossierId, string Klantnummer, Dictionary<string, Guid> Rekeningen)> Bakkerij(string naam = "Voorbeeld Bakkerij B.V.", bool alles = true)
    {
        await server.MetRgs();
        var klantnummer = TestGegevens.Klantnummer();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(klantnummer, naam, Rechtsvorm.BV, null, "Ergenshuizen"), Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv"), $"{jaar}.csv", Tester)).IsGelukt);
        }

        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var rubriceren = server.Dienst<RubriceerDienst>();
        var rekeningen = (await rubriceren.Overzicht(dossierId, metVoorstellen: false))!.Rekeningen.ToDictionary(r => r.Nummer, r => r.Id);
        foreach (var (nummer, code) in Rubricering.Where(r => alles || r.Key != "4300"))
        {
            var uitkomst = await rubriceren.Koppel(klantId, [rekeningen[nummer]], code, false, Tester);
            Assert.True(uitkomst.IsGelukt, $"{nummer} → {code}: {string.Join("; ", uitkomst.Fouten)}");
        }

        return (klantId, dossierId, klantnummer, rekeningen);
    }

    private static Tabel Tabel(Rapport rapport, string? titel) =>
        rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>().Select(b => b.Tabel).First(t => t.Titel == titel);

    [Fact]
    public async Task Rapport_van_een_gerubriceerd_dossier_sluit_en_toont_het_resultaat()
    {
        var (_, dossierId, _, _) = await Bakkerij();

        var inhoud = (await Rapporten.Maak(dossierId))!;

        Assert.True(inhoud.IsGelukt, string.Join("; ", inhoud.Fouten));
        var rapport = inhoud.Waarde!.Rapport;
        Assert.Equal("Jaarrekening 2025", rapport.Titel);
        Assert.Equal("Ergenshuizen", rapport.Plaats);
        Assert.True(rapport.Concept);
        Assert.Empty(rapport.Waarschuwingen);

        var activa = Tabel(rapport, "Activa").Regels[^1];
        var passiva = Tabel(rapport, "Passiva").Regels[^1];
        Assert.Equal(activa.Totaal, passiva.Totaal);
        Assert.Equal(activa.TotaalVorig, passiva.TotaalVorig);
        Assert.True(activa.TotaalVorig > 0);
        Assert.Contains(Tabel(rapport, "Passiva").Regels, r => r.Soort == RegelSoort.Post && r.Omschrijving == "Eigen vermogen");

        var overzicht = (await server.Dienst<RubriceerDienst>().Overzicht(dossierId, metVoorstellen: false))!;
        var wv = Tabel(rapport, null);
        Assert.Equal("Resultaat na belastingen", wv.Regels[^1].Omschrijving);
        Assert.Equal(overzicht.Balans!.ResultaatHuidig, wv.Regels[^1].Totaal);
        Assert.Equal(overzicht.Balans.ResultaatVorig, wv.Regels[^1].TotaalVorig);
        Assert.Contains(wv.Regels, r => r.Omschrijving == "Brutomarge");
    }

    [Fact]
    public async Task Niet_gerubriceerde_rekeningen_maken_het_rapport_onvolledig()
    {
        var (_, dossierId, _, _) = await Bakkerij(alles: false);

        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;

        Assert.Contains(rapport.Waarschuwingen, w => w.Contains("niet gerubriceerd", StringComparison.Ordinal));
        Assert.Contains(Tabel(rapport, "Activa").Regels, r => r.Omschrijving == "Nog niet gerubriceerd");
        Assert.Equal(Tabel(rapport, "Activa").Regels[^1].Totaal, Tabel(rapport, "Passiva").Regels[^1].Totaal);
    }

    [Fact]
    public async Task Presentatie_per_klant_komt_in_het_rapport_en_in_het_wijzigingslog()
    {
        var (klantId, dossierId, klantnummer, rekeningen) = await Bakkerij();

        Assert.True((await Rapporten.ZetWeergave(klantId, rekeningen["4200"], "Kosten van de bestelauto", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetWeergave(klantId, rekeningen["4500"], null, true, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetSpecificatie(klantId, "BVrd", false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetWeergave(klantId, rekeningen["4300"], new string('x', 301), false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetWeergave(Guid.NewGuid(), rekeningen["4300"], "Andere klant", false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetSpecificatie(klantId, new string('B', 61), false, Tester)).IsGelukt);

        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        var kosten = Tabel(rapport, "Overige bedrijfskosten");
        Assert.Contains(kosten.Regels, r => r.Omschrijving == "Kosten van de bestelauto");
        Assert.DoesNotContain(kosten.Regels, r => r.Omschrijving == "Accountantskosten");
        Assert.Equal(8_378m, kosten.Regels.Single(r => r.Omschrijving == "Overige").Detail);
        Assert.DoesNotContain(rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>(), b => b.Tabel.Titel == "Voorraden");

        var presentatie = (await Rapporten.Presentatie(dossierId))!;
        var bedrijfskosten = presentatie.Posten.Single(p => p.Code == "WBed");
        Assert.Equal("Kosten van de bestelauto", bedrijfskosten.Rekeningen.Single(r => r.Nummer == "4200").RapportOmschrijving);
        Assert.True(bedrijfskosten.Rekeningen.Single(r => r.Nummer == "4500").Verbergen);
        Assert.False(presentatie.Posten.Single(p => p.Code == "BVrd").ToonSpecificatie);

        // Terug naar de administratie: dan is er niets meer opgeslagen.
        Assert.True((await Rapporten.ZetWeergave(klantId, rekeningen["4200"], "  ", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetSpecificatie(klantId, "BVrd", true, Tester)).IsGelukt);
        Assert.DoesNotContain(rekeningen["4200"], (await Rapporten.Weergave(klantId)).Keys);
        Assert.Empty(await Rapporten.ZonderSpecificatie(klantId));

        var log = await server.Dienst<AuditDienst>().Recent(AuditActies.PresentatieGewijzigd);
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "4200 Autokosten: in het rapport als 'Kosten van de bestelauto'");
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "4500 Accountantskosten: in Overige");
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "specificatie BVrd niet in het rapport");
    }

    [Fact]
    public async Task Html_is_veilig_en_gebruikt_de_huisstijl()
    {
        var (_, dossierId, _, _) = await Bakkerij("<script>alert('x')</script> & Zn");
        await server.Dienst<KantoorDienst>().Bewaar(new KantoorInvoer("Voorbeeldkantoor", "Dorpsstraat 1", "1234 AB", "Ergens", "#2E6B4F"), Tester);

        var html = await RapportHtml.Maak(server.Services, (await Rapporten.Maak(dossierId))!.Waarde!);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/script&gt; &amp; Zn", html, StringComparison.Ordinal);
        Assert.Contains(RapportHtml.Beveiliging, html, StringComparison.Ordinal);
        Assert.Contains("--huisstijl: #2e6b4f", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"klap\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"bewerk-actie", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"bewerkbaar", html, StringComparison.Ordinal);
        Assert.Contains("Voorbeeldkantoor", html, StringComparison.Ordinal);
        Assert.Contains("JAARREKENING 2025", html, StringComparison.Ordinal);
        Assert.Contains("1.458.938", html, StringComparison.Ordinal);
        Assert.Contains("id=\"h3\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"post-BMva\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tekst_in_de_voettekst_kan_niet_uit_de_css_breken()
    {
        Assert.Equal("\"a\\22  b\\5C  \\3C /style\\3E \"", RapportHtml.CssTekst("a\" b\\ </style>\n"));
        Assert.Equal("-", RapportHtml.Bedrag(0.4m));
        Assert.Equal(string.Empty, RapportHtml.Bedrag(null));
        Assert.Equal("-1.235", RapportHtml.Bedrag(-1_234.5m));
    }

    [PdfBrowserFact]
    public async Task Pdf_vraagt_om_inloggen_en_komt_in_het_wijzigingslog()
    {
        var (_, dossierId, klantnummer, _) = await Bakkerij();
        await server.Beheerder();
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var pdf = new Uri($"/dossiers/{dossierId}/rapport.pdf", UriKind.Relative);

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(pdf)).StatusCode);

        await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "/");

        var antwoord = await client.GetAsync(pdf);
        Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);
        Assert.Equal("application/pdf", antwoord.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"Jaarrekening 2025 {klantnummer} (concept).pdf", antwoord.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("%PDF-"u8.ToArray(), (await antwoord.Content.ReadAsByteArrayAsync())[..5]);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.RapportGemaakt),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "Jaarrekening 2025 (concept), PDF");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/dossiers/{Guid.NewGuid()}/rapport.pdf", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Uitklappen_toelichting_en_uitsplitsing_staan_in_het_rapport_en_de_pdf()
    {
        var (klantId, dossierId, klantnummer, rekeningen) = await Bakkerij();

        Assert.True((await Rapporten.ZetUitgeklapt(klantId, "WBed", true, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetToelichting(dossierId, "BMva", "In 2025 is een nieuwe oven gekocht.", Tester)).IsGelukt);
        var voorstel = await Rapporten.Uitsplitsing(dossierId, rekeningen["1800"], "Nog te betalen kosten", 7_890m, 8_433m);
        Assert.False(voorstel.Bewaard);
        Assert.Equal(new SplitsRegel("Nog te betalen kosten", 7_890m, 8_433m), Assert.Single(voorstel.Regels));
        Assert.True((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"],
            [new("Accountantskosten", 3_000m, 2_800m), new("Energie", 2_500m, 2_000m), new("Overige", 2_389.75m, 3_632.87m)], Tester)).IsGelukt);

        var inhoud = (await Rapporten.Maak(dossierId))!.Waarde!;
        var rapport = inhoud.Rapport;
        var wv = Tabel(rapport, null);
        Assert.True(wv.Regels.Single(r => r.Omschrijving == "Overige bedrijfskosten").Uitgeklapt);
        Assert.Contains(wv.Regels, r => r.Soort == RegelSoort.Detail && r.Bron?.Sleutel == "WBedAut");
        var schulden = Tabel(rapport, "Kortlopende schulden");
        Assert.Equal([3_000m, 2_500m, 2_390m], schulden.Regels.Where(r => r.Bron?.Soort == Onderwerp.Uitsplitsing).Select(r => r.Detail!.Value));
        Assert.Equal([2_800m, 2_000m, 3_633m], schulden.Regels.Where(r => r.Bron?.Soort == Onderwerp.Uitsplitsing).Select(r => r.DetailVorig!.Value));
        Assert.Empty(rapport.Waarschuwingen);
        Assert.Contains(rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TekstBlok>(), b => b.Toelichting == "BMva");

        var html = await RapportHtml.Maak(server.Services, inhoud);
        Assert.Contains("In 2025 is een nieuwe oven gekocht.", html, StringComparison.Ordinal);
        Assert.Contains("Energie", html, StringComparison.Ordinal);

        var bewaard = await Rapporten.Uitsplitsing(dossierId, rekeningen["1800"], "Nog te betalen kosten", 7_890m, 8_433m);
        Assert.True(bewaard.Bewaard);
        Assert.Equal(3, bewaard.Regels.Count);

        var log = await server.Dienst<AuditDienst>().Recent(AuditActies.UitsplitsingGewijzigd);
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details!.StartsWith("1800 Nog te betalen kosten: Accountantskosten 3.000,00 (vorig jaar 2.800,00)", StringComparison.Ordinal));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.ToelichtingGewijzigd), a => a.Details == "BMva: In 2025 is een nieuwe oven gekocht.");
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.PresentatieGewijzigd), a => a.Details == "WBed uitgeklapt in het rapport");
    }

    [Fact]
    public async Task Een_uitsplitsing_die_niet_aansluit_geeft_een_waarschuwing_en_kan_weer_weg()
    {
        var (_, dossierId, _, rekeningen) = await Bakkerij();

        Assert.False((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"], [new(" ", 1m, null)], Tester)).IsGelukt);
        Assert.False((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"], [new("Te veel decimalen", 1.234m, null)], Tester)).IsGelukt);
        Assert.False((await Rapporten.BewaarUitsplitsing(Guid.NewGuid(), rekeningen["1800"], [new("Ander dossier", 1m, null)], Tester)).IsGelukt);
        Assert.True((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"], [new("Accountantskosten", 3_000m, null)], Tester)).IsGelukt);

        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        var rest = Tabel(rapport, "Kortlopende schulden").Regels.Single(r => r.Omschrijving == RapportOpbouw.NietGespecificeerd);
        Assert.Equal(4_890m, rest.Detail);
        Assert.Equal(8_433m, rest.DetailVorig);
        Assert.Contains(rapport.Waarschuwingen, w => w.StartsWith("De uitsplitsing van 1800", StringComparison.Ordinal));

        Assert.True((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"], [], Tester)).IsGelukt);
        rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        Assert.Contains(Tabel(rapport, "Kortlopende schulden").Regels, r => r.Omschrijving == "Nog te betalen kosten" && r.Detail == 7_890m);
        Assert.Empty(rapport.Waarschuwingen);
    }

    [Fact]
    public async Task Toelichting_en_uitsplitsing_van_vorig_jaar_zijn_een_voorstel()
    {
        var (klantId, dossierId, _, rekeningen) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await Rapporten.ZetToelichting(vorigDossier, "BMva", "Tekst van vorig jaar.", Tester);
        await Rapporten.BewaarUitsplitsing(vorigDossier, rekeningen["1800"], [new("Accountantskosten", 2_800m, null), new("Overige", 5_633m, null)], Tester);

        Assert.Equal("Tekst van vorig jaar.", await Rapporten.ToelichtingVorigJaar(dossierId, "BMva"));
        Assert.Null(await Rapporten.ToelichtingVorigJaar(dossierId, "BVor"));
        var voorstel = await Rapporten.Uitsplitsing(dossierId, rekeningen["1800"], "Nog te betalen kosten", 7_890m, 8_433m);
        Assert.True(voorstel.UitVorigJaar);
        Assert.False(voorstel.Bewaard);
        Assert.Equal([new SplitsRegel("Accountantskosten", 0m, 2_800m), new SplitsRegel("Overige", 0m, 5_633m)], voorstel.Regels);

        // Het rapport van dit jaar verandert pas als de toelichting of uitsplitsing hier wordt bewaard.
        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        Assert.DoesNotContain(rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TekstBlok>(), b => b.Toelichting is not null);
    }
}
