using System.Text.Json;
using System.Text.RegularExpressions;
using Boekerhouden.Data;
using Boekerhouden.Import;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Identiteit;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Demo;

/// <summary>Instellingen voor <see cref="DemoVulling"/> (sectie <c>Demo</c>; <c>--demo</c> op de opdrachtregel zet <c>Aan</c>).</summary>
public sealed class DemoOpties
{
    /// <summary>Of de demo bij het starten wordt gevuld.</summary>
    public bool Aan { get; set; }

    /// <summary>Het RGS-werkboek; standaard <c>BOEKERHOUDEN_RGS_WERKBOEK</c> of het eerste <c>RGS*.xlsx</c> in <c>testdata/</c>.</summary>
    public string? RgsWerkboek { get; set; }

    /// <summary>Mappen met een submap per klant; standaard <c>tests/Fictief</c> en <c>testdata/demo</c>.</summary>
    public IList<string> Mappen { get; } = [];
}

/// <summary>Een klant in een demomap: een submap met <c>saldibalans-JJJJ.csv</c> (of .xlsx) en eventueel <c>klant.json</c>.</summary>
/// <param name="Grondslag">Alleen bij een BV; standaard fiscaal.</param>
public sealed record DemoKlant(string Map, string Naam, Rechtsvorm Rechtsvorm, string? Plaats, string? Kvk, Verslaggeving Grondslag, IReadOnlyDictionary<int, string> Saldibalansen)
{
    /// <summary>Adres, contact en registratie uit klant.json (klantenkaart).</summary>
    public KlantGegevens? Gegevens { get; init; }
}

/// <summary>
/// Vult een lokale installatie om te proberen: een account, het RGS-werkboek, en per klant in de demomappen een
/// dossier voor het laatste jaar met alle saldibalansen ingelezen en de rubricering volgens de voorstellen. Wat er al
/// is (account, RGS, klant met dezelfde naam) blijft staan, dus opnieuw starten met <c>--demo</c> kan geen kwaad.
/// Echte klantgegevens horen in <c>testdata/</c>, dat buiten git blijft.
/// </summary>
public static partial class DemoVulling
{
    public const string Email = "demo@kantoor.test";
    public const string Wachtwoord = "Blauwe-Fiets-Kade-73";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    /// <summary>Zet <c>--demo</c> om naar de instelling <c>Demo:Aan=true</c>.</summary>
    public static string[] Argumenten(string[] args) =>
        [.. args.Select(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ? "--Demo:Aan=true" : a)];

    /// <summary>De map van de solution (met <c>Boekerhouden.slnx</c>), gezocht vanaf <paramref name="start"/> naar boven.</summary>
    public static string? Hoofdmap(string start)
    {
        for (var map = new DirectoryInfo(start); map is not null; map = map.Parent)
        {
            if (File.Exists(Path.Combine(map.FullName, "Boekerhouden.slnx")))
            {
                return map.FullName;
            }
        }

        return null;
    }

    /// <summary>De klanten in de mappen, in alfabetische volgorde van de submappen.</summary>
    public static IReadOnlyList<DemoKlant> Klanten(IEnumerable<string> mappen)
    {
        ArgumentNullException.ThrowIfNull(mappen);
        var klanten = new List<DemoKlant>();
        foreach (var submap in mappen.Where(Directory.Exists).SelectMany(Directory.GetDirectories).Order(StringComparer.Ordinal))
        {
            var saldibalansen = Directory.GetFiles(submap)
                .Select(f => (Pad: f, Match: SaldibalansPatroon().Match(Path.GetFileName(f))))
                .Where(f => f.Match.Success)
                .GroupBy(f => int.Parse(f.Match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => g.First().Pad);
            if (saldibalansen.Count == 0)
            {
                continue;
            }

            var mapnaam = Path.GetFileName(submap);
            var info = File.Exists(Path.Combine(submap, "klant.json"))
                ? JsonSerializer.Deserialize<KlantJson>(File.ReadAllText(Path.Combine(submap, "klant.json")), Json)
                : null;
            var rechtsvorm = info?.Rechtsvorm is { } r ? Enum.Parse<Rechtsvorm>(r, ignoreCase: true)
                : mapnaam.StartsWith("ez-", StringComparison.OrdinalIgnoreCase) ? Rechtsvorm.Eenmanszaak : Rechtsvorm.BV;
            var grondslag = info?.Grondslag is { } s ? Enum.Parse<Verslaggeving>(s, ignoreCase: true) : Verslaggeving.Fiscaal;
            var naam = info?.Naam ?? KlantUitBestand(saldibalansen[saldibalansen.Keys.Max()]) ?? mapnaam;
            klanten.Add(new DemoKlant(submap, naam, rechtsvorm, info?.Plaats, info?.Kvk, grondslag, saldibalansen)
            {
                Gegevens = info is null ? null : new KlantGegevens(info.Straat, info.Huisnummer, info.Toevoeging, info.Postcode, info.Telefoon, info.Email, info.Website,
                    info.Rsin, info.Sbi, info.StatutaireNaam, info.StatutaireZetel, info.Oprichtingsdatum),
            });
        }

        return klanten;
    }

    /// <summary>Vult de demo als dat is ingesteld; meldt in het log wat er is gebeurd en met welk account je inlogt.</summary>
    public static async Task Vul(IServiceProvider services, DemoOpties opties, string inhoudsmap, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(opties);
        ArgumentNullException.ThrowIfNull(logger);
        if (!opties.Aan)
        {
            return;
        }

        var hoofdmap = Hoofdmap(inhoudsmap);
        var mappen = opties.Mappen.Count > 0 ? [.. opties.Mappen]
            : hoofdmap is null ? new List<string>()
            : [Path.Combine(hoofdmap, "tests", "Fictief"), Path.Combine(hoofdmap, "testdata", "demo")];
        var werkboek = opties.RgsWerkboek ?? Environment.GetEnvironmentVariable("BOEKERHOUDEN_RGS_WERKBOEK")
            ?? (hoofdmap is not null && Directory.Exists(Path.Combine(hoofdmap, "testdata"))
                ? Directory.GetFiles(Path.Combine(hoofdmap, "testdata"), "RGS*.xlsx").Order(StringComparer.Ordinal).FirstOrDefault()
                : null);

        var wie = await Account(services, logger);
        await Rgs(services, werkboek, wie, logger);
        await Kantoor(services, wie);
        foreach (var klant in Klanten(mappen))
        {
            await VoegToe(services, klant, wie, logger);
        }
    }

    private static async Task<Uitvoerder> Account(IServiceProvider services, ILogger logger)
    {
        var gebruikers = services.GetRequiredService<GebruikerDienst>();
        if (await gebruikers.EersteBeheerderNodig())
        {
            var uitkomst = await gebruikers.MaakEersteBeheerder(new GebruikerInvoer(Email, "Demo Gebruiker", "DG", null, Rollen.Alle), Wachtwoord);
            if (!uitkomst.IsGelukt)
            {
                throw new InvalidOperationException("Demo-account aanmaken mislukt: " + string.Join("; ", uitkomst.Fouten));
            }
        }

        var demo = (await gebruikers.Lijst()).FirstOrDefault(g => g.Email == Email);
        if (demo is not null)
        {
            Inloggen(logger, Email, Wachtwoord);
            return new Uitvoerder(demo.Id, demo.Naam);
        }

        EigenAccount(logger);
        return new Uitvoerder(null, "Demo");
    }

    private static async Task Rgs(IServiceProvider services, string? werkboek, Uitvoerder wie, ILogger logger)
    {
        var rgs = services.GetRequiredService<RgsDienst>();
        if (await rgs.ActiefSchema() is not null)
        {
            return;
        }

        if (werkboek is null || !File.Exists(werkboek))
        {
            GeenRgs(logger);
            return;
        }

        var uitkomst = await rgs.Importeer(await File.ReadAllBytesAsync(werkboek), Path.GetFileName(werkboek), wie);
        if (!uitkomst.IsGelukt)
        {
            Mislukt(logger, Path.GetFileName(werkboek), string.Join("; ", uitkomst.Fouten));
        }
    }

    private static async Task Kantoor(IServiceProvider services, Uitvoerder wie)
    {
        var kantoor = services.GetRequiredService<KantoorDienst>();
        if (string.IsNullOrWhiteSpace((await kantoor.Haal()).Naam))
        {
            await kantoor.Bewaar(new KantoorInvoer("Voorbeeld Accountants", null, null, "Zwolle", "#1f4e79"), wie);
        }
    }

    private static async Task VoegToe(IServiceProvider services, DemoKlant klant, Uitvoerder wie, ILogger logger)
    {
        var dbFactory = services.GetRequiredService<IDbContextFactory<BoekerhoudenDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (await db.Klanten.AnyAsync(k => k.Naam == klant.Naam))
            {
                return;
            }
        }

        var klantnummer = await VrijKlantnummer(dbFactory);
        var klantId = (await services.GetRequiredService<KlantDienst>().Maak(new KlantInvoer(klantnummer, klant.Naam, klant.Rechtsvorm, klant.Kvk, klant.Plaats) { Gegevens = klant.Gegevens }, wie)).Waarde;
        var import = services.GetRequiredService<ImportDienst>();
        foreach (var (jaar, pad) in klant.Saldibalansen.OrderBy(s => s.Key))
        {
            var uitkomst = await import.Importeer(klantId, jaar, await File.ReadAllBytesAsync(pad), Path.GetFileName(pad), wie);
            if (!uitkomst.IsGelukt)
            {
                Mislukt(logger, pad, string.Join("; ", uitkomst.Fouten));
            }
        }

        var boekjaar = klant.Saldibalansen.Keys.Max();
        var soort = klant.Rechtsvorm == Rechtsvorm.BV ? SoortOpdracht.SamenstellenJaarrekening : SoortOpdracht.WinstaangifteOpstellen;
        var dossier = await services.GetRequiredService<DossierDienst>().Maak(klantId, boekjaar, soort, wie, klant.Rechtsvorm == Rechtsvorm.BV ? klant.Grondslag : null);
        if (dossier.IsGelukt)
        {
            await services.GetRequiredService<RubriceerDienst>().AccepteerVoorstellen(dossier.Waarde, null, onthouden: false, wie);
        }

        KlantToegevoegd(logger, klant.Naam, klant.Saldibalansen.Count, boekjaar);
    }

    private static async Task<string> VrijKlantnummer(IDbContextFactory<BoekerhoudenDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var bezet = await db.Klanten.Select(k => k.Klantnummer).ToListAsync();
        return Enumerable.Range(1, 999).Select(n => $"D{n:000}").First(n => !bezet.Contains(n));
    }

    /// <summary>De klantnaam uit de kop van het bestand (regel <c>Klant;...</c>), als die er is.</summary>
    private static string? KlantUitBestand(string pad) =>
        ImportSjabloonLezer.Lees(pad).Saldibalans?.Klant is { Length: > 0 } naam ? naam.Trim() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: inloggen met {Email} en wachtwoord {Wachtwoord}.")]
    private static partial void Inloggen(ILogger logger, string email, string wachtwoord);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: er was al een account; log in met je eigen account.")]
    private static partial void EigenAccount(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: geen RGS-werkboek gevonden. Zet het in testdata/ (bijv. testdata/RGS 3.8-def.xlsx) of lees het in via Beheer › RGS; de rekeningen worden dan nog niet gerubriceerd.")]
    private static partial void GeenRgs(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: {Bestand} niet ingelezen: {Fouten}")]
    private static partial void Mislukt(ILogger logger, string bestand, string fouten);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo: klant {Naam} toegevoegd ({Aantal} saldibalans(en), dossier {Boekjaar}).")]
    private static partial void KlantToegevoegd(ILogger logger, string naam, int aantal, int boekjaar);

    [GeneratedRegex(@"^saldibalans-(\d{4})\.(csv|xlsx)$", RegexOptions.IgnoreCase)]
    private static partial Regex SaldibalansPatroon();

    private sealed record KlantJson(
        string? Naam,
        string? Rechtsvorm,
        string? Plaats,
        string? Kvk,
        string? Grondslag,
        string? Straat = null,
        string? Huisnummer = null,
        string? Toevoeging = null,
        string? Postcode = null,
        string? Telefoon = null,
        string? Email = null,
        string? Website = null,
        string? Rsin = null,
        string? Sbi = null,
        string? StatutaireNaam = null,
        string? StatutaireZetel = null,
        DateOnly? Oprichtingsdatum = null);
}
