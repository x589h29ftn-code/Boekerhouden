using System.Security.Claims;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Identiteit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>
/// De samenstelling van de Windows-app: dezelfde schermen en diensten als de webapp, maar zonder webserver en zonder
/// databaseserver. De gegevens staan in één SQLite-bestand in <c>gegevensmap</c>; wie de app start, is de lokale
/// gebruiker (beheerder en kantoor), zonder inloggen of tweestapsverificatie. Geüploade bestanden zijn van de
/// gebruiker zelf en worden niet door een virusscanner van het pakket gehaald.
/// </summary>
public static class LokaleApp
{
    public const string Bestandsnaam = "administratie.db";

    /// <summary>De standaardmap: <c>%LOCALAPPDATA%\Boekerhouden\Administratie</c>.</summary>
    public static string StandaardMap =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boekerhouden", "Administratie");

    public static void ConfigureerDiensten(IServiceCollection services, string gegevensmap)
    {
        ArgumentNullException.ThrowIfNull(services);
        Directory.CreateDirectory(gegevensmap);
        var configuratie = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Opstart.Verbindingsnaam}"] = $"Data Source={Path.Combine(gegevensmap, Bestandsnaam)}",
            })
            .Build();
        services.AddLogging();
        Opstart.ConfigureerKern(services, configuratie, ontwikkeling: false, lokaal: true);
        var sleutels = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(gegevensmap, "sleutels")));
        if (OperatingSystem.IsWindows())
        {
            sleutels.ProtectKeysWithDpapi();
        }

        services.AddAuthorizationCore(o => o.AddPolicy(Rollen.Beheerder, p => p.RequireRole(Rollen.Beheerder)));
        services.AddCascadingAuthenticationState();
        services.AddSingleton<LokaleSessie>();
        services.AddScoped<AuthenticationStateProvider>(sp => new LokaleAuthenticatie(sp.GetRequiredService<LokaleSessie>().Gebruiker));
    }

    /// <summary>Maakt of werkt de database bij en zorgt voor de lokale gebruiker (met de naam van de Windows-gebruiker).</summary>
    public static async Task Start(IServiceProvider diensten, string naam)
    {
        ArgumentNullException.ThrowIfNull(diensten);
        await Opstart.InitialiseerDatabase(diensten);
        diensten.GetRequiredService<LokaleSessie>().Gebruiker = await LokaleGebruiker.Zorg(diensten, naam);
    }
}

/// <summary>Wie de Windows-app gebruikt; gezet bij het starten.</summary>
public sealed class LokaleSessie
{
    public ClaimsPrincipal Gebruiker { get; set; } = new(new ClaimsIdentity());
}
