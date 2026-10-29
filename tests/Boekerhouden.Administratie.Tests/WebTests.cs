using System.Net;
using System.Text.RegularExpressions;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Inloggen, pagina's en headers via echte HTTP-verzoeken (zoals een browser zonder JavaScript).</summary>
public sealed class WebTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private HttpClient NieuweClient() => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Zonder_inloggen_naar_de_inlogpagina()
    {
        await app.Beheerder();
        using var client = NieuweClient();

        foreach (var pad in new[] { "/", "/administraties/nieuw", $"/a/{Guid.NewGuid()}", "/beheer/gebruikers" })
        {
            var antwoord = await client.GetAsync(new Uri(pad, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Redirect, antwoord.StatusCode);
            Assert.StartsWith("/inloggen?ReturnUrl=", antwoord.Doel(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Beveiligingsheaders_op_elk_antwoord()
    {
        using var client = NieuweClient();

        var antwoord = await client.GetAsync(new Uri("/inloggen", UriKind.Relative));

        Assert.Contains("frame-ancestors 'none'", antwoord.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", antwoord.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", antwoord.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", antwoord.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Gedeelde_huisstijl_wordt_geserveerd_ook_gecomprimeerd()
    {
        using var client = NieuweClient();
        var html = await client.GetStringAsync(new Uri("/inloggen", UriKind.Relative));

        foreach (var bestand in new[] { "app", "bootstrap.min", "administratie" })
        {
            var pad = Regex.Match(html, $"href=\"([^\"]*{Regex.Escape(bestand)}[.][a-z0-9]+[.]css)\"").Groups[1].Value;
            using var verzoek = new HttpRequestMessage(HttpMethod.Get, new Uri("/" + pad, UriKind.Relative));
            verzoek.Headers.AcceptEncoding.ParseAdd("gzip, br");
            var antwoord = await client.SendAsync(verzoek);

            Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);
            Assert.Equal("text/css", antwoord.Content.Headers.ContentType?.MediaType);
            Assert.True((await antwoord.Content.ReadAsByteArrayAsync()).Length > 100, $"{pad} is leeg");
        }

        Assert.Contains("_content/Boekerhouden.Huisstijl/", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingelogd_lid_ziet_de_administratie_en_een_buitenstaander_niet()
    {
        var id = await app.Administratie("Webwinkel Voorbeeld B.V.");
        await app.Dienst<GrootboekDienst>().Boek(await app.Beheerder(), id,
            new("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new("1100", 1234.56m), new("0500", -1234.56m)]));
        using var beheerder = NieuweClient();
        var sleutel = await app.Tweestap(await app.Beheerder());
        Assert.Equal("/", (await Aanmelden.LogInMetCode(beheerder, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/")).Doel());

        var lijst = await beheerder.GetStringAsync(new Uri("/", UriKind.Relative));
        var kaart = await beheerder.GetStringAsync(new Uri($"/a/{id}/grootboek?rekening=1100&jaar=2026", UriKind.Relative));

        Assert.Contains("Webwinkel Voorbeeld B.V.", lijst, StringComparison.Ordinal);
        Assert.Contains("1.234,56", kaart, StringComparison.Ordinal);

        // Het zijmenu in groepen, met de kolommenbalans onder Rapportages.
        var koba = await beheerder.GetStringAsync(new Uri($"/a/{id}/kolommenbalans", UriKind.Relative));
        Assert.Contains("Boekhouden", koba, StringComparison.Ordinal);
        Assert.Contains("Rapportages", koba, StringComparison.Ordinal);
        Assert.Contains("Inrichting", koba, StringComparison.Ordinal);
        Assert.Contains("Saldibalans", koba, StringComparison.Ordinal);
        Assert.Contains("1.234,56", koba, StringComparison.Ordinal);
        var bank = await beheerder.GetStringAsync(new Uri($"/a/{id}/bank", UriKind.Relative));
        Assert.Contains("Afschriften inlezen", bank, StringComparison.Ordinal);
        Assert.Contains($"href=\"/a/{id}/bank\"", koba, StringComparison.Ordinal);

        var buiten = await app.Gebruiker("Bert Buiten");
        var email = (await app.Dienst<GebruikerDienst>().Lijst()).Single(g => g.Id == buiten.Id).Email;
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, email, AdministratieFixture.Wachtwoord, "/");
        var geweigerd = await client.GetStringAsync(new Uri($"/a/{id}/grootboek?rekening=1100&jaar=2026", UriKind.Relative));

        Assert.Contains("bestaat niet of je hebt er geen toegang toe", geweigerd, StringComparison.Ordinal);
        Assert.DoesNotContain("1.234,56", geweigerd, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Relaties_regels_en_instellingen_tonen_wat_de_rol_mag()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Inrichting Voorbeeld B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", null, null, "4310"), "NL91ABNA0417164300");
        await inrichting.VoegRegelToe(wie, id, new Boekhouding.Regels.Verwerkingsregel("Belnet is telefoon", "4310", "I21", NaamBevat: "Belnet", BedragVan: 10, BedragTot: 30));
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);

        using var beheerder = NieuweClient();
        await Aanmelden.LogInMetCode(beheerder, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, await app.Tweestap(wie), "/");
        var relaties = await beheerder.GetStringAsync(new Uri($"/a/{id}/relaties", UriKind.Relative));
        var regels = WebUtility.HtmlDecode(await beheerder.GetStringAsync(new Uri($"/a/{id}/regels", UriKind.Relative)));
        var instellingen = await beheerder.GetStringAsync(new Uri($"/a/{id}/instellingen", UriKind.Relative));

        Assert.Contains("NL91 ABNA 0417 1643 00", relaties, StringComparison.Ordinal);
        Assert.Contains("naam bevat ‘Belnet’", regels, StringComparison.Ordinal);
        Assert.Contains("Regel toevoegen", regels, StringComparison.Ordinal);
        Assert.Contains("Inrichting Voorbeeld B.V.", instellingen, StringComparison.Ordinal);
        Assert.Contains("Limiet automatisch boeken", instellingen, StringComparison.Ordinal);

        using var client = NieuweClient();
        var email = (await app.Dienst<GebruikerDienst>().Lijst()).Single(g => g.Id == ondernemer.Id).Email;
        await Aanmelden.LogIn(client, email, AdministratieFixture.Wachtwoord, "/");
        var regelsOndernemer = await client.GetStringAsync(new Uri($"/a/{id}/regels", UriKind.Relative));
        var instellingenOndernemer = await client.GetStringAsync(new Uri($"/a/{id}/instellingen", UriKind.Relative));

        Assert.Contains("Belnet is telefoon", regelsOndernemer, StringComparison.Ordinal);
        Assert.DoesNotContain("Regel toevoegen", regelsOndernemer, StringComparison.Ordinal);
        Assert.Contains("De instellingen zijn voor het kantoor.", instellingenOndernemer, StringComparison.Ordinal);
        Assert.DoesNotContain("Limiet automatisch boeken", instellingenOndernemer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kantoorinstellingen_alleen_voor_de_beheerder()
    {
        var wie = await app.Beheerder();
        using var beheerder = NieuweClient();
        await Aanmelden.LogInMetCode(beheerder, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, await app.Tweestap(wie), "/");
        var pagina = await beheerder.GetStringAsync(new Uri("/beheer/instellingen", UriKind.Relative));
        Assert.Contains("Documentmap", pagina, StringComparison.Ordinal);
        Assert.Contains("Adrescontrole", pagina, StringComparison.Ordinal);

        var medewerker = await app.Gebruiker("Max Medewerker");
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, await app.Email(medewerker), AdministratieFixture.Wachtwoord, "/");
        var geweigerd = await client.GetAsync(new Uri("/beheer/instellingen", UriKind.Relative));
        Assert.NotEqual(HttpStatusCode.OK, geweigerd.StatusCode);
        Assert.DoesNotContain("Documentmap", await geweigerd.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inloggen_en_uitloggen_komen_in_het_log()
    {
        await app.Beheerder();
        using var client = NieuweClient();

        await Aanmelden.LogIn(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, "/");
        await Aanmelden.LogIn(NieuweClient(), "beheer@kantoor.test", "fout-wachtwoord", "/");

        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.Ingelogd && r.Gebruiker == "Bea Beheer");
        Assert.Contains(log, r => r.Actie == AuditActies.InloggenMislukt);
        Assert.Equal(2, Rollen.Alle.Count);
    }
}
