namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Overgangen: vensters, het Weergave-menu en de panelen komen zacht binnen; een andere pagina (bijv. van Inkoop naar
/// Verkoop) staat er meteen, zonder beweging (een overvloeier of een inhoud die omhoog schuift voelde onrustig). De
/// laadschets en de melding passen bij het donkere thema. In de browser: <c>scripts/ui/overgangen.js</c>.
/// </summary>
public sealed class OvergangenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Een_andere_pagina_staat_er_meteen_zonder_beweging()
    {
        var id = await app.Administratie("Overgang Voorbeeld B.V.");
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(await app.Beheerder());
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        Assert.DoesNotContain("overgang.", await client.GetStringAsync(new Uri($"/a/{id}/journaal", UriKind.Relative)), StringComparison.Ordinal);
        var index = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Administratie.Windows", "wwwroot", "index.html"));
        Assert.DoesNotContain("overgang.js", index, StringComparison.Ordinal);

        var uitstraling = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Huisstijl", "wwwroot", "uitstraling.css"));
        Assert.DoesNotContain(".administratie-inhoud, .beheer-inhoud", uitstraling, StringComparison.Ordinal);
        var vormgeving = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Administratie", "wwwroot", "vormgeving.css"));
        Assert.DoesNotContain("view-transition", vormgeving, StringComparison.Ordinal);
    }

    [Fact]
    public void Vensters_menu_s_en_panelen_komen_zacht_binnen()
    {
        var wwwroot = Path.Combine(Repository(), "src", "Boekerhouden.Administratie", "wwwroot");
        var vormgeving = File.ReadAllText(Path.Combine(wwwroot, "vormgeving.css"));
        foreach (var deel in new[] { ".venster, .zoekvenster", ".venster-achtergrond, .zoekvenster-achter", ".weergavemenu[open] .menu",
            ".actiepaneel, .bankwerk .verwerkpaneel" })
        {
            Assert.Contains(deel, vormgeving, StringComparison.Ordinal);
        }

        var donker = File.ReadAllText(Path.Combine(wwwroot, "donker.css"));
        Assert.Contains("--bh-schets:", donker, StringComparison.Ordinal);
        Assert.DoesNotContain("backdrop-filter", File.ReadAllText(Path.Combine(wwwroot, "administratie.css")), StringComparison.Ordinal);
        var uitstraling = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Huisstijl", "wwwroot", "uitstraling.css"));
        Assert.DoesNotContain("backdrop-filter", uitstraling, StringComparison.Ordinal);
        Assert.Contains("var(--bh-schets", uitstraling, StringComparison.Ordinal);
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
