using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Overzichten › Dagboeken: alle dagboeken op één plek met een tabblad per dagboek (inkoop, verkoop, bank, memoriaal …)
/// en per tabblad wat er geboekt is, met het totaal; staat in het menu onder Overzichten; wie geen toegang heeft, ziet
/// niets.
/// </summary>
public sealed class DagboekenPaginaTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Een_tabblad_per_dagboek_met_wat_er_geboekt_is()
    {
        var id = await app.Administratie("Dagboek Voorbeeld B.V.");
        var wie = await app.Beheerder();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans dagboekproef", [new("1100", 1234.56m), new("0500", -1234.56m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new("MEM", "2026-0002", new(2026, 2, 1), "Huur februari", [new("4100", 750m), new("1100", -750m)]))).IsGelukt);

        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(wie);
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        // Zonder keuze het eerste dagboek (inkoop), met de tabbladen van alle dagboeken en de regel in het menu.
        var eerste = await client.GetStringAsync(new Uri($"/a/{id}/dagboeken", UriKind.Relative));
        Assert.Contains("dagboektabs", eerste, StringComparison.Ordinal);
        foreach (var dagboek in (await grootboek.Dagboeken(wie, id)).Select(d => d.Code))
        {
            Assert.Contains($"dagboek={dagboek}&amp;", eerste, StringComparison.Ordinal);
        }

        Assert.Contains($"href=\"/a/{id}/dagboeken\"", eerste, StringComparison.Ordinal);
        Assert.DoesNotContain("Huur februari", eerste, StringComparison.Ordinal);

        // Het memoriaal: beide boekingen en het totaal.
        var memoriaal = await client.GetStringAsync(new Uri($"/a/{id}/dagboeken?dagboek=MEM&van=2026-01-01&tot=2026-12-31", UriKind.Relative));
        Assert.Contains("Beginbalans dagboekproef", memoriaal, StringComparison.Ordinal);
        Assert.Contains("Huur februari", memoriaal, StringComparison.Ordinal);
        Assert.Contains("1.984,56", memoriaal, StringComparison.Ordinal);

        // Het journaal: de dagboekkeuze met de naam ("Memoriaal"), de code in de tooltip.
        var journaal = await client.GetStringAsync(new Uri($"/a/{id}/journaal?van=2026-01-01&tot=2026-12-31", UriKind.Relative));
        Assert.Contains("<option value=\"MEM\" title=\"MEM\">Memoriaal</option>", journaal, StringComparison.Ordinal);

        // Wie geen lid is, ziet niets.
        var buiten = await app.Gebruiker("Bert Buiten");
        var email = (await app.Dienst<GebruikerDienst>().Lijst()).Single(g => g.Id == buiten.Id).Email;
        using var anders = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Aanmelden.LogIn(anders, email, AdministratieFixture.Wachtwoord, "/");
        var geweigerd = await anders.GetStringAsync(new Uri($"/a/{id}/dagboeken?dagboek=MEM&van=2026-01-01&tot=2026-12-31", UriKind.Relative));
        Assert.Contains("bestaat niet of je hebt er geen toegang toe", geweigerd, StringComparison.Ordinal);
        Assert.DoesNotContain("Huur februari", geweigerd, StringComparison.Ordinal);
    }
}
