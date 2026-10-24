using System.Net;
using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rubricering;
using Boekerhouden.Server.Components.Rapportweergave;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(klantnummer, naam, Rechtsvorm.BV, "12345678", "Ergenshuizen"), Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv"), $"{jaar}.csv", Tester)).IsGelukt);
        }

        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester, Verslaggeving.Fiscaal)).Waarde;
        Assert.True((await server.Dienst<GrondslagDienst>().ZetTekst(dossierId, "activiteiten", "De activiteiten van {naam} bestaan uit het bakken en verkopen van brood en banket.", Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetToelichting(dossierId, "WPer", "Gedurende {boekjaar} waren gemiddeld 3 werknemers in dienst ({boekjaar_vorig}: 3).", Tester)).IsGelukt);
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

        Assert.True((await Rapporten.ZetWeergave(dossierId, rekeningen["4200"], "Kosten van de bestelauto", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetWeergave(dossierId, rekeningen["4500"], null, true, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetSpecificatie(dossierId, "BVrd", false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetWeergave(dossierId, rekeningen["4300"], new string('x', 301), false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetWeergave(Guid.NewGuid(), rekeningen["4300"], "Andere klant", false, Tester)).IsGelukt);
        Assert.False((await Rapporten.ZetSpecificatie(dossierId, new string('B', 61), false, Tester)).IsGelukt);

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
        Assert.True((await Rapporten.ZetWeergave(dossierId, rekeningen["4200"], "  ", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetSpecificatie(dossierId, "BVrd", true, Tester)).IsGelukt);
        Assert.DoesNotContain(rekeningen["4200"], (await Rapporten.Weergave(klantId, 2025)).Keys);
        Assert.Empty(await Rapporten.ZonderSpecificatie(klantId, 2025));

        var log = await server.Dienst<AuditDienst>().Recent(AuditActies.PresentatieGewijzigd);
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "4200 Autokosten: in het rapport als 'Kosten van de bestelauto' vanaf 2025");
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "4500 Accountantskosten: in Overige vanaf 2025");
        Assert.Contains(log, a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "specificatie BVrd niet in het rapport vanaf 2025");
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
        Assert.Contains("data-post=\"BMva\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tekst_in_de_voettekst_kan_niet_uit_de_css_breken()
    {
        Assert.Equal("\"a\\22  b\\5C  \\3C /style\\3E \"", RapportHtml.CssTekst("a\" b\\ </style>\n"));
        Assert.Equal("-", RapportHtml.Bedrag(0.4m));
        Assert.Equal(string.Empty, RapportHtml.Bedrag(null));
        Assert.Equal("-1.235", RapportHtml.Bedrag(-1_234.5m));
    }

    [Fact]
    public async Task Het_rapportdeel_van_een_post_heeft_alleen_die_post_met_verloop_en_grondslagen()
    {
        var (_, dossierId, _, _) = await Bakkerij();
        var inhoud = (await Rapporten.Maak(dossierId))!.Waarde!;
        await using var scope = server.Services.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<RapportDocument>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(RapportDocument.Inhoud)] = inhoud, [nameof(RapportDocument.Post)] = "BMva" }))).ToHtmlString());
        html = WebUtility.HtmlDecode(html);

        Assert.Contains("rapport-fragment", html, StringComparison.Ordinal);
        Assert.Contains("data-post=\"BMva\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-post=\"BVor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("voorblad", html, StringComparison.Ordinal);
        Assert.Contains("Verloop materiële vaste activa", html, StringComparison.Ordinal);
        Assert.Contains("Grondslagen", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Verloop materiële vaste activa", StringComparison.Ordinal) < html.IndexOf("Grondslagen", StringComparison.Ordinal));
    }

    [Fact]
    public void Posten_en_rekeningen_staan_gelijk_met_de_kantlijn()
    {
        Assert.Contains("td:first-child { padding-left: 0; }", RapportHtml.Stijl, StringComparison.Ordinal);
        Assert.DoesNotContain("tr.detail td.omschrijving { padding-left", RapportHtml.Stijl, StringComparison.Ordinal);
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

        Assert.True((await Rapporten.ZetUitgeklapt(dossierId, "WBed", true, Tester)).IsGelukt);
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
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.PresentatieGewijzigd), a => a.Details == "WBed uitgeklapt in het rapport vanaf 2025");
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
        Assert.Contains(rapport.Waarschuwingen, w => w.StartsWith("Uitsplitsing 1800", StringComparison.Ordinal));

        Assert.True((await Rapporten.BewaarUitsplitsing(dossierId, rekeningen["1800"], [], Tester)).IsGelukt);
        rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        Assert.Contains(Tabel(rapport, "Kortlopende schulden").Regels, r => r.Omschrijving == "Nog te betalen kosten" && r.Detail == 7_890m);
        Assert.Empty(rapport.Waarschuwingen);
    }

    private static string? Toelichting(Rapport rapport, string sleutel) =>
        rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TekstBlok>().SingleOrDefault(b => b.Toelichting == sleutel)?.Tekst;

    [Fact]
    public async Task Een_toelichting_rolt_door_maar_een_aanpassing_werkt_niet_terug()
    {
        var (klantId, dossierId, _, _) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester, Verslaggeving.Fiscaal)).Waarde;
        Assert.True((await Rapporten.ZetToelichting(vorigDossier, "BMva", "Tekst van vorig jaar.", Tester)).IsGelukt);
        Assert.Equal("Tekst van vorig jaar.", Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BMva"));

        Assert.True((await Rapporten.ZetToelichting(dossierId, "BMva", "In 2025 is een nieuwe oven gekocht.", Tester)).IsGelukt);
        Assert.Equal("In 2025 is een nieuwe oven gekocht.", Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BMva"));
        Assert.Equal("Tekst van vorig jaar.", Toelichting((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport, "BMva"));

        // Leeg maken in 2025 haalt hem alleen uit 2025.
        Assert.True((await Rapporten.ZetToelichting(dossierId, "BMva", " ", Tester)).IsGelukt);
        Assert.Null(Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BMva"));
        Assert.Equal("Tekst van vorig jaar.", Toelichting((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport, "BMva"));

        // Een aanpassing in 2024 gaat mee naar 2025 zolang 2025 niets eigens heeft.
        Assert.True((await Rapporten.ZetToelichting(dossierId, "BMva", "Tekst van vorig jaar.", Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetToelichting(vorigDossier, "BMva", "Verbeterde tekst.", Tester)).IsGelukt);
        Assert.Equal("Verbeterde tekst.", Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BMva"));
    }

    [Fact]
    public async Task Namen_en_presentatie_gelden_vanaf_het_jaar_van_het_dossier()
    {
        var (klantId, dossierId, klantnummer, rekeningen) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester, Verslaggeving.Fiscaal)).Waarde;
        var rubriceren = server.Dienst<RubriceerDienst>();

        Assert.True((await rubriceren.Hernoem(dossierId, "BVrd", "Voorraad grondstoffen", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetSpecificatie(dossierId, "BMva", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetWeergave(vorigDossier, rekeningen["4200"], "Kosten van de bestelauto", false, Tester)).IsGelukt);

        var dit = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        var vorig = (await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport;
        Assert.Contains(Tabel(dit, "Activa").Regels, r => r.Omschrijving == "Voorraad grondstoffen");
        Assert.Contains(Tabel(vorig, "Activa").Regels, r => r.Omschrijving == "Voorraden");
        Assert.DoesNotContain(dit.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>(), b => b.Tabel.Titel == "Materiële vaste activa");
        Assert.Contains(vorig.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>(), b => b.Tabel.Titel == "Materiële vaste activa");
        Assert.Contains(Tabel(dit, "Overige bedrijfskosten").Regels, r => r.Omschrijving == "Kosten van de bestelauto");
        Assert.Contains(Tabel(vorig, "Overige bedrijfskosten").Regels, r => r.Omschrijving == "Kosten van de bestelauto");

        // Een naam in 2024 laat de eigen naam van 2025 staan; terug naar standaard in 2025 laat 2024 staan.
        Assert.True((await rubriceren.Hernoem(vorigDossier, "BVrd", "Voorraden bakkerij", false, Tester)).IsGelukt);
        Assert.True((await Rapporten.ZetWeergave(dossierId, rekeningen["4200"], null, false, Tester)).IsGelukt);
        dit = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        vorig = (await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport;
        Assert.Contains(Tabel(dit, "Activa").Regels, r => r.Omschrijving == "Voorraad grondstoffen");
        Assert.Contains(Tabel(vorig, "Activa").Regels, r => r.Omschrijving == "Voorraden bakkerij");
        Assert.Contains(Tabel(dit, "Overige bedrijfskosten").Regels, r => r.Omschrijving == "Autokosten");
        Assert.Contains(Tabel(vorig, "Overige bedrijfskosten").Regels, r => r.Omschrijving == "Kosten van de bestelauto");
        Assert.Equal("Kosten van de bestelauto", (await Rapporten.Weergave(klantId, 2024))[rekeningen["4200"]].Omschrijving);
        Assert.DoesNotContain(rekeningen["4200"], (await Rapporten.Weergave(klantId, 2025)).Keys);

        // Een eigen post kan vanaf een jaar anders heten.
        var postId = (await rubriceren.VoegEigenPostToe(klantId, "BVorOvr", "Overige vorderingen 1", Tester)).Waarde;
        var sleutel = Domain.Rubricering.PostBalans.EigenSleutel(postId);
        Assert.True((await rubriceren.Hernoem(dossierId, sleutel, "Vordering op de huurder", false, Tester)).IsGelukt);
        Assert.Equal("Vordering op de huurder", (await rubriceren.Overzicht(dossierId))!.Naam(sleutel));
        Assert.Equal("Overige vorderingen 1", (await rubriceren.Overzicht(vorigDossier))!.Naam(sleutel));

        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.PostHernoemd),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "BVrd: 'Voorraden' → 'Voorraad grondstoffen' vanaf 2025");
    }

    [Fact]
    public async Task Meegeleverde_toelichtingen_vullen_zichzelf_in_en_zijn_weg_te_halen()
    {
        var (klantId, dossierId, _, _) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester, Verslaggeving.Fiscaal)).Waarde;

        var inhoud = (await Rapporten.Maak(dossierId))!.Waarde!;
        var resultaat = Tabel(inhoud.Rapport, null).Regels[^1];
        var eigen = inhoud.Rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TekstBlok>().Single(b => b.Toelichting == "BEiv");
        Assert.Equal($"Het ingehouden deel van het resultaat over 2025 bedraagt € {Domain.Bedragen.Heel(resultaat.Totaal!.Value)} (2024: € {Domain.Bedragen.Heel(resultaat.TotaalVorig!.Value)}).", eigen.Tekst);
        Assert.StartsWith("Het ingehouden deel van het resultaat over {boekjaar}", eigen.Sjabloon, StringComparison.Ordinal);
        Assert.Contains("Gedurende 2025 waren gemiddeld 3 werknemers in dienst (2024: 3).", Toelichting(inhoud.Rapport, "WPer"), StringComparison.Ordinal);
        Assert.Contains("[aantal]", Toelichting((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport, "WPer"), StringComparison.Ordinal);

        // Weghalen in 2025: dan staat er vanaf 2025 geen toelichting, in 2024 wel.
        Assert.True((await Rapporten.ZetToelichting(dossierId, "BEiv", null, Tester)).IsGelukt);
        Assert.Null(Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BEiv"));
        Assert.StartsWith("Het ingehouden deel van het resultaat over 2024", Toelichting((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport, "BEiv"), StringComparison.Ordinal);

        // Een eigen tekst met variabelen blijft een sjabloon voor volgende jaren.
        Assert.True((await Rapporten.ZetToelichting(vorigDossier, "BEiv", "Het eigen vermogen bedraagt {valuta} {eigen_vermogen} ({solvabiliteit} van het balanstotaal).", Tester)).IsGelukt);
        Assert.Matches(@"^Het eigen vermogen bedraagt € [0-9.]+ \([0-9]+,[0-9]%", Toelichting((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport, "BEiv"));
        Assert.Null(Toelichting((await Rapporten.Maak(dossierId))!.Waarde!.Rapport, "BEiv"));
    }

    private static List<Verloop> Verlopen(Rapport rapport) =>
        [.. rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<VerloopBlok>().Select(b => b.Verloop)];

    [Fact]
    public async Task Verloopoverzichten_bij_vaste_activa_en_op_verzoek_bij_een_rekening()
    {
        var (klantId, dossierId, klantnummer, rekeningen) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester, Verslaggeving.Fiscaal)).Waarde;

        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        var mva = Verlopen(rapport).Single(v => v.Sleutel == "BMva");
        Assert.True(mva.Standaard);
        Assert.Equal(mva.Regels.Single(r => r.Soort == VerloopRegelSoort.Eindstand).Bedragen[^1], Tabel(rapport, "Activa").Regels.Single(r => r.Bron?.Sleutel == "BMva").Totaal);
        var ev = Verlopen(rapport).Single(v => v.Sleutel == "BEiv");
        Assert.Equal(ev.Regels[^1].Bedragen[^1], Tabel(rapport, "Passiva").Regels.Single(r => r.Bron?.Sleutel == "BEiv").Totaal);
        Assert.DoesNotContain(rapport.Waarschuwingen, w => w.Contains("Verloop", StringComparison.Ordinal));

        // Een verloop bij de debiteuren, vanaf 2025: 2024 heeft het niet.
        var sleutel = RapportOpbouw.RekeningSleutel(rekeningen["1300"]);
        Assert.True((await Rapporten.ZetVerloop(dossierId, sleutel, true, Tester)).IsGelukt);
        var debiteuren = Verlopen((await Rapporten.Maak(dossierId))!.Waarde!.Rapport).Single(v => v.Sleutel == sleutel);
        Assert.Equal("Verloop Debiteuren", debiteuren.Titel);
        Assert.True(debiteuren.Vergelijkend);
        Assert.Equal(["2025", "2024"], debiteuren.Kolommen);
        Assert.True((await Rapporten.ZetVerloopVergelijkend(dossierId, sleutel, false, Tester)).IsGelukt);
        Assert.False(Verlopen((await Rapporten.Maak(dossierId))!.Waarde!.Rapport).Single(v => v.Sleutel == sleutel).Vergelijkend);
        Assert.True((await Rapporten.ZetVerloopVergelijkend(dossierId, sleutel, true, Tester)).IsGelukt);

        // Een eigen afschrijvingspercentage, vanaf 2025.
        var kolom = mva.Kolomsleutels[0];
        Assert.True((await Rapporten.ZetAfschrijvingspercentages(dossierId, new Dictionary<string, string?> { [kolom] = "10 - 20" }, Tester)).IsGelukt);
        Assert.Equal("10 - 20", Verlopen((await Rapporten.Maak(dossierId))!.Waarde!.Rapport).Single(v => v.Sleutel == "BMva").Percentages![kolom]);
        Assert.NotEqual("10 - 20", Verlopen((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport).Single(v => v.Sleutel == "BMva").Percentages![kolom]);
        Assert.DoesNotContain(Verlopen((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport), v => v.Sleutel == sleutel);

        // Eigen mutaties die niet aansluiten: een regel en een waarschuwing; leeg = weer het voorstel.
        Assert.True((await Rapporten.BewaarVerloop(dossierId, sleutel, [new("Gefactureerd", new Dictionary<string, decimal> { [""] = 1_000m, [RapportOpbouw.KolomVorig] = 0m })], Tester)).IsGelukt);
        rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        debiteuren = Verlopen(rapport).Single(v => v.Sleutel == sleutel);
        Assert.True(debiteuren.Aangepast);
        Assert.Contains(debiteuren.Regels, r => r.Omschrijving == RapportOpbouw.NietVerklaard);
        Assert.Contains(rapport.Waarschuwingen, w => w.StartsWith("Verloop Debiteuren sluit niet aan", StringComparison.Ordinal));
        Assert.True((await Rapporten.BewaarVerloop(dossierId, sleutel, [], Tester)).IsGelukt);
        Assert.False(Verlopen((await Rapporten.Maak(dossierId))!.Waarde!.Rapport).Single(v => v.Sleutel == sleutel).Aangepast);

        // Het standaardverloop weghalen vanaf 2025.
        Assert.True((await Rapporten.ZetVerloop(dossierId, "BMva", false, Tester)).IsGelukt);
        Assert.DoesNotContain(Verlopen((await Rapporten.Maak(dossierId))!.Waarde!.Rapport), v => v.Sleutel == "BMva");
        Assert.Contains(Verlopen((await Rapporten.Maak(vorigDossier))!.Waarde!.Rapport), v => v.Sleutel == "BMva");
        Assert.False((await Rapporten.ZetVerloop(dossierId, RapportOpbouw.RekeningSleutel(Guid.NewGuid()), true, Tester)).IsGelukt);

        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.VerloopGewijzigd),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == $"{sleutel}: Gefactureerd 1.000,00");
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.VerloopGewijzigd), a => a.Details == $"afschrijvingspercentage {kolom} 10 - 20 vanaf 2025");
    }

    [Fact]
    public async Task Een_uitsplitsing_van_vorig_jaar_is_een_voorstel()
    {
        var (klantId, dossierId, _, rekeningen) = await Bakkerij();
        var vorigDossier = (await server.Dienst<DossierDienst>().Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await Rapporten.BewaarUitsplitsing(vorigDossier, rekeningen["1800"], [new("Accountantskosten", 2_800m, null), new("Overige", 5_633m, null)], Tester);

        var voorstel = await Rapporten.Uitsplitsing(dossierId, rekeningen["1800"], "Nog te betalen kosten", 7_890m, 8_433m);
        Assert.True(voorstel.UitVorigJaar);
        Assert.False(voorstel.Bewaard);
        Assert.Equal([new SplitsRegel("Accountantskosten", 0m, 2_800m), new SplitsRegel("Overige", 0m, 5_633m)], voorstel.Regels);
    }

    [Fact]
    public async Task Een_gesaldeerde_eigen_post_staat_bij_een_debetsaldo_onder_de_vorderingen()
    {
        var (klantId, dossierId, klantnummer, rekeningen) = await Bakkerij();
        var rubriceren = server.Dienst<RubriceerDienst>();
        var btw = (await rubriceren.VoegEigenPostToe(klantId, "BSchBep", "Omzetbelasting", Tester)).Waarde;
        Assert.True((await rubriceren.Koppel(klantId, [rekeningen["1600"]], PostBalans.EigenSleutel(btw), false, Tester)).IsGelukt);

        Assert.False((await rubriceren.ZetSaldering(klantId, btw, "BSchCreHac", Tester)).IsGelukt);
        Assert.False((await rubriceren.ZetSaldering(klantId, btw, "WBedKanKan", Tester)).IsGelukt);
        Assert.True((await rubriceren.ZetSaldering(klantId, btw, "BVorVbkTvo", Tester)).IsGelukt);

        // Een correctie maakt de omzetbelasting dit jaar per saldo te vorderen (vorig jaar te betalen).
        Assert.True((await server.Dienst<CorrectieDienst>().Bewaar(dossierId, null, new CorrectieInvoer("Teruggaaf omzetbelasting", CorrectieSoort.Correctie, false, false,
            [new(rekeningen["1600"], 60_000m), new(rekeningen["1100"], -60_000m)]), Tester)).IsGelukt);

        var rapport = (await Rapporten.Maak(dossierId))!.Waarde!.Rapport;
        var vorderingen = Tabel(rapport, "Vorderingen");
        Assert.Contains(vorderingen.Regels, r => r.Omschrijving == "Omzetbelasting" && r.Detail == 8_937m && r.DetailVorig == 0m);
        var schulden = Tabel(rapport, "Kortlopende schulden");
        Assert.Contains(schulden.Regels, r => r.Omschrijving == "Omzetbelasting" && r.Detail == 0m && r.DetailVorig == 48_060m);
        Assert.Equal(Tabel(rapport, "Activa").Regels[^1].Totaal, Tabel(rapport, "Passiva").Regels[^1].Totaal);

        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.EigenPostGesaldeerd),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details!.StartsWith("'Omzetbelasting': salderen", StringComparison.Ordinal));
        Assert.True((await rubriceren.ZetSaldering(klantId, btw, null, Tester)).IsGelukt);
    }
}
