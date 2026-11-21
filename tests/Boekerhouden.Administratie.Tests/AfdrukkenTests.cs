namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Afdrukken (Ctrl+P): elke pagina van een administratie heeft de afdrukkop (administratie, moment, wie), de afdrukstijl
/// is geladen (webapp en Windows-app) en zet paginanummers en het menu uit. De opmaak op papier zelf controleert
/// <c>scripts/ui/afdrukken.js</c> in de browser.
/// </summary>
public sealed class AfdrukkenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Elke_pagina_heeft_de_afdrukkop_en_de_afdrukstijl()
    {
        var id = await app.Administratie("Afdruk Voorbeeld B.V.");
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(await app.Beheerder());
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        foreach (var pad in new[] { "/kolommenbalans", "/grootboek", "/journaal", "/debiteuren" })
        {
            var html = await client.GetStringAsync(new Uri($"/a/{id}{pad}", UriKind.Relative));
            Assert.Contains("afdrukken.", html, StringComparison.Ordinal);
            Assert.Contains("class=\"afdrukkop\"", html, StringComparison.Ordinal);
            Assert.Contains("<strong>Afdruk Voorbeeld B.V.</strong>", html, StringComparison.Ordinal);
            Assert.Contains("class=\"afdrukmoment\"", html, StringComparison.Ordinal);
        }

        var stijl = await client.GetStringAsync(new Uri("/afdrukken.css", UriKind.Relative));
        Assert.Contains("counter(pages)", stijl, StringComparison.Ordinal);
        foreach (var weg in new[] { ".kopbalk", ".zijbalk", ".statusbalk", ".hulp-knop", "button" })
        {
            Assert.Contains(weg, stijl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void De_windows_app_laadt_de_afdrukstijl_na_de_donkere_kleuren()
    {
        var index = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Administratie.Windows", "wwwroot", "index.html"));
        Assert.True(index.IndexOf("afdrukken.css", StringComparison.Ordinal) > index.IndexOf("donker.css", StringComparison.Ordinal));
    }

    private static string Repository()
    {
        var map = new DirectoryInfo(AppContext.BaseDirectory);
        while (map is not null && !File.Exists(Path.Combine(map.FullName, "Boekerhouden.slnx")))
        {
            map = map.Parent;
        }

        return map?.FullName ?? throw new InvalidOperationException("De map van de repository is niet gevonden.");
    }
}
