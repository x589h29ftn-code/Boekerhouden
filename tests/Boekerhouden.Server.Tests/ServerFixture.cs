using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Boekerhouden.Server.Tests;

/// <summary>
/// Start de applicatie tegen een eigen, lege PostgreSQL-database die na afloop wordt verwijderd.
/// De server staat in de omgevingsvariabele BOEKERHOUDEN_TEST_DB (standaard: de lokale ontwikkeldatabase-rol).
/// </summary>
public sealed class ServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string StandaardServer = "Host=localhost;Username=boekerhouden;Password=boekerhouden-dev";

    private readonly string server = Environment.GetEnvironmentVariable("BOEKERHOUDEN_TEST_DB") ?? StandaardServer;
    private readonly string databasenaam = $"boekerhouden_test_{Guid.NewGuid():N}";

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

    /// <summary>Maakt (eenmalig) een beheerder aan en geeft die als uitvoerder terug.</summary>
    public async Task<Uitvoerder> Beheerder()
    {
        var gebruikers = Dienst<GebruikerDienst>();
        if (await gebruikers.EersteBeheerderNodig())
        {
            var uitkomst = await gebruikers.MaakEersteBeheerder(
                new GebruikerInvoer("beheer@kantoor.test", "Bea Beheer", "BB", "AA", Rollen.Alle), TestGegevens.Wachtwoord);
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        }

        var beheerder = (await gebruikers.Lijst()).Single(g => g.Email == "beheer@kantoor.test");
        return new Uitvoerder(beheerder.Id, beheerder.Naam);
    }
}

internal static class TestGegevens
{
    public const string Wachtwoord = "Groene-Appel-Boom-42";

    public static string FictiefPad(params string[] delen) => Path.Combine([AppContext.BaseDirectory, "Fictief", .. delen]);

    public static byte[] Fictief(params string[] delen) => File.ReadAllBytes(FictiefPad(delen));

    private static int teller;

    /// <summary>Een uniek klantnummer, zodat tests in dezelfde database elkaar niet raken.</summary>
    public static string Klantnummer() => $"T{Interlocked.Increment(ref teller):0000}-{Guid.NewGuid().ToString("N")[..6]}";
}
