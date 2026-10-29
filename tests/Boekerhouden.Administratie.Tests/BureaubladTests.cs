using Boekerhouden.Administratie.Bureaublad;
using Npgsql;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De Windows-app: een eigen database en een eigen venster (de programma's zelf zijn op Linux nagemaakt of de Linux-versie).</summary>
public sealed class BureaubladTests : IDisposable
{
    private readonly string map = Path.Combine(Path.GetTempPath(), "bureaublad-test-" + Guid.NewGuid().ToString("N"));

    public BureaubladTests() => Directory.CreateDirectory(map);

    public void Dispose() => Directory.Delete(map, recursive: true);

    /// <summary>De PostgreSQL-programma's van deze machine; PostgreSQL draait niet als root, dan slaan we over.</summary>
    private static string? Programmas =>
        OperatingSystem.IsLinux() && Environment.UserName != "root" && Directory.Exists("/usr/lib/postgresql")
            ? Directory.GetDirectories("/usr/lib/postgresql").Select(d => Path.Combine(d, "bin")).FirstOrDefault(d => File.Exists(Path.Combine(d, "initdb")))
            : null;

    [Fact]
    public async Task Eigen_database_aanmaken_starten_hergebruiken_en_stoppen()
    {
        if (Programmas is not { } programmas)
        {
            return;
        }

        var database = new LokaleDatabase(programmas, map);
        var verbinding = await database.Start("boekerhouden_administratie");
        try
        {
            Assert.True(database.Bestaat);
            var gegevens = new NpgsqlConnectionStringBuilder(verbinding);
            Assert.Equal(("127.0.0.1", "boekerhouden", "boekerhouden_administratie"), (gegevens.Host, gegevens.Username, gegevens.Database));
            await using (var db = new NpgsqlConnection(verbinding))
            {
                await db.OpenAsync();
                await using var vraag = new NpgsqlCommand("SHOW listen_addresses", db);
                Assert.Equal("127.0.0.1", await vraag.ExecuteScalarAsync());
            }

            // Draait al (bijv. na hard afsluiten): dezelfde database op dezelfde poort.
            Assert.Equal(gegevens.Port, new NpgsqlConnectionStringBuilder(await new LokaleDatabase(programmas, map).Start("boekerhouden_administratie")).Port);

            await using var verkeerd = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(verbinding) { Password = "fout" }.ConnectionString);
            await Assert.ThrowsAsync<PostgresException>(() => verkeerd.OpenAsync());
        }
        finally
        {
            await database.Stop();
        }

        await using var gestopt = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(verbinding) { Pooling = false }.ConnectionString);
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => gestopt.OpenAsync());
    }

    [Fact]
    public async Task Zonder_postgresql_een_duidelijke_melding()
    {
        var fout = await Assert.ThrowsAsync<BureaubladException>(() => new LokaleDatabase(Path.Combine(map, "pgsql", "bin"), map).Start("x"));

        Assert.StartsWith("PostgreSQL ontbreekt", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task App_venster_met_een_eigen_profiel_en_sluiten_stopt_de_app()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // De nagemaakte browser is een shellscript.
        }

        var argumenten = Path.Combine(map, "argumenten.txt");
        var browser = Path.Combine(map, "browser.sh");
        await File.WriteAllTextAsync(browser, $"#!/bin/sh\necho \"$@\" > '{argumenten}'\n");
        File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var gesloten = new TaskCompletionSource();

        Assert.True(AppVenster.Open("http://127.0.0.1:5090", Path.Combine(map, "venster"), browser, () => gesloten.SetResult()));
        await gesloten.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal($"--app=http://127.0.0.1:5090 --user-data-dir={Path.Combine(map, "venster")} --no-first-run --no-default-browser-check --window-size=1440,960",
            (await File.ReadAllTextAsync(argumenten)).Trim());
    }

    [Fact]
    public void Aan_met_de_schakelaar() =>
        Assert.True(Bureaublad.Bureaublad.IsAan(["--bureaublad"]));
}
