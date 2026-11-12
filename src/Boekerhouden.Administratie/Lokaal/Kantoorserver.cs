using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Wat een werkplek meestuurt als hij zich meldt bij de kantoorserver.</summary>
public sealed record Werkplekmelding(string? Sleutel, string? WindowsNaam, string? Computer);

/// <summary>
/// De kantoorserver: de Windows-app die zijn gegevens deelt met de andere pc's op kantoor. Dezelfde webapp, maar met
/// de gegevens in het SQLite-bestand van de Windows-app, versleuteld verkeer (een eigen certificaat dat de werkplekken
/// bij het koppelen vastleggen) en inloggen met de sleutel van een gekoppelde werkplek in plaats van een wachtwoord.
/// Aan met <c>Kantoorserver:Gegevensmap</c> (de map van de Windows-app; dan ook de database en de poort), of alleen
/// <c>Kantoorserver:Aan</c> (de inlogwijze, voor tests).
/// </summary>
public static class Kantoorserver
{
    public const int StandaardPoort = 5095;

    /// <summary>De pagina voor wie zonder gekoppelde werkplek komt (bijv. met een gewone browser).</summary>
    public const string GeenWerkplek = "/werkplek-nodig";

    public const string Certificaatbestand = "kantoorserver.pfx";

    /// <summary>De vingerafdruk (SHA-256, hex) van het certificaat waarmee deze server luistert; leeg zonder eigen certificaat.</summary>
    public static string? Certificaatvingerafdruk { get; private set; }

    private static readonly string[] Wachtwoordpaginas = ["/inloggen", "/eerste-gebruiker", "/wachtwoord-wijzigen", "/account/beveiliging", "/account/tweede-stap", "/account/passkey"];

    public static bool Aan(IConfiguration configuratie)
    {
        ArgumentNullException.ThrowIfNull(configuratie);
        return configuratie.GetValue("Kantoorserver:Aan", false) || !string.IsNullOrWhiteSpace(configuratie["Kantoorserver:Gegevensmap"]);
    }

    /// <summary>
    /// Start de kantoorserver in dit proces (de Windows-app doet dat zelf, er is geen los serverprogramma): dezelfde
    /// webapp op de gegevens in <paramref name="map"/>. Vooraf hoort <see cref="Kantoorstart.Bereid"/> gedraaid te zijn.
    /// </summary>
    /// <param name="extra">Diensten van de Windows-app zelf, bijv. opnieuw starten.</param>
    public static async Task<WebApplication> Start(string map, int poort, Action<IServiceCollection>? extra = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [.. Kantoorstart.Argumenten(map, poort)], ContentRootPath = AppContext.BaseDirectory });
        Opstart.ConfigureerDiensten(builder);
        extra?.Invoke(builder.Services);
        var app = builder.Build();
        await Opstart.InitialiseerDatabase(app.Services).ConfigureAwait(false);
        Opstart.ConfigureerPijplijn(app);
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    /// <summary>Zet de database, de poort en het certificaat als de app als kantoorserver start; geeft of dat zo is.</summary>
    public static bool Configureer(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Configuration["Kantoorserver:Gegevensmap"] is { Length: > 0 } map)
        {
            Directory.CreateDirectory(map);
            builder.Configuration[$"ConnectionStrings:{Opstart.Verbindingsnaam}"] = $"Data Source={Path.Combine(map, LokaleApp.Bestandsnaam)}";
            var poort = builder.Configuration.GetValue("Kantoorserver:Poort", StandaardPoort);
            var certificaat = Certificaat(map);
            Certificaatvingerafdruk = certificaat.GetCertHashString(HashAlgorithmName.SHA256);
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Any, poort, l => l.UseHttps(certificaat)));

            // Reservekopieën en de werkwijze van deze pc, zoals in de Windows-app alleen.
            builder.Services.AddSingleton(sp => new Reservekopieen(map, sp.GetRequiredService<TimeProvider>()));
            builder.Services.AddSingleton(sp => new Werkwijzebeheer(map, sp.GetService<IHerstart>()));
        }

        return Aan(builder.Configuration);
    }

    /// <summary>
    /// Het certificaat van de kantoorserver: één keer gemaakt en in de gegevensmap bewaard (naast de gegevens zelf, dus
    /// even goed afgeschermd). Het is door niemand ondertekend; de werkplekken leggen de vingerafdruk vast bij de eerste
    /// verbinding en de controlecode bij het koppelen hangt ervan af, zodat een tussenpersoon opvalt.
    /// </summary>
    public static X509Certificate2 Certificaat(string map)
    {
        var pad = Path.Combine(map, Certificaatbestand);
        if (File.Exists(pad))
        {
            try
            {
                var bestaand = X509CertificateLoader.LoadPkcs12FromFile(pad, null, X509KeyStorageFlags.Exportable);
                if (bestaand.NotAfter > DateTime.Now.AddDays(30))
                {
                    return bestaand;
                }
            }
            catch (CryptographicException)
            {
                // Onleesbaar: een nieuw certificaat; de werkplekken moeten dan opnieuw verbinden.
            }
        }

        using var sleutel = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var aanvraag = new CertificateRequest("CN=Sifer kantoorserver", sleutel, HashAlgorithmName.SHA256);
        var namen = new SubjectAlternativeNameBuilder();
        namen.AddDnsName("localhost");
        namen.AddDnsName(Environment.MachineName);
        namen.AddIpAddress(IPAddress.Loopback);
        aanvraag.CertificateExtensions.Add(namen.Build());
        aanvraag.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        aanvraag.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var nieuw = aanvraag.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(20));
        var pfx = nieuw.Export(X509ContentType.Pkcs12);
        File.WriteAllBytes(pad, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>De pagina's rond wachtwoorden bestaan op de kantoorserver niet: wie ze opent, gaat naar de startpagina.</summary>
    public static Task ZonderWachtwoordpaginas(HttpContext context, RequestDelegate volgende)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(volgende);
        if (Wachtwoordpaginas.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(context.User.Identity?.IsAuthenticated == true ? "/" : GeenWerkplek);
            return Task.CompletedTask;
        }

        return volgende(context);
    }

    /// <summary>
    /// Melden en inloggen voor werkplekken. Beide zonder inlog bereikbaar (het is de weg naar binnen), begrensd per
    /// adres; de sleutel en de handtekening bepalen wie er binnenkomt.
    /// </summary>
    public static void MapWerkplekken(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var rem = new Rem(app.Services.GetRequiredService<TimeProvider>());

        // De werkplek meldt zich met zijn publieke sleutel: wacht op de beheerder, of krijgt een uitdaging.
        app.MapPost("/werkplek/meld", async (Werkplekmelding melding, HttpContext context, ApparaatDienst apparaten) =>
        {
            if (!rem.Mag(context.Connection.RemoteIpAddress))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            byte[] sleutel;
            try
            {
                sleutel = Convert.FromBase64String(melding.Sleutel ?? "");
            }
            catch (FormatException)
            {
                return Results.BadRequest();
            }

            var antwoord = await apparaten.Meld(sleutel, melding.WindowsNaam, melding.Computer);
            return Results.Json(new { stand = antwoord.Stand.ToString(), code = antwoord.Code, uitdaging = antwoord.Uitdaging });
        }).AllowAnonymous().DisableAntiforgery();

        // Het venster van de werkplek opent dit adres met de ondertekende uitdaging: dan is de gebruiker ingelogd.
        app.MapGet("/werkplek/binnen", async (string? uitdaging, string? handtekening, HttpContext context, ApparaatDienst apparaten, SignInManager<Gebruiker> signIn) =>
        {
            if (!rem.Mag(context.Connection.RemoteIpAddress))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            byte[]? bytes = null;
            try
            {
                bytes = Convert.FromBase64String((handtekening ?? "").Replace('-', '+').Replace('_', '/'));
            }
            catch (FormatException)
            {
                // Geen geldige handtekening: geweigerd, hieronder.
            }

            if (await apparaten.Verifieer(uitdaging, bytes) is not { } gebruiker)
            {
                return Results.LocalRedirect(GeenWerkplek);
            }

            await signIn.SignInAsync(gebruiker, isPersistent: false, authenticationMethod: "werkplek");
            return Results.LocalRedirect("/");
        }).AllowAnonymous();
    }

    /// <summary>Hoogstens 60 verzoeken per minuut per adres op de werkplekadressen.</summary>
    private sealed class Rem(TimeProvider tijd)
    {
        private readonly ConcurrentDictionary<string, (long Minuut, int Aantal)> tellers = new();

        public bool Mag(IPAddress? adres)
        {
            var minuut = tijd.GetUtcNow().ToUnixTimeSeconds() / 60;
            if (tellers.Count > 10_000)
            {
                tellers.Clear();
            }

            var stand = tellers.AddOrUpdate(adres?.ToString() ?? "", _ => (minuut, 1), (_, oud) => oud.Minuut == minuut ? (minuut, oud.Aantal + 1) : (minuut, 1));
            return stand.Aantal <= 60;
        }
    }
}
