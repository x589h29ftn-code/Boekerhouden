namespace Boekerhouden.Administratie.Bureaublad;

/// <summary>
/// De Windows-app voor één pc: dezelfde webapp, maar met een eigen database (<see cref="LokaleDatabase"/>), alleen
/// bereikbaar op deze pc (127.0.0.1), en in een eigen venster (<see cref="AppVenster"/>). Aan als er een
/// <c>bureaublad.json</c> naast de app staat (zo levert het publicatiescript hem op) of met <c>--bureaublad</c>.
/// Instellingen in de sectie <c>Bureaublad</c>: <c>Gegevensmap</c>, <c>PostgreSql</c> (map met de programma's),
/// <c>Database</c> (<c>Lokaal</c> of <c>Extern</c>: dan geldt de gewone verbinding), <c>Poort</c>, <c>Browser</c> en
/// <c>Venster</c> (<c>Nee</c>: geen venster openen, bijv. voor de proef in CI).
/// </summary>
public static class Bureaublad
{
    public const string Bestand = "bureaublad.json";
    private const string Databasenaam = "boekerhouden_administratie";

    public static bool IsAan(string[] args) =>
        args.Contains("--bureaublad", StringComparer.OrdinalIgnoreCase) || File.Exists(Path.Combine(AppContext.BaseDirectory, Bestand));

    /// <summary>Leest de instellingen, start de eigen database en kiest het adres. Geeft de database terug om te stoppen.</summary>
    public static async Task<LokaleDatabase?> Voorbereiden(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, Bestand), optional: true);
        var sectie = builder.Configuration.GetSection("Bureaublad");
        var gegevensmap = Gegevensmap(builder.Configuration);
        Directory.CreateDirectory(gegevensmap);
        var instellingen = new Dictionary<string, string?>();

        LokaleDatabase? database = null;
        if (!string.Equals(sectie["Database"], "Extern", StringComparison.OrdinalIgnoreCase))
        {
            database = new LokaleDatabase(sectie["PostgreSql"] ?? Path.Combine(AppContext.BaseDirectory, "pgsql", "bin"), gegevensmap);
            Console.WriteLine(database.Bestaat ? "Database starten…" : "Eerste start: de database wordt aangemaakt…");
            instellingen[$"ConnectionStrings:{Opstart.Verbindingsnaam}"] = await database.Start(Databasenaam);
        }

        var poort = LokaleDatabase.VrijePoort(int.TryParse(sectie["Poort"], out var p) ? p : 5090);
        instellingen["Bureaublad:Adres"] = $"http://127.0.0.1:{poort}";
        builder.Configuration.AddInMemoryCollection(instellingen);
        builder.WebHost.UseUrls($"http://127.0.0.1:{poort}");
        return database;
    }

    /// <summary>Opent het venster zodra de app luistert; sluit de gebruiker het, dan stopt de app.</summary>
    public static void OpenVenster(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var adres = app.Configuration["Bureaublad:Adres"]!;
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Console.WriteLine($"Boekerhouden Administratie draait op {adres}. Sluit het app-venster of dit venster om te stoppen.");
            if (string.Equals(app.Configuration["Bureaublad:Venster"], "Nee", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var venster = AppVenster.Open(adres, Path.Combine(Gegevensmap(app.Configuration), "venster"), app.Configuration["Bureaublad:Browser"],
                () => app.Lifetime.StopApplication());
            if (!venster)
            {
                Console.WriteLine("Geen Edge gevonden: de app is in de standaardbrowser geopend. Sluit dit venster om te stoppen.");
            }
        });
    }

    private static string Gegevensmap(IConfiguration configuratie) =>
        configuratie["Bureaublad:Gegevensmap"] is { Length: > 0 } map
            ? Environment.ExpandEnvironmentVariables(map)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boekerhouden", "Administratie");
}
