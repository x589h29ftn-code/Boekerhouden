using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De webapp als kantoorserver: inloggen met een gekoppelde werkplek, zonder wachtwoorden.</summary>
public sealed class KantoorserverFixture : AdministratieFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Kantoorserver:Aan", "true");
    }
}

/// <summary>
/// Werkplekken van de kantoorserver: een pc meldt zich met zijn sleutel, de beheerder koppelt hem aan een gebruiker,
/// daarna logt de pc in met een ondertekende uitdaging. En wat er niet mag: zonder koppeling, met een andere sleutel,
/// een uitdaging twee keer, een gebruiker die uit staat.
/// </summary>
public sealed class WerkplekTests(KantoorserverFixture app) : IClassFixture<KantoorserverFixture>
{
    private HttpClient NieuweClient() => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<(string Stand, string Code, string? Uitdaging)> Meld(HttpClient client, Werkpleksleutel sleutel, string naam = "piet", string computer = "PC-02")
    {
        var antwoord = await client.PostAsJsonAsync("/werkplek/meld", new Werkplekmelding(Convert.ToBase64String(sleutel.Publiek), naam, computer));
        Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);
        var json = await antwoord.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("stand").GetString()!, json.GetProperty("code").GetString()!, json.GetProperty("uitdaging").GetString());
    }

    private static string Binnen(string uitdaging, byte[] handtekening) =>
        $"/werkplek/binnen?uitdaging={uitdaging}&handtekening={Uri.EscapeDataString(Convert.ToBase64String(handtekening))}";

    private async Task<Guid> Gebruiker(string naam)
    {
        var nieuw = await app.Dienst<GebruikerDienst>().MaakZonderWachtwoord(naam, null, null, [], await app.Beheerder());
        Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
        return nieuw.Waarde;
    }

    [Fact]
    public async Task Een_werkplek_meldt_zich_wordt_gekoppeld_en_logt_in_zonder_wachtwoord()
    {
        var beheerder = await app.Beheerder();
        var administratie = await app.Administratie("Gedeelde Zaak B.V.");
        var apparaten = app.Dienst<ApparaatDienst>();
        using var client = NieuweClient();
        using var sleutel = Werkpleksleutel.Nieuw();

        // Zonder koppeling: geen inlogpagina, alleen de melding dat de pc niet gekoppeld is.
        Assert.Equal(Kantoorserver.GeenWerkplek, (await client.GetAsync("/")).Headers.Location!.AbsolutePath);
        Assert.Equal(Kantoorserver.GeenWerkplek, (await client.GetAsync("/inloggen")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Kantoorserver.GeenWerkplek)).StatusCode);

        // Melden: wacht op de beheerder, met dezelfde controlecode als op de werkplek.
        var eerste = await Meld(client, sleutel);
        Assert.Equal(("Wacht", sleutel.Code(null), null), eerste);
        var wachtend = Assert.Single(await apparaten.Lijst(), a => a.GebruikerId is null && a.Computer == "PC-02");
        Assert.Equal(("piet", sleutel.Code(null)), (wachtend.WindowsNaam, wachtend.Code));

        // De beheerder koppelt de werkplek aan een gebruiker en geeft hem toegang tot één administratie.
        var piet = await Gebruiker("Piet Jansen");
        Assert.True((await apparaten.Koppel(beheerder, wachtend.Id, piet)).IsGelukt);
        Assert.True((await app.Dienst<AdministratieDienst>().ZetLid(administratie, piet, Lidmaatschapsrol.Ondernemer, beheerder)).IsGelukt);

        // Nu krijgt de werkplek een uitdaging; ondertekend opent die de app als Piet.
        var tweede = await Meld(client, sleutel);
        Assert.Equal("Gekoppeld", tweede.Stand);
        var binnen = await client.GetAsync(Binnen(tweede.Uitdaging!, sleutel.Teken(tweede.Uitdaging!)));
        Assert.Equal("/", binnen.Headers.Location!.OriginalString);
        var start = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Contains("Piet Jansen", await start.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/a/{administratie}")).StatusCode);

        // Geen beheerder: het beheer van gebruikers blijft dicht; pagina's rond wachtwoorden bestaan niet.
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/beheer/gebruikers")).StatusCode);
        Assert.Equal("/", (await client.GetAsync("/wachtwoord-wijzigen")).Headers.Location!.OriginalString);
        Assert.NotNull(Assert.Single(await apparaten.Lijst(), a => a.GebruikerId == piet).LaatstGezien);
    }

    [Fact]
    public async Task Een_uitdaging_werkt_een_keer_en_alleen_met_de_eigen_sleutel()
    {
        var beheerder = await app.Beheerder();
        var apparaten = app.Dienst<ApparaatDienst>();
        using var sleutel = Werkpleksleutel.Nieuw();
        using var vreemd = Werkpleksleutel.Nieuw();
        await apparaten.Meld(sleutel.Publiek, "kees", "PC-03");
        var werkplek = (await apparaten.Lijst()).Single(a => a.Computer == "PC-03");
        Assert.True((await apparaten.Koppel(beheerder, werkplek.Id, await Gebruiker("Kees de Boer"))).IsGelukt);

        // Een andere sleutel ondertekent de uitdaging: geweigerd, en de uitdaging is verbruikt.
        var uitdaging = (await apparaten.Meld(sleutel.Publiek, "kees", "PC-03")).Uitdaging!;
        Assert.Null(await apparaten.Verifieer(uitdaging, vreemd.Teken(uitdaging)));
        Assert.Null(await apparaten.Verifieer(uitdaging, sleutel.Teken(uitdaging)));

        // De eigen sleutel: goed, maar maar één keer.
        uitdaging = (await apparaten.Meld(sleutel.Publiek, "kees", "PC-03")).Uitdaging!;
        Assert.Equal("Kees de Boer", (await apparaten.Verifieer(uitdaging, sleutel.Teken(uitdaging)))!.Naam);
        Assert.Null(await apparaten.Verifieer(uitdaging, sleutel.Teken(uitdaging)));

        // Verzonnen uitdaging, lege handtekening, een sleutel die geen sleutel is.
        Assert.Null(await apparaten.Verifieer("0123", sleutel.Teken("0123")));
        Assert.Null(await apparaten.Verifieer(null, null));
        Assert.Equal(Werkplekstand.Geweigerd, (await apparaten.Meld([1, 2, 3], "x", "y")).Stand);

        using var client = NieuweClient();
        Assert.Equal(Kantoorserver.GeenWerkplek, (await client.GetAsync(Binnen("0123", [1, 2, 3]))).Headers.Location!.OriginalString);
        Assert.Equal(Kantoorserver.GeenWerkplek, (await client.GetAsync("/werkplek/binnen")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Een_gebruiker_die_uit_staat_of_een_ontkoppelde_werkplek_komt_niet_meer_binnen()
    {
        var beheerder = await app.Beheerder();
        var apparaten = app.Dienst<ApparaatDienst>();
        var gebruikers = app.Dienst<GebruikerDienst>();
        using var sleutel = Werkpleksleutel.Nieuw();
        await apparaten.Meld(sleutel.Publiek, "anna", "PC-04");
        var werkplek = (await apparaten.Lijst()).Single(a => a.Computer == "PC-04");
        var anna = await Gebruiker("Anna Smit");
        Assert.True((await apparaten.Koppel(beheerder, werkplek.Id, anna)).IsGelukt);

        // Uitgezet tussen de uitdaging en het inloggen: geweigerd; daarna krijgt de werkplek geen uitdaging meer.
        var uitdaging = (await apparaten.Meld(sleutel.Publiek, "anna", "PC-04")).Uitdaging!;
        Assert.True((await gebruikers.ZetActief(anna, false, beheerder)).IsGelukt);
        Assert.Null(await apparaten.Verifieer(uitdaging, sleutel.Teken(uitdaging)));
        Assert.Equal(Werkplekstand.Geweigerd, (await apparaten.Meld(sleutel.Publiek, "anna", "PC-04")).Stand);
        Assert.Equal(["Kies een actieve gebruiker."], (await apparaten.Koppel(beheerder, werkplek.Id, anna)).Fouten);

        // Weer aan, dan ontkoppeld: de werkplek staat opnieuw te wachten.
        Assert.True((await gebruikers.ZetActief(anna, true, beheerder)).IsGelukt);
        Assert.Equal(Werkplekstand.Gekoppeld, (await apparaten.Meld(sleutel.Publiek, "anna", "PC-04")).Stand);
        Assert.True((await apparaten.Verwijder(beheerder, werkplek.Id)).IsGelukt);
        Assert.Equal(Werkplekstand.Wacht, (await apparaten.Meld(sleutel.Publiek, "anna", "PC-04")).Stand);

        // De enige actieve beheerder kan niet uit.
        Assert.Equal(["Dit is de enige actieve beheerder; maak eerst iemand anders beheerder."], (await gebruikers.ZetActief(beheerder.Id!.Value, false, beheerder)).Fouten);
    }

    [Fact]
    public async Task Toegang_per_administratie_is_te_geven_en_weer_af_te_nemen()
    {
        var beheerder = await app.Beheerder();
        var administratie = await app.Administratie("Toegang B.V.");
        var dienst = app.Dienst<AdministratieDienst>();
        var toegang = app.Dienst<Toegang>();
        var id = await Gebruiker("Tom Toegang");
        var tom = new Uitvoerder(id, "Tom Toegang");

        Assert.Null(await toegang.Rol(tom, administratie));
        Assert.True((await dienst.ZetLid(administratie, id, Lidmaatschapsrol.Inzien, beheerder)).IsGelukt);
        Assert.Equal(Lidmaatschapsrol.Inzien, await toegang.Rol(tom, administratie));
        Assert.Single(await dienst.Lijst(tom));

        Assert.True((await dienst.VerwijderLid(administratie, id, beheerder)).IsGelukt);
        Assert.Null(await toegang.Rol(tom, administratie));
        Assert.Empty(await dienst.Lijst(tom));
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<GrootboekDienst>().Schema(tom, administratie));
    }

    [Fact]
    public async Task Er_kunnen_niet_eindeloos_werkplekken_wachten()
    {
        var apparaten = app.Dienst<ApparaatDienst>();
        var standen = new List<Werkplekstand>();
        for (var i = 0; i < ApparaatDienst.MaximumWachtend + 3; i++)
        {
            using var sleutel = Werkpleksleutel.Nieuw();
            standen.Add((await apparaten.Meld(sleutel.Publiek, $"vreemd{i}", "ONBEKEND")).Stand);
        }

        Assert.Contains(Werkplekstand.Wacht, standen);
        Assert.Equal(Werkplekstand.Geweigerd, standen[^1]);
        Assert.True((await apparaten.Lijst()).Count(a => a.GebruikerId is null) <= ApparaatDienst.MaximumWachtend);

        // Opruimen, zodat de andere tests in deze klasse weer kunnen melden.
        var beheerder = await app.Beheerder();
        foreach (var a in (await apparaten.Lijst()).Where(a => a.Computer == "ONBEKEND"))
        {
            await apparaten.Verwijder(beheerder, a.Id);
        }
    }

    [Fact]
    public void De_sleutel_van_een_werkplek_is_te_bewaren_en_terug_te_lezen()
    {
        using var sleutel = Werkpleksleutel.Nieuw();
        using var terug = Werkpleksleutel.Van(sleutel.Geheim);

        Assert.Equal(sleutel.Publiek, terug.Publiek);
        Assert.Equal(sleutel.Code(null), terug.Code(null));
        Assert.Matches(@"^\d{3} \d{3}$", sleutel.Code(null));
        // Een ander servercertificaat (iemand ertussen) geeft een andere code.
        Assert.NotEqual(sleutel.Code("AB12"), sleutel.Code("CD34"));
        Assert.Equal(sleutel.Code("ab12"), sleutel.Code("AB12"));
    }

    [Fact]
    public void Het_certificaat_van_de_kantoorserver_wordt_een_keer_gemaakt_en_daarna_hergebruikt()
    {
        var map = Path.Combine(Path.GetTempPath(), "kantoorserver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(map);
        try
        {
            using var eerste = Kantoorserver.Certificaat(map);
            using var tweede = Kantoorserver.Certificaat(map);

            Assert.Equal(eerste.Thumbprint, tweede.Thumbprint);
            Assert.True(tweede.HasPrivateKey);
            Assert.True(tweede.NotAfter > DateTime.Now.AddYears(10));
        }
        finally
        {
            Directory.Delete(map, recursive: true);
        }
    }
}
