using Boekerhouden.Data;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record KoppelInfo(string Code, Guid? EigenPostId, KoppelBron Bron)
{
    public string Sleutel => PostBalans.Sleutel(new KoppelDoel(Code, EigenPostId));
}

/// <param name="Herkomst">Waar het voorstel vandaan komt: "kantoorsjabloon", "andere klanten" of "omschrijving".</param>
public sealed record VoorstelInfo(string Code, string Herkomst);

public sealed record RubriceerRekening(
    Guid Id,
    string Nummer,
    string Omschrijving,
    decimal? Saldo,
    decimal? SaldoVorig,
    KoppelInfo? Koppeling,
    VoorstelInfo? Voorstel,
    decimal Correcties = 0)
{
    public bool HeeftSaldo => (Saldo ?? 0) != 0 || (SaldoVorig ?? 0) != 0;
}

/// <summary>Alles wat het rubriceerscherm nodig heeft voor één dossier (klant en boekjaar).</summary>
public sealed record RubriceerOverzicht(
    DossierDetail Dossier,
    RgsSchema? Schema,
    RgsSelectie Selectie,
    IReadOnlySet<string> Codes,
    IReadOnlyList<RubriceerRekening> Rekeningen,
    IReadOnlyList<EigenPost> EigenPosten,
    IReadOnlyDictionary<string, string> KlantNamen,
    IReadOnlyDictionary<string, string> KantoorNamen,
    PostBalans? Balans)
{
    public int Relevant => Rekeningen.Count(r => r.HeeftSaldo);

    public int Gekoppeld => Rekeningen.Count(r => r.HeeftSaldo && r.Koppeling is not null);

    /// <summary>Gekoppeld %, naar beneden afgerond: 100% betekent echt alles.</summary>
    public int Percentage => Relevant == 0 ? 100 : Gekoppeld * 100 / Relevant;

    /// <summary>De naam van een post zoals in de jaarrekening: klantnaam, anders kantoornaam, anders de RGS-omschrijving.</summary>
    public string Naam(string sleutel)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        if (sleutel.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal))
        {
            return EigenPosten.FirstOrDefault(p => PostBalans.EigenSleutel(p.Id) == sleutel)?.Naam ?? sleutel;
        }

        return KlantNamen.GetValueOrDefault(sleutel) ?? KantoorNamen.GetValueOrDefault(sleutel) ?? Schema?.Zoek(sleutel)?.Omschrijving ?? sleutel;
    }

    public bool IsHernoemd(string code) => KlantNamen.ContainsKey(code) || KantoorNamen.ContainsKey(code);
}

/// <summary>
/// Rubriceren: grootboekrekeningen van een klant koppelen aan RGS-codes (of eigen posten daaronder), posten hernoemen
/// en automatisch koppelen via het kantoorsjabloon of de RGS-code uit het bestand.
/// </summary>
public sealed class RubriceerDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RgsDienst rgs, AuditDienst audit, TimeProvider tijd)
{
    private readonly Lock voorstellerSlot = new();
    private (RgsSchema Schema, Dictionary<string, Voorsteller> PerSelectie)? voorstellers;

    public async Task<RubriceerOverzicht?> Overzicht(Guid dossierId, bool metVoorstellen = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking()
            .Where(d => d.Id == dossierId)
            .Select(d => new DossierDetail(d.Id, d.Naam, d.Boekjaar, d.SoortOpdracht, d.Status, d.KlantId, d.Klant!.Klantnummer, d.Klant.Naam, d.Klant.Rechtsvorm))
            .SingleOrDefaultAsync();
        if (dossier is null)
        {
            return null;
        }

        var schema = await rgs.ActiefSchema();
        var selectie = RgsDienst.SelectieVoor(dossier.Rechtsvorm);
        var saldi = await DossierSaldi.Haal(db, dossier.KlantId, dossier.Boekjaar, dossier.Soort);
        var rekeningen = await db.Grootboekrekeningen.AsNoTracking()
            .Where(g => g.KlantId == dossier.KlantId)
            .OrderBy(g => g.Nummer)
            .ToListAsync();
        rekeningen = [.. rekeningen.Where(r => saldi.ContainsKey(r.Id))];
        var koppelingen = await db.Koppelingen.AsNoTracking().Where(k => k.KlantId == dossier.KlantId)
            .ToDictionaryAsync(k => k.GrootboekrekeningId);
        // Namen zoals ze gelden in het boekjaar van het dossier; een eigen post kan vanaf een jaar anders heten.
        var klantNamen = Jaarversies.Geldend(
                await db.KlantPostNamen.AsNoTracking().Where(n => n.KlantId == dossier.KlantId && n.VanafBoekjaar <= dossier.Boekjaar).ToListAsync(),
                n => n.Code, dossier.Boekjaar, StringComparer.Ordinal)
            .Where(n => n.Value.Naam is not null).ToDictionary(n => n.Key, n => n.Value.Naam!, StringComparer.Ordinal);
        var kantoorNamen = Jaarversies.Geldend(
                await db.KantoorPostNamen.AsNoTracking().Where(n => n.VanafBoekjaar <= dossier.Boekjaar).ToListAsync(),
                n => n.Code, dossier.Boekjaar, StringComparer.Ordinal)
            .Where(n => n.Value.Naam is not null).ToDictionary(n => n.Key, n => n.Value.Naam!, StringComparer.Ordinal);
        var eigenPosten = (await db.EigenPosten.AsNoTracking().Where(p => p.KlantId == dossier.KlantId)
                .OrderBy(p => p.OuderCode).ThenBy(p => p.Volgorde)
                .ToListAsync())
            .Select(p => new EigenPost(p.Id, p.OuderCode, klantNamen.GetValueOrDefault(PostBalans.EigenSleutel(p.Id)) ?? p.Naam, p.OmslagCode))
            .ToList();
        foreach (var eigen in klantNamen.Keys.Where(k => k.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal)).ToList())
        {
            klantNamen.Remove(eigen);
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        PostBalans? balans = null;
        var voorstellen = new Dictionary<Guid, VoorstelInfo>();
        if (schema is not null)
        {
            codes.UnionWith(schema.Selecteer(selectie));
            foreach (var code in koppelingen.Values.Select(k => k.Code).Concat(eigenPosten.Select(p => p.OuderCode)))
            {
                if (schema.Zoek(code) is not null && codes.Add(code))
                {
                    codes.UnionWith(schema.Voorouders(code).Select(o => o.Code));
                }
            }

            balans = PostBalans.Bereken(
                schema,
                eigenPosten,
                koppelingen.Values.ToDictionary(k => k.GrootboekrekeningId, k => new KoppelDoel(k.Code, k.EigenPostId)),
                [.. rekeningen.Select(r => new RekeningSaldo(r.Id, r.Nummer, r.Omschrijving, saldi[r.Id].Huidig, saldi[r.Id].Vorig, saldi[r.Id].Correcties))]);

            if (metVoorstellen)
            {
                var open = rekeningen.Where(r => !koppelingen.ContainsKey(r.Id)).ToList();
                var bekend = rekeningen.Where(r => koppelingen.ContainsKey(r.Id))
                    .Select(r => (r.Nummer, Zijde: koppelingen[r.Id].Code[0])).ToList();
                voorstellen = await Voorstellen(db, schema, selectie, dossier.KlantId, open, bekend);
            }
        }

        return new RubriceerOverzicht(
            dossier,
            schema,
            selectie,
            codes,
            [.. rekeningen.Select(r => new RubriceerRekening(
                r.Id,
                r.Nummer,
                r.Omschrijving,
                saldi[r.Id].Huidig,
                saldi[r.Id].Vorig,
                koppelingen.TryGetValue(r.Id, out var k) ? new KoppelInfo(k.Code, k.EigenPostId, k.Bron) : null,
                voorstellen.GetValueOrDefault(r.Id),
                saldi[r.Id].Correcties))],
            eigenPosten,
            klantNamen,
            kantoorNamen,
            balans);
    }

    /// <summary>
    /// Koppelt rekeningen aan een post: een RGS-code (niveau 3 of 4) of een eigen post ("eigen:&lt;id&gt;").
    /// Met <paramref name="onthouden"/> komt het rekeningnummer ook in het kantoorsjabloon, zodat dezelfde rekening
    /// bij andere klanten automatisch wordt gekoppeld.
    /// </summary>
    public async Task<Uitkomst> Koppel(Guid klantId, IReadOnlyCollection<Guid> rekeningIds, string doel, bool onthouden, Uitvoerder wie, KoppelBron bron = KoppelBron.Handmatig)
    {
        ArgumentNullException.ThrowIfNull(rekeningIds);
        ArgumentNullException.ThrowIfNull(doel);
        ArgumentNullException.ThrowIfNull(wie);
        var schema = await rgs.ActiefSchema();
        if (schema is null)
        {
            return Uitkomst.Mislukt("Er is nog geen RGS-werkboek ingelezen.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var (code, eigenPostId, fout) = await BepaalDoel(db, schema, klantId, doel);
        if (fout is not null)
        {
            return Uitkomst.Mislukt(fout);
        }

        var rekeningen = await db.Grootboekrekeningen.Where(g => g.KlantId == klantId && rekeningIds.Contains(g.Id)).ToListAsync();
        if (rekeningen.Count == 0)
        {
            return Uitkomst.Mislukt("Geen rekeningen gekozen.");
        }

        var bestaand = await db.Koppelingen.Where(k => k.KlantId == klantId && rekeningIds.Contains(k.GrootboekrekeningId))
            .ToDictionaryAsync(k => k.GrootboekrekeningId);
        var nu = tijd.GetUtcNow();
        foreach (var rekening in rekeningen)
        {
            if (!bestaand.TryGetValue(rekening.Id, out var koppeling))
            {
                koppeling = new Koppeling { Id = Guid.NewGuid(), KlantId = klantId, GrootboekrekeningId = rekening.Id };
                db.Koppelingen.Add(koppeling);
            }

            koppeling.Code = code!;
            koppeling.EigenPostId = eigenPostId;
            koppeling.RgsVersie = schema.Versie;
            koppeling.Bron = bron;
            koppeling.GewijzigdOp = nu;
            koppeling.GewijzigdDoor = wie.Naam;
        }

        if (onthouden)
        {
            await Onthoud(db, rekeningen, code!, wie, nu);
        }

        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        await audit.Log(wie, AuditActies.Gekoppeld, KlantDienst.Onderwerp(klant),
            $"{Opsomming(rekeningen.Select(r => $"{r.Nummer} {r.Omschrijving}"))} → {code} {schema.Zoek(code)?.Omschrijving}" +
            (eigenPostId is null ? string.Empty : " (eigen post)") + (onthouden ? "; opgenomen in kantoorsjabloon" : string.Empty));
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> Ontkoppel(Guid klantId, IReadOnlyCollection<Guid> rekeningIds, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(rekeningIds);
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var koppelingen = await db.Koppelingen.Where(k => k.KlantId == klantId && rekeningIds.Contains(k.GrootboekrekeningId)).ToListAsync();
        if (koppelingen.Count == 0)
        {
            return Uitkomst.Gelukt;
        }

        var rekeningen = await db.Grootboekrekeningen.AsNoTracking().Where(g => rekeningIds.Contains(g.Id)).ToListAsync();
        db.Koppelingen.RemoveRange(koppelingen);
        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        await audit.Log(wie, AuditActies.Ontkoppeld, KlantDienst.Onderwerp(klant), Opsomming(rekeningen.Select(r => $"{r.Nummer} {r.Omschrijving}")));
        return Uitkomst.Gelukt;
    }

    /// <summary>Bevestigt de voorstellen voor de gekozen rekeningen (of alle rekeningen met een voorstel).</summary>
    public async Task<Uitkomst<int>> AccepteerVoorstellen(Guid dossierId, IReadOnlyCollection<Guid>? rekeningIds, bool onthouden, Uitvoerder wie)
    {
        var overzicht = await Overzicht(dossierId);
        if (overzicht is null)
        {
            return Uitkomst<int>.Mislukt("Het dossier bestaat niet (meer).");
        }

        var aantal = 0;
        foreach (var groep in overzicht.Rekeningen
                     .Where(r => r.Voorstel is not null && r.Koppeling is null && (rekeningIds is null || rekeningIds.Contains(r.Id)))
                     .GroupBy(r => r.Voorstel!.Code))
        {
            var uitkomst = await Koppel(overzicht.Dossier.KlantId, [.. groep.Select(r => r.Id)], groep.Key, onthouden, wie, KoppelBron.Voorstel);
            if (!uitkomst.IsGelukt)
            {
                return Uitkomst<int>.Mislukt([.. uitkomst.Fouten]);
            }

            aantal += groep.Count();
        }

        return Uitkomst<int>.Met(aantal);
    }

    /// <summary>
    /// Koppelt automatisch wat zeker is: via het kantoorsjabloon (zelfde nummer, vergelijkbare omschrijving) of de
    /// RGS-code uit het importbestand. Handmatige en bevestigde koppelingen blijven altijd staan.
    /// </summary>
    public async Task<int> AutomatischKoppelen(Guid klantId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var schema = await rgs.ActiefSchema();
        if (schema is null)
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var rekeningen = await db.Grootboekrekeningen.Where(g => g.KlantId == klantId).ToListAsync();
        var koppelingen = await db.Koppelingen.Where(k => k.KlantId == klantId).ToDictionaryAsync(k => k.GrootboekrekeningId);
        var nummers = rekeningen.Select(r => r.Nummer).ToList();
        var sjabloon = await db.SjabloonRegels.AsNoTracking().Where(r => nummers.Contains(r.Rekeningnummer))
            .ToDictionaryAsync(r => r.Rekeningnummer, StringComparer.Ordinal);
        var nu = tijd.GetUtcNow();
        var gewijzigd = new List<string>();

        foreach (var rekening in rekeningen)
        {
            koppelingen.TryGetValue(rekening.Id, out var koppeling);
            if (koppeling is { Bron: KoppelBron.Handmatig or KoppelBron.Voorstel })
            {
                continue;
            }

            (string Code, KoppelBron Bron)? doel = null;
            if (sjabloon.TryGetValue(rekening.Nummer, out var regel) && Voorsteller.LijktOp(regel.Omschrijving, rekening.Omschrijving)
                && schema.Koppelbaar(regel.Code) is { } viaSjabloon)
            {
                doel = (viaSjabloon.Code, KoppelBron.Sjabloon);
            }
            else if (schema.Koppelbaar(rekening.RgsCode) is { } viaBestand)
            {
                doel = (viaBestand.Code, KoppelBron.Bestand);
            }

            if (doel is not { } d || (koppeling is not null && koppeling.Code == d.Code && koppeling.Bron == d.Bron && koppeling.EigenPostId is null))
            {
                continue;
            }

            if (koppeling is null)
            {
                koppeling = new Koppeling { Id = Guid.NewGuid(), KlantId = klantId, GrootboekrekeningId = rekening.Id };
                db.Koppelingen.Add(koppeling);
            }

            koppeling.Code = d.Code;
            koppeling.EigenPostId = null;
            koppeling.Bron = d.Bron;
            koppeling.RgsVersie = schema.Versie;
            koppeling.GewijzigdOp = nu;
            koppeling.GewijzigdDoor = wie.Naam;
            gewijzigd.Add($"{rekening.Nummer} → {d.Code}");
        }

        if (gewijzigd.Count > 0)
        {
            await db.SaveChangesAsync();
            var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
            await audit.Log(wie, AuditActies.AutomatischGekoppeld, KlantDienst.Onderwerp(klant), $"{gewijzigd.Count} rekening(en): {Opsomming(gewijzigd)}");
        }

        return gewijzigd.Count;
    }

    /// <summary>Voegt onder een RGS-post (niveau 3 of 4) een eigen post toe, bijv. "Overige vorderingen 1".</summary>
    public async Task<Uitkomst<Guid>> VoegEigenPostToe(Guid klantId, string ouderCode, string naam, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var schema = await rgs.ActiefSchema();
        if (schema?.Zoek(ouderCode) is not { Niveau: 3 or 4 } ouder)
        {
            return Uitkomst<Guid>.Mislukt("Een eigen post kan alleen onder een subpost of grootboekcode (RGS niveau 3 of 4).");
        }

        naam = naam?.Trim() ?? string.Empty;
        if (naam.Length == 0)
        {
            return Uitkomst<Guid>.Mislukt("Geef de nieuwe post een naam.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var volgorde = await db.EigenPosten.Where(p => p.KlantId == klantId && p.OuderCode == ouder.Code).MaxAsync(p => (int?)p.Volgorde) ?? 0;
        var post = new KlantEigenPost
        {
            Id = Guid.NewGuid(),
            KlantId = klantId,
            OuderCode = ouder.Code,
            Naam = naam,
            Volgorde = volgorde + 1,
            AangemaaktOp = tijd.GetUtcNow(),
            AangemaaktDoor = wie.Naam,
        };
        db.EigenPosten.Add(post);
        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        await audit.Log(wie, AuditActies.EigenPostToegevoegd, KlantDienst.Onderwerp(klant), $"'{naam}' onder {ouder.Code} {ouder.Omschrijving}");
        return Uitkomst<Guid>.Met(post.Id);
    }

    /// <summary>
    /// Het voorstel voor salderen onder een RGS-post: de omslagcode van de post zelf, of anders die van een
    /// grootboekcode eronder (bijv. onder belastingen en premies: terug te vorderen omzetbelasting).
    /// </summary>
    public static string? VoorstelOmslag(RgsSchema schema, string ouderCode)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Zoek(ouderCode)?.Omslagcode
            ?? schema.Kinderen(ouderCode).Select(c => c.Omslagcode).FirstOrDefault(c => c is not null && schema.Zoek(c) is not null);
    }

    /// <summary>
    /// Salderen voor een eigen post aan of uit: met een omslagcode tellen de rekeningen onder de post samen, en staat
    /// het saldo aan de andere kant dan de RGS-post erboven, dan staat de hele post (per jaar) onder de omslagcode.
    /// </summary>
    public async Task<Uitkomst> ZetSaldering(Guid klantId, Guid postId, string? omslagCode, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        omslagCode = string.IsNullOrWhiteSpace(omslagCode) ? null : omslagCode.Trim();
        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await db.EigenPosten.SingleOrDefaultAsync(p => p.Id == postId && p.KlantId == klantId);
        if (post is null)
        {
            return Uitkomst.Mislukt("Deze post bestaat niet (meer).");
        }

        var schema = await rgs.ActiefSchema();
        if (omslagCode is not null)
        {
            if (schema?.Zoek(post.OuderCode) is not { Kant: { } kant } ouder
                || schema.Zoek(omslagCode) is not { Niveau: 3 or 4 } omslag
                || omslag.Kant == kant || omslag.Code[0] != ouder.Code[0])
            {
                return Uitkomst.Mislukt("Kies voor salderen een RGS-post (niveau 3 of 4) aan de andere kant van de balans, bijv. een vordering bij een schuld.");
            }
        }

        if (post.OmslagCode == omslagCode)
        {
            return Uitkomst.Gelukt;
        }

        post.OmslagCode = omslagCode;
        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        await audit.Log(wie, AuditActies.EigenPostGesaldeerd, KlantDienst.Onderwerp(klant),
            omslagCode is null ? $"'{post.Naam}': niet meer salderen" : $"'{post.Naam}': salderen, aan de andere kant naar {omslagCode} {schema?.Zoek(omslagCode)?.Omschrijving}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Verwijdert een eigen post; gekoppelde rekeningen blijven gekoppeld aan de RGS-post erboven.</summary>
    public async Task<Uitkomst> VerwijderEigenPost(Guid klantId, Guid postId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var post = await db.EigenPosten.SingleOrDefaultAsync(p => p.Id == postId && p.KlantId == klantId);
        if (post is null)
        {
            return Uitkomst.Mislukt("Deze post bestaat niet (meer).");
        }

        await db.Koppelingen.Where(k => k.EigenPostId == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.EigenPostId, (Guid?)null));
        var sleutel = PostBalans.EigenSleutel(postId);
        await db.KlantPostNamen.Where(n => n.KlantId == klantId && n.Code == sleutel).ExecuteDeleteAsync();
        db.EigenPosten.Remove(post);
        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        await audit.Log(wie, AuditActies.EigenPostVerwijderd, KlantDienst.Onderwerp(klant), $"'{post.Naam}' onder {post.OuderCode}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Geeft een post een eigen naam voor de jaarrekening, vanaf het boekjaar van het dossier: een eigen post, of een
    /// RGS-post voor deze klant of (met <paramref name="kantoorbreed"/>) voor alle klanten. Een lege naam zet de
    /// standaardnaam terug. Eerdere jaren houden hun naam; latere jaren volgen, tenzij ze zelf een naam hebben.
    /// </summary>
    public async Task<Uitkomst> Hernoem(Guid dossierId, string sleutel, string? naam, bool kantoorbreed, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        ArgumentNullException.ThrowIfNull(wie);
        naam = string.IsNullOrWhiteSpace(naam) ? null : naam.Trim();
        if (naam?.Length > 300)
        {
            return Uitkomst.Mislukt("Een naam is hoogstens 300 tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier?.Klant is not { } klant)
        {
            return Uitkomst.Mislukt("Dossier niet gevonden.");
        }

        var jaar = dossier.Boekjaar;
        string oud;
        bool gewijzigd;
        if (sleutel.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal))
        {
            var post = Guid.TryParseExact(sleutel[PostBalans.EigenVoorvoegsel.Length..], "N", out var id)
                ? await db.EigenPosten.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.KlantId == klant.Id)
                : null;
            if (post is null)
            {
                return Uitkomst.Mislukt("Deze post bestaat niet (meer).");
            }

            if (naam is null)
            {
                return Uitkomst.Mislukt("Een eigen post moet een naam hebben.");
            }

            var versies = await db.KlantPostNamen.Where(n => n.KlantId == klant.Id && n.Code == sleutel).ToListAsync();
            oud = Jaarversies.Geldend(versies, n => n.Code, jaar).GetValueOrDefault(sleutel)?.Naam ?? post.Naam;
            gewijzigd = Jaarversies.Zet(db.KlantPostNamen, versies, jaar,
                v => new KlantPostNaam { KlantId = klant.Id, Code = sleutel, Naam = v?.Naam ?? post.Naam },
                n => n.Naam = naam,
                n => n.Naam ?? post.Naam);
        }
        else
        {
            var schema = await rgs.ActiefSchema();
            if (schema?.Zoek(sleutel) is not { } code)
            {
                return Uitkomst.Mislukt("Deze post bestaat niet in de actieve RGS-versie.");
            }

            oud = code.Omschrijving;
            if (kantoorbreed)
            {
                var versies = await db.KantoorPostNamen.Where(n => n.Code == sleutel).ToListAsync();
                gewijzigd = Jaarversies.Zet(db.KantoorPostNamen, versies, jaar,
                    v => new KantoorPostNaam { Code = sleutel, Naam = v?.Naam },
                    n => n.Naam = naam,
                    n => n.Naam ?? string.Empty);
            }
            else
            {
                var versies = await db.KlantPostNamen.Where(n => n.KlantId == klant.Id && n.Code == sleutel).ToListAsync();
                gewijzigd = Jaarversies.Zet(db.KlantPostNamen, versies, jaar,
                    v => new KlantPostNaam { KlantId = klant.Id, Code = sleutel, Naam = v?.Naam },
                    n => n.Naam = naam,
                    n => n.Naam ?? string.Empty);
            }
        }

        if (!gewijzigd)
        {
            return Uitkomst.Gelukt;
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.PostHernoemd, kantoorbreed ? "Kantoorstandaard" : KlantDienst.Onderwerp(klant),
            $"{sleutel}: '{oud}' → '{naam ?? "(standaardnaam)"}' vanaf {jaar}");
        return Uitkomst.Gelukt;
    }

    private static async Task<(string? Code, Guid? EigenPostId, string? Fout)> BepaalDoel(BoekerhoudenDbContext db, RgsSchema schema, Guid klantId, string doel)
    {
        if (doel.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal))
        {
            if (!Guid.TryParseExact(doel[PostBalans.EigenVoorvoegsel.Length..], "N", out var id)
                || await db.EigenPosten.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.KlantId == klantId) is not { } post)
            {
                return (null, null, "Deze eigen post bestaat niet (meer).");
            }

            return (post.OuderCode, post.Id, null);
        }

        return schema.Zoek(doel) switch
        {
            null => (null, null, $"RGS-code {doel} bestaat niet in de actieve versie."),
            { Inactief: true } => (null, null, $"RGS-code {doel} is vervallen (inactief)."),
            { Niveau: < 3 } c => (null, null, $"'{c.Omschrijving}' is een hoofdpost. Kies een subpost of grootboekcode daaronder."),
            { Niveau: > 4 } c => (null, null, $"'{c.Omschrijving}' is een mutatie (niveau 5); koppel aan de grootboekcode erboven."),
            var c => (c.Code, null, null),
        };
    }

    private static async Task Onthoud(BoekerhoudenDbContext db, List<Grootboekrekening> rekeningen, string code, Uitvoerder wie, DateTimeOffset nu)
    {
        var nummers = rekeningen.Select(r => r.Nummer).ToList();
        var regels = await db.SjabloonRegels.Where(r => nummers.Contains(r.Rekeningnummer)).ToDictionaryAsync(r => r.Rekeningnummer, StringComparer.Ordinal);
        foreach (var rekening in rekeningen)
        {
            if (!regels.TryGetValue(rekening.Nummer, out var regel))
            {
                regel = new SjabloonRegel { Id = Guid.NewGuid(), Rekeningnummer = rekening.Nummer };
                db.SjabloonRegels.Add(regel);
                regels[rekening.Nummer] = regel;
            }

            regel.Omschrijving = rekening.Omschrijving;
            regel.Code = code;
            regel.GewijzigdOp = nu;
            regel.GewijzigdDoor = wie.Naam;
        }
    }

    private async Task<Dictionary<Guid, VoorstelInfo>> Voorstellen(
        BoekerhoudenDbContext db, RgsSchema schema, RgsSelectie selectie, Guid klantId, List<Grootboekrekening> open, List<(string Nummer, char Zijde)> bekend)
    {
        var resultaat = new Dictionary<Guid, VoorstelInfo>();
        if (open.Count == 0)
        {
            return resultaat;
        }

        var nummers = open.Select(r => r.Nummer).Distinct().ToList();
        var sjabloon = await db.SjabloonRegels.AsNoTracking().Where(r => nummers.Contains(r.Rekeningnummer))
            .ToDictionaryAsync(r => r.Rekeningnummer, StringComparer.Ordinal);
        var elders = await (from k in db.Koppelingen.AsNoTracking()
                            join g in db.Grootboekrekeningen.AsNoTracking() on k.GrootboekrekeningId equals g.Id
                            where k.KlantId != klantId && nummers.Contains(g.Nummer)
                                  && (k.Bron == KoppelBron.Handmatig || k.Bron == KoppelBron.Voorstel)
                            select new { g.Nummer, g.Omschrijving, k.Code })
            .ToListAsync();
        var voorsteller = VoorstellerVoor(schema, selectie);

        foreach (var rekening in open)
        {
            if (sjabloon.TryGetValue(rekening.Nummer, out var regel) && schema.Koppelbaar(regel.Code) is not null)
            {
                resultaat[rekening.Id] = new VoorstelInfo(regel.Code, "kantoorsjabloon");
                continue;
            }

            var bijAnderen = elders
                .Where(e => e.Nummer == rekening.Nummer && Voorsteller.LijktOp(e.Omschrijving, rekening.Omschrijving))
                .GroupBy(e => e.Code)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault(c => schema.Koppelbaar(c) is not null);
            if (bijAnderen is not null)
            {
                resultaat[rekening.Id] = new VoorstelInfo(bijAnderen, "andere klanten");
                continue;
            }

            if (voorsteller.StelVoor(rekening.Omschrijving, Zijde(rekening.Nummer, bekend)) is { } voorstel)
            {
                resultaat[rekening.Id] = new VoorstelInfo(voorstel.Code.Code, "omschrijving");
            }
        }

        return resultaat;
    }

    private Voorsteller VoorstellerVoor(RgsSchema schema, RgsSelectie selectie)
    {
        lock (voorstellerSlot)
        {
            if (voorstellers is not { } v || !ReferenceEquals(v.Schema, schema))
            {
                voorstellers = (schema, new Dictionary<string, Voorsteller>(StringComparer.Ordinal));
            }

            var perSelectie = voorstellers.Value.PerSelectie;
            if (!perSelectie.TryGetValue(selectie.Naam, out var voorsteller))
            {
                voorsteller = new Voorsteller(schema, schema.Selecteer(selectie).Select(schema.Zoek).OfType<RgsCode>());
                perSelectie[selectie.Naam] = voorsteller;
            }

            return voorsteller;
        }
    }

    /// <summary>
    /// Balans of W&amp;V afleiden uit de buren: liggen de dichtstbijzijnde gekoppelde rekeningnummers erboven en eronder
    /// aan dezelfde kant, dan hoort deze rekening daar waarschijnlijk ook (rekeningschema's zijn op nummer ingedeeld).
    /// Geven de buren geen uitsluitsel, dan het decimale rekeningstelsel (<see cref="ZijdeVolgensStelsel"/>), maar alleen
    /// als de al gekoppelde rekeningen van de klant dat stelsel volgen.
    /// </summary>
    internal static char? Zijde(string nummer, IReadOnlyList<(string Nummer, char Zijde)> bekend)
    {
        var onder = bekend.Where(b => VergelijkNummer(b.Nummer, nummer) < 0).OrderByDescending(b => b.Nummer, NummerVergelijker).FirstOrDefault();
        var boven = bekend.Where(b => VergelijkNummer(b.Nummer, nummer) > 0).OrderBy(b => b.Nummer, NummerVergelijker).FirstOrDefault();
        if (onder.Nummer is not null && boven.Nummer is not null && onder.Zijde == boven.Zijde)
        {
            return onder.Zijde;
        }

        return ZijdeVolgensStelsel(nummer) is { } zijde && VolgtStelsel(bekend) ? zijde : null;
    }

    /// <summary>
    /// Het decimale rekeningstelsel: het eerste cijfer is de rubriek. Rubriek 0–3 (vaste activa en vermogen, financiële
    /// rekeningen, tussenrekeningen, voorraden) is balans, 4–9 (kosten, kostenplaatsen, fabricage, kostprijs, omzet,
    /// resultaten) is W&amp;V.
    /// </summary>
    internal static char? ZijdeVolgensStelsel(string nummer) =>
        nummer.Length > 0 && char.IsAsciiDigit(nummer[0]) ? (nummer[0] <= '3' ? 'B' : 'W') : null;

    /// <summary>Volgen de gekoppelde rekeningen het decimale stelsel (op hooguit een op de tien na)?</summary>
    private static bool VolgtStelsel(IReadOnlyList<(string Nummer, char Zijde)> bekend)
    {
        var getoetst = bekend.Where(b => ZijdeVolgensStelsel(b.Nummer) is not null).ToList();
        return getoetst.Count(b => ZijdeVolgensStelsel(b.Nummer) == b.Zijde) * 10 >= getoetst.Count * 9;
    }

    private static readonly Comparer<string> NummerVergelijker = Comparer<string>.Create(VergelijkNummer);

    /// <summary>Vergelijkt rekeningnummers als getal waar dat kan ("950" vóór "1000"), anders als tekst.</summary>
    private static int VergelijkNummer(string a, string b) =>
        decimal.TryParse(a, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var x)
        && decimal.TryParse(b, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var y)
            ? x.CompareTo(y)
            : string.CompareOrdinal(a, b);

    private static string Opsomming(IEnumerable<string> items)
    {
        var lijst = items.ToList();
        return lijst.Count <= 10 ? string.Join("; ", lijst) : $"{string.Join("; ", lijst.Take(10))} en nog {lijst.Count - 10}";
    }
}
