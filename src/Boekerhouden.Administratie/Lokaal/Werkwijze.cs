using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Boekerhouden.Administratie.Diensten;
using Microsoft.Data.Sqlite;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Hoe de Windows-app op deze pc werkt.</summary>
public enum Werkstand
{
    /// <summary>Alleen op deze pc: eigen gegevens, geen netwerk (zoals het altijd was).</summary>
    Alleen,

    /// <summary>Deze pc houdt de gegevens bij en deelt ze met de andere pc's op kantoor.</summary>
    Kantoorserver,

    /// <summary>Deze pc werkt in de gegevens van de kantoorserver.</summary>
    Werkplek,
}

/// <summary>
/// De werkwijze van de Windows-app op deze pc, bewaard als <c>werkwijze.json</c> in de gegevensmap.
/// </summary>
/// <param name="Poort">Kantoorserver: de poort waarop hij luistert.</param>
/// <param name="Server">Werkplek: het adres van de kantoorserver (https://naam:poort).</param>
/// <param name="Certificaat">Werkplek: de vingerafdruk van het certificaat van de server, vastgelegd bij de eerste verbinding.</param>
public sealed record Werkwijze(Werkstand Stand = Werkstand.Alleen, int Poort = Kantoorserver.StandaardPoort, string? Server = null, string? Certificaat = null)
{
    public const string Bestand = "werkwijze.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>De bewaarde werkwijze, of leeg als er nog niet gekozen is (of het bestand onleesbaar is).</summary>
    public static Werkwijze? Lees(string map)
    {
        var pad = Path.Combine(map, Bestand);
        if (!File.Exists(pad))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Werkwijze>(File.ReadAllText(pad), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Bewaar(string map)
    {
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, Bestand), JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Het adres van een kantoorserver uit wat de gebruiker intikt: een pc-naam of adres, met of zonder poort.</summary>
    public static Uri? Adres(string? invoer)
    {
        var tekst = (invoer ?? "").Trim().TrimEnd('/');
        if (tekst.Length == 0)
        {
            return null;
        }

        if (!tekst.Contains("://", StringComparison.Ordinal))
        {
            tekst = "https://" + tekst;
        }

        if (!Uri.TryCreate(tekst, UriKind.Absolute, out var adres) || adres.Scheme != Uri.UriSchemeHttps || adres.Host.Length == 0)
        {
            return null;
        }

        return new UriBuilder(adres) { Port = adres.IsDefaultPort ? Kantoorserver.StandaardPoort : adres.Port, Path = "/" }.Uri;
    }
}

/// <summary>
/// De verbinding van een werkplek met de kantoorserver: melden met de eigen sleutel en het adres om mee in te loggen.
/// Het certificaat van de server is door niemand ondertekend: bij de eerste verbinding wordt de vingerafdruk
/// vastgelegd (<see cref="GezienCertificaat"/>), daarna moet hij dezelfde zijn.
/// </summary>
public sealed class Werkplekverbinding : IDisposable
{
    private readonly HttpClient client;
    private readonly Werkpleksleutel sleutel;
    private readonly string? vastgelegd;

    public Werkplekverbinding(Uri server, Werkpleksleutel sleutel, string? certificaat)
    {
        ArgumentNullException.ThrowIfNull(server);
        this.sleutel = sleutel ?? throw new ArgumentNullException(nameof(sleutel));
        vastgelegd = string.IsNullOrWhiteSpace(certificaat) ? null : certificaat;
        Server = server;
#pragma warning disable CA2000 // De client ruimt de handler op.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                GezienCertificaat = cert?.GetCertHashString(HashAlgorithmName.SHA256);
                return GezienCertificaat is not null && (vastgelegd is null || string.Equals(vastgelegd, GezienCertificaat, StringComparison.OrdinalIgnoreCase));
            },
        };
#pragma warning restore CA2000
        client = new HttpClient(handler, disposeHandler: true) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(10) };
    }

    public Uri Server { get; }

    /// <summary>De vingerafdruk van het certificaat dat de server bij de laatste verbinding liet zien.</summary>
    public string? GezienCertificaat { get; private set; }

    /// <summary>De controlecode die de gebruiker aan de beheerder doorgeeft; pas bekend na de eerste <see cref="Meld"/>.</summary>
    public string Code => sleutel.Code(vastgelegd ?? GezienCertificaat);

    /// <summary>Meldt de werkplek bij de server.</summary>
    /// <exception cref="HttpRequestException">De server is niet te bereiken, of toont een ander certificaat dan vastgelegd.</exception>
    public async Task<Werkplekantwoord> Meld(string windowsNaam, string computer, CancellationToken annulering = default)
    {
        using var antwoord = await client.PostAsJsonAsync("/werkplek/meld", new Werkplekmelding(Convert.ToBase64String(sleutel.Publiek), windowsNaam, computer), annulering).ConfigureAwait(false);
        antwoord.EnsureSuccessStatusCode();
        var json = await antwoord.Content.ReadFromJsonAsync<JsonElement>(annulering).ConfigureAwait(false);
        var stand = Enum.TryParse<Werkplekstand>(json.GetProperty("stand").GetString(), out var s) ? s : Werkplekstand.Geweigerd;
        return new(stand, json.GetProperty("code").GetString() ?? "", json.TryGetProperty("uitdaging", out var u) ? u.GetString() : null);
    }

    /// <summary>Het adres dat het venster opent om in te loggen: de uitdaging met de handtekening van deze werkplek.</summary>
    public Uri Binnen(string uitdaging) =>
        new(Server, $"/werkplek/binnen?uitdaging={Uri.EscapeDataString(uitdaging)}&handtekening={Uri.EscapeDataString(Convert.ToBase64String(sleutel.Teken(uitdaging)))}");

    public void Dispose() => client.Dispose();
}

/// <summary>
/// De Windows-app als kantoorserver starten: eerst (in het eigen proces) de gewone start van de Windows-app (update met
/// reservekopie, de gebruiker van deze pc) en de eigen werkplek koppelen, daarna de database loslaten zodat de server
/// (een eigen proces met dezelfde webapp) hem kan openen.
/// </summary>
public static class Kantoorstart
{
    /// <returns>Wat de gebruiker moet weten (zoals <see cref="LokaleApp.Start"/>).</returns>
    public static async Task<IReadOnlyList<string>> Bereid(string map, string windowsNaam, string computer, Werkpleksleutel sleutel)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        var services = new ServiceCollection();
        LokaleApp.ConfigureerDiensten(services, map);
        IReadOnlyList<string> meldingen;
        await using (var diensten = services.BuildServiceProvider())
        {
            meldingen = await LokaleApp.Start(diensten, windowsNaam).ConfigureAwait(false);
            var wie = Identiteit.AdministratieClaims.AlsUitvoerder(diensten.GetRequiredService<LokaleSessie>().Gebruiker);
            await diensten.GetRequiredService<ApparaatDienst>().ZorgEigen(sleutel.Publiek, wie.Id!.Value, windowsNaam, computer).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
        return meldingen;
    }

    /// <summary>De opdrachtregel waarmee de server start (na het programma zelf).</summary>
    public static IReadOnlyList<string> Argumenten(string map, int poort) =>
        [$"--Kantoorserver:Gegevensmap={map}", $"--Kantoorserver:Poort={poort.ToString(System.Globalization.CultureInfo.InvariantCulture)}"];
}
