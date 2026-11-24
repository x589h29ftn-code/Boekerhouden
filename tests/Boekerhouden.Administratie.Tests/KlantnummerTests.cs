using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Het klantnummer van het kantoor bij een administratie (bijv. 2920): bij het aanmaken en in de instellingen, vooraan in
/// de lijst met administraties, te zoeken, in het wijzigingslog; hoogstens 20 tekens. Het instellingenscherm toont bij
/// de Belastingdienst de rekening die Sifer zelf kiest als grijze tekst.
/// </summary>
public sealed class KlantnummerTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Klantnummer_bij_aanmaken_in_de_lijst_en_te_zoeken()
    {
        var wie = await app.Beheerder();
        var dienst = app.Dienst<AdministratieDienst>();
        var gemaakt = await dienst.Maak(new AdministratieInvoer("Klantnummer Voorbeeld B.V.", Boekhouding.Grootboek.Ondernemingsvorm.BV, null, null, Klantnummer: " 2920 "), wie);
        Assert.True(gemaakt.IsGelukt, string.Join("; ", gemaakt.Fouten));

        Assert.Equal("2920", Assert.Single(await dienst.Lijst(wie, "2920")).Klantnummer);
        Assert.Equal("2920", (await dienst.Open(wie, gemaakt.Waarde))!.Klantnummer);
        Assert.False((await dienst.Maak(new AdministratieInvoer("Te lang B.V.", Boekhouding.Grootboek.Ondernemingsvorm.BV, null, null, Klantnummer: new string('9', 21)), wie)).IsGelukt);
    }

    [Fact]
    public async Task Klantnummer_in_de_instellingen_wijzigen_staat_in_het_log()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Instelling Voorbeeld B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var huidig = await inrichting.Instellingen(wie, id);
        Assert.Null(huidig.Klantnummer);

        var uitkomst = await inrichting.WijzigInstellingen(wie, id, huidig with { Klantnummer = "3105" });
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal("3105", (await inrichting.Instellingen(wie, id)).Klantnummer);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.InstellingenGewijzigd && r.AdministratieId == id && r.Details!.Contains("klantnummer - → 3105", StringComparison.Ordinal));
        Assert.False((await inrichting.WijzigInstellingen(wie, id, huidig with { Klantnummer = new string('1', 21) })).IsGelukt);
    }

    [Fact]
    public async Task De_lijst_en_de_instellingen_tonen_klantnummer_en_standaardrekening()
    {
        var wie = await app.Beheerder();
        var gemaakt = await app.Dienst<AdministratieDienst>().Maak(new AdministratieInvoer("Webklant Voorbeeld B.V.", Boekhouding.Grootboek.Ondernemingsvorm.BV, null, null, Klantnummer: "7311"), wie);
        var id = gemaakt.Waarde;
        using var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var sleutel = await app.Tweestap(wie);
        await Aanmelden.LogInMetCode(client, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, sleutel, "/");

        var lijst = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        Assert.Matches("<th class=\"klantnummer\">Klantnr\\.</th><th>Naam</th>", lijst);
        Assert.Contains("<td class=\"klantnummer text-nowrap\">7311</td>", lijst, StringComparison.Ordinal);

        var instellingen = await client.GetStringAsync(new Uri($"/a/{id}/instellingen", UriKind.Relative));
        Assert.Contains("value=\"7311\"", instellingen, StringComparison.Ordinal);
        var btw = await client.GetStringAsync(new Uri($"/a/{id}/instellingen/btw", UriKind.Relative));
        Assert.Contains("Betalingen aan de Belastingdienst", btw, StringComparison.Ordinal);
        Assert.Matches("placeholder=\"standaard [0-9]+ [^\"]+\"", btw);
    }
}
