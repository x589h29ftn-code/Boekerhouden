using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Administratie.Lokaal;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Draait alleen met BOEKERHOUDEN_KOPIESTRESS=&lt;grootte van de database in MB&gt; (bijv. 500). De test gebruikt een
/// eigen map met het SQLite-bestand van de Windows-app; reken op tien keer die grootte aan schijfruimte.
/// </summary>
internal sealed class KopiestressFactAttribute : FactAttribute
{
    public KopiestressFactAttribute()
    {
        if (Megabytes == 0)
        {
            Skip = "Zet BOEKERHOUDEN_KOPIESTRESS=<MB> (bijv. 500) om de stresstest van reservekopie en update te draaien.";
        }
    }

    public static int Megabytes => int.TryParse(Environment.GetEnvironmentVariable("BOEKERHOUDEN_KOPIESTRESS"), CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;
}

/// <summary>
/// Reservekopie en update van de Windows-app onder druk, op een echt SQLite-bestand: een grote database (vijf
/// administraties met boekingen en vooral documenten, want die maken het bestand groot), dan
/// (1) hoe lang een kopie, de controle, terugzetten en het starten duren;
/// (2) kopieën achter elkaar terwijl vijf gebruikers doorwerken: elke kopie moet kloppen en op zichzelf te openen zijn;
/// (3) een update vanaf een oudere versie, en een update die mislukt (alles terug);
/// (4) onderbroken situaties: een half geschreven kopie in de map, een database die halverwege een update is blijven steken;
/// (5) herhaalde updates en wat er aan kopieën blijft staan.
/// </summary>
public sealed class KopieEnUpdateStressTests(ITestOutputHelper uitvoer) : IDisposable
{
    /// <summary>Een eerdere databaseversie (dezelfde als in <see cref="UpdateEnReservekopieTests"/>).</summary>
    private const string OudeVersie = "20261107084043_Margegroep";
    private const int Administraties = 5;

    private readonly string map = Path.Combine(Environment.GetEnvironmentVariable("BOEKERHOUDEN_KOPIESTRESS_MAP") ?? Path.GetTempPath(), "kopiestress-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> verslag = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(map))
        {
            Directory.Delete(map, recursive: true);
        }
    }

    private string Database => Path.Combine(map, LokaleApp.Bestandsnaam);

    private void Schrijf(string regel)
    {
        uitvoer.WriteLine(regel);
        verslag.Add(regel);
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_KOPIESTRESS_VERSLAG") is { Length: > 0 } pad)
        {
            File.WriteAllLines(pad, verslag);
        }
    }

    private ServiceProvider Bouw(string? andereMap = null)
    {
        var services = new ServiceCollection();
        LokaleApp.ConfigureerDiensten(services, andereMap ?? map);
        return services.BuildServiceProvider();
    }

    private static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

    private static async Task<(T Uitkomst, double Seconden)> Tijd<T>(Func<Task<T>> werk)
    {
        var klok = Stopwatch.StartNew();
        var uitkomst = await werk();
        return (uitkomst, klok.Elapsed.TotalSeconds);
    }

    private static (T Uitkomst, double Seconden) Tijd<T>(Func<T> werk)
    {
        var klok = Stopwatch.StartNew();
        var uitkomst = werk();
        return (uitkomst, klok.Elapsed.TotalSeconds);
    }

    [KopiestressFact]
    public async Task Reservekopie_en_update_op_een_grote_database_en_terwijl_er_gewerkt_wordt()
    {
        var doel = KopiestressFactAttribute.Megabytes;
        List<Guid> administraties;
        long boekingen;

        // 0. Vullen: vijf administraties met boekingen en documenten tot de gevraagde grootte.
        await using (var diensten = Bouw())
        {
            Assert.Empty(await LokaleApp.Start(diensten, "Stress Test"));
            var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
            var (ids, seconden) = await Tijd(() => Vul(diensten, wie, doel));
            administraties = ids;
            SqliteConnection.ClearAllPools();
            boekingen = Reservekopieen.Onderzoek(Database).Boekingen;
            Schrijf($"database gevuld in {seconden:0} s: {Mb(new FileInfo(Database).Length):N0} MB, {Administraties} administraties, {boekingen:N0} boekingen");
        }

        // 1. Kopie, controle, tweede map, terugzetten en starten op de grote database.
        Schrijf("");
        Schrijf("--- 1. de grote database");
        await using (var diensten = Bouw())
        {
            var (_, start) = await Tijd(() => LokaleApp.Start(diensten, "Stress Test"));
            Schrijf($"   {"starten (geen update, dagelijkse kopie al gemaakt)",-52} {start,7:0.0} s");
            var kopieen = diensten.GetRequiredService<Reservekopieen>();
            var (kopie, maken) = Tijd(() => kopieen.Maak(Reservekopiesoort.Handmatig));
            Schrijf($"   {"reservekopie maken (met controle en controlegetal)",-52} {maken,7:0.0} s   {Mb(kopie.Grootte):N0} MB");
            var (oordeel, controleren) = Tijd(() => kopieen.Controleer(kopie));
            Assert.Null(oordeel);
            Schrijf($"   {"reservekopie controleren",-52} {controleren,7:0.0} s");
            kopieen.TweedeMap = Path.Combine(map, "tweede");
            var (metTweede, tweede) = Tijd(() => kopieen.Maak(Reservekopiesoort.Handmatig));
            Assert.True(File.Exists(Path.Combine(map, "tweede", metTweede.Bestand)));
            Schrijf($"   {"reservekopie maken met een tweede map",-52} {tweede,7:0.0} s");
            kopieen.TweedeMap = null;
            Directory.Delete(Path.Combine(map, "tweede"), recursive: true);
            kopieen.Verwijder(metTweede);

            // Terugzetten: na de kopie nog een boeking; na het terugzetten is die weg en klopt alles.
            var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
            Assert.True((await diensten.GetRequiredService<GrootboekDienst>().Boek(wie, administraties[0], new("MEM", "2026-9001", new(2026, 12, 30), "Na de kopie", [new("4300", 1m), new("1100", -1m)]))).IsGelukt);
            Assert.Null(kopieen.PlanTerugzetten(kopie));
        }

        SqliteConnection.ClearAllPools();
        await using (var diensten = Bouw())
        {
            var (meldingen, terug) = await Tijd(() => LokaleApp.Start(diensten, "Stress Test"));
            Assert.Single(meldingen);
            Schrijf($"   {"terugzetten bij de start (huidige gegevens eerst bewaard)",-52} {terug,7:0.0} s");
            SqliteConnection.ClearAllPools();
            Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
        }

        // 2. Kopieën terwijl vijf gebruikers doorwerken.
        Schrijf("");
        Schrijf("--- 2. reservekopieën terwijl vijf gebruikers doorwerken");
        SqliteConnection.ClearAllPools();
        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Stress Test");
            var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
            var kopieen = diensten.GetRequiredService<Reservekopieen>();
            var zonder = await Werk(diensten, wie, administraties, TimeSpan.FromSeconds(15), null);
            Schrijf($"   zonder kopie:  {zonder.Tijden.Count,5} boekingen, mediaan {zonder.Mediaan:0} ms, p95 {zonder.P95:0} ms, max {zonder.Max:0} ms, {zonder.Fouten.Count} fouten");

            var gemaakt = new List<(Reservekopie Kopie, double Seconden, long Voor, long Na)>();
            var met = await Werk(diensten, wie, administraties, TimeSpan.FromSeconds(1), async stop =>
            {
                // Tien kopieën achter elkaar, terwijl de anderen doorboeken.
                for (var i = 0; i < 10; i++)
                {
                    var voor = Reservekopieen.Onderzoek(Database).Boekingen;
                    var (kopie, seconden) = await Task.Run(() => Tijd(() => kopieen.Maak(Reservekopiesoort.Handmatig)));
                    gemaakt.Add((kopie, seconden, voor, Reservekopieen.Onderzoek(Database).Boekingen));
                }

                stop();
            });
            Schrijf($"   tijdens kopie: {met.Tijden.Count,5} boekingen, mediaan {met.Mediaan:0} ms, p95 {met.P95:0} ms, max {met.Max:0} ms, {met.Fouten.Count} fouten");
            Schrijf($"   10 kopieën: {gemaakt.Min(g => g.Seconden):0.0} tot {gemaakt.Max(g => g.Seconden):0.0} s per stuk");
            foreach (var fout in zonder.Fouten.Concat(met.Fouten).GroupBy(f => f).Take(5))
            {
                Schrijf($"      {fout.Count()}× {fout.Key}");
            }

            Assert.Empty(zonder.Fouten);
            Assert.Empty(met.Fouten);

            // Elke kopie is heel en bevat een stand tussen "voor" en "na"; drie ervan ook echt openen en de ketens nalopen.
            foreach (var (kopie, _, voor, na) in gemaakt)
            {
                Assert.Null(kopieen.Controleer(kopie));
                Assert.InRange(kopie.Boekingen, voor, na);
            }

            foreach (var (kopie, _, _, _) in new[] { gemaakt[0], gemaakt[5], gemaakt[^1] })
            {
                var los = Path.Combine(map, "los-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(los);
                File.Copy(Path.Combine(kopieen.Map, kopie.Bestand), Path.Combine(los, LokaleApp.Bestandsnaam));
                await using (var geopend = Bouw(los))
                {
                    Assert.Null(await Databaseupdate.ControleerKetens(geopend));
                    var eigenaar = (await LokaleGebruiker.Zorg(geopend, "Stress Test")).AlsUitvoerder();
                    foreach (var id in administraties)
                    {
                        var balans = await geopend.GetRequiredService<GrootboekDienst>().Kolommenbalans(eigenaar, id, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31));
                        Assert.Equal(0m, balans.Regels.Sum(r => r.Saldo));
                    }
                }

                SqliteConnection.ClearAllPools();
                Directory.Delete(los, recursive: true);
            }

            Schrijf("   elke kopie: integriteit en controlegetal goed; drie kopieën geopend: hash-ketens heel, balansen sluiten");
            foreach (var (kopie, _, _, _) in gemaakt)
            {
                kopieen.Verwijder(kopie);
            }

            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
        }

        SqliteConnection.ClearAllPools();
        boekingen = Reservekopieen.Onderzoek(Database).Boekingen;

        // 3. Update vanaf een oudere versie, en een update die mislukt.
        Schrijf("");
        Schrijf("--- 3. update");
        var (_, omlaag) = await Tijd(async () => { await NaarVersie(OudeVersie); return 0; });
        Schrijf($"   {"(voorbereiding: terug naar de oude databaseversie)",-52} {omlaag,7:0.0} s");
        await using (var diensten = Bouw())
        {
            var (meldingen, update) = await Tijd(() => LokaleApp.Start(diensten, "Stress Test"));
            Assert.Empty(meldingen);
            Schrijf($"   {"update bij de start (kopie, bijwerken, controles)",-52} {update,7:0.0} s");
            var voor = Assert.Single(diensten.GetRequiredService<Reservekopieen>().Lijst(), k => k.Soort == Reservekopiesoort.VoorUpdate);
            Assert.Equal((OudeVersie, boekingen), (voor.Migratie, voor.Boekingen));
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);

        // Mislukt: een tabel met de naam van een tabel uit de nieuwste versie laat de laatste stap van de update vastlopen.
        await NaarVersie(OudeVersie);
        Sql("CREATE TABLE \"Apparaten\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Iets\" TEXT)");
        var oud = await Toegepast();
        await using (var diensten = Bouw())
        {
            var klok = Stopwatch.StartNew();
            var fout = await Assert.ThrowsAsync<DatabaseupdateMisluktException>(() => LokaleApp.Start(diensten, "Stress Test"));
            Assert.Contains("teruggezet zoals ze waren", fout.Message, StringComparison.Ordinal);
            Schrijf($"   {"mislukte update: kopie, poging, alles teruggezet",-52} {klok.Elapsed.TotalSeconds,7:0.0} s");
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(oud, await Toegepast());
        var naMislukt = Reservekopieen.Onderzoek(Database);
        Assert.True(naMislukt.Integer);
        Assert.Equal(boekingen, naMislukt.Boekingen);
        Schrijf("   na de mislukte update: de oude versie, alle boekingen, integriteit goed");

        // De oorzaak weg: de update lukt alsnog.
        Sql("DROP TABLE \"Apparaten\"");
        await using (var diensten = Bouw())
        {
            Assert.Empty(await LokaleApp.Start(diensten, "Stress Test"));
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
        }

        SqliteConnection.ClearAllPools();

        // 4. Onderbroken situaties.
        Schrijf("");
        Schrijf("--- 4. onderbroken");
        var kopiemap = Path.Combine(map, "reservekopieen");

        // Een half geschreven kopie (de pc viel uit tijdens het kopiëren): geen gegevens erbij, dus hij telt niet mee.
        File.WriteAllBytes(Path.Combine(kopiemap, "administratie-20260101-000000-handmatig.db"), RandomNumberGenerator.GetBytes(4096));

        // Een kopie die na het maken is afgekapt (een volle schijf, een afgebroken kopie naar de cloud): wel in de lijst, niet terug te zetten.
        await using (var diensten = Bouw())
        {
            await LokaleApp.Start(diensten, "Stress Test");
            var kopieen = diensten.GetRequiredService<Reservekopieen>();
            Assert.DoesNotContain(kopieen.Lijst(), k => k.Bestand == "administratie-20260101-000000-handmatig.db");
            var heel = kopieen.Maak(Reservekopiesoort.Handmatig);
            var pad = Path.Combine(kopiemap, heel.Bestand);
            using (var bestand = new FileStream(pad, FileMode.Open, FileAccess.Write))
            {
                bestand.SetLength(bestand.Length / 2);
            }

            Assert.NotNull(kopieen.Controleer(heel));
            Assert.NotNull(kopieen.PlanTerugzetten(heel));
            Assert.Null(kopieen.GeplandTerugzetten);
            kopieen.Verwijder(heel);
        }

        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(kopiemap, "administratie-20260101-000000-handmatig.db"));
        Schrijf("   een half geschreven kopie telt niet mee; een afgekapte kopie wordt herkend en niet teruggezet");

        // Halverwege een update blijven steken (de pc viel uit): de database staat op een tussenversie; de volgende start maakt hem af.
        await NaarVersie(OudeVersie);
        var alle = await Bekend();
        var tussen = alle[alle.IndexOf(OudeVersie) + Math.Max(1, (alle.Count - 1 - alle.IndexOf(OudeVersie)) / 2)];
        await NaarVersie(tussen);
        await using (var diensten = Bouw())
        {
            var (meldingen, afmaken) = await Tijd(() => LokaleApp.Start(diensten, "Stress Test"));
            Assert.Empty(meldingen);
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
            Schrijf($"   {"een halve update afmaken bij de volgende start",-52} {afmaken,7:0.0} s");
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(alle[^1], (await Toegepast())[^1]);
        Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);

        // 5. Herhaald bijwerken: het bewaarschema houdt de map binnen de perken.
        Schrijf("");
        Schrijf("--- 5. herhaald");
        for (var ronde = 0; ronde < 4; ronde++)
        {
            await NaarVersie(OudeVersie);
            await using var diensten = Bouw();
            Assert.Empty(await LokaleApp.Start(diensten, "Stress Test"));
            SqliteConnection.ClearAllPools();
        }

        await using (var diensten = Bouw())
        {
            var lijst = diensten.GetRequiredService<Reservekopieen>().Lijst();
            Assert.True(lijst.Count(k => k.Soort == Reservekopiesoort.VoorUpdate) <= 5);
            Assert.All(lijst, k => Assert.Null(diensten.GetRequiredService<Reservekopieen>().Controleer(k)));
            Assert.Null(await Databaseupdate.ControleerKetens(diensten));
            Schrijf($"   na {4 + 4} updates: {lijst.Count} kopieën bewaard ({string.Join(", ", lijst.GroupBy(k => k.Soort).Select(g => $"{g.Count()} {g.Key}"))}), samen {Mb(lijst.Sum(k => k.Grootte)):N0} MB bij een database van {Mb(new FileInfo(Database).Length):N0} MB");
        }

        SqliteConnection.ClearAllPools();
        Assert.Equal(boekingen, Reservekopieen.Onderzoek(Database).Boekingen);
        Schrijf("");
        Schrijf($"einde: {boekingen:N0} boekingen, alle hash-ketens heel");
    }

    /// <summary>Vijf administraties met boekingen, en e-facturen met een flinke bijlage tot de database groot genoeg is.</summary>
    private async Task<List<Guid>> Vul(ServiceProvider diensten, Uitvoerder wie, int megabytes)
    {
        var grootboek = diensten.GetRequiredService<GrootboekDienst>();
        var inkoop = diensten.GetRequiredService<InkoopDienst>();
        var codes = (await diensten.GetRequiredService<BtwDienst>().Tabel()).Op(new DateOnly(2026, 6, 1));
        var ids = new List<Guid>();
        for (var a = 0; a < Administraties; a++)
        {
            var nieuw = await diensten.GetRequiredService<AdministratieDienst>().Maak(new AdministratieInvoer($"Kopie {a + 1} B.V.", Ondernemingsvorm.BV, null, null), wie);
            Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
            ids.Add(nieuw.Waarde);
            for (var i = 0; i < 100; i++)
            {
                var geboekt = await grootboek.Boek(wie, nieuw.Waarde, new("MEM", $"2026-{i + 1:0000}", new DateOnly(2026, 1, 1).AddDays(i * 3), $"Boeking {i + 1}", [new("4300", 10m + i), new("1100", -(10m + i))]));
                Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
            }
        }

        // Documenten van ongeveer 250 KB (een gewone gescande factuur), verdeeld over de administraties.
        var ontvanger = new Partij("Kopie B.V.", new Adres("Proefstraat 1", "1234 AB", "Proefstad"));
        for (var i = 0; new FileInfo(Database).Length + WalGrootte() < megabytes * 1024L * 1024L; i++)
        {
            var l = i % 8;
            var leverancier = new Partij($"Leverancier {l + 1} B.V.", new Adres($"Handelsweg {l + 1}", "9999 ZZ", "Verzonnen"), $"NL{800000000 + l:000000000}B01");
            var factuur = new Verkoopfactuur("KOPIE", ontvanger, $"F-{i + 1:00000}", new DateOnly(2026, 1, 1).AddDays(i % 330), new DateOnly(2026, 2, 1).AddDays(i % 330), $"Levering {i + 1}", [new("Artikel", 1, 25m + i % 300, "4300", "V21")]);
            var xml = UblFactuurSchrijver.Schrijf(factuur, leverancier, codes);
            var sluit = xml.LastIndexOf("</", StringComparison.Ordinal);
            var metBijlage = string.Concat(xml.AsSpan(0, sluit), $"<!-- {Convert.ToBase64String(RandomNumberGenerator.GetBytes(187_000))} -->", xml.AsSpan(sluit));
            using var stroom = new MemoryStream(Encoding.UTF8.GetBytes(metBijlage));
            var gelezen = await inkoop.Lees(wie, ids[i % Administraties], stroom, $"F-{i + 1:00000}.xml", "application/xml");
            Assert.NotNull(gelezen.DocumentId);
        }

        return ids;
    }

    private long WalGrootte() => File.Exists(Database + "-wal") ? new FileInfo(Database + "-wal").Length : 0;

    /// <summary>
    /// Vijf gebruikers boeken door (elk in een eigen administratie: een memoriaalpost en af en toe een inkoopfactuur),
    /// tot de tijd om is of <paramref name="ondertussen"/> klaar is.
    /// </summary>
    private static async Task<Werkmeting> Werk(ServiceProvider diensten, Uitvoerder wie, List<Guid> administraties, TimeSpan duur, Func<Action, Task>? ondertussen)
    {
        var meting = new Werkmeting();
        using var stop = new CancellationTokenSource();
        var grootboek = diensten.GetRequiredService<GrootboekDienst>();
        var reeks = Guid.NewGuid().ToString("N")[..6];
        var werkers = administraties.Select((id, g) => Task.Run(async () =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                var klok = Stopwatch.StartNew();
                try
                {
                    var geboekt = await grootboek.Boek(wie, id, new("MEM", $"S{reeks}-{g}-{i:00000}", new DateOnly(2026, 12, 1 + i % 28), $"Tijdens de kopie {i}", [new("4300", 1m + i % 50), new("1100", -(1m + i % 50))]));
                    if (!geboekt.IsGelukt)
                    {
                        meting.Fouten.Add(string.Join("; ", geboekt.Fouten));
                    }
                }
                catch (Exception fout) when (fout is not OutOfMemoryException)
                {
                    var kern = fout.GetBaseException();
                    meting.Fouten.Add($"{kern.GetType().Name}: {kern.Message}");
                }

                meting.Tijden.Add(klok.Elapsed.TotalMilliseconds);
                await Task.Delay(20);
            }
        })).ToList();

        if (ondertussen is null)
        {
            await Task.Delay(duur);
            await stop.CancelAsync();
        }
        else
        {
            await ondertussen(stop.Cancel);
        }

        await Task.WhenAll(werkers);
        return meting;
    }

    private sealed class Werkmeting
    {
        public ConcurrentBag<double> Tijden { get; } = [];

        public ConcurrentBag<string> Fouten { get; } = [];

        private List<double> Gesorteerd => [.. Tijden.Order()];

        public double Mediaan => Tijden.IsEmpty ? 0 : Gesorteerd[Tijden.Count / 2];

        public double P95 => Tijden.IsEmpty ? 0 : Gesorteerd[Math.Min(Tijden.Count - 1, (int)(Tijden.Count * 0.95))];

        public double Max => Tijden.IsEmpty ? 0 : Gesorteerd[^1];
    }

    private async Task NaarVersie(string versie)
    {
        await using var diensten = Bouw();
        await using var scope = diensten.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        await db.GetService<IMigrator>().MigrateAsync(versie);
        SqliteConnection.ClearAllPools();
    }

    private async Task<List<string>> Toegepast()
    {
        await using var diensten = Bouw();
        await using var scope = diensten.CreateAsyncScope();
        var lijst = (await scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().Database.GetAppliedMigrationsAsync()).ToList();
        SqliteConnection.ClearAllPools();
        return lijst;
    }

    private async Task<List<string>> Bekend()
    {
        await using var diensten = Bouw();
        await using var scope = diensten.CreateAsyncScope();
        return [.. scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().Database.GetMigrations()];
    }

    private void Sql(string sql)
    {
        using var verbinding = new SqliteConnection($"Data Source={Database};Pooling=False");
        verbinding.Open();
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = sql;
        opdracht.ExecuteNonQuery();
    }
}
