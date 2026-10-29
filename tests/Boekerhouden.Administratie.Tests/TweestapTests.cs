using System.Net;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Tweestapsverificatie met een authenticator-app en passkeys (beveiliging B1).</summary>
public sealed class TweestapTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private HttpClient NieuweClient() => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Beheerder_zonder_tweestap_moet_die_eerst_instellen()
    {
        var beheerder = await app.Gebruiker("Bram Beheer");
        await app.MaakBeheerder(beheerder);
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, await app.Email(beheerder), AdministratieFixture.Wachtwoord, "/");

        var antwoord = await client.GetAsync(new Uri("/", UriKind.Relative));
        var pagina = await client.GetStringAsync(new Uri("/account/beveiliging", UriKind.Relative));

        Assert.Equal("/account/beveiliging", antwoord.Doel());
        Assert.Contains("Als beheerder stel je eerst tweestapsverificatie in.", pagina, StringComparison.Ordinal);
        Assert.Contains("<svg", pagina, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aanzetten_met_de_juiste_code_geeft_herstelcodes_en_daarna_een_tweede_stap()
    {
        var wie = await app.Gebruiker("Tina Tweestap");
        var dienst = app.Dienst<BeveiligingDienst>();
        var status = await dienst.Authenticator(wie.Id!.Value);
        var sleutel = status.Sleutel!.Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.False(status.Aan);
        Assert.StartsWith("otpauth://totp/Boekerhouden%20Administratie:", status.Uri, StringComparison.Ordinal);
        Assert.False((await dienst.ZetAan(wie.Id.Value, "000000")).IsGelukt);
        var aan = await dienst.ZetAan(wie.Id.Value, Totp.Code(sleutel));
        Assert.Equal(BeveiligingDienst.AantalHerstelcodes, aan.Waarde!.Count);

        using var client = NieuweClient();
        var eerste = await Aanmelden.LogIn(client, await app.Email(wie), AdministratieFixture.Wachtwoord, "/");
        Assert.StartsWith("/inloggen/tweede-stap", eerste.Doel(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);

        var fout = await Aanmelden.TweedeStap(client, eerste.Doel()!, code: "123456");
        Assert.Contains("De code klopt niet", await fout.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var goed = await Aanmelden.TweedeStap(client, eerste.Doel()!, code: Totp.Code(sleutel));
        Assert.Equal("/", goed.Doel());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Herstelcode_werkt_een_keer()
    {
        var wie = await app.Gebruiker("Hans Herstel");
        await app.Tweestap(wie);
        var codes = await app.Dienst<BeveiligingDienst>().NieuweHerstelcodes(wie.Id!.Value);

        using var eerste = NieuweClient();
        var stap = await Aanmelden.LogIn(eerste, await app.Email(wie), AdministratieFixture.Wachtwoord, "/");
        Assert.Equal("/", (await Aanmelden.TweedeStap(eerste, stap.Doel()!, herstelcode: codes[0])).Doel());

        using var tweede = NieuweClient();
        stap = await Aanmelden.LogIn(tweede, await app.Email(wie), AdministratieFixture.Wachtwoord, "/");
        var opnieuw = await Aanmelden.TweedeStap(tweede, stap.Doel()!, herstelcode: codes[0]);
        Assert.Contains("ongeldig of al gebruikt", await opnieuw.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.HerstelcodeGebruikt && r.Gebruiker == "Hans Herstel");
    }

    [Fact]
    public async Task Beheerder_kan_tweestap_niet_uitzetten_een_ander_wel()
    {
        var beheerder = await app.Gebruiker("Bea Beheer Twee");
        await app.MaakBeheerder(beheerder);
        await app.Tweestap(beheerder);
        var ander = await app.Gebruiker("Olaf Ondernemer");
        await app.Tweestap(ander);
        var dienst = app.Dienst<BeveiligingDienst>();

        Assert.Equal("Voor een beheerder is tweestapsverificatie verplicht.", (await dienst.ZetUit(beheerder.Id!.Value)).Fouten[0]);
        Assert.True((await dienst.ZetUit(ander.Id!.Value)).IsGelukt);
        Assert.False((await dienst.Authenticator(ander.Id.Value)).Aan);
    }

    [Fact]
    public async Task Passkey_opties_alleen_met_antiforgery_en_nieuw_alleen_ingelogd()
    {
        await app.Beheerder();
        using var client = NieuweClient();

        var zonderToken = await client.PostAsync(new Uri("/account/passkey/opties-inloggen", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.BadRequest, zonderToken.StatusCode);

        var token = Aanmelden.Token(await client.GetStringAsync(new Uri("/inloggen", UriKind.Relative)));
        using var verzoek = new HttpRequestMessage(HttpMethod.Post, new Uri("/account/passkey/opties-inloggen", UriKind.Relative));
        verzoek.Headers.Add("RequestVerificationToken", token);
        var opties = await client.SendAsync(verzoek);
        Assert.Equal(HttpStatusCode.OK, opties.StatusCode);
        Assert.Contains("\"challenge\"", await opties.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var nieuw = await client.PostAsync(new Uri("/account/passkey/opties-nieuw", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Redirect, nieuw.StatusCode);
        Assert.StartsWith("/inloggen", nieuw.Doel(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Onbekende_passkey_verwijderen_en_de_qr_code()
    {
        var wie = await app.Gebruiker("Pim Passkey");
        var dienst = app.Dienst<BeveiligingDienst>();

        Assert.Empty(await dienst.Passkeys(wie.Id!.Value));
        Assert.False((await dienst.VerwijderPasskey(wie.Id.Value, "bestaat-niet")).IsGelukt);
        var svg = BeveiligingDienst.QrSvg("otpauth://totp/test?secret=ABC");
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("<path d=\"M", svg, StringComparison.Ordinal);
    }
}

/// <summary>De pagina Beveiliging via echte HTTP-verzoeken: instellen en daarna meteen verder kunnen.</summary>
public sealed class BeveiligingPaginaTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Na_aanzetten_kan_de_beheerder_direct_verder()
    {
        var beheerder = await app.Gebruiker("Bob Beheer");
        await app.MaakBeheerder(beheerder);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await Aanmelden.LogIn(client, await app.Email(beheerder), AdministratieFixture.Wachtwoord, "/");
        var pagina = await client.GetStringAsync(new Uri("/account/beveiliging", UriKind.Relative));
        var sleutel = System.Text.RegularExpressions.Regex.Match(pagina, "<code class=\"fs-6\">([a-z0-9 ]+)</code>").Groups[1].Value;

        var antwoord = await client.PostAsync(new Uri("/account/beveiliging", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Aanmelden.Token(pagina),
            ["_handler"] = "authenticator-aan",
            ["Aanzetten.Code"] = Totp.Code(sleutel),
        }));

        Assert.Contains("Je herstelcodes", await antwoord.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/", UriKind.Relative))).StatusCode);
    }
}
