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
    }
}
