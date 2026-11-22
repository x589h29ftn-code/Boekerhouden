namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Overgangen: vensters, het Weergave-menu en de panelen komen zacht binnen, een paginawissel vloeit over met de kop en
/// de zijbalk die blijven staan (webapp en Windows-app), bij "minder beweging" valt dat weg, en de laadschets en de
/// melding passen bij het donkere thema. Hoe het er in de browser uitziet, controleert <c>scripts/ui/overgangen.js</c>.
/// </summary>
public sealed class OvergangenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task De_paginawissel_wordt_geladen_en_wijkt_voor_minder_beweging()
    {
        var id = await app.Administratie("Overgang Voorbeeld B.V.");
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(await app.Beheerder());
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        Assert.Contains("overgang.", await client.GetStringAsync(new Uri($"/a/{id}/journaal", UriKind.Relative)), StringComparison.Ordinal);
        var script = await client.GetStringAsync(new Uri("/overgang.js", UriKind.Relative));
        Assert.Contains("startViewTransition", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", script, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame(", script, StringComparison.Ordinal);

        var index = File.ReadAllText(Path.Combine(Repository(), "src", "Boekerhouden.Administratie.Windows", "wwwroot", "index.html"));
        Assert.Contains("overgang.js", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Vensters_menu_s_en_panelen_komen_zacht_binnen()
    {
        var wwwroot = Path.Combine(Repository(), "src", "Boekerhouden.Administratie", "wwwroot");
        var vormgeving = File.ReadAllText(Path.Combine(wwwroot, "vormgeving.css"));
        foreach (var deel in new[] { ".venster, .zoekvenster", ".venster-achtergrond, .zoekvenster-achter", ".weergavemenu[open] .menu",
            ".actiepaneel, .bankwerk .verwerkpaneel", "view-transition-name: zijbalk", "view-transition-name: kopbalk" })
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
