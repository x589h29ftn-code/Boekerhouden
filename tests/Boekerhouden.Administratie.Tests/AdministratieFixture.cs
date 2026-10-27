using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Start het administratiepakket tegen een eigen, lege PostgreSQL-database die na afloop wordt verwijderd.
/// De server staat in de omgevingsvariabele BOEKERHOUDEN_TEST_DB (standaard: de lokale ontwikkeldatabase-rol).
/// </summary>
public sealed class AdministratieFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Wachtwoord = "Groene-Appel-Boom-42";
    private const string StandaardServer = "Host=localhost;Username=boekerhouden;Password=boekerhouden-dev";

    private readonly string server = Environment.GetEnvironmentVariable("BOEKERHOUDEN_TEST_DB") ?? StandaardServer;
    private readonly string databasenaam = $"administratie_test_{Guid.NewGuid():N}";

    public string Verbinding => new NpgsqlConnectionStringBuilder(server) { Database = databasenaam }.ConnectionString;

    public T Dienst<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        await using var verbinding = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
        await verbinding.OpenAsync();
        await using var opdracht = new NpgsqlCommand($"CREATE DATABASE \"{databasenaam}\"", verbinding);
        await opdracht.ExecuteNonQueryAsync();

        // Start de server; die past de migraties toe en maakt de rollen aan.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var verbinding = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
        await verbinding.OpenAsync();
        await using var opdracht = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databasenaam}\" WITH (FORCE)", verbinding);
        await opdracht.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{Opstart.Verbindingsnaam}", Verbinding);
    }

    /// <summary>Maakt (eenmalig) de eerste beheerder aan (ook kantoor) en geeft die als uitvoerder terug.</summary>
    public async Task<Uitvoerder> Beheerder()
    {
        var gebruikers = Dienst<GebruikerDienst>();
        if (await gebruikers.EersteBeheerderNodig())
        {
            var uitkomst = await gebruikers.MaakEersteBeheerder(new GebruikerInvoer("beheer@kantoor.test", "Bea Beheer", "BB", Rollen.Alle), Wachtwoord);
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        }

        var beheerder = (await gebruikers.Lijst()).Single(g => g.Email == "beheer@kantoor.test");
        return new Uitvoerder(beheerder.Id, beheerder.Naam);
    }

    /// <summary>Een gebruiker met een vast (eigen) wachtwoord, zonder rollen.</summary>
    public async Task<Uitvoerder> Gebruiker(string naam)
    {
        var email = $"{naam.ToLowerInvariant().Replace(' ', '.')}-{Guid.NewGuid():N}@klant.test";
        var uitkomst = await Dienst<GebruikerDienst>().Maak(new GebruikerInvoer(email, naam, naam[..1], []), await Beheerder());
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = (await users.FindByEmailAsync(email))!;
        await users.ChangePasswordAsync(gebruiker, uitkomst.Waarde!, Wachtwoord);
        gebruiker.MoetWachtwoordWijzigen = false;
        await users.UpdateAsync(gebruiker);
        return new Uitvoerder(gebruiker.Id, gebruiker.Naam);
    }

    /// <summary>Een nieuwe administratie, aangemaakt door de beheerder (die er kantoorlid van wordt).</summary>
    public async Task<Guid> Administratie(string naam = "Bakkerij Voorbeeld", Boekhouding.Grootboek.Ondernemingsvorm vorm = Boekhouding.Grootboek.Ondernemingsvorm.BV)
    {
        var uitkomst = await Dienst<AdministratieDienst>().Maak(new AdministratieInvoer(naam, vorm, null, null), await Beheerder());
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return uitkomst.Waarde;
    }

    /// <summary>Een databaseverbinding buiten de app om, zoals iemand met toegang tot de server die zou hebben.</summary>
    public async Task<int> VoerSqlUit(string sql)
    {
        await using var db = await Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        return await db.Database.ExecuteSqlRawAsync(sql);
    }
}
