using System.Text.RegularExpressions;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <param name="BtwCode">De btw-code die bij de rekening hoort; leeg = variabel.</param>
public sealed record RekeningInvoer(string Nummer, string Naam, Rekeningsoort Soort, string? RgsCode, string? BtwCode = null);

/// <summary>Een geboekte journaalpost met zijn plek in de keten.</summary>
public sealed record GeboektePost(long Volgnummer, Journaalpost Post, DateTimeOffset GeboektOp, string GeboektDoor);

/// <summary>Een eerdere versie van een aangepaste boeking.</summary>
public sealed record Boekingsversie(int Versie, GeboektePost Boeking);

/// <summary>Eén boeking om in te zien (de geldende versie), met het document (de factuur) waar hij bij hoort, als dat er is.</summary>
/// <param name="Versie">De versie: 1 = nooit aangepast.</param>
/// <param name="Eerder">De eerdere versies, de nieuwste eerst.</param>
/// <param name="Herstelt">Is dit een tegenboeking (terugdraaien): de boeking die hij ongedaan maakt.</param>
/// <param name="Vast">Waarom de boeking hier niet aan te passen is (bijv. via de factuur); leeg = wel.</param>
public sealed record Boekingsdetail(
    GeboektePost Boeking,
    Guid? Document,
    string? Documentnaam,
    int Versie = 1,
    IReadOnlyList<Boekingsversie>? Eerder = null,
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

    /// <summary>
    /// Een extra memoriaal, bijv. <c>MLON</c> "Memoriaal lonen" of <c>MACC</c> "Memoriaal accountant": om de boekingen
    /// van het kantoor te ordenen. Een code van 2 tot 10 letters of cijfers; alleen met het recht Boekhouden.
    /// </summary>
    public async Task<Uitkomst> VoegMemoriaalToe(Uitvoerder wie, Guid administratie, string code, string naam)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(naam);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var dagboek = new Dagboek(code.Trim().ToUpperInvariant(), naam.Trim(), Dagboeksoort.Memoriaal);
        if (!DagboekcodeVorm().IsMatch(dagboek.Code))
        {
            return Uitkomst.Mislukt("Een dagboekcode is 2 tot 10 letters of cijfers, bijv. MLON.");
        }

        if (dagboek.Naam.Length == 0)
        {
            return Uitkomst.Mislukt("Geef het dagboek een naam, bijv. Memoriaal lonen.");
        }

        if (await db.Dagboeken.AnyAsync(d => d.Code == dagboek.Code))
        {
            return Uitkomst.Mislukt($"Dagboek {dagboek.Code} bestaat al.");
        }

        db.Dagboeken.Add(new Dagboekdefinitie { Id = Guid.NewGuid(), AdministratieId = administratie, Code = dagboek.Code, Naam = dagboek.Naam, Soort = dagboek.Soort });
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DagboekToegevoegd, $"{dagboek.Code} {dagboek.Naam}", "memoriaal", administratie);
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
        var jaren = await db.Geldend.Select(b => b.Datum.Year).Distinct().ToListAsync();
        return [.. jaren.Append(tijd.GetLocalNow().Year).Distinct().OrderDescending()];
    }

    /// <summary>De boekingen, de nieuwste eerst; eventueel van één dagboek en binnen een periode (op boekingsdatum).</summary>
    public async Task<IReadOnlyList<GeboektePost>> Posten(Uitvoerder wie, Guid administratie, string? dagboek = null, DateOnly? van = null, DateOnly? tot = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Geldend.AsNoTracking().Include(b => b.Regels).Where(b => b.Regels.Any());
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

    /// <summary>Eén boeking (dagboek en boekstuk, de geldende versie), met de factuur waar hij bij hoort; leeg als hij niet bestaat.</summary>
    public async Task<Boekingsdetail?> Boeking(Uitvoerder wie, Guid administratie, string dagboek, string boekstuk)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Geldend.AsNoTracking().Include(b => b.Regels).FirstOrDefaultAsync(b => b.Dagboek == dagboek && b.Boekstuk == boekstuk) is not { } boeking)
        {
            return null;
        }

        var document = await db.Documenten.AsNoTracking().Where(d => d.BoekingId == boeking.Id).Select(d => new { d.Id, d.Bestandsnaam }).FirstOrDefaultAsync()
            ?? await db.OpenstaandePosten.AsNoTracking().Where(p => p.BoekingId == boeking.Id && p.DocumentId != null)
                .Join(db.Documenten.AsNoTracking(), p => p.DocumentId, d => d.Id, (p, d) => new { d.Id, d.Bestandsnaam }).FirstOrDefaultAsync();
        var eerder = await db.Boekingen.AsNoTracking().Include(b => b.Regels).Where(b => b.Dagboek == dagboek && b.Boekstuk == boekstuk && b.Id != boeking.Id)
            .OrderByDescending(b => b.Versie).ToListAsync();
        var hersteld = boeking.Herstelt is { } id
            ? await db.Boekingen.AsNoTracking().Where(b => b.Id == id).Select(b => b.Dagboek + " " + b.Boekstuk).FirstOrDefaultAsync()
            : null;
        return new Boekingsdetail(Geboekt(boeking), document?.Id, document?.Bestandsnaam, boeking.Versie,
            [.. eerder.Select(b => new Boekingsversie(b.Versie, Geboekt(b)))], hersteld, await NietAanpasbaar(db, boeking));
    }

    private static GeboektePost Geboekt(Boeking b) => new(b.Volgnummer, b.NaarDomein(), b.GeboektOp, b.GeboektDoor);

    /// <summary>Waarom een boeking niet via <see cref="PasAan"/> aan te passen is, of leeg als dat wel kan.</summary>
    private static async Task<string?> NietAanpasbaar(AdministratieDbContext db, Boeking boeking)
    {
        if (boeking.Regels.Count == 0)
        {
            return "Deze boeking is vervallen.";
        }

        if (boeking.Herstelt is not null || await db.Boekingen.AnyAsync(b => b.Herstelt == boeking.Id))
        {
            return "Deze boeking is teruggedraaid met een tegenboeking.";
        }

        if (await db.Documenten.AnyAsync(d => d.BoekingId == boeking.Id) || await db.OpenstaandePosten.AnyAsync(p => p.BoekingId == boeking.Id))
        {
            return "Deze boeking hoort bij een factuur: pas hem aan via de factuur.";
        }

        var stuk = $"\"{boeking.Dagboek} {boeking.Boekstuk}\"";
        if (await db.Documenten.AnyAsync(d => d.Verdeling != null && d.Verdeling.Contains(stuk)))
        {
            return "Deze boeking hoort bij de periodeverdeling van een factuur: pas hem aan via de factuur.";
        }

        return null;
    }

    /// <summary>
    /// Past een boeking aan: een nieuwe versie met hetzelfde dagboek en boekstuk, achteraan in de keten
    /// (<see cref="NieuweVersie"/>). De vorige versie blijft bewaard maar telt niet meer mee. Regels op bank, debiteuren
    /// en crediteuren blijven gelijk (<see cref="Boekingscorrectie"/>); een boeking bij een factuur gaat via de factuur.
    /// </summary>
    public async Task<Uitkomst<int>> PasAan(Uitvoerder wie, Guid administratie, string dagboek, string boekstuk, Journaalpost nieuw)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(nieuw);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dagboeken = await LaadDagboeken(db);
        var nodig = dagboeken.FirstOrDefault(d => d.Code == dagboek)?.Soort == Dagboeksoort.Memoriaal ? Recht.Boekhouden : Recht.Invoeren;
        if (!Toegang.Mag((await toegang.Rol(wie, administratie))!.Value, nodig))
        {
            return Uitkomst<int>.Mislukt("Je hebt in deze administratie niet het recht om hier te boeken.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await db.Geldend.AsNoTracking().Include(b => b.Regels).FirstOrDefaultAsync(b => b.Dagboek == dagboek && b.Boekstuk == boekstuk) is not { } oud)
        {
            return Uitkomst<int>.Mislukt($"Boeking {dagboek} {boekstuk} bestaat niet.");
        }

        if (await NietAanpasbaar(db, oud) is { } reden)
        {
            return Uitkomst<int>.Mislukt(reden);
        }

        var schema = await LaadSchema(db);
        var post = nieuw with
        {
            Dagboek = dagboek,
            Boekstuk = boekstuk,
            Omschrijving = nieuw.Omschrijving.Trim(),
            Regels = [.. nieuw.Regels.Where(r => r.Bedrag != 0 || !string.IsNullOrWhiteSpace(r.Rekeningnummer))],
        };
        var fouten = Boekingscorrectie.Controleer(oud.NaarDomein(), post, schema).Concat(Journaalcontrole.Controleer(post, schema, dagboeken)).Distinct().ToList();
        if (fouten.Count > 0)
        {
            return Uitkomst<int>.Mislukt([.. fouten]);
        }

        var versie = await NieuweVersie(db, administratie, oud, post, wie);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.BoekingAangepast, $"{dagboek} {boekstuk}", $"versie {versie.Versie}; {versie.Omschrijving}; {post.TotaalDebet:0.00}", administratie);
        return Uitkomst<int>.Met(versie.Versie);
    }

    /// <summary>
    /// Een nieuwe versie van een boeking achteraan in de keten: hetzelfde dagboek en boekstuk, versie plus één, met een
    /// verwijzing naar de vorige. Wat naar de vorige versie wees (bankmutatie, document, openstaande post), wijst daarna
    /// naar de nieuwe. Zonder regels is de boeking vervallen. Alleen binnen een transactie; slaat niet op.
    /// </summary>
    internal async Task<Boeking> NieuweVersie(AdministratieDbContext db, Guid administratie, Boeking vorige, Journaalpost post, Uitvoerder wie)
    {
        var boeking = await VoegToeAanKeten(db, administratie, post with { Dagboek = vorige.Dagboek, Boekstuk = vorige.Boekstuk }, wie);
        (boeking.Versie, boeking.Vervangt) = (vorige.Versie + 1, vorige.Id);
        foreach (var mutatie in await db.Banktransacties.Where(t => t.BoekingId == vorige.Id).ToListAsync())
        {
            mutatie.BoekingId = boeking.Id;
        }

        foreach (var document in await db.Documenten.Where(d => d.BoekingId == vorige.Id).ToListAsync())
        {
            document.BoekingId = boeking.Id;
        }

        foreach (var open in await db.OpenstaandePosten.Where(p => p.BoekingId == vorige.Id).ToListAsync())
        {
            open.BoekingId = boeking.Id;
        }

        return boeking;
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
        var eerder = await (from r in db.GeldendeRegels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where b.Datum < van
                            group r.Bedrag by new { VoorBoekjaar = b.Datum < boekjaar, r.Rekeningnummer } into g
                            select new { g.Key.VoorBoekjaar, g.Key.Rekeningnummer, Saldo = g.Sum() }).ToListAsync();
        var begin = new[] { (true, boekjaar.AddDays(-1)), (false, van.AddDays(-1)) }
            .Select(x => new Journaalpost(string.Empty, "begin", x.Item2, "Beginstand",
                [.. eerder.Where(e => e.VoorBoekjaar == x.Item1 && e.Saldo != 0).OrderBy(e => e.Rekeningnummer, StringComparer.Ordinal).Select(e => new Journaalregel(e.Rekeningnummer, e.Saldo))]))
            .Where(p => p.Regels.Count > 0);
        var boekingen = await db.Geldend.AsNoTracking().Include(b => b.Regels).Where(b => b.Datum >= van && b.Datum <= tot)
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

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex DagboekcodeVorm();
}
