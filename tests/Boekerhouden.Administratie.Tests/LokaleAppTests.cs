using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Administratie.Lokaal;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Documenten;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De Windows-app zonder venster: dezelfde diensten als in het venster, met een SQLite-bestand en de lokale gebruiker,
/// zonder inloggen en zonder server.
/// </summary>
public sealed class LokaleAppTests : IAsyncLifetime
{
    private readonly string map = Path.Combine(Path.GetTempPath(), "lokale-app-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider? diensten;

    public async Task InitializeAsync()
    {
        diensten = Bouw();
        await LokaleApp.Start(diensten, "Piet Proef");
    }

    public async Task DisposeAsync()
    {
        if (diensten is not null)
        {
            await diensten.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        Directory.Delete(map, recursive: true);
    }

    [Fact]
    public async Task Lokale_gebruiker_is_meteen_ingelogd_als_beheerder_en_kantoor()
    {
        var gebruiker = (await Status()).User;

        Assert.True(gebruiker.Identity?.IsAuthenticated);
        Assert.True(gebruiker.IsInRole(Rollen.Beheerder));
        Assert.True(gebruiker.IsInRole(Rollen.Kantoor));
        Assert.Equal("Piet Proef", gebruiker.AlsUitvoerder().Naam);
        Assert.True(File.Exists(Path.Combine(map, LokaleApp.Bestandsnaam)));

        // Opnieuw starten (zelfde bestand): dezelfde gebruiker, geen tweede.
        await using var opnieuw = Bouw();
        await LokaleApp.Start(opnieuw, "Piet Proef");
        Assert.Single(await opnieuw.GetRequiredService<GebruikerDienst>().Lijst());
        Assert.True(opnieuw.GetRequiredService<Werkomgeving>().Lokaal);
    }

    [Fact]
    public async Task Boekhouden_zonder_inloggen()
    {
        var wie = (await Status()).User.AlsUitvoerder();
        var diensten = this.diensten!;
        var administratie = await diensten.GetRequiredService<AdministratieDienst>().Maak(new AdministratieInvoer("Proef B.V.", Ondernemingsvorm.BV, null, null), wie);
        Assert.True(administratie.IsGelukt, string.Join("; ", administratie.Fouten));
        var id = administratie.Waarde;

        var grootboek = diensten.GetRequiredService<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new("1100", 1234.56m), new("0500", -1234.56m)]))).IsGelukt);
        Assert.Equal(1234.56m, (await grootboek.Kaart(wie, id, "1100", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);

        Assert.True((await diensten.GetRequiredService<KantoorDienst>().Wijzig(wie, Verwerkingsinstellingen.Standaard with { Drempel = 3 })).IsGelukt);

        var inkoop = diensten.GetRequiredService<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.Equal(Documentstatus.TeVerwerken, gelezen.Status);
        var herkend = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!.Herkend!;
        Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId.Value, new Factuurinvoer(null, new("Belnet B.V.", herkend.Iban, herkend.BtwNummer, null), "BN-2026-0411",
            new(2026, 4, 1), new(2026, 4, 15), "Telefoon #maand", [new("", "4310", "I21", 20.65m, 4.34m)]))).IsGelukt);

        // IBAN van een relatie wijzigen: zonder wachtwoord (er is er geen), wel in het log.
        var inrichting = diensten.GetRequiredService<InrichtingDienst>();
        var relatie = (await inrichting.Relaties(wie, id)).Single();
        Assert.True((await inrichting.ZetIbans(wie, id, relatie.Id, ["NL20INGB0001234567"], "")).IsGelukt);
        Assert.Contains(await diensten.GetRequiredService<AuditDienst>().Recent(), r => r.Actie == AuditActies.IbanGewijzigd && r.Gebruiker == "Piet Proef");
    }

    private ServiceProvider Bouw()
    {
        var services = new ServiceCollection();
        LokaleApp.ConfigureerDiensten(services, map);

        // Geen Microsoft Defender op de testmachine: een scanner die alles goedkeurt.
        services.RemoveAll<IVirusscanner>();
        services.AddSingleton<IVirusscanner, SchoneScanner>();
        return services.BuildServiceProvider();
    }

    private async Task<AuthenticationState> Status()
    {
        await using var scope = diensten!.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
    }

    private sealed class SchoneScanner : IVirusscanner
    {
        public Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default) => Task.FromResult(new Scanresultaat(Scanuitslag.Schoon));
    }
}
