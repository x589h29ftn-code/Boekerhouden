using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Waarom een reservekopie is gemaakt; bepaalt ook hoe lang hij bewaard blijft.</summary>
public enum Reservekopiesoort
{
    /// <summary>Vlak voor een update van de database (de laatste 5 blijven).</summary>
    VoorUpdate,

    /// <summary>Eén per dag bij het starten (14 dagen, daarna één per maand, een jaar lang).</summary>
    Dagelijks,

    /// <summary>Door de gebruiker gemaakt (blijft tot de gebruiker hem weggooit).</summary>
    Handmatig,

    /// <summary>De gegevens zoals ze waren vlak voordat een reservekopie werd teruggezet (de laatste 3 blijven).</summary>
    VoorTerugzetten,
}

/// <summary>Een reservekopie: het bestand en wat er bij het maken over is vastgelegd.</summary>
/// <param name="Bestand">De bestandsnaam in de map met reservekopieën.</param>
/// <param name="Migratie">De laatste databaseversie (migratie) in de kopie.</param>
/// <param name="Sha256">Controlegetal van het bestand; bij terugzetten moet het kloppen.</param>
public sealed record Reservekopie(
    string Bestand, Reservekopiesoort Soort, DateTimeOffset Gemaakt, long Grootte, string? Migratie, long Boekingen, long Administraties, string Sha256,
    string? Opmerking = null);

/// <summary>
/// Reservekopieën van de Windows-app (één SQLite-bestand met alles, ook de documenten): maken met de backup van
/// SQLite zelf (ook terwijl de app het bestand open heeft), controleren (integriteit en controlegetal), bewaren volgens
/// een vast schema, een kopie in een tweede map (bijv. een netwerkschijf of een map die naar de cloud gaat), en
/// terugzetten bij de volgende start. Bij elke kopie hoort een <c>.json</c> met de gegevens van <see cref="Reservekopie"/>.
/// </summary>
public sealed class Reservekopieen(string gegevensmap, TimeProvider tijd)
{
    private const string Instellingenbestand = "reservekopieen.json";
    private const string Terugzetbestand = "terugzetten.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Gegevensmap { get; } = gegevensmap;

    public string Map => Path.Combine(Gegevensmap, "reservekopieen");

    public string Database => Path.Combine(Gegevensmap, LokaleApp.Bestandsnaam);

    /// <summary>Een tweede map waar elke reservekopie ook naartoe gaat; leeg = geen.</summary>
    public string? TweedeMap
    {
        get => LeesInstellingen().TweedeMap;
        set => File.WriteAllText(Path.Combine(Gegevensmap, Instellingenbestand), JsonSerializer.Serialize(new Instellingen(string.IsNullOrWhiteSpace(value) ? null : value.Trim()), Json));
    }

    private sealed record Instellingen(string? TweedeMap);

    private Instellingen LeesInstellingen()
    {
        var pad = Path.Combine(Gegevensmap, Instellingenbestand);
        try
        {
            return File.Exists(pad) ? JsonSerializer.Deserialize<Instellingen>(File.ReadAllText(pad)) ?? new(null) : new(null);
        }
        catch (JsonException)
        {
            return new(null);
        }
    }

    /// <summary>
    /// Maakt een reservekopie van de database met de backup van SQLite (een consistent beeld, ook als er een verbinding
    /// open is), controleert hem (integriteit, aantallen) en legt het controlegetal vast. Daarna ook naar de tweede map
    /// (lukt dat niet, dan staat het in de opmerking) en ruimt oude kopieën op.
    /// </summary>
    public Reservekopie Maak(Reservekopiesoort soort, string? opmerking = null)
    {
        if (!File.Exists(Database))
        {
            throw new InvalidOperationException("Er is nog geen database om een reservekopie van te maken.");
        }

        Directory.CreateDirectory(Map);
        var moment = tijd.GetLocalNow();
        var naam = $"administratie-{moment:yyyyMMdd-HHmmss}-{Soortnaam(soort)}.db";
        for (var i = 2; File.Exists(Path.Combine(Map, naam)); i++)
        {
            naam = $"administratie-{moment:yyyyMMdd-HHmmss}-{Soortnaam(soort)}-{i}.db";
        }

        var doel = Path.Combine(Map, naam);
        using (var bron = new SqliteConnection($"Data Source={Database};Mode=ReadOnly;Pooling=False"))
        using (var kopie = new SqliteConnection($"Data Source={doel};Pooling=False"))
        {
            bron.Open();
            kopie.Open();
            bron.BackupDatabase(kopie);

            // De database draait in WAL-modus en de kopie erft dat: dan kan een deel nog in een los -wal-bestand staan.
            // Terug naar een gewoon journaal schrijft alles in het ene bestand, zodat de kopie op zichzelf staat.
            using var opdracht = kopie.CreateCommand();
            opdracht.CommandText = "PRAGMA journal_mode = DELETE";
            opdracht.ExecuteNonQuery();
        }

        VerwijderBijbestanden(doel);
        var (integer, migratie, boekingen, administraties) = Onderzoek(doel);
        if (!integer)
        {
            File.Delete(doel);
            throw new InvalidOperationException("De reservekopie is niet in orde (integriteitscontrole); er is niets bewaard.");
        }

        var reservekopie = new Reservekopie(naam, soort, tijd.GetUtcNow(), new FileInfo(doel).Length, migratie, boekingen, administraties, Controlegetal(doel), opmerking);
        File.WriteAllText(Path.ChangeExtension(doel, ".json"), JsonSerializer.Serialize(reservekopie, Json));
        reservekopie = NaarTweedeMap(reservekopie);
        RuimOp();
        return reservekopie;
    }

    private Reservekopie NaarTweedeMap(Reservekopie kopie)
    {
        if (TweedeMap is not { } tweede)
        {
            return kopie;
        }

        try
        {
            Directory.CreateDirectory(tweede);
            File.Copy(Path.Combine(Map, kopie.Bestand), Path.Combine(tweede, kopie.Bestand), overwrite: true);
            File.Copy(Path.Combine(Map, Path.ChangeExtension(kopie.Bestand, ".json")), Path.Combine(tweede, Path.ChangeExtension(kopie.Bestand, ".json")), overwrite: true);
            return kopie;
        }
        catch (Exception fout) when (fout is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            var bijgewerkt = kopie with { Opmerking = $"{kopie.Opmerking} Niet naar de tweede map gekopieerd: {fout.Message}".Trim() };
            File.WriteAllText(Path.Combine(Map, Path.ChangeExtension(kopie.Bestand, ".json")), JsonSerializer.Serialize(bijgewerkt, Json));
            return bijgewerkt;
        }
    }

    /// <summary>Alle reservekopieën in de map, nieuwste eerst; een kopie zonder gegevens (.json) telt niet mee.</summary>
    public IReadOnlyList<Reservekopie> Lijst()
    {
        if (!Directory.Exists(Map))
        {
            return [];
        }

        var lijst = new List<Reservekopie>();
        foreach (var json in Directory.EnumerateFiles(Map, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Reservekopie>(File.ReadAllText(json)) is { } kopie && File.Exists(Path.Combine(Map, kopie.Bestand)))
                {
                    lijst.Add(kopie);
                }
            }
            catch (JsonException)
            {
                // Een beschadigd gegevensbestand: de kopie wordt niet aangeboden.
            }
        }

        return [.. lijst.OrderByDescending(k => k.Gemaakt)];
    }

    /// <summary>Klopt de kopie nog: hetzelfde controlegetal als bij het maken, en de integriteitscontrole van SQLite? Geeft de fout, of leeg.</summary>
    public string? Controleer(Reservekopie kopie)
    {
        ArgumentNullException.ThrowIfNull(kopie);
        var pad = Path.Combine(Map, Path.GetFileName(kopie.Bestand));
        if (!File.Exists(pad))
        {
            return $"Reservekopie {kopie.Bestand} bestaat niet meer.";
        }

        if (!string.Equals(Controlegetal(pad), kopie.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"Reservekopie {kopie.Bestand} is na het maken veranderd of beschadigd (het controlegetal klopt niet).";
        }

        return Onderzoek(pad).Integer ? null : $"Reservekopie {kopie.Bestand} is beschadigd (integriteitscontrole).";
    }

    /// <summary>Gooit een reservekopie weg (alleen handmatige; de rest ruimt het pakket zelf op).</summary>
    public void Verwijder(Reservekopie kopie)
    {
        ArgumentNullException.ThrowIfNull(kopie);
        var pad = Path.Combine(Map, Path.GetFileName(kopie.Bestand));
        File.Delete(pad);
        File.Delete(Path.ChangeExtension(pad, ".json"));
        VerwijderBijbestanden(pad);
    }

    /// <summary>Losse -wal-, -shm- en -journal-bestanden naast een kopie (van een kopie die nog in WAL-modus stond).</summary>
    private static void VerwijderBijbestanden(string pad)
    {
        foreach (var achtervoegsel in new[] { "-wal", "-shm", "-journal" })
        {
            File.Delete(pad + achtervoegsel);
        }
    }

    /// <summary>
    /// Bewaarschema: vóór een update de laatste 5, dagelijks de laatste 14 plus de eerste van elke maand een jaar lang,
    /// vóór terugzetten de laatste 3, handmatige altijd.
    /// </summary>
    public void RuimOp()
    {
        var lijst = Lijst();
        var weg = new List<Reservekopie>();
        weg.AddRange(lijst.Where(k => k.Soort == Reservekopiesoort.VoorUpdate).Skip(5));
        weg.AddRange(lijst.Where(k => k.Soort == Reservekopiesoort.VoorTerugzetten).Skip(3));
        var dagelijks = lijst.Where(k => k.Soort == Reservekopiesoort.Dagelijks).ToList();
        var maandelijks = dagelijks.GroupBy(k => (k.Gemaakt.Year, k.Gemaakt.Month)).Select(g => g.MinBy(k => k.Gemaakt)!).Take(12).ToHashSet();
        weg.AddRange(dagelijks.Skip(14).Where(k => !maandelijks.Contains(k)));
        foreach (var kopie in weg)
        {
            Verwijder(kopie);
        }
    }

    /// <summary>Is er de afgelopen 20 uur al een dagelijkse (of andere) kopie gemaakt?</summary>
    public bool DagelijkseNodig() =>
        !Lijst().Any(k => k.Soort != Reservekopiesoort.VoorTerugzetten && tijd.GetUtcNow() - k.Gemaakt < TimeSpan.FromHours(20));

    /// <summary>Zet een reservekopie klaar om bij de volgende start terug te zetten (dan is de database niet open).</summary>
    public string? PlanTerugzetten(Reservekopie kopie)
    {
        ArgumentNullException.ThrowIfNull(kopie);
        if (Controleer(kopie) is { } fout)
        {
            return fout;
        }

        File.WriteAllText(Path.Combine(Gegevensmap, Terugzetbestand), JsonSerializer.Serialize(kopie, Json));
        return null;
    }

    public Reservekopie? GeplandTerugzetten =>
        File.Exists(Path.Combine(Gegevensmap, Terugzetbestand)) ? JsonSerializer.Deserialize<Reservekopie>(File.ReadAllText(Path.Combine(Gegevensmap, Terugzetbestand))) : null;

    public void AnnuleerTerugzetten() => File.Delete(Path.Combine(Gegevensmap, Terugzetbestand));

    /// <summary>
    /// Bij het starten, vóór de database opengaat: staat er een reservekopie klaar, dan eerst een kopie van de huidige
    /// gegevens (<see cref="Reservekopiesoort.VoorTerugzetten"/>), daarna de reservekopie terug. Klopt de reservekopie niet
    /// meer, dan blijft alles zoals het was. Geeft wat er gebeurde, of leeg als er niets klaarstond.
    /// </summary>
    public string? VoerGeplandTerugzettenUit()
    {
        if (GeplandTerugzetten is not { } kopie)
        {
            return null;
        }

        AnnuleerTerugzetten();
        if (Controleer(kopie) is { } fout)
        {
            return $"Niet teruggezet: {fout}";
        }

        if (File.Exists(Database))
        {
            Maak(Reservekopiesoort.VoorTerugzetten, $"vóór het terugzetten van {kopie.Bestand}");
        }

        ZetBestandTerug(Path.Combine(Map, kopie.Bestand));
        return $"Reservekopie van {kopie.Gemaakt.ToLocalTime():dd-MM-yyyy HH:mm} ({kopie.Bestand}) is teruggezet.";
    }

    /// <summary>Zet een kopie over de database (met de SQLite-backup, dus ook de -wal en -shm kloppen daarna).</summary>
    internal void ZetBestandTerug(string kopie)
    {
        SqliteConnection.ClearAllPools();
        using var bron = new SqliteConnection($"Data Source={kopie};Mode=ReadOnly;Pooling=False");
        using var doel = new SqliteConnection($"Data Source={Database};Pooling=False");
        bron.Open();
        doel.Open();
        bron.BackupDatabase(doel);
    }

    /// <summary>Integriteitscontrole, laatste migratie en aantallen van een databasebestand.</summary>
    internal static (bool Integer, string? Migratie, long Boekingen, long Administraties) Onderzoek(string pad)
    {
        using var verbinding = new SqliteConnection($"Data Source={pad};Mode=ReadOnly;Pooling=False");
        verbinding.Open();
        var integer = string.Equals(Scalar(verbinding, "PRAGMA integrity_check") as string, "ok", StringComparison.Ordinal);
        return (integer,
            Bestaat(verbinding, "__EFMigrationsHistory") ? Scalar(verbinding, "SELECT MAX(\"MigrationId\") FROM \"__EFMigrationsHistory\"") as string : null,
            Bestaat(verbinding, "Boekingen") ? Convert.ToInt64(Scalar(verbinding, "SELECT COUNT(*) FROM \"Boekingen\""), CultureInfo.InvariantCulture) : 0,
            Bestaat(verbinding, "Administraties") ? Convert.ToInt64(Scalar(verbinding, "SELECT COUNT(*) FROM \"Administraties\""), CultureInfo.InvariantCulture) : 0);
    }

    private static bool Bestaat(SqliteConnection verbinding, string tabel)
    {
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $naam";
        opdracht.Parameters.AddWithValue("$naam", tabel);
        return Convert.ToInt64(opdracht.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static object? Scalar(SqliteConnection verbinding, string sql)
    {
        using var opdracht = verbinding.CreateCommand();
        opdracht.CommandText = sql;
        return opdracht.ExecuteScalar();
    }

    internal static string Controlegetal(string pad)
    {
        using var stroom = File.OpenRead(pad);
        return Convert.ToHexString(SHA256.HashData(stroom));
    }

    private static string Soortnaam(Reservekopiesoort soort) => soort switch
    {
        Reservekopiesoort.VoorUpdate => "voor-update",
        Reservekopiesoort.Dagelijks => "dagelijks",
        Reservekopiesoort.VoorTerugzetten => "voor-terugzetten",
        _ => "handmatig",
    };
}
