using System.Net;
using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Server.Tests;

/// <summary>Inloggen, uitloggen en autorisatie via echte HTTP-verzoeken (zoals een browser zonder JavaScript).</summary>
public sealed class InloggenTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private HttpClient NieuweClient() => server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Zonder_inloggen_naar_de_inlogpagina()
    {
        await server.Beheerder();
        using var client = NieuweClient();

        foreach (var pad in new[] { "/", "/klanten/nieuw", "/beheer/gebruikers", "/importsjabloon.xlsx" })
        {
            var antwoord = await client.GetAsync(new Uri(pad, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Redirect, antwoord.StatusCode);
            Assert.StartsWith("/inloggen?ReturnUrl=", antwoord.Doel(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Inlogpagina_is_in_het_nederlands()
    {
        await server.Beheerder();
        using var client = NieuweClient();

        var html = await client.GetStringAsync(new Uri("/inloggen", UriKind.Relative));

        Assert.Contains("<html lang=\"nl\">", html, StringComparison.Ordinal);
        Assert.Contains("E-mailadres", html, StringComparison.Ordinal);
        Assert.Contains("Inloggen", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inloggen_geeft_toegang_tot_de_app_en_het_sjabloon()
    {
        await server.Beheerder();
        using var client = NieuweClient();

        var antwoord = await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "/");
        Assert.Equal(HttpStatusCode.Redirect, antwoord.StatusCode);
        Assert.Equal("/", antwoord.Doel());

        var klanten = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, klanten.StatusCode);
        Assert.Contains("Klanten", await klanten.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var sjabloon = await client.GetAsync(new Uri("/importsjabloon.xlsx?boekjaar=2025&klant=Test", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, sjabloon.StatusCode);
        Assert.Equal("importsjabloon-2025.xlsx", sjabloon.Content.Headers.ContentDisposition?.FileName);
        var bytes = await sjabloon.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        var log = await server.Dienst<AuditDienst>().Recent(AuditActies.Ingelogd);
        Assert.Contains(log, r => r.Gebruiker == "Bea Beheer");
    }

    [Fact]
    public async Task Open_redirect_naar_een_andere_site_wordt_genegeerd()
    {
        await server.Beheerder();
        using var client = NieuweClient();

        var antwoord = await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "https://voorbeeld.invalid/phishing");

        Assert.Equal(HttpStatusCode.Redirect, antwoord.StatusCode);
        Assert.Equal("/", antwoord.Doel());
    }

    [Fact]
    public async Task Verkeerd_wachtwoord_en_blokkade_na_vijf_pogingen()
    {
        var beheerder = await server.Beheerder();
        var tijdelijk = (await server.Dienst<GebruikerDienst>().Maak(
            new GebruikerInvoer("blokkade@kantoor.test", "Bob Blokkade", "BB", null, [Rollen.Medewerker]), beheerder)).Waarde!;
        using var client = NieuweClient();

        for (var poging = 1; poging <= 4; poging++)
        {
            var fout = await Aanmelden.LogIn(client, "blokkade@kantoor.test", "Fout-Wachtwoord-99", "/");
            Assert.Equal(HttpStatusCode.OK, fout.StatusCode);
            Assert.Contains("Onjuist e-mailadres of wachtwoord", await fout.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var vijfde = await Aanmelden.LogIn(client, "blokkade@kantoor.test", "Fout-Wachtwoord-99", "/");
        Assert.Equal("/geblokkeerd", vijfde.Doel());

        // Ook het juiste wachtwoord helpt nu niet meer.
        var juist = await Aanmelden.LogIn(client, "blokkade@kantoor.test", tijdelijk, "/");
        Assert.Equal("/geblokkeerd", juist.Doel());
    }

    [Fact]
    public async Task Tijdelijk_wachtwoord_moet_eerst_worden_gewijzigd()
    {
        var beheerder = await server.Beheerder();
        var tijdelijk = (await server.Dienst<GebruikerDienst>().Maak(
            new GebruikerInvoer("eerstewijzigen@kantoor.test", "Eva Eerst", "EE", null, [Rollen.Medewerker]), beheerder)).Waarde!;
        using var client = NieuweClient();

        var inloggen = await Aanmelden.LogIn(client, "eerstewijzigen@kantoor.test", tijdelijk, "/");
        Assert.Equal("/wachtwoord-wijzigen", inloggen.Doel());

        var elders = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal("/wachtwoord-wijzigen", elders.Doel());

        var formulier = await client.GetStringAsync(new Uri("/wachtwoord-wijzigen", UriKind.Relative));
        var gewijzigd = await client.PostAsync(new Uri("/wachtwoord-wijzigen", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Aanmelden.Token(formulier),
            ["_handler"] = "wachtwoord-wijzigen",
            ["Invoer.Huidig"] = tijdelijk,
            ["Invoer.Nieuw"] = "Rode-Fiets-Berg-77",
            ["Invoer.Herhaal"] = "Rode-Fiets-Berg-77",
        }));
        Assert.Equal("/", gewijzigd.Doel());

        var daarna = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, daarna.StatusCode);
    }

    [Fact]
    public async Task Medewerker_heeft_geen_toegang_tot_beheer()
    {
        var beheerder = await server.Beheerder();
        var gebruikers = server.Dienst<GebruikerDienst>();
        var tijdelijk = (await gebruikers.Maak(new GebruikerInvoer("geenbeheer@kantoor.test", "Gijs Geen", "GG", null, [Rollen.Medewerker]), beheerder)).Waarde!;
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, "geenbeheer@kantoor.test", tijdelijk, "/");
        var formulier = await client.GetStringAsync(new Uri("/wachtwoord-wijzigen", UriKind.Relative));
        await client.PostAsync(new Uri("/wachtwoord-wijzigen", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Aanmelden.Token(formulier),
            ["_handler"] = "wachtwoord-wijzigen",
            ["Invoer.Huidig"] = tijdelijk,
            ["Invoer.Nieuw"] = "Blauwe-Zee-Golf-31",
            ["Invoer.Herhaal"] = "Blauwe-Zee-Golf-31",
        }));

        var beheer = await client.GetAsync(new Uri("/beheer/gebruikers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, beheer.StatusCode);
        Assert.StartsWith("/geen-toegang", beheer.Doel(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uitloggen()
    {
        await server.Beheerder();
        using var client = NieuweClient();
        await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "/");
        var pagina = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        var uit = await client.PostAsync(new Uri("/account/uitloggen", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Aanmelden.Token(pagina),
        }));

        Assert.Equal("/inloggen", uit.Doel());
        var daarna = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, daarna.StatusCode);
    }
}

internal static class AntwoordUitbreidingen
{
    /// <summary>Het pad waarheen wordt doorverwezen (zonder http://localhost).</summary>
    public static string? Doel(this HttpResponseMessage antwoord) =>
        antwoord.Headers.Location is not { } uri ? null : uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
}

/// <summary>Een lege installatie stuurt door naar het aanmaken van de eerste gebruiker.</summary>
public sealed class EersteGebruikerTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Lege_database_vraagt_om_eerste_gebruiker()
    {
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var inloggen = await client.GetAsync(new Uri("/inloggen", UriKind.Relative));
        Assert.Equal("/eerste-gebruiker", inloggen.Doel());

        var pagina = await client.GetStringAsync(new Uri("/eerste-gebruiker", UriKind.Relative));
        Assert.Contains("Account aanmaken", pagina, StringComparison.Ordinal);

        await server.Beheerder();
        var daarna = await client.GetAsync(new Uri("/eerste-gebruiker", UriKind.Relative));
        Assert.Equal("/inloggen", daarna.Doel());
    }
}
