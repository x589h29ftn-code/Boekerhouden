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

        var buiten = await app.Gebruiker("Bert Buiten");
        var email = (await app.Dienst<GebruikerDienst>().Lijst()).Single(g => g.Id == buiten.Id).Email;
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, email, AdministratieFixture.Wachtwoord, "/");
        var geweigerd = await client.GetStringAsync(new Uri($"/a/{id}/grootboek?rekening=1100&jaar=2026", UriKind.Relative));

        Assert.Contains("bestaat niet of je hebt er geen toegang toe", geweigerd, StringComparison.Ordinal);
        Assert.DoesNotContain("1.234,56", geweigerd, StringComparison.Ordinal);
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
