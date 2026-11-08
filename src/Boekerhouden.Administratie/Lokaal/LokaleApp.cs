using System.Security.Claims;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
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
        services.AddSingleton(sp => new Reservekopieen(gegevensmap, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<AuthenticationStateProvider>(sp => new LokaleAuthenticatie(sp.GetRequiredService<LokaleSessie>().Gebruiker));
    }

    /// <summary>
    /// Het starten van de Windows-app: eerst een klaargezette reservekopie terugzetten, dan de database veilig bijwerken
    /// (<see cref="Databaseupdate"/>: eerst een reservekopie, na afloop controles, bij een fout alles terug), de lokale
    /// gebruiker (met de naam van de Windows-gebruiker), en eens per dag een reservekopie. Geeft wat de gebruiker moet
    /// weten (bijv. dat een reservekopie is teruggezet).
    /// </summary>
    /// <exception cref="NieuwereDatabaseException">De gegevens zijn van een nieuwere versie van het pakket.</exception>
    /// <exception cref="DatabaseupdateMisluktException">De update mislukte; de gegevens zijn teruggezet.</exception>
    public static async Task<IReadOnlyList<string>> Start(IServiceProvider diensten, string naam)
    {
        ArgumentNullException.ThrowIfNull(diensten);
        var meldingen = new List<string>();
        var kopieen = diensten.GetRequiredService<Reservekopieen>();
        var teruggezet = kopieen.VoerGeplandTerugzettenUit();
        var update = await Databaseupdate.Voer(diensten, kopieen);
        await Opstart.InitialiseerDatabase(diensten);
        var gebruiker = await LokaleGebruiker.Zorg(diensten, naam);
        diensten.GetRequiredService<LokaleSessie>().Gebruiker = gebruiker;
        var wie = gebruiker.AlsUitvoerder();
        var audit = diensten.GetRequiredService<Diensten.AuditDienst>();
        if (teruggezet is not null)
        {
            meldingen.Add(teruggezet);
            await audit.Log(wie, Diensten.AuditActies.ReservekopieTeruggezet, teruggezet);
        }

        if (update is { Van: not null })
        {
            await audit.Log(wie, Diensten.AuditActies.DatabaseBijgewerkt, $"{update.Van} naar {update.Naar}",
                $"{update.Migraties} migratie(s); {update.Administraties} administraties, {update.Boekingen} boekingen gecontroleerd; reservekopie {update.Reservekopie?.Bestand}");
        }

        if (kopieen.DagelijkseNodig())
        {
            try
            {
                var dagelijks = kopieen.Maak(Reservekopiesoort.Dagelijks);
                await audit.Log(wie, Diensten.AuditActies.ReservekopieGemaakt, dagelijks.Bestand, "dagelijks" + (dagelijks.Opmerking is { } o ? $"; {o}" : null));
            }
            catch (Exception fout) when (fout is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                meldingen.Add($"De dagelijkse reservekopie is niet gelukt: {fout.Message}");
            }
        }

        return meldingen;
    }
}

/// <summary>Wie de Windows-app gebruikt; gezet bij het starten.</summary>
public sealed class LokaleSessie
{
    public ClaimsPrincipal Gebruiker { get; set; } = new(new ClaimsIdentity());
}
