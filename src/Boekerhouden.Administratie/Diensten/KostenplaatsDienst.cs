using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een kostenplaats in de lijst, met hoe vaak hij op een (geldende) boekingsregel staat.</summary>
public sealed record Kostenplaatsregel(Guid Id, string Code, string Naam, bool Afgesloten, int Regels)
{
    /// <summary>Zoals hij in een keuzelijst staat: "AUTO1 Auto Tesla".</summary>
    public string Weergave => $"{Code} {Naam}";
}

/// <summary>
/// De cijfers van één kostenplaats over een periode, alleen de resultaatrekeningen: kosten (rekeningen met een
/// debetsaldo), opbrengsten (creditsaldo, positief) en het resultaat (opbrengsten min kosten).
/// </summary>
public sealed record Kostenplaatstotaal(Kostenplaatsregel Kostenplaats, decimal Kosten, decimal Opbrengsten, int Regels)
{
    public decimal Resultaat => Opbrengsten - Kosten;
}

/// <summary>Een boekingsregel op een kostenplaats, met de boeking eromheen.</summary>
public sealed record Kostenplaatsboeking(
    string Dagboek,
    string Boekstuk,
    DateOnly Datum,
    string Rekeningnummer,
    string Rekeningnaam,
    string Omschrijving,
    string? Relatie,
    string? Factuurnummer,
    decimal Bedrag,
    bool IsBalans);

/// <summary>
/// Kostenplaatsen: een extra indeling naast het grootboek, bijv. een auto, afdeling of vestiging. Alleen als de module
/// aanstaat (kantoorinstelling). Een kostenplaats kies je per boekingsregel en is nooit verplicht. Een kostenplaats die
/// op een boeking staat, verwijder je niet (het journaal verandert nooit); hij wordt afgesloten en staat dan niet meer in de
/// keuzelijst. Beheren: recht Boekhouden; de overzichten: Lezen.
/// </summary>
public sealed class KostenplaatsDienst(Toegang toegang, AuditDienst audit)
{
    public const int MaxCode = 20;

    public const int MaxNaam = 100;

    /// <summary>Alle kostenplaatsen, op code; afgesloten erbij.</summary>
    public async Task<IReadOnlyList<Kostenplaatsregel>> Lijst(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Lees(db);
    }

    /// <summary>Of kostenplaatsen aanstaan voor de administratie (Instellingen, Verwerken).</summary>
    internal static async Task<bool> Aan(AdministratieDbContext db, Guid administratie) =>
        await db.Administraties.AsNoTracking().Where(a => a.Id == administratie).Select(a => a.Kostenplaatsen).FirstOrDefaultAsync();

    /// <summary>De kostenplaatsen om te kiezen op een boekingsregel (niet afgesloten), of leeg als de module uitstaat.</summary>
    public async Task<IReadOnlyList<Kostenplaatsregel>> Keuze(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (!await Aan(db, administratie))
        {
            return [];
        }

        return [.. (await Lees(db)).Where(k => !k.Afgesloten)];
    }

    public async Task<Uitkomst<Guid>> VoegToe(Uitvoerder wie, Guid administratie, string code, string naam)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (!await Aan(db, administratie))
        {
            return Uitkomst<Guid>.Mislukt("Kostenplaatsen staan uit voor deze administratie (Instellingen, Verwerken).");
        }

        var (c, n) = (code?.Trim() ?? string.Empty, naam?.Trim() ?? string.Empty);
        var fouten = Controleer(c, n);
        if (fouten.Count == 0 && await db.Kostenplaatsen.AnyAsync(k => k.Code == c))
        {
            fouten.Add($"Er is al een kostenplaats {c}.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        var kostenplaats = new Kostenplaats { Id = Guid.NewGuid(), AdministratieId = administratie, Code = c, Naam = n };
        db.Kostenplaatsen.Add(kostenplaats);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KostenplaatsToegevoegd, $"{c} {n}", null, administratie);
        return Uitkomst<Guid>.Met(kostenplaats.Id);
    }

    /// <summary>Naam wijzigen, afsluiten of weer openen. De code blijft: die staat op de boekingen.</summary>
    public async Task<Uitkomst> Wijzig(Uitvoerder wie, Guid administratie, Guid id, string naam, bool afgesloten)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Kostenplaatsen.FirstOrDefaultAsync(k => k.Id == id) is not { } kostenplaats)
        {
            return Uitkomst.Mislukt("Deze kostenplaats bestaat niet (meer).");
        }

        var n = naam?.Trim() ?? string.Empty;
        if (Controleer(kostenplaats.Code, n) is { Count: > 0 } fouten)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        if (kostenplaats.Naam == n && kostenplaats.Afgesloten == afgesloten)
        {
            return Uitkomst.Gelukt;
        }

        var details = string.Join("; ", new[]
        {
            kostenplaats.Naam == n ? null : $"naam {kostenplaats.Naam} → {n}",
            kostenplaats.Afgesloten == afgesloten ? null : afgesloten ? "afgesloten" : "heropend",
        }.Where(d => d is not null));
        (kostenplaats.Naam, kostenplaats.Afgesloten) = (n, afgesloten);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KostenplaatsGewijzigd, kostenplaats.Code, details, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Verwijderen kan alleen als er nooit op geboekt is (ook niet in een eerdere versie van een boeking).</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Kostenplaatsen.FirstOrDefaultAsync(k => k.Id == id) is not { } kostenplaats)
        {
            return Uitkomst.Mislukt("Deze kostenplaats bestaat niet (meer).");
        }

        if (await db.Boekingsregels.AnyAsync(r => r.Kostenplaats == kostenplaats.Code))
        {
            return Uitkomst.Mislukt($"Op {kostenplaats.Code} is geboekt; sluit hem af in plaats van te verwijderen.");
        }

        db.Kostenplaatsen.Remove(kostenplaats);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KostenplaatsVerwijderd, $"{kostenplaats.Code} {kostenplaats.Naam}", null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Per kostenplaats de kosten, opbrengsten en het resultaat in de periode (op boekingsdatum).</summary>
    public async Task<IReadOnlyList<Kostenplaatstotaal>> Overzicht(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var kostenplaatsen = await Lees(db);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        // In de database opgeteld per kostenplaats en rekening: een paar duizend rijen in plaats van alle regels.
        var perRekening = await (from r in db.GeldendeRegels
                                 join b in db.Boekingen on r.BoekingId equals b.Id
                                 where r.Kostenplaats != null && b.Datum >= van && b.Datum <= tot
                                 group r by new { r.Kostenplaats, r.Rekeningnummer } into g
                                 select new { g.Key.Kostenplaats, g.Key.Rekeningnummer, Saldo = g.Sum(r => r.Bedrag), Aantal = g.Count() }).ToListAsync();
        // Een factuur die over de maanden verdeeld is en vóór de verbetering is geboekt, heeft de kostenplaats ook op de
        // regel naar de verdeelrekening: die telt niet mee (de maandboekingen tellen al).
        var dubbel = await DubbeleVerdeelregels(db);
        if (dubbel.Count > 0)
        {
            var ids = dubbel.Select(d => d.Boeking).Distinct().ToList();
            var aftrek = (await (from r in db.GeldendeRegels
                                 join b in db.Boekingen on r.BoekingId equals b.Id
                                 where r.Kostenplaats != null && ids.Contains(r.BoekingId) && b.Datum >= van && b.Datum <= tot
                                 select new { r.BoekingId, r.Kostenplaats, r.Rekeningnummer, r.Bedrag }).ToListAsync())
                .Where(r => dubbel.Contains((r.BoekingId, r.Rekeningnummer))).ToList();
            perRekening = [.. perRekening.Select(x =>
            {
                var weg = aftrek.Where(a => a.Kostenplaats == x.Kostenplaats && a.Rekeningnummer == x.Rekeningnummer).ToList();
                return weg.Count == 0 ? x : x with { Saldo = x.Saldo - weg.Sum(a => a.Bedrag), Aantal = x.Aantal - weg.Count };
            })];
        }

        var perKostenplaats = perRekening.ToLookup(x => x.Kostenplaats!, StringComparer.Ordinal);
        return
        [
            .. kostenplaatsen.Select(k =>
            {
                var eigen = perKostenplaats[k.Code].ToList();
                var resultaat = eigen.Where(x => schema.Zoek(x.Rekeningnummer) is { IsBalans: false }).Select(x => x.Saldo).ToList();
                return new Kostenplaatstotaal(k, resultaat.Where(s => s > 0).Sum(), -resultaat.Where(s => s < 0).Sum(), eigen.Sum(x => x.Aantal));
            }),
        ];
    }

    /// <summary>De boekingsregels op één kostenplaats in de periode, op datum.</summary>
    public async Task<IReadOnlyList<Kostenplaatsboeking>> Boekingen(Uitvoerder wie, Guid administratie, string code, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var regels = await (from r in db.GeldendeRegels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where r.Kostenplaats == code && b.Datum >= van && b.Datum <= tot
                            orderby b.Datum, b.Dagboek, b.Boekstuk, r.Regelnummer
                            select new { r.BoekingId, b.Dagboek, b.Boekstuk, b.Datum, r.Rekeningnummer, Omschrijving = r.Omschrijving ?? b.Omschrijving, r.Relatie, r.Factuurnummer, r.Bedrag })
            .ToListAsync();
        var dubbel = await DubbeleVerdeelregels(db);
        return
        [
            .. regels.Where(x => !dubbel.Contains((x.BoekingId, x.Rekeningnummer))).Select(x =>
            {
                var rekening = schema.Zoek(x.Rekeningnummer);
                return new Kostenplaatsboeking(x.Dagboek, x.Boekstuk, x.Datum, x.Rekeningnummer, rekening?.Naam ?? string.Empty, x.Omschrijving, x.Relatie,
                    x.Factuurnummer, x.Bedrag, rekening?.IsBalans ?? false);
            }),
        ];
    }

    /// <summary>
    /// De factuurboekingen van verdeelde facturen met hun verdeelrekening: een regel daarop telt niet voor een
    /// kostenplaats (de kosten of omzet staan op de maandboekingen). Nieuwe boekingen hebben daar geen kostenplaats meer.
    /// </summary>
    private static async Task<HashSet<(Guid Boeking, string Rekening)>> DubbeleVerdeelregels(AdministratieDbContext db)
    {
        var inkoop = await db.Documenten.AsNoTracking().Where(d => d.Verdeling != null && d.BoekingId != null).Select(d => new { d.BoekingId, d.Verdeling }).ToListAsync();
        var verkoop = await db.Verkoopdocumenten.AsNoTracking().Where(d => d.Verdeling != null && d.BoekingId != null).Select(d => new { d.BoekingId, d.Verdeling }).ToListAsync();
        var uitkomst = new HashSet<(Guid, string)>();
        foreach (var d in inkoop.Concat(verkoop))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(d.Verdeling!);
                if (json.RootElement.TryGetProperty("Rekening", out var rekening) || json.RootElement.TryGetProperty("rekening", out rekening))
                {
                    uitkomst.Add((d.BoekingId!.Value, rekening.GetString() ?? string.Empty));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Geen leesbare verdeling: dan telt de regel gewoon mee.
            }
        }

        return uitkomst;
    }

    /// <summary>
    /// Wat er niet klopt aan de kostenplaatsen op een journaalpost: een code die niet bestaat, of (bij een nieuwe regel)
    /// een afgesloten kostenplaats. Regels die al zo geboekt waren (<paramref name="vorige"/>) mogen een afgesloten houden.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> Fouten(AdministratieDbContext db, Journaalpost post, Journaalpost? vorige = null)
    {
        var codes = post.Regels.Select(r => r.Kostenplaats?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            return [];
        }

        var bekend = await db.Kostenplaatsen.AsNoTracking().Where(k => codes.Contains(k.Code)).ToDictionaryAsync(k => k.Code, k => k.Afgesloten, StringComparer.Ordinal);
        var eerder = vorige?.Regels.Select(r => r.Kostenplaats).Where(c => c is not null).ToHashSet(StringComparer.Ordinal) ?? [];
        var fouten = new List<string>();
        foreach (var code in codes)
        {
            if (!bekend.TryGetValue(code!, out var afgesloten))
            {
                fouten.Add($"Kostenplaats {code} bestaat niet.");
            }
            else if (afgesloten && !eerder.Contains(code))
            {
                fouten.Add($"Kostenplaats {code} is afgesloten.");
            }
        }

        return fouten;
    }


    private static async Task<IReadOnlyList<Kostenplaatsregel>> Lees(AdministratieDbContext db)
    {
        var gebruik = await db.GeldendeRegels.Where(r => r.Kostenplaats != null).GroupBy(r => r.Kostenplaats!)
            .Select(g => new { Code = g.Key, Aantal = g.Count() }).ToDictionaryAsync(x => x.Code, x => x.Aantal, StringComparer.Ordinal);
        return
        [
            .. (await db.Kostenplaatsen.AsNoTracking().ToListAsync()).OrderBy(k => k.Code, StringComparer.OrdinalIgnoreCase)
                .Select(k => new Kostenplaatsregel(k.Id, k.Code, k.Naam, k.Afgesloten, gebruik.GetValueOrDefault(k.Code))),
        ];
    }

    private static List<string> Controleer(string code, string naam)
    {
        var fouten = new List<string>();
        if (code.Length == 0)
        {
            fouten.Add("Vul de code in, bijv. AUTO1.");
        }
        else if (code.Length > MaxCode || code.Any(char.IsWhiteSpace))
        {
            fouten.Add($"Een code is hooguit {MaxCode} tekens, zonder spaties.");
        }

        if (naam.Length == 0)
        {
            fouten.Add("Vul de naam in.");
        }
        else if (naam.Length > MaxNaam)
        {
            fouten.Add($"Een naam is hooguit {MaxNaam} tekens.");
        }

        return fouten;
    }
}
