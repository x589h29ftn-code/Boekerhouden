using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Administratie.Lokaal;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Updates en reservekopieën van de Windows-app, op echte SQLite-bestanden: een nieuwe versie op gegevens van een
/// oudere versie (met reservekopie ervoor en controles erna), een database van een nieuwere versie (niet openen), een
/// mislukte update (alles terug), terugzetten bij de volgende start, het bewaarschema, de tweede map en een
/// beschadigde kopie.
/// </summary>
public sealed class UpdateEnReservekopieTests : IDisposable
{
    /// <summary>Een eerdere databaseversie: vóór perioden afsluiten en vaste activa.</summary>
    private const string OudeVersie = "20261107084043_Margegroep";

    private readonly string map = Path.Combine(Path.GetTempPath(), "update-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(map))
        {
            Directory.Delete(map, recursive: true);
        }
    }

    private ServiceProvider Bouw()
    {
        var services = new ServiceCollection();
        LokaleApp.ConfigureerDiensten(services, map);
        return services.BuildServiceProvider();
    }

    private string Database => Path.Combine(map, LokaleApp.Bestandsnaam);

    /// <summary>Start de app, vult hem met proefgegevens (boekingen, bank, inkoop met documenten, een actief met afschrijvingen) en sluit hem weer.</summary>
    private async Task<(Guid Administratie, long Boekingen)> GevuldeApp()
    {
        await using var diensten = Bouw();
        await LokaleApp.Start(diensten, "Piet Proef");
        var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
        var uitkomst = await Proefgegevens.Vul(diensten, wie, Proefomvang.Klein, 2026);
        SqliteConnection.ClearAllPools();
        return (uitkomst.Administratie, Reservekopieen.Onderzoek(Database).Boekingen);
    }

    /// <summary>Zet de database terug naar een oudere versie, zoals een gebruiker die nog een oude versie van de app had.</summary>
    private async Task NaarVersie(string versie)
    {
        await using var diensten = Bouw();
        await using var scope = diensten.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(versie);
        SqliteConnection.ClearAllPools();
    }

    private async Task<IReadOnlyList<string>> Toegepast()
    {
        await using var diensten = Bouw();
        await using var scope = diensten.CreateAsyncScope();
        var lijst = (await scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().Database.GetAppliedMigrationsAsync()).ToList();
        SqliteConnection.ClearAllPools();
        return lijst;
    }

    private void Sql(string sql)
    {
        using var verbinding = new SqliteConnection($"Data Source={Database};Pooling=False");
        verbinding.Open();
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = sql;
        opdracht.ExecuteNonQuery();
    }

    [Fact]
    public async Task Een_nieuwe_versie_op_gegevens_van_een_oudere_versie()
    {
        var (_, boekingen) = await GevuldeApp();
        await NaarVersie(OudeVersie);
        Assert.Equal(OudeVersie, (await Toegepast())[^1]);
        Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);

        await using (var diensten = Bouw())
        {
            Assert.Empty(await LokaleApp.Start(diensten, "Piet Proef"));
            var kopieen = diensten.GetRequiredService<Reservekopieen>();

            // Een gecontroleerde reservekopie van de oude versie, en alles is er nog.
            var voor = Assert.Single(kopieen.Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
            Assert.Equal((OudeVersie, boekingen), (voor.Migratie, voor.Boekingen));
            Assert.Null(kopieen.Controleer(voor));
            Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));

            // In het wijzigingslog: van welke naar welke versie, met de reservekopie.
            await using var scope = diensten.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
            var log = await db.AuditRegels.AsNoTracking().SingleAsync(a => a.Actie == AuditActies.DatabaseBijgewerkt);
            Assert.StartsWith($"{OudeVersie} naar ", log.Onderwerp, StringComparison.Ordinal);
            Assert.Contains(voor.Bestand, log.Details, StringComparison.Ordinal);

            // De app werkt daarna gewoon: boeken kan, en de nieuwe tabellen zijn er.
            var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
            var id = await db.Administraties.AsNoTracking().Select(a => a.Id).FirstAsync();
            Assert.True((await diensten.GetRequiredService<GrootboekDienst>().Boek(wie, id,
                new Journaalpost("MEM", "2026-0901", new(2026, 11, 1), "Na de update", [new Journaalregel("4300", 10m), new Journaalregel("1800", -10m)]))).IsGelukt);
            Assert.Empty(await diensten.GetRequiredService<ActivaDienst>().Lijst(wie, id));
        }

        // Een tweede start: niets meer bij te werken, geen nieuwe kopie vóór een update.
        SqliteConnection.ClearAllPools();
        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Piet Proef");
            Assert.Single(diensten.GetRequiredService<Reservekopieen>().Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
        }
    }

    /// <summary>Vanaf elke van de laatste databaseversies, met gegevens, in één keer naar de huidige.</summary>
    [Theory]
    [InlineData("20261103112622_Standaardomschrijving")]
    [InlineData("20261103122001_VasteVerkoopregel")]
    [InlineData("20261106154932_BankmutatieDubbel")]
    [InlineData("20261106203142_ArchiverenEnVerwijderen")]
    [InlineData("20261106205721_Dashboard")]
    [InlineData("20261107195623_PeriodenAfsluiten")]
    [InlineData("20261107200506_VasteActiva")]
    public async Task Bijwerken_vanaf_een_eerdere_versie(string versie)
    {
        var (_, boekingen) = await GevuldeApp();
        await NaarVersie(versie);
        Assert.Equal(versie, (await Toegepast())[^1]);

        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Piet Proef");
            var voor = Assert.Single(diensten.GetRequiredService<Reservekopieen>().Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
            Assert.Equal(versie, voor.Migratie);
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
        }

        SqliteConnection.ClearAllPools();
        var na = Reservekopieen.Onderzoek(Database);
        Assert.Equal((true, boekingen), (na.Integer, na.Boekingen));
        await using var nieuw = Bouw();
        await using var scope = nieuw.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Gegevens_van_een_nieuwere_versie_worden_niet_geopend_en_niet_veranderd()
    {
        await GevuldeApp();
        Sql("INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231000000_Toekomst', '99.0')");
        var controlegetal = Reservekopieen.Controlegetal(Database);

        await using var diensten = Bouw();
        var fout = await Assert.ThrowsAsync<NieuwereDatabaseException>(() => LokaleApp.Start(diensten, "Piet Proef"));

        Assert.Contains("nieuwere versie", fout.Message, StringComparison.Ordinal);
        Assert.Contains("29991231000000_Toekomst", fout.Message, StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
        Assert.Equal(controlegetal, Reservekopieen.Controlegetal(Database));
        Assert.DoesNotContain(diensten.GetRequiredService<Reservekopieen>().Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
    }

    [Fact]
    public async Task Een_mislukte_update_zet_alles_terug_zoals_het_was()
    {
        var (_, boekingen) = await GevuldeApp();
        await NaarVersie(OudeVersie);
        // Iets in de database waar de migratie op vastloopt: een tabel met de naam van een nieuwe tabel.
        Sql("CREATE TABLE \"Activa\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Iets\" TEXT)");
        Sql("INSERT INTO \"Activa\" VALUES ('a', 'van de gebruiker')");
        var voor = await Toegepast();

        await using (var diensten = Bouw())
        {
            var fout = await Assert.ThrowsAsync<DatabaseupdateMisluktException>(() => LokaleApp.Start(diensten, "Piet Proef"));
            Assert.Contains("teruggezet zoals ze waren", fout.Message, StringComparison.Ordinal);
            Assert.Single(diensten.GetRequiredService<Reservekopieen>().Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
        }

        SqliteConnection.ClearAllPools();
        // Precies de oude versie, met alle boekingen en de eigen tabel.
        Assert.Equal(voor, await Toegepast());
        var na = Reservekopieen.Onderzoek(Database);
        Assert.True(na.Integer);
        Assert.Equal(boekingen, na.Boekingen);
        using var verbinding = new SqliteConnection($"Data Source={Database};Pooling=False");
        verbinding.Open();
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = "SELECT \"Iets\" FROM \"Activa\"";
        Assert.Equal("van de gebruiker", opdracht.ExecuteScalar());
        // Geen half bijgewerkte tabellen van de nieuwe versie.
        opdracht.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Afschrijvingen', 'Desinvesteringen')";
        Assert.Equal(0L, opdracht.ExecuteScalar());
    }

    [Fact]
    public async Task Een_reservekopie_terugzetten_bij_de_volgende_start_en_dat_weer_ongedaan_maken()
    {
        var (id, boekingen) = await GevuldeApp();
        Reservekopie kopie;
        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Piet Proef");
            var kopieen = diensten.GetRequiredService<Reservekopieen>();
            kopie = kopieen.Maak(Reservekopiesoort.Handmatig);
            Assert.Equal(boekingen, kopie.Boekingen);

            // Daarna nog geboekt; dan de kopie klaarzetten.
            var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
            Assert.True((await diensten.GetRequiredService<GrootboekDienst>().Boek(wie, id,
                new Journaalpost("MEM", "2026-0902", new(2026, 11, 2), "Na de kopie", [new Journaalregel("4300", 25m), new Journaalregel("1800", -25m)]))).IsGelukt);
            Assert.Null(kopieen.PlanTerugzetten(kopie));
            Assert.Equal(kopie.Bestand, kopieen.GeplandTerugzetten!.Bestand);
        }

        SqliteConnection.ClearAllPools();
        Reservekopie voorTerugzetten;
        await using (var diensten = Bouw())
        {
            var meldingen = await LokaleApp.Start(diensten, "Piet Proef");
            Assert.Contains(meldingen, m => m.Contains("is teruggezet", StringComparison.Ordinal));
            var kopieen = diensten.GetRequiredService<Reservekopieen>();
            Assert.Null(kopieen.GeplandTerugzetten);
            Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));

            // De gegevens van vlak ervoor zijn bewaard (met de boeking van na de kopie).
            voorTerugzetten = Assert.Single(kopieen.Lijst(), k => k.Soort == Reservekopiesoort.VoorTerugzetten);
            Assert.Equal(boekingen + 1, voorTerugzetten.Boekingen);
            await using var scope = diensten.CreateAsyncScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().AuditRegels.AnyAsync(a => a.Actie == AuditActies.ReservekopieTeruggezet));

            // Toch niet: die kopie weer terugzetten.
            Assert.Null(kopieen.PlanTerugzetten(voorTerugzetten));
        }

        SqliteConnection.ClearAllPools();
        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Piet Proef");
            Assert.Equal(boekingen + 1, Reservekopieen.Onderzoek(Database).Boekingen);
        }
    }

    [Fact]
    public async Task Een_beschadigde_of_veranderde_kopie_wordt_niet_teruggezet()
    {
        await GevuldeApp();
        var kopieen = new Reservekopieen(map, TimeProvider.System);
        var kopie = kopieen.Maak(Reservekopiesoort.Handmatig);
        Assert.Null(kopieen.Controleer(kopie));

        // Eén byte anders in het bestand.
        var pad = Path.Combine(kopieen.Map, kopie.Bestand);
        var inhoud = await File.ReadAllBytesAsync(pad);
        inhoud[inhoud.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(pad, inhoud);

        Assert.Contains("controlegetal klopt niet", kopieen.Controleer(kopie), StringComparison.Ordinal);
        Assert.NotNull(kopieen.PlanTerugzetten(kopie));
        Assert.Null(kopieen.GeplandTerugzetten);

        // Ook als hij toch klaarstond (bijv. later beschadigd): bij het starten niet terugzetten.
        await File.WriteAllTextAsync(Path.Combine(map, "terugzetten.json"), System.Text.Json.JsonSerializer.Serialize(kopie));
        var controlegetal = Reservekopieen.Controlegetal(Database);
        Assert.StartsWith("Niet teruggezet", kopieen.VoerGeplandTerugzettenUit(), StringComparison.Ordinal);
        Assert.Equal(controlegetal, Reservekopieen.Controlegetal(Database));
    }

    [Fact]
    public async Task Elke_dag_een_kopie_en_een_vast_bewaarschema()
    {
        await GevuldeApp();
        var klok = new Klok(new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero));
        var kopieen = new Reservekopieen(map, klok);
        foreach (var k in kopieen.Lijst())
        {
            kopieen.Verwijder(k);
        }

        Assert.True(kopieen.DagelijkseNodig());
        for (var dag = 0; dag < 20; dag++)
        {
            kopieen.Maak(Reservekopiesoort.Dagelijks);
            Assert.False(kopieen.DagelijkseNodig());
            klok.Nu = klok.Nu.AddDays(1);
        }

        for (var i = 0; i < 8; i++)
        {
            kopieen.Maak(Reservekopiesoort.VoorUpdate);
            klok.Nu = klok.Nu.AddMinutes(1);
        }

        for (var i = 0; i < 4; i++)
        {
            kopieen.Maak(Reservekopiesoort.VoorTerugzetten);
            klok.Nu = klok.Nu.AddMinutes(1);
        }

        kopieen.Maak(Reservekopiesoort.Handmatig);
        kopieen.Maak(Reservekopiesoort.Handmatig);

        var lijst = kopieen.Lijst();
        Assert.Equal(5, lijst.Count(k => k.Soort == Reservekopiesoort.VoorUpdate));
        Assert.Equal(3, lijst.Count(k => k.Soort == Reservekopiesoort.VoorTerugzetten));
        Assert.Equal(2, lijst.Count(k => k.Soort == Reservekopiesoort.Handmatig));
        // Dagelijks: de laatste 14 (26 januari t/m 8 februari) plus de eerste van januari (20 januari).
        var dagelijks = lijst.Where(k => k.Soort == Reservekopiesoort.Dagelijks).Select(k => k.Gemaakt.UtcDateTime.Date).Order().ToList();
        Assert.Equal(15, dagelijks.Count);
        Assert.Equal(new DateTime(2026, 1, 20), dagelijks[0]);
        Assert.Equal(new DateTime(2026, 1, 26), dagelijks[1]);
        // Wat opgeruimd is, staat ook niet meer op schijf (bestand en gegevens).
        Assert.True(lijst.Count * 2 == Directory.GetFiles(kopieen.Map).Length, string.Join("\n", Directory.GetFiles(kopieen.Map).Select(Path.GetFileName).Order()));
    }

    [Fact]
    public async Task Een_tweede_map_krijgt_elke_kopie_en_een_onbereikbare_map_houdt_niets_tegen()
    {
        await GevuldeApp();
        var kopieen = new Reservekopieen(map, TimeProvider.System);
        var tweede = Path.Combine(map, "nas");
        kopieen.TweedeMap = tweede;
        Assert.Equal(tweede, new Reservekopieen(map, TimeProvider.System).TweedeMap);

        var kopie = kopieen.Maak(Reservekopiesoort.Handmatig);
        Assert.Null(kopie.Opmerking);
        Assert.True(File.Exists(Path.Combine(tweede, kopie.Bestand)));
        Assert.True(File.Exists(Path.Combine(tweede, Path.ChangeExtension(kopie.Bestand, ".json"))));
        Assert.Equal(kopie.Sha256, Reservekopieen.Controlegetal(Path.Combine(tweede, kopie.Bestand)));

        // Een tweede "map" die een bestand is: de kopie staat er toch, met een opmerking.
        var bestand = Path.Combine(map, "geen-map.txt");
        await File.WriteAllTextAsync(bestand, "x");
        kopieen.TweedeMap = bestand;
        var tweedeKopie = kopieen.Maak(Reservekopiesoort.Handmatig);
        Assert.Contains("Niet naar de tweede map", tweedeKopie.Opmerking, StringComparison.Ordinal);
        Assert.Null(kopieen.Controleer(tweedeKopie));

        kopieen.TweedeMap = " ";
        Assert.Null(kopieen.TweedeMap);
    }

    [Fact]
    public async Task Een_kopie_terwijl_de_app_open_is_klopt_en_is_te_openen()
    {
        var (id, boekingen) = await GevuldeApp();
        await using var diensten = Bouw();
        await LokaleApp.Start(diensten, "Piet Proef");
        await using var scope = diensten.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        await db.Database.OpenConnectionAsync();
        Assert.True(await db.Administraties.AnyAsync(a => a.Id == id));

        var kopie = diensten.GetRequiredService<Reservekopieen>().Maak(Reservekopiesoort.Handmatig);

        Assert.Equal(boekingen, kopie.Boekingen);
        Assert.Equal(1, kopie.Administraties);
        Assert.Equal((await Toegepast())[^1], kopie.Migratie);
        // De kopie is één volledig bestand (geen los -wal-bestand ernaast): alleen dat bestand ergens anders neerzetten
        // geeft dezelfde database, met dezelfde aantallen.
        var kopiemap = diensten.GetRequiredService<Reservekopieen>().Map;
        Assert.Empty(Directory.GetFiles(kopiemap, "*-wal"));
        var elders = Path.Combine(map, "elders.db");
        File.Copy(Path.Combine(kopiemap, kopie.Bestand), elders);
        var onderzoek = Reservekopieen.Onderzoek(elders);
        Assert.Equal((true, boekingen, 1L), (onderzoek.Integer, onderzoek.Boekingen, onderzoek.Administraties));
        using var verbinding = new SqliteConnection($"Data Source={elders};Mode=ReadOnly;Pooling=False");
        verbinding.Open();
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = "PRAGMA journal_mode";
        Assert.Equal("delete", opdracht.ExecuteScalar());
    }

    private sealed class Klok(DateTimeOffset nu) : TimeProvider
    {
        public DateTimeOffset Nu { get; set; } = nu;

        public override DateTimeOffset GetUtcNow() => Nu;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
