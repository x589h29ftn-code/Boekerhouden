using System.Text.RegularExpressions;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

public sealed record RekeningInvoer(string Nummer, string Naam, Rekeningsoort Soort, string? RgsCode);

/// <summary>Een geboekte journaalpost met zijn plek in de keten.</summary>
public sealed record GeboektePost(long Volgnummer, Journaalpost Post, DateTimeOffset GeboektOp, string GeboektDoor);

/// <summary>
/// Rekeningschema, boeken en de overzichten (grootboekkaart, proefbalans, balans en W&amp;V) van één administratie.
/// Alles gaat via <see cref="Toegang"/>; boeken voegt toe aan de hash-keten en wijzigt of verwijdert nooit.
/// </summary>
public sealed partial class GrootboekDienst(Toegang toegang, AuditDienst audit, TimeProvider tijd)
{
    public async Task<Rekeningschema> Schema(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await LaadSchema(db);
    }

    public async Task<IReadOnlyList<Dagboek>> Dagboeken(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await LaadDagboeken(db);
    }

    public async Task<Uitkomst> VoegRekeningToe(Uitvoerder wie, Guid administratie, RekeningInvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var rgs = string.IsNullOrWhiteSpace(invoer.RgsCode) ? null : invoer.RgsCode.Trim();
        if (rgs is not null && !RgsVorm().IsMatch(rgs))
        {
            return Uitkomst.Mislukt("Een RGS-code begint met B of W, gevolgd door letters, bijv. WBedKanTef.");
        }

        var rekening = new Rekening(invoer.Nummer.Trim(), invoer.Naam.Trim(), invoer.Soort, rgs);
        if (rekening.Nummer.Length == 0 || rekening.Naam.Length == 0)
        {
            return Uitkomst.Mislukt("Vul nummer en naam in.");
        }

        try
        {
            (await LaadSchema(db)).Voegtoe(rekening);
        }
        catch (ArgumentException fout)
        {
            return Uitkomst.Mislukt(fout.Message.Split(" (Parameter", 2)[0]);
        }

        db.Rekeningen.Add(new Grootboekrekening
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Nummer = rekening.Nummer,
            Naam = rekening.Naam,
            Soort = rekening.Soort,
            RgsCode = rekening.RgsCode,
        });
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RekeningToegevoegd, $"{rekening.Nummer} {rekening.Naam}", rekening.RgsCode, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Het volgende boekstuknummer in een dagboek, bijv. <c>2026-0007</c>.</summary>
    public async Task<string> VolgendBoekstuk(Uitvoerder wie, Guid administratie, string dagboek, int jaar)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await VolgendBoekstuk(db, dagboek, jaar);
    }

    /// <summary>
    /// Boekt een journaalpost: dezelfde controles als in het domein, dan een volgnummer en hash in de keten. Het
    /// memoriaal is voor het kantoor; de andere dagboeken ook voor de ondernemer.
    /// </summary>
    public async Task<Uitkomst<long>> Boek(Uitvoerder wie, Guid administratie, Journaalpost post)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(post);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dagboeken = await LaadDagboeken(db);
        var dagboek = dagboeken.FirstOrDefault(d => d.Code == post.Dagboek);
        var nodig = dagboek?.Soort == Dagboeksoort.Memoriaal ? Recht.Boekhouden : Recht.Invoeren;
        if (!Toegang.Mag((await toegang.Rol(wie, administratie))!.Value, nodig))
        {
            return Uitkomst<long>.Mislukt("Je hebt in deze administratie niet het recht om hier te boeken.");
        }

        var fouten = Journaalcontrole.Controleer(post, await LaadSchema(db), dagboeken).ToList();
        if (await db.Boekingen.AnyAsync(b => b.Dagboek == post.Dagboek && b.Boekstuk == post.Boekstuk))
        {
            fouten.Add($"Boekstuk {post.Boekstuk} bestaat al in dagboek {post.Dagboek}.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<long>.Mislukt([.. fouten]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        var boeking = await VoegToeAanKeten(db, administratie, post, wie);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.Geboekt, $"{post.Dagboek} {boeking.Boekstuk}", $"{boeking.Omschrijving}; {post.TotaalDebet:0.00}", administratie);
        return Uitkomst<long>.Met(boeking.Volgnummer);
    }

    /// <summary>
    /// Zet een (gecontroleerde) journaalpost achteraan in de keten: volgnummer en hash na de laatste boeking. Alleen
    /// binnen een transactie aanroepen; de rij-lock op de administratie zorgt dat er één boeking tegelijk is. Slaat
    /// niet op: dat doet de aanroeper, samen met wat er verder bij hoort (bijv. de openstaande post).
    /// </summary>
    internal async Task<Boeking> VoegToeAanKeten(AdministratieDbContext db, Guid administratie, Journaalpost post, Uitvoerder wie)
    {
        await db.Vergrendel(administratie);
        var vorige = await db.Boekingen.OrderByDescending(b => b.Volgnummer).Select(b => new { b.Volgnummer, b.Hash }).FirstOrDefaultAsync();
        var volgnummer = (vorige?.Volgnummer ?? 0) + 1;
        var vorigeHash = vorige?.Hash ?? Journaalketen.Begin;
        var boeking = new Boeking
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Volgnummer = volgnummer,
            Dagboek = post.Dagboek,
            Boekstuk = post.Boekstuk.Trim(),
            Datum = post.Datum,
            Omschrijving = post.Omschrijving.Trim(),
            GeboektOp = tijd.GetUtcNow(),
            GeboektDoorId = wie.Id,
            GeboektDoor = wie.Naam,
            VorigeHash = vorigeHash,
            Regels =
            [
                .. post.Regels.Select((r, i) => new Boekingsregel
                {
                    AdministratieId = administratie,
                    Regelnummer = i + 1,
                    Rekeningnummer = r.Rekeningnummer,
                    Bedrag = r.Bedrag,
                    Omschrijving = Leeg(r.Omschrijving),
                    BtwCode = Leeg(r.BtwCode),
                    Relatie = Leeg(r.Relatie),
                    Factuurnummer = Leeg(r.Factuurnummer),
                }),
            ],
        };
        boeking.Hash = Journaalketen.Hash(boeking.NaarDomein(), volgnummer, vorigeHash);
        db.Boekingen.Add(boeking);
        return boeking;
    }

    /// <summary>Het rekeningschema en de dagboeken van een geopende administratie (voor andere diensten).</summary>
    internal static async Task<(Rekeningschema Schema, IReadOnlyList<Dagboek> Dagboeken)> Inrichting(AdministratieDbContext db) =>
        (await LaadSchema(db), await LaadDagboeken(db));

    /// <summary>Het volgende boekstuknummer in een dagboek binnen een geopende administratie.</summary>
    internal static async Task<string> VolgendBoekstuk(AdministratieDbContext db, string dagboek, int jaar)
    {
        var voorvoegsel = $"{jaar}-";
        var boekstukken = await db.Boekingen.Where(b => b.Dagboek == dagboek && b.Boekstuk.StartsWith(voorvoegsel)).Select(b => b.Boekstuk).ToListAsync();
        var hoogste = boekstukken.Select(b => int.TryParse(b.AsSpan(voorvoegsel.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{voorvoegsel}{hoogste + 1:0000}";
    }

    /// <summary>De jaren met boekingen, plus het jaar van vandaag; nieuwste eerst.</summary>
    public async Task<IReadOnlyList<int>> Boekjaren(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var jaren = await db.Boekingen.Select(b => b.Datum.Year).Distinct().ToListAsync();
        return [.. jaren.Append(tijd.GetLocalNow().Year).Distinct().OrderDescending()];
    }

    public async Task<IReadOnlyList<GeboektePost>> Posten(Uitvoerder wie, Guid administratie, string? dagboek = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Boekingen.AsNoTracking().Include(b => b.Regels).AsQueryable();
        if (dagboek is not null)
        {
            query = query.Where(b => b.Dagboek == dagboek);
        }

        var boekingen = await query.OrderByDescending(b => b.Volgnummer).ToListAsync();
        return [.. boekingen.Select(b => new GeboektePost(b.Volgnummer, b.NaarDomein(), b.GeboektOp, b.GeboektDoor))];
    }

    public async Task<Grootboekkaart> Kaart(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot) =>
        (await Journaal(wie, administratie)).Kaart(rekening, van, tot);

    public async Task<Proefbalans> Proefbalans(Uitvoerder wie, Guid administratie, int boekjaar) =>
        (await Journaal(wie, administratie)).Proefbalans(boekjaar);

    public async Task<Kolommenbalans> Kolommenbalans(Uitvoerder wie, Guid administratie, int boekjaar) =>
        (await Journaal(wie, administratie)).Kolommenbalans(boekjaar);

    public async Task<Jaaroverzicht> Jaaroverzicht(Uitvoerder wie, Guid administratie, int boekjaar) =>
        Boekhouding.Grootboek.Jaaroverzicht.Maak(await Proefbalans(wie, administratie, boekjaar), RgsHoofdposten.Schema);

    /// <summary>
    /// Controleert de hash-keten: is er buiten de app iets in het journaal gewijzigd, verwijderd of tussengevoegd?
    /// Geeft de volgnummers waar de keten breekt; leeg = in orde.
    /// </summary>
    public async Task<IReadOnlyList<long>> ControleerKeten(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var boekingen = await db.Boekingen.AsNoTracking().Include(b => b.Regels).ToListAsync();
        var fout = Journaalketen.Controleer(boekingen.Select(b => new Ketenschakel(b.Volgnummer, b.NaarDomein(), b.Hash, b.VorigeHash)));
        await audit.Log(wie, AuditActies.KetenGecontroleerd, fout.Count == 0 ? "in orde" : $"afwijking bij {string.Join(", ", fout)}", null, administratie);
        return fout;
    }

    private async Task<Journaal> Journaal(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var boekingen = await db.Boekingen.AsNoTracking().Include(b => b.Regels).OrderBy(b => b.Volgnummer).ToListAsync();
        return Boekhouding.Grootboek.Journaal.Herstel(await LaadSchema(db), await LaadDagboeken(db), boekingen.Select(b => b.NaarDomein()));
    }

    private static async Task<Rekeningschema> LaadSchema(AdministratieDbContext db) =>
        new((await db.Rekeningen.AsNoTracking().ToListAsync()).Select(r => r.NaarDomein()));

    private static async Task<IReadOnlyList<Dagboek>> LaadDagboeken(AdministratieDbContext db) =>
        [.. (await db.Dagboeken.AsNoTracking().OrderBy(d => d.Code).ToListAsync()).Select(d => d.NaarDomein())];

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    [GeneratedRegex("^[BW][A-Z][a-z]{2}([A-Z][a-z]{2}){0,4}$")]
    private static partial Regex RgsVorm();
}
