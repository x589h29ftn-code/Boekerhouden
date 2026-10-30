using System.Text.RegularExpressions;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <param name="BtwCode">De btw-code die bij de rekening hoort; leeg = variabel.</param>
public sealed record RekeningInvoer(string Nummer, string Naam, Rekeningsoort Soort, string? RgsCode, string? BtwCode = null);

/// <summary>Een geboekte journaalpost met zijn plek in de keten.</summary>
public sealed record GeboektePost(long Volgnummer, Journaalpost Post, DateTimeOffset GeboektOp, string GeboektDoor);

/// <summary>Eén boeking om in te zien, met het document (de factuur) waar hij bij hoort, als dat er is.</summary>
/// <param name="AangepastIn">Is de boeking aangepast: de nieuwe boeking (dagboek en boekstuk).</param>
/// <param name="Vervangt">Is dit de nieuwe boeking van een aanpassing: de boeking die hij vervangt.</param>
/// <param name="Herstelt">Is dit de tegenboeking van een aanpassing: de boeking die hij ongedaan maakt.</param>
/// <param name="Vast">Waarom de boeking hier niet aan te passen is (bijv. via de factuur); leeg = wel.</param>
public sealed record Boekingsdetail(
    GeboektePost Boeking,
    Guid? Document,
    string? Documentnaam,
    string? AangepastIn = null,
    string? Vervangt = null,
    string? Herstelt = null,
    string? Vast = null);

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

        var btw = string.IsNullOrWhiteSpace(invoer.BtwCode) ? null : invoer.BtwCode.Trim();
        if (btw is not null && Boekhouding.Btw.BtwCodes.Zoek((await BtwDienst.Lees(db)).Codes, btw) is null)
        {
            return Uitkomst.Mislukt($"Btw-code {btw} bestaat niet.");
        }

        var rekening = new Rekening(invoer.Nummer.Trim(), invoer.Naam.Trim(), invoer.Soort, rgs, BtwCode: btw);
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
            BtwCode = rekening.BtwCode,
        });
        await db.SaveChangesAsync();
        var details = string.Join("; ", new[] { rekening.RgsCode, rekening.BtwCode is { } b ? $"btw {b}" : null }.Where(x => x is not null));
        await audit.Log(wie, AuditActies.RekeningToegevoegd, $"{rekening.Nummer} {rekening.Naam}", details.Length > 0 ? details : null, administratie);
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

    /// <summary>De boekingen, de nieuwste eerst; eventueel van één dagboek en binnen een periode (op boekingsdatum).</summary>
    public async Task<IReadOnlyList<GeboektePost>> Posten(Uitvoerder wie, Guid administratie, string? dagboek = null, DateOnly? van = null, DateOnly? tot = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Boekingen.AsNoTracking().Include(b => b.Regels).AsQueryable();
        if (dagboek is not null)
        {
            query = query.Where(b => b.Dagboek == dagboek);
        }

        if (van is { } begin)
        {
            query = query.Where(b => b.Datum >= begin);
        }

        if (tot is { } eind)
        {
            query = query.Where(b => b.Datum <= eind);
        }

        var boekingen = await query.OrderByDescending(b => b.Volgnummer).ToListAsync();
        return [.. boekingen.Select(b => new GeboektePost(b.Volgnummer, b.NaarDomein(), b.GeboektOp, b.GeboektDoor))];
    }

    public async Task<Grootboekkaart> Kaart(Uitvoerder wie, Guid administratie, string rekening, DateOnly van, DateOnly tot) =>
        (await Journaal(wie, administratie, van, tot)).Kaart(rekening, van, tot);

    public async Task<Proefbalans> Proefbalans(Uitvoerder wie, Guid administratie, int boekjaar) =>
        (await Journaal(wie, administratie, new DateOnly(boekjaar, 1, 1), new DateOnly(boekjaar, 12, 31))).Proefbalans(boekjaar);

    public async Task<Kolommenbalans> Kolommenbalans(Uitvoerder wie, Guid administratie, int boekjaar) =>
        (await Journaal(wie, administratie, new DateOnly(boekjaar, 1, 1), new DateOnly(boekjaar, 12, 31))).Kolommenbalans(boekjaar);

    /// <summary>De kolommenbalans over een periode: alleen boekingen tussen de datums, balansrekeningen met hun beginstand.</summary>
    public async Task<Kolommenbalans> Kolommenbalans(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot) =>
        (await Journaal(wie, administratie, van, tot)).Kolommenbalans(van, tot);

    /// <summary>Eén boeking (dagboek en boekstuk), met de factuur waar hij bij hoort; leeg als hij niet bestaat.</summary>
    public async Task<Boekingsdetail?> Boeking(Uitvoerder wie, Guid administratie, string dagboek, string boekstuk)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Boekingen.AsNoTracking().Include(b => b.Regels).FirstOrDefaultAsync(b => b.Dagboek == dagboek && b.Boekstuk == boekstuk) is not { } boeking)
        {
            return null;
        }

        var document = await db.Documenten.AsNoTracking().Where(d => d.BoekingId == boeking.Id).Select(d => new { d.Id, d.Bestandsnaam }).FirstOrDefaultAsync()
            ?? await db.OpenstaandePosten.AsNoTracking().Where(p => p.BoekingId == boeking.Id && p.DocumentId != null)
                .Join(db.Documenten.AsNoTracking(), p => p.DocumentId, d => d.Id, (p, d) => new { d.Id, d.Bestandsnaam }).FirstOrDefaultAsync();
        var aangepastIn = await db.Boekingen.AsNoTracking().Where(b => b.Vervangt == boeking.Id).Select(b => b.Dagboek + " " + b.Boekstuk).FirstOrDefaultAsync();
        return new Boekingsdetail(new GeboektePost(boeking.Volgnummer, boeking.NaarDomein(), boeking.GeboektOp, boeking.GeboektDoor), document?.Id, document?.Bestandsnaam,
            aangepastIn, await Boekstuk(db, boeking.Vervangt), await Boekstuk(db, boeking.Herstelt), await NietAanpasbaar(db, boeking));
    }

    private static async Task<string?> Boekstuk(AdministratieDbContext db, Guid? boeking) =>
        boeking is { } id ? await db.Boekingen.AsNoTracking().Where(b => b.Id == id).Select(b => b.Dagboek + " " + b.Boekstuk).FirstOrDefaultAsync() : null;

    /// <summary>Waarom een boeking niet via <see cref="PasAan"/> aan te passen is, of leeg als dat wel kan.</summary>
    private static async Task<string?> NietAanpasbaar(AdministratieDbContext db, Boeking boeking)
    {
        if (await db.Boekingen.AnyAsync(b => b.Herstelt == boeking.Id))
        {
            return "Deze boeking is al aangepast.";
        }

        if (boeking.Herstelt is not null)
        {
            return "Dit is de tegenboeking van een aanpassing.";
        }

        if (await db.Documenten.AnyAsync(d => d.BoekingId == boeking.Id) || await db.OpenstaandePosten.AnyAsync(p => p.BoekingId == boeking.Id))
        {
            return "Deze boeking hoort bij een factuur: pas hem aan door de factuur te heropenen.";
        }

        var stuk = $"\"{boeking.Dagboek} {boeking.Boekstuk}\"";
        if (await db.Documenten.AnyAsync(d => d.Verdeling != null && d.Verdeling.Contains(stuk)))
        {
            return "Deze boeking hoort bij de periodeverdeling van een factuur: pas hem aan door de factuur te heropenen.";
        }

        return null;
    }

    /// <summary>
    /// Past een boeking aan. Het journaal wijzigt nooit: er komt een tegenboeking (alles terug) en een nieuwe boeking,
    /// beide in hetzelfde dagboek en verwezen naar de oorspronkelijke. Regels op bank, debiteuren en crediteuren blijven
    /// gelijk (<see cref="Boekingscorrectie"/>); een boeking bij een factuur gaat via het heropenen van de factuur.
    /// Geeft de nieuwe boeking (dagboek en boekstuk).
    /// </summary>
    public async Task<Uitkomst<string>> PasAan(Uitvoerder wie, Guid administratie, string dagboek, string boekstuk, Journaalpost nieuw)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(nieuw);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dagboeken = await LaadDagboeken(db);
        var nodig = dagboeken.FirstOrDefault(d => d.Code == dagboek)?.Soort == Dagboeksoort.Memoriaal ? Recht.Boekhouden : Recht.Invoeren;
        if (!Toegang.Mag((await toegang.Rol(wie, administratie))!.Value, nodig))
        {
            return Uitkomst<string>.Mislukt("Je hebt in deze administratie niet het recht om hier te boeken.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await db.Boekingen.AsNoTracking().Include(b => b.Regels).FirstOrDefaultAsync(b => b.Dagboek == dagboek && b.Boekstuk == boekstuk) is not { } oud)
        {
            return Uitkomst<string>.Mislukt($"Boeking {dagboek} {boekstuk} bestaat niet.");
        }

        if (await NietAanpasbaar(db, oud) is { } reden)
        {
            return Uitkomst<string>.Mislukt(reden);
        }

        var schema = await LaadSchema(db);
        var post = nieuw with
        {
            Dagboek = dagboek,
            Boekstuk = "?",
            Omschrijving = nieuw.Omschrijving.Trim(),
            Regels = [.. nieuw.Regels.Where(r => r.Bedrag != 0 || !string.IsNullOrWhiteSpace(r.Rekeningnummer))],
        };
        var fouten = Boekingscorrectie.Controleer(oud.NaarDomein(), post, schema).Concat(Journaalcontrole.Controleer(post, schema, dagboeken)).Distinct().ToList();
        if (fouten.Count > 0)
        {
            return Uitkomst<string>.Mislukt([.. fouten]);
        }

        var tegen = oud.NaarDomein().Tegenboeking(await VolgendBoekstuk(db, dagboek, oud.Datum.Year), oud.Datum, Kort($"Aanpassing {dagboek} {boekstuk}: terug ({oud.Omschrijving})"));
        (await VoegToeAanKeten(db, administratie, tegen, wie)).Herstelt = oud.Id;
        await db.SaveChangesAsync();
        var vervanging = await VoegToeAanKeten(db, administratie, post with { Boekstuk = await VolgendBoekstuk(db, dagboek, post.Datum.Year) }, wie);
        vervanging.Vervangt = oud.Id;

        // De bankmutatie wijst voortaan naar de nieuwe boeking.
        foreach (var mutatie in await db.Banktransacties.Where(t => t.BoekingId == oud.Id).ToListAsync())
        {
            mutatie.BoekingId = vervanging.Id;
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.BoekingAangepast, $"{dagboek} {boekstuk}",
            $"tegenboeking {dagboek} {tegen.Boekstuk}, nieuw {dagboek} {vervanging.Boekstuk}; {vervanging.Omschrijving}; {post.TotaalDebet:0.00}", administratie);
        return Uitkomst<string>.Met($"{dagboek} {vervanging.Boekstuk}");
    }

    private static string Kort(string tekst) => tekst.Length > 200 ? tekst[..200] : tekst;

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

    /// <summary>
    /// Het journaal voor een periode, zonder alle jaren in te lezen: de boekingen in de periode helemaal, en wat ervóór
    /// ligt per rekening opgeteld door de database, als twee beginposten: vóór het boekjaar van <paramref name="van"/>, en
    /// in dat boekjaar vóór <paramref name="van"/>. Proefbalans, kolommenbalans en grootboekkaart komen zo precies uit
    /// als met het hele journaal (balansrekeningen nemen hun stand mee, resultaat van eerder gaat naar het onverdeelde
    /// resultaat), maar de tijd hangt af van de periode en niet van het aantal jaren.
    /// </summary>
    private async Task<Journaal> Journaal(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var boekjaar = new DateOnly(van.Year, 1, 1);
        var eerder = await (from r in db.Boekingsregels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where b.Datum < van
                            group r.Bedrag by new { VoorBoekjaar = b.Datum < boekjaar, r.Rekeningnummer } into g
                            select new { g.Key.VoorBoekjaar, g.Key.Rekeningnummer, Saldo = g.Sum() }).ToListAsync();
        var begin = new[] { (true, boekjaar.AddDays(-1)), (false, van.AddDays(-1)) }
            .Select(x => new Journaalpost(string.Empty, "begin", x.Item2, "Beginstand",
                [.. eerder.Where(e => e.VoorBoekjaar == x.Item1 && e.Saldo != 0).OrderBy(e => e.Rekeningnummer, StringComparer.Ordinal).Select(e => new Journaalregel(e.Rekeningnummer, e.Saldo))]))
            .Where(p => p.Regels.Count > 0);
        var boekingen = await db.Boekingen.AsNoTracking().Include(b => b.Regels).Where(b => b.Datum >= van && b.Datum <= tot)
            .OrderBy(b => b.Volgnummer).ToListAsync();
        return Boekhouding.Grootboek.Journaal.Herstel(await LaadSchema(db), await LaadDagboeken(db), begin.Concat(boekingen.Select(b => b.NaarDomein())));
    }

    private static async Task<Rekeningschema> LaadSchema(AdministratieDbContext db) =>
        new((await db.Rekeningen.AsNoTracking().ToListAsync()).Select(r => r.NaarDomein()));

    private static async Task<IReadOnlyList<Dagboek>> LaadDagboeken(AdministratieDbContext db) =>
        [.. (await db.Dagboeken.AsNoTracking().OrderBy(d => d.Code).ToListAsync()).Select(d => d.NaarDomein())];

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    [GeneratedRegex("^[BW][A-Z][a-z]{2}([A-Z][a-z]{2}){0,4}$")]
    private static partial Regex RgsVorm();
}
