using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Instellingen in tabbladen: Gegevens (met de IBAN's van de bank al ingevuld, het veld alleen voor andere eigen
/// rekeningen), Btw en belastingen, Verwerken (per groepje "Zoals het kantoor") en Beginbalans. De fiscale indeling
/// staat op Fiscale positie, de loonkoppelingen bij het memoriaal.
/// </summary>
public sealed class InstellingenIndelingTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Tabbladen_bankrekeningen_en_wat_verhuisd_is()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Indeling Voorbeeld B.V.");
        var afschrift = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "ing-2026-03.mt940"));
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, new MemoryStream(afschrift), "ing-2026-03.mt940")).IsGelukt);

        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, await app.Tweestap(wie), "/");

        var gegevens = await client.GetStringAsync(new Uri($"/a/{id}/instellingen", UriKind.Relative));
        foreach (var (pad, naam) in new[] { ("/instellingen", "Gegevens"), ("/instellingen/btw", "Btw en belastingen"), ("/instellingen/verwerken", "Verwerken"), ("/beginbalans", "Beginbalans") })
        {
            Assert.Contains($"href=\"/a/{id}{pad}\"", gegevens, StringComparison.Ordinal);
            Assert.Contains($">{naam}</a>", gegevens, StringComparison.Ordinal);
        }

        // De IBAN van de bank staat er al; het veld is voor de andere eigen rekeningen en is leeg.
        Assert.Contains("NL20 INGB 0001 2345 67", gegevens, StringComparison.Ordinal);
        Assert.Contains("Andere eigen rekeningen", gegevens, StringComparison.Ordinal);
        Assert.Matches("id=\"ibans\"[^>]*value=\"\"", gegevens);
        Assert.DoesNotContain("Betalingen aan de Belastingdienst", gegevens, StringComparison.Ordinal);
        Assert.DoesNotContain("Loonjournaal", gegevens, StringComparison.Ordinal);
        Assert.DoesNotContain("Niet aftrekbaar", gegevens, StringComparison.Ordinal);

        var btw = await client.GetStringAsync(new Uri($"/a/{id}/instellingen/btw", UriKind.Relative));
        Assert.Contains("Btw-plichtig", btw, StringComparison.Ordinal);
        Assert.DoesNotContain("Klantnummer", btw, StringComparison.Ordinal);

        var verwerken = await client.GetStringAsync(new Uri($"/a/{id}/instellingen/verwerken", UriKind.Relative));
        Assert.Contains("Inkoop vanzelf verwerken", verwerken, StringComparison.Ordinal);
        Assert.Contains("Map voor facturen", verwerken, StringComparison.Ordinal);
        Assert.DoesNotContain("Nooit vanzelf boeken boven", verwerken, StringComparison.Ordinal);

        // Bank afwijkend (betaalverschil € 5 op 1100): het bedrag met een €-teken, de rekening als rekeningkeuze.
        var inrichting = app.Dienst<InrichtingDienst>();
        var huidig = await inrichting.Instellingen(wie, id);
        var gewijzigd = await inrichting.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Betaalverschil = 5m, Verschilrekening = "4300" } });
        Assert.True(gewijzigd.IsGelukt, string.Join("; ", gewijzigd.Fouten));
        var bank = await client.GetStringAsync(new Uri($"/a/{id}/instellingen/verwerken", UriKind.Relative));
        Assert.Matches("<span class=\"input-group-text\">€</span>\\s*<input id=\"betaalverschil\"", bank);
        Assert.Matches("<input id=\"verschilrekening\" class=\"rekeningkeuze-invoer\"[^>]*value=\"4300\"", bank);

        var vpb = await client.GetStringAsync(new Uri($"/a/{id}/vpb", UriKind.Relative));
        Assert.Contains("Welke rekeningen niet of beperkt aftrekbaar zijn", vpb, StringComparison.Ordinal);
    }
}
