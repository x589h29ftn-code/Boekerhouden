using Boekerhouden.Data;
using Boekerhouden.Server.Werkprogramma;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record StapInfo(
    Guid Id,
    string Titel,
    string? Instructie,
    bool Verplicht,
    bool Eigen,
    StapControle Controle,
    StapStatus Status,
    string? Antwoord,
    string? Reden,
    string? UitgevoerdDoor,
    DateTimeOffset? UitgevoerdOp,
    bool Verwijderd,
    string? VerwijderdDoor,
    DateTimeOffset? VerwijderdOp);

public sealed record OnderdeelInfo(
    Guid Id,
    Fase Fase,
    OnderdeelSoort Soort,
    string Titel,
    string? HoofdpostCode,
    bool Verplicht,
    string? Notitie,
    string? GereviewdDoor,
    DateTimeOffset? GereviewdOp,
    IReadOnlyList<StapInfo> Stappen)
{
    public IEnumerable<StapInfo> Actief => Stappen.Where(s => !s.Verwijderd);

    public int Aantal => Actief.Count();

    public int Klaar => Actief.Count(s => s.Status != StapStatus.Open);

    /// <summary>Alle (niet-verwijderde) stappen gereed of niet van toepassing.</summary>
    public bool IsGereed => Klaar == Aantal;
}

/// <param name="Keuzes">Per hoofdpost (RGS niveau 2): materieel of bewust niet materieel; ontbreekt = nog niet gekozen.</param>
public sealed record WerkprogrammaOverzicht(
    DossierDetail Dossier,
    string Versie,
    IReadOnlyList<OnderdeelInfo> Onderdelen,
    IReadOnlyDictionary<string, bool> Keuzes)
{
    public OnderdeelInfo? Post(string code) => Onderdelen.FirstOrDefault(o => o.HoofdpostCode == code);
}

/// <summary>
/// Het werkprogramma van een dossier (docs hoofdstuk 10): een kopie van het kantoorsjabloon, de keuze van materiële
/// posten, en stappen die worden afgetekend, toegevoegd of (met reden) verwijderd.
/// </summary>
public sealed class WerkprogrammaDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RubriceerDienst rubriceer, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleTekst = 20000;

    /// <summary>Het werkprogramma van het dossier; de eerste keer wordt het uit het sjabloon gestart.</summary>
    public async Task<WerkprogrammaOverzicht?> Overzicht(Guid dossierId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.WerkprogrammaVersie is null)
        {
            await Start(db, dossier, wie);
        }

        var onderdelen = await db.DossierOnderdelen.AsNoTracking().Include(o => o.Stappen)
            .Where(o => o.DossierId == dossierId)
            .ToListAsync();
        var keuzes = await db.PostKeuzes.AsNoTracking().Where(k => k.DossierId == dossierId)
            .ToDictionaryAsync(k => k.HoofdpostCode, k => k.Materieel, StringComparer.Ordinal);

        return new WerkprogrammaOverzicht(
            new DossierDetail(dossier.Id, dossier.Naam, dossier.Boekjaar, dossier.SoortOpdracht, dossier.Status, dossier.KlantId,
                dossier.Klant!.Klantnummer, dossier.Klant.Naam, dossier.Klant.Rechtsvorm),
            dossier.WerkprogrammaVersie!,
            [.. onderdelen.OrderBy(o => o.Fase).ThenBy(o => o.Volgorde).Select(Info)],
            keuzes);
    }

    /// <summary>
    /// Een hoofdpost materieel maken (het postprogramma uit het sjabloon komt in de uitvoering) of niet materieel.
    /// Staan er bij een post die niet meer materieel is al ingevulde stappen, dan moet <paramref name="ingevuldBewaren"/>
    /// zeggen of die blijven (als "niet van toepassing") of weg mogen: ingevulde documentatie verdwijnt nooit ongemerkt.
    /// </summary>
    public async Task<Uitkomst> KiesMaterieel(Guid dossierId, string hoofdpostCode, bool materieel, bool? ingevuldBewaren, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(hoofdpostCode);
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst.Mislukt("Dit dossier bestaat niet (meer).");
        }

        if (dossier.WerkprogrammaVersie is null)
        {
            await Start(db, dossier, wie);
        }

        var post = await db.DossierOnderdelen.Include(o => o.Stappen).SingleOrDefaultAsync(o => o.DossierId == dossierId && o.HoofdpostCode == hoofdpostCode);
        var nu = tijd.GetUtcNow();
        if (materieel)
        {
            if (post is null)
            {
                await VoegPostToe(db, dossier, hoofdpostCode, wie, nu);
            }
            else
            {
                // Eerder bewaard als niet van toepassing: de stappen gaan weer open.
                foreach (var stap in post.Stappen.Where(s => s.Status == StapStatus.NietVanToepassing && s.Reden == NietMeerMaterieel))
                {
                    stap.Status = StapStatus.Open;
                    stap.Reden = null;
                }
            }
        }
        else if (post is not null)
        {
            var ingevuld = post.Stappen.Any(IsIngevuld) || !string.IsNullOrWhiteSpace(post.Notitie);
            if (ingevuld && ingevuldBewaren is null)
            {
                return Uitkomst.Mislukt(BewarenOfVerwijderen);
            }

            if (ingevuld && ingevuldBewaren == true)
            {
                foreach (var stap in post.Stappen.Where(s => s.Status == StapStatus.Open && !s.Verwijderd))
                {
                    stap.Status = StapStatus.NietVanToepassing;
                    stap.Reden = NietMeerMaterieel;
                }
            }
            else
            {
                db.DossierOnderdelen.Remove(post);
            }
        }

        var keuze = await db.PostKeuzes.SingleOrDefaultAsync(k => k.DossierId == dossierId && k.HoofdpostCode == hoofdpostCode);
        if (keuze is null)
        {
            keuze = new DossierPostKeuze { DossierId = dossierId, HoofdpostCode = hoofdpostCode };
            db.PostKeuzes.Add(keuze);
        }

        keuze.Materieel = materieel;
        keuze.GewijzigdOp = nu;
        keuze.GewijzigdDoor = wie.Naam;
        await db.SaveChangesAsync();
        await audit.Log(wie, materieel ? AuditActies.PostMaterieel : AuditActies.PostNietMaterieel, Onderwerp(dossier), hoofdpostCode);
        return Uitkomst.Gelukt;
    }

    /// <summary>Bevestigt voor de gegeven hoofdposten zonder keuze dat ze niet materieel zijn.</summary>
    public async Task<Uitkomst<int>> BevestigNietMaterieel(Guid dossierId, IReadOnlyCollection<string> hoofdpostCodes, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(hoofdpostCodes);
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst<int>.Mislukt("Dit dossier bestaat niet (meer).");
        }

        var gekozen = await db.PostKeuzes.Where(k => k.DossierId == dossierId).Select(k => k.HoofdpostCode).ToListAsync();
        var nieuw = hoofdpostCodes.Except(gekozen, StringComparer.Ordinal).ToList();
        var nu = tijd.GetUtcNow();
        db.PostKeuzes.AddRange(nieuw.Select(code => new DossierPostKeuze { DossierId = dossierId, HoofdpostCode = code, Materieel = false, GewijzigdOp = nu, GewijzigdDoor = wie.Naam }));
        await db.SaveChangesAsync();
        if (nieuw.Count > 0)
        {
            await audit.Log(wie, AuditActies.PostNietMaterieel, Onderwerp(dossier), string.Join(", ", nieuw));
        }

        return Uitkomst<int>.Met(nieuw.Count);
    }

    public async Task<Uitkomst> BewaarAntwoord(Guid stapId, string? antwoord, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (antwoord?.Length > MaximaleTekst)
        {
            return Uitkomst.Mislukt($"Een antwoord is hoogstens {MaximaleTekst} tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await Stap(db, stapId) is not { } gevonden)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var (stap, onderdeel, dossier) = gevonden;
        var nieuw = string.IsNullOrWhiteSpace(antwoord) ? null : antwoord.Trim();
        if (nieuw == stap.Antwoord)
        {
            return Uitkomst.Gelukt;
        }

        stap.Antwoord = nieuw;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.StapBeantwoord, Onderwerp(dossier), $"{onderdeel.Titel} – {stap.Titel}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Een stap aftekenen (gereed), op niet van toepassing zetten (met reden) of weer openzetten. Een stap met een
    /// controle mag pas gereed als de controle slaagt.
    /// </summary>
    public async Task<Uitkomst> ZetStatus(Guid stapId, StapStatus status, string? reden, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await Stap(db, stapId) is not { } gevonden)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var (stap, onderdeel, dossier) = gevonden;
        if (stap.Verwijderd)
        {
            return Uitkomst.Mislukt("Deze stap is verwijderd; herstel hem eerst.");
        }

        reden = string.IsNullOrWhiteSpace(reden) ? null : reden.Trim();
        if (status == StapStatus.NietVanToepassing && reden is null)
        {
            return Uitkomst.Mislukt("Geef een reden waarom de stap niet van toepassing is.");
        }

        if (status == StapStatus.Gereed && await ControleFout(stap.Controle, dossier.Id, db) is { } fout)
        {
            return Uitkomst.Mislukt(fout);
        }

        stap.Status = status;
        stap.Reden = status == StapStatus.NietVanToepassing ? reden : null;
        stap.UitgevoerdDoor = status == StapStatus.Open ? null : wie.Naam;
        stap.UitgevoerdOp = status == StapStatus.Open ? null : tijd.GetUtcNow();
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.StapAfgetekend, Onderwerp(dossier),
            $"{onderdeel.Titel} – {stap.Titel}: {Weergave.Tekst(status)}{(reden is null ? null : $" ({reden})")}");
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst<Guid>> VoegStapToe(Guid onderdeelId, string titel, string? instructie, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        titel = titel?.Trim() ?? string.Empty;
        if (titel.Length is 0 or > 300)
        {
            return Uitkomst<Guid>.Mislukt("Geef de stap een titel van hoogstens 300 tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var onderdeel = await db.DossierOnderdelen.Include(o => o.Stappen).SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        var nu = tijd.GetUtcNow();
        var stap = new DossierStap
        {
            Id = Guid.NewGuid(),
            OnderdeelId = onderdeelId,
            Titel = titel,
            Instructie = string.IsNullOrWhiteSpace(instructie) ? null : instructie.Trim(),
            Volgorde = onderdeel.Stappen.Count == 0 ? 0 : onderdeel.Stappen.Max(s => s.Volgorde) + 1,
            AangemaaktOp = nu,
            AangemaaktDoor = wie.Naam,
        };
        db.DossierStappen.Add(stap);
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        await audit.Log(wie, AuditActies.StapToegevoegd, Onderwerp(dossier), $"{onderdeel.Titel} – {titel}");
        return Uitkomst<Guid>.Met(stap.Id);
    }

    /// <summary>
    /// Een stap verwijderen. Een verplichte stap kan niet weg (wel "niet van toepassing"). Een eigen stap die nog niet is
    /// ingevuld verdwijnt echt; anders blijft de stap bewaard als verwijderd, met wie, wanneer en de reden.
    /// </summary>
    public async Task<Uitkomst> VerwijderStap(Guid stapId, string? reden, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await Stap(db, stapId) is not { } gevonden)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var (stap, onderdeel, dossier) = gevonden;
        if (stap.Verplicht)
        {
            return Uitkomst.Mislukt("Een verplichte stap kan niet worden verwijderd. Zet hem op niet van toepassing, met een reden.");
        }

        reden = string.IsNullOrWhiteSpace(reden) ? null : reden.Trim();
        if (stap.BronStapId is null && !IsIngevuld(stap))
        {
            db.DossierStappen.Remove(stap);
        }
        else if (reden is null)
        {
            return Uitkomst.Mislukt("Geef een reden waarom de stap wordt verwijderd.");
        }
        else
        {
            stap.Verwijderd = true;
            stap.VerwijderdDoor = wie.Naam;
            stap.VerwijderdOp = tijd.GetUtcNow();
            stap.Reden = reden;
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.StapVerwijderd, Onderwerp(dossier), $"{onderdeel.Titel} – {stap.Titel}{(reden is null ? null : $" ({reden})")}");
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> HerstelStap(Guid stapId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await Stap(db, stapId) is not { } gevonden || !gevonden.Stap.Verwijderd)
        {
            return Uitkomst.Mislukt("Deze stap is niet verwijderd.");
        }

        var (stap, onderdeel, dossier) = gevonden;
        stap.Verwijderd = false;
        stap.VerwijderdDoor = null;
        stap.VerwijderdOp = null;
        stap.Reden = stap.Status == StapStatus.NietVanToepassing ? stap.Reden : null;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.StapHersteld, Onderwerp(dossier), $"{onderdeel.Titel} – {stap.Titel}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Een stap één plaats omhoog (-1) of omlaag (+1) zetten tussen de niet-verwijderde stappen.</summary>
    public async Task<Uitkomst> Verplaats(Guid stapId, int richting, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await Stap(db, stapId) is not { } gevonden)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var stappen = await db.DossierStappen.Where(s => s.OnderdeelId == gevonden.Onderdeel.Id && !s.Verwijderd).OrderBy(s => s.Volgorde).ToListAsync();
        var i = stappen.FindIndex(s => s.Id == stapId);
        var j = i + Math.Sign(richting);
        if (i < 0 || j < 0 || j >= stappen.Count)
        {
            return Uitkomst.Gelukt;
        }

        // Opnieuw nummeren, zodat gelijke volgnummers geen rol spelen.
        (stappen[i], stappen[j]) = (stappen[j], stappen[i]);
        for (var k = 0; k < stappen.Count; k++)
        {
            stappen[k].Volgorde = k;
        }

        await db.SaveChangesAsync();
        return Uitkomst.Gelukt;
    }

    /// <summary>Notitie bij een onderdeel: de cijferbeoordeling van een post of de toelichting op de keuze materiële posten.</summary>
    public async Task<Uitkomst> BewaarNotitie(Guid onderdeelId, string? notitie, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (notitie?.Length > MaximaleTekst)
        {
            return Uitkomst.Mislukt($"Een notitie is hoogstens {MaximaleTekst} tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var onderdeel = await db.DossierOnderdelen.SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        var nieuw = string.IsNullOrWhiteSpace(notitie) ? null : notitie.Trim();
        if (nieuw == onderdeel.Notitie)
        {
            return Uitkomst.Gelukt;
        }

        onderdeel.Notitie = nieuw;
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        await audit.Log(wie, AuditActies.NotitieGewijzigd, Onderwerp(dossier), onderdeel.Titel);
        return Uitkomst.Gelukt;
    }

    /// <summary>De hoofdposten (RGS niveau 2) met saldo in dit of het vorige jaar, met hun RGS-omschrijving.</summary>
    public async Task<IReadOnlyList<(string Code, string Naam)>> HoofdpostenMetSaldo(Guid dossierId)
    {
        var o = await rubriceer.Overzicht(dossierId, metVoorstellen: false);
        if (o is not { Schema: { } schema, Balans: { } balans })
        {
            return [];
        }

        return [.. schema.Hoofdposten
            .Where(p => balans.Totaal(p.Code) is { Aantal: > 0 } t && (t.Huidig != 0 || t.Vorig != 0))
            .Select(p => (p.Code, o.Naam(p.Code)))];
    }

    public const string BewarenOfVerwijderen = "Bij deze post zijn al stappen ingevuld. Kies of die bewaard blijven (als niet van toepassing) of verwijderd worden.";

    private const string NietMeerMaterieel = "De post is niet meer materieel.";

    private async Task<string?> ControleFout(StapControle controle, Guid dossierId, BoekerhoudenDbContext db)
    {
        switch (controle)
        {
            case StapControle.AllesGekoppeld:
                var o = await rubriceer.Overzicht(dossierId, metVoorstellen: false);
                return o is null || o.Gekoppeld < o.Relevant
                    ? $"Nog niet alle rekeningen met saldo zijn gekoppeld ({o?.Gekoppeld} van {o?.Relevant}). Rubriceer ze eerst."
                    : null;
            case StapControle.MaterielePostenGekozen:
                var gekozen = await db.PostKeuzes.Where(k => k.DossierId == dossierId).Select(k => k.HoofdpostCode).ToListAsync();
                var open = (await HoofdpostenMetSaldo(dossierId)).Where(p => !gekozen.Contains(p.Code)).Select(p => p.Naam).ToList();
                return open.Count > 0
                    ? $"Kies eerst voor elke hoofdpost of die materieel is. Nog open: {string.Join(", ", open)}."
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Kopieert de onderdelen van voorbereiding en afronding uit het kantoorsjabloon naar het dossier.</summary>
    private async Task Start(BoekerhoudenDbContext db, Dossier dossier, Uitvoerder wie)
    {
        var sjabloon = await Sjabloon(db, dossier.SoortOpdracht);
        var nu = tijd.GetUtcNow();
        foreach (var bron in sjabloon.Onderdelen.Where(o => o.Soort != OnderdeelSoort.Post).OrderBy(o => o.Volgorde))
        {
            db.DossierOnderdelen.Add(Kopie(bron, dossier.Id, wie, nu));
        }

        dossier.WerkprogrammaVersie = sjabloon.Versie;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.WerkprogrammaGestart, Onderwerp(dossier), $"{sjabloon.Naam} {sjabloon.Versie}");
    }

    private async Task VoegPostToe(BoekerhoudenDbContext db, Dossier dossier, string code, Uitvoerder wie, DateTimeOffset nu)
    {
        var sjabloon = await Sjabloon(db, dossier.SoortOpdracht);
        var bron = sjabloon.Onderdelen.FirstOrDefault(o => o.Soort == OnderdeelSoort.Post && o.HoofdpostCode == code);
        var naam = (await HoofdpostenMetSaldo(dossier.Id)).FirstOrDefault(p => p.Code == code).Naam;
        var onderdeel = bron is not null
            ? Kopie(bron, dossier.Id, wie, nu)
            : new DossierOnderdeel { Id = Guid.NewGuid(), DossierId = dossier.Id, Fase = Fase.Uitvoering, Soort = OnderdeelSoort.Post, HoofdpostCode = code, Titel = naam ?? code };

        // Volgorde zoals in het sjabloon; posten zonder sjabloon achteraan.
        onderdeel.Volgorde = bron?.Volgorde ?? 1000;
        db.DossierOnderdelen.Add(onderdeel);
    }

    private static DossierOnderdeel Kopie(SjabloonOnderdeel bron, Guid dossierId, Uitvoerder wie, DateTimeOffset nu)
    {
        var onderdeel = new DossierOnderdeel
        {
            Id = Guid.NewGuid(),
            DossierId = dossierId,
            BronOnderdeelId = bron.Id,
            Fase = bron.Fase,
            Soort = bron.Soort,
            Titel = bron.Titel,
            HoofdpostCode = bron.HoofdpostCode,
            Verplicht = bron.Verplicht,
            Volgorde = bron.Volgorde,
        };
        onderdeel.Stappen.AddRange(bron.Stappen.OrderBy(s => s.Volgorde).Select(s => new DossierStap
        {
            Id = Guid.NewGuid(),
            OnderdeelId = onderdeel.Id,
            BronStapId = s.Id,
            Titel = s.Titel,
            Instructie = s.Instructie,
            Verplicht = s.Verplicht,
            Controle = s.Controle,
            Volgorde = s.Volgorde,
            AangemaaktOp = nu,
            AangemaaktDoor = wie.Naam,
        }));
        return onderdeel;
    }

    private async Task<WerkprogrammaSjabloon> Sjabloon(BoekerhoudenDbContext db, SoortOpdracht soort)
    {
        var sjabloon = await db.WerkprogrammaSjablonen.AsNoTracking().Include(w => w.Onderdelen).ThenInclude(o => o.Stappen)
            .AsSplitQuery()
            .SingleOrDefaultAsync(w => w.SoortOpdracht == soort);
        if (sjabloon is null)
        {
            // Normaal maakt de opstart het standaardwerkprogramma aan; dit is een vangnet.
            sjabloon = StandaardWerkprogramma.Maak(soort, tijd.GetUtcNow());
            db.WerkprogrammaSjablonen.Add(sjabloon);
            await db.SaveChangesAsync();
        }

        return sjabloon;
    }

    private static async Task<(DossierStap Stap, DossierOnderdeel Onderdeel, Dossier Dossier)?> Stap(BoekerhoudenDbContext db, Guid stapId)
    {
        var stap = await db.DossierStappen.SingleOrDefaultAsync(s => s.Id == stapId);
        if (stap is null)
        {
            return null;
        }

        var onderdeel = await db.DossierOnderdelen.SingleAsync(o => o.Id == stap.OnderdeelId);
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        return (stap, onderdeel, dossier);
    }

    private static bool IsIngevuld(DossierStap stap) => stap.Status != StapStatus.Open || !string.IsNullOrWhiteSpace(stap.Antwoord);

    private static OnderdeelInfo Info(DossierOnderdeel o) => new(
        o.Id,
        o.Fase,
        o.Soort,
        o.Titel,
        o.HoofdpostCode,
        o.Verplicht,
        o.Notitie,
        o.GereviewdDoor,
        o.GereviewdOp,
        [.. o.Stappen.OrderBy(s => s.Volgorde).Select(s => new StapInfo(
            s.Id, s.Titel, s.Instructie, s.Verplicht, s.BronStapId is null, s.Controle, s.Status, s.Antwoord, s.Reden,
            s.UitgevoerdDoor, s.UitgevoerdOp, s.Verwijderd, s.VerwijderdDoor, s.VerwijderdOp))]);

    private static string Onderwerp(Dossier dossier) => $"{KlantDienst.Onderwerp(dossier.Klant!)} – {dossier.Naam}";
}
