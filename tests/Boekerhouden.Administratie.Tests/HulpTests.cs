using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De documentatie (/hulp) en het vraagteken: elke pagina wijst naar een hoofdstuk dat er is.</summary>
public sealed class HulpTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Theory]
    [InlineData("", "administraties")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427", "overzicht")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/inkoopboek?jaar=2026", "inkoop")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/inkoop/8c3a1b40-0000-0000-0000-000000000001", "inkoop")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoopboek", "verkoop")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoop/nieuw", "facturen")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoop/terugkerend", "terugkerend")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/crediteuren", "openstaand")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/boeking/MEM/2026-0001", "journaal")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/btw/rondrekening", "btw")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/beginbalans", "instellingen")]
    [InlineData("beheer/gebruikers", "beheer")]
    public void Elke_pagina_wijst_naar_zijn_hoofdstuk(string pad, string hoofdstuk) => Assert.Equal(hoofdstuk, Hulpsectie.Van(pad));

    [Fact]
    public void Elke_regel_en_elk_tabblad_van_het_menu_heeft_een_eigen_hoofdstuk()
    {
        var paden = Administratiemenu.Groepen.SelectMany(g => g.Items).Select(i => i.Pad)
            .Concat(Administratiemenu.Tabgroepen.SelectMany(t => t.Tabs).Select(t => t.Pad));
        Assert.All(paden, p => Assert.NotEqual("overzicht", Hulpsectie.Van("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427" + p)));
    }

    [Fact]
    public async Task De_documentatie_heeft_elk_hoofdstuk_en_het_vraagteken_wijst_erheen()
    {
        var id = await app.Administratie("Hulp Voorbeeld B.V.");
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(await app.Beheerder());
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        var hulp = await client.GetStringAsync(new Uri("/hulp", UriKind.Relative));
        Assert.All(Hulpsectie.Hoofdstukken, h => Assert.Contains($"id=\"{h.Id}\"", hulp, StringComparison.Ordinal));
        Assert.DoesNotContain("hulp-knop", hulp, StringComparison.Ordinal);

        var bank = await client.GetStringAsync(new Uri($"/a/{id}/bank", UriKind.Relative));
        Assert.Contains("href=\"/hulp#bank\"", bank, StringComparison.Ordinal);
        Assert.Contains("popovertarget=\"paginahulp\"", bank, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(Paginahulp.Uitleg["bank"].Hoe[0]), bank, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "administraties")]
    [InlineData("administraties/nieuw", "nieuwe-administratie")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427", "overzicht")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/inkoop", "inkoop")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/inkoop/8c3a1b40-0000-0000-0000-000000000001", "inkoopfactuur")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/inkoopboek?jaar=2026", "inkoopboek")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/stapel/8c3a1b40-0000-0000-0000-000000000001", "stapel")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoop", "verkoop")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoop/nieuw", "factuur")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/verkoop/terugkerend", "terugkerend")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/facturen", "facturen")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/crediteuren", "openstaand")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/boeking/MEM/2026-0001", "boeking")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/btw/rondrekening", "rondrekening")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/activa/staat", "activastaat")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/activa/nieuw", "activum")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/kostenplaatsen/AUTO1", "kostenplaats")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/relaties/L0001", "relatie")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/instellingen/btw", "instellingen-btw")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/beginbalans", "beginbalans")]
    [InlineData("a/1b4e28ba-2fa1-11d2-883f-0016d3cca427/proefbalans", "proefbalans")]
    [InlineData("beheer/btw", "beheer-btw")]
    [InlineData("beheer/iets-anders", "beheer-gebruikers")]
    [InlineData("account/beveiliging", "beveiliging")]
    public void Elke_pagina_heeft_zijn_eigen_uitleg(string pad, string sleutel) => Assert.Equal(sleutel, Paginahulp.Sleutel(pad));

    [Fact]
    public void Elke_pagina_van_de_app_heeft_uitleg_en_die_is_kort()
    {
        // Alle routes van de app, met een voorbeeld voor elke parameter.
        var routes = typeof(Paginahulp).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false).Cast<Microsoft.AspNetCore.Components.RouteAttribute>())
            .Select(r => System.Text.RegularExpressions.Regex.Replace(r.Template.TrimStart('/'), "\\{[^}]+\\}", "x1"))
            .ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, r => Assert.True(Paginahulp.Uitleg.ContainsKey(Paginahulp.Sleutel(r)), r));

        // Elke pagina van een administratie die niet onder het overzicht valt, heeft zijn eigen uitleg.
        Assert.All(routes.Where(r => r.StartsWith("a/x1/", StringComparison.Ordinal)), r => Assert.NotEqual("overzicht", Paginahulp.Sleutel(r)));

        Assert.All(Paginahulp.Uitleg, u =>
        {
            Assert.False(string.IsNullOrWhiteSpace(u.Value.Titel));
            Assert.InRange(u.Value.Hoe.Count, 2, 5);
            Assert.True(u.Value.Wat.Length <= 120, u.Key);
            Assert.All(u.Value.Hoe, p => Assert.True(p.Length <= 130 && p.EndsWith('.'), $"{u.Key}: {p}"));
        });
    }
}
