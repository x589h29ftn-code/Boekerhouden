using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Npgsql;

namespace Boekerhouden.Administratie.Bureaublad;

/// <summary>
/// Een eigen PostgreSQL voor de Windows-app op één pc: de programma's staan naast de app (<c>pgsql\bin</c>), de
/// gegevens in de map van de gebruiker. Bij de eerste start maakt <c>initdb</c> de database, met een willekeurig
/// wachtwoord dat alleen in die map staat. De database luistert alleen op 127.0.0.1, op een vrije poort.
/// </summary>
public sealed class LokaleDatabase(string programmas, string gegevensmap)
{
    public const string Gebruiker = "boekerhouden";

    private static readonly TimeSpan Limiet = TimeSpan.FromSeconds(90);

    /// <summary>Windows: een DLL ontbreekt (meestal de Visual C++-runtime van Microsoft).</summary>
    private const int DllOntbreekt = unchecked((int)0xC0000135);

    public string Map => Path.Combine(gegevensmap, "database");

    private string Wachtwoordbestand => Path.Combine(gegevensmap, "database.wachtwoord");

    private string Logbestand => Path.Combine(gegevensmap, "database.log");

    /// <summary>Is de database al aangemaakt?</summary>
    public bool Bestaat => File.Exists(Path.Combine(Map, "PG_VERSION"));

    /// <summary>Maakt de database zo nodig aan, start hem (of gebruikt hem als hij al draait) en geeft de verbinding.</summary>
    public async Task<string> Start(string database)
    {
        Directory.CreateDirectory(gegevensmap);
        if (!Bestaat)
        {
            await Maak();
        }

        var poort = await Draait() ?? await StartServer();
        var verbinding = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = poort,
            Username = Gebruiker,
            Password = (await File.ReadAllTextAsync(Wachtwoordbestand)).Trim(),
            Database = "postgres",
        };

        // De database van de app zelf aanmaken (de migraties doen de rest).
        await using (var beheer = new NpgsqlConnection(verbinding.ConnectionString))
        {
            await beheer.OpenAsync();
            await using var bestaat = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @naam", beheer);
            bestaat.Parameters.AddWithValue("naam", database);
            if (await bestaat.ExecuteScalarAsync() is null)
            {
                await using var maak = new NpgsqlCommand($"CREATE DATABASE \"{database.Replace("\"", string.Empty, StringComparison.Ordinal)}\"", beheer);
                await maak.ExecuteNonQueryAsync();
            }
        }

        verbinding.Database = database;
        return verbinding.ConnectionString;
    }

    public async Task Stop()
    {
        if (Bestaat && await Draait() is not null)
        {
            await Opdracht.Voer(Programma("pg_ctl"), ["stop", "-D", Map, "-m", "fast", "-w"], Limiet);
        }
    }

    private async Task Maak()
    {
        var wachtwoord = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
        var tijdelijk = Path.Combine(gegevensmap, "initdb.wachtwoord");
        await File.WriteAllTextAsync(tijdelijk, wachtwoord);
        try
        {
            var uitslag = await Opdracht.Voer(Programma("initdb"),
                ["-D", Map, "-U", Gebruiker, $"--pwfile={tijdelijk}", "-A", "scram-sha-256", "-E", "UTF8", "--no-locale"], Limiet);
            if (uitslag.Code == DllOntbreekt)
            {
                throw new BureaubladException(
                    "PostgreSQL mist de Visual C++-runtime van Microsoft. Installeer die (winget install Microsoft.VCRedist.2015+.x64) en start opnieuw.");
            }

            if (uitslag.Code != 0)
            {
                throw new BureaubladException($"De database kon niet worden aangemaakt:{Environment.NewLine}{uitslag.Uitvoer}");
            }

            // Alleen via TCP op deze pc; geen socketbestand (op Linux zou dat een systeemmap vragen).
            await File.AppendAllTextAsync(Path.Combine(Map, "postgresql.conf"),
                $"{Environment.NewLine}# Boekerhouden: alleen bereikbaar op deze pc{Environment.NewLine}listen_addresses = '127.0.0.1'{Environment.NewLine}unix_socket_directories = ''{Environment.NewLine}");
            await File.WriteAllTextAsync(Wachtwoordbestand, wachtwoord);
        }
        finally
        {
            File.Delete(tijdelijk);
        }
    }

    /// <summary>De poort als de database al draait (bijv. na het hard afsluiten van de app), anders leeg.</summary>
    private async Task<int?> Draait()
    {
        var status = await Opdracht.Voer(Programma("pg_ctl"), ["status", "-D", Map], Limiet);
        var pid = Path.Combine(Map, "postmaster.pid");
        return status.Code == 0 && File.Exists(pid)
               && (await File.ReadAllLinesAsync(pid)) is { Length: > 3 } regels
               && int.TryParse(regels[3], System.Globalization.CultureInfo.InvariantCulture, out var poort)
            ? poort
            : null;
    }

    private async Task<int> StartServer()
    {
        var poort = VrijePoort();
        var uitslag = await Opdracht.Voer(Programma("pg_ctl"),
            ["start", "-D", Map, "-w", "-t", "60", "-l", Logbestand, "-o", $"-p {poort}"], Limiet, uitvoerOpvangen: false);
        if (uitslag.Code != 0)
        {
            var log = File.Exists(Logbestand) ? string.Join(Environment.NewLine, (await File.ReadAllLinesAsync(Logbestand)).TakeLast(15)) : string.Empty;
            throw new BureaubladException($"De database kon niet starten (zie {Logbestand}):{Environment.NewLine}{log}");
        }

        return poort;
    }

    private string Programma(string naam)
    {
        var pad = Path.Combine(programmas, OperatingSystem.IsWindows() ? naam + ".exe" : naam);
        return File.Exists(pad)
            ? pad
            : throw new BureaubladException($"PostgreSQL ontbreekt: {pad} bestaat niet. Pak de hele zip uit, inclusief de map pgsql.");
    }

    internal static int VrijePoort(int voorkeur = 0)
    {
        try
        {
            using var luisteraar = new TcpListener(IPAddress.Loopback, voorkeur);
            luisteraar.Start();
            return ((IPEndPoint)luisteraar.LocalEndpoint).Port;
        }
        catch (SocketException) when (voorkeur != 0)
        {
            return VrijePoort();
        }
    }
}
