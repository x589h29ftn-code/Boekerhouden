using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Identiteit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Server.Tests;

public sealed class GebruikerDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private GebruikerDienst Gebruikers => server.Dienst<GebruikerDienst>();

    private static GebruikerInvoer Invoer(string email, params string[] rollen) =>
        new(email, "Mia Medewerker", "MM", null, rollen.Length == 0 ? [Rollen.Medewerker] : rollen);

    [Fact]
    public async Task Eerste_beheerder_kan_maar_een_keer()
    {
        await server.Beheerder();

        Assert.False(await Gebruikers.EersteBeheerderNodig());
        var tweede = await Gebruikers.MaakEersteBeheerder(Invoer("tweede@kantoor.test"), TestGegevens.Wachtwoord);
        Assert.False(tweede.IsGelukt);
        var beheerder = (await Gebruikers.Lijst()).Single(g => g.Email == "beheer@kantoor.test");
        Assert.Equal(Rollen.Alle, beheerder.Rollen);
    }

    [Fact]
    public async Task Nieuwe_gebruiker_krijgt_tijdelijk_wachtwoord()
    {
        var beheerder = await server.Beheerder();

        var uitkomst = await Gebruikers.Maak(Invoer("nieuw@kantoor.test", Rollen.Medewerker, Rollen.Reviewer), beheerder);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var regel = (await Gebruikers.Lijst()).Single(g => g.Email == "nieuw@kantoor.test");
        Assert.True(regel.MoetWachtwoordWijzigen);
        Assert.Equal([Rollen.Medewerker, Rollen.Reviewer], regel.Rollen);
        Assert.True(await KanInloggen("nieuw@kantoor.test", uitkomst.Waarde!));
    }

    [Fact]
    public async Task Dubbel_emailadres_wordt_geweigerd_in_het_nederlands()
    {
        var beheerder = await server.Beheerder();
        await Gebruikers.Maak(Invoer("dubbel@kantoor.test"), beheerder);

        var tweede = await Gebruikers.Maak(Invoer("dubbel@kantoor.test"), beheerder);

        Assert.False(tweede.IsGelukt);
        Assert.Contains(tweede.Fouten, f => f.Contains("bestaat al een gebruiker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rol_is_verplicht()
    {
        var uitkomst = await Gebruikers.Maak(new GebruikerInvoer("geenrol@kantoor.test", "Naam", "N", null, []), await server.Beheerder());
        Assert.Equal(["Kies minstens één rol."], uitkomst.Fouten);
    }

    [Fact]
    public async Task Jezelf_deactiveren_kan_niet()
    {
        var beheerder = await server.Beheerder();

        var uitkomst = await Gebruikers.ZetActief(beheerder.Id!.Value, false, beheerder);

        Assert.False(uitkomst.IsGelukt);
    }

    [Fact]
    public async Task Eigen_beheerdersrol_intrekken_kan_niet()
    {
        var beheerder = await server.Beheerder();

        var uitkomst = await Gebruikers.Wijzig(beheerder.Id!.Value,
            new GebruikerInvoer("beheer@kantoor.test", "Bea Beheer", "BB", "AA", [Rollen.Medewerker]), beheerder);

        Assert.False(uitkomst.IsGelukt);
    }

    [Fact]
    public async Task Laatste_actieve_beheerder_blijft_beheerder()
    {
        var beheerder = await server.Beheerder();
        var anderId = await MaakGebruiker("tweede-beheerder@kantoor.test", Rollen.Beheerder);

        // De ander mag de (enige andere) beheerder niet deactiveren als die daarna de laatste zou zijn... maar
        // er zijn er nu twee: deactiveren van de tweede kan dus wel.
        Assert.True((await Gebruikers.ZetActief(anderId, false, beheerder)).IsGelukt);
        var ander = new Uitvoerder(anderId, "Tweede beheerder");

        var uitkomst = await Gebruikers.ZetActief(beheerder.Id!.Value, false, ander);

        Assert.False(uitkomst.IsGelukt);
        Assert.Contains("laatste actieve beheerder", Assert.Single(uitkomst.Fouten), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gedeactiveerde_gebruiker_kan_niet_inloggen_en_na_activeren_weer_wel()
    {
        var beheerder = await server.Beheerder();
        var uitkomst = await Gebruikers.Maak(Invoer("tijdelijk-weg@kantoor.test"), beheerder);
        var id = (await Gebruikers.Lijst()).Single(g => g.Email == "tijdelijk-weg@kantoor.test").Id;

        await Gebruikers.ZetActief(id, false, beheerder);
        Assert.False(await KanInloggen("tijdelijk-weg@kantoor.test", uitkomst.Waarde!));

        await Gebruikers.ZetActief(id, true, beheerder);
        Assert.True(await KanInloggen("tijdelijk-weg@kantoor.test", uitkomst.Waarde!));
    }

    [Fact]
    public async Task Wachtwoord_reset_geeft_nieuw_tijdelijk_wachtwoord()
    {
        var beheerder = await server.Beheerder();
        var oud = (await Gebruikers.Maak(Invoer("reset@kantoor.test"), beheerder)).Waarde!;
        var id = (await Gebruikers.Lijst()).Single(g => g.Email == "reset@kantoor.test").Id;

        var nieuw = await Gebruikers.ResetWachtwoord(id, beheerder);

        Assert.True(nieuw.IsGelukt);
        Assert.NotEqual(oud, nieuw.Waarde);
        Assert.False(await KanInloggen("reset@kantoor.test", oud));
        Assert.True(await KanInloggen("reset@kantoor.test", nieuw.Waarde!));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent("reset@kantoor.test"), r => r.Actie == AuditActies.WachtwoordGereset);
    }

    [Fact]
    public async Task Tijdelijke_wachtwoorden_voldoen_aan_het_beleid()
    {
        await using var scope = server.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = new Gebruiker { UserName = "x@kantoor.test", Email = "x@kantoor.test", Naam = "X" };
        var gezien = new HashSet<string>();

        for (var i = 0; i < 200; i++)
        {
            var wachtwoord = GebruikerDienst.TijdelijkWachtwoord();
            Assert.Matches("^[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$", wachtwoord);
            Assert.DoesNotMatch("[0O1lI]", wachtwoord);
            foreach (var validator in users.PasswordValidators)
            {
                var resultaat = await validator.ValidateAsync(users, gebruiker, wachtwoord);
                Assert.True(resultaat.Succeeded, $"{wachtwoord}: {string.Join("; ", resultaat.Errors.Select(e => e.Description))}");
            }

            Assert.True(gezien.Add(wachtwoord));
        }
    }

    [Theory]
    [InlineData("Wachtwoord2025", false)]
    [InlineData("Welkom123456", false)]
    [InlineData("MiaMedewerker1", false)]
    [InlineData("medewerker-x1A", false)]
    [InlineData("Groene-Appel-Boom-42", true)]
    public async Task Veelgebruikte_wachtwoorden_worden_geweigerd(string wachtwoord, bool toegestaan)
    {
        var gebruiker = new Gebruiker { Email = "medewerker@kantoor.test", Naam = "Mia Medewerker" };
        var resultaat = await new VeelgebruiktWachtwoordValidator().ValidateAsync(null!, gebruiker, wachtwoord);
        Assert.Equal(toegestaan, resultaat.Succeeded);
    }

    [Fact]
    public async Task Wachtwoordbeleid_in_het_nederlands()
    {
        await using var scope = server.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();

        var resultaat = await users.CreateAsync(new Gebruiker { UserName = "kort@kantoor.test", Email = "kort@kantoor.test", Naam = "Kort" }, "kort");

        Assert.Contains(resultaat.Errors, e => e.Description == "Het wachtwoord moet minstens 12 tekens lang zijn.");
        Assert.Contains(resultaat.Errors, e => e.Description == "Het wachtwoord moet minstens één hoofdletter bevatten.");
        Assert.Contains(resultaat.Errors, e => e.Description == "Het wachtwoord moet minstens één cijfer bevatten.");
    }

    private async Task<Guid> MaakGebruiker(string email, params string[] rollen)
    {
        var uitkomst = await Gebruikers.Maak(Invoer(email, rollen), await server.Beheerder());
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await Gebruikers.Lijst()).Single(g => g.Email == email).Id;
    }

    private async Task<bool> KanInloggen(string email, string wachtwoord)
    {
        await using var scope = server.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.FindByEmailAsync(email);
        return gebruiker is not null && !await users.IsLockedOutAsync(gebruiker) && await users.CheckPasswordAsync(gebruiker, wachtwoord);
    }
}
