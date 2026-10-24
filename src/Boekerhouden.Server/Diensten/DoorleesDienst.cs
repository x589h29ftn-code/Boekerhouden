using Boekerhouden.Data;
using Boekerhouden.Domain.Doorlezen;
using Boekerhouden.Domain.Rapport;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>Een signaal bij het doorlezen met zijn afhandeling; null = nog open.</summary>
public sealed record LeespuntInfo(Leessignaal Signaal, LeesAfhandeling? Afhandeling, string? Toelichting, string? Door, DateTimeOffset? Op);

public sealed record BespreekpuntInfo(
    Guid Id,
    string Onderwerp,
    string? Toelichting,
    string? Signaal,
    bool Significant,
    string? Uitkomst,
    string AangemaaktDoor,
    DateTimeOffset AangemaaktOp)
{
    public bool IsBesproken => Uitkomst is not null;
}

/// <param name="Geparafeerd">Het doorlezen is door de accountant geparafeerd; de afhandeling ligt vast.</param>
public sealed record DoorleesOverzicht(IReadOnlyList<LeespuntInfo> Signalen, IReadOnlyList<BespreekpuntInfo> Bespreekpunten, bool Geparafeerd)
{
    public int Open => Signalen.Count(s => s.Afhandeling is null);
}

/// <summary>
/// Doorlezen in de afronding (plakje 7b): de signalen uit de cijfers en het rapport, elk af te handelen als in orde
/// (met een toelichting) of als bespreekpunt met de klant; plus de lijst met bespreekpunten.
/// </summary>
public sealed class DoorleesDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RubriceerDienst rubriceer, RapportDienst rapporten, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleTekst = 4000;

    public const string Geparafeerd = "Het doorlezen is geparafeerd. Zet de paraaf eerst terug om iets te wijzigen.";

    public async Task<DoorleesOverzicht?> Overzicht(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Dossiers.AnyAsync(d => d.Id == dossierId))
        {
            return null;
        }

        var afgehandeld = await db.Leespunten.AsNoTracking().Where(l => l.DossierId == dossierId).ToDictionaryAsync(l => l.Sleutel, StringComparer.Ordinal);
        var signalen = await Signalen(db, dossierId);
        var punten = await db.Bespreekpunten.AsNoTracking().Where(p => p.DossierId == dossierId)
            .OrderBy(p => p.Volgorde).ThenBy(p => p.AangemaaktOp).ToListAsync();
        return new DoorleesOverzicht(
            [.. signalen.Select(s => afgehandeld.GetValueOrDefault(s.Sleutel) is { } l
                ? new LeespuntInfo(s, l.Afhandeling, l.Toelichting, l.Door, l.Op)
                : new LeespuntInfo(s, null, null, null, null))],
            [.. punten.Select(Info)],
            await IsGeparafeerd(db, dossierId));
    }

    /// <summary>Het aantal signalen dat nog niet is afgehandeld (voor de controle bij het aftekenen).</summary>
    public async Task<int> OpenSignalen(Guid dossierId) => (await Overzicht(dossierId))?.Open ?? 0;

    /// <summary>Het signaal is bekeken en in orde; de toelichting zegt waarom.</summary>
    public async Task<Uitkomst> InOrde(Guid dossierId, string sleutel, string? toelichting, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        toelichting = toelichting?.Trim();
        if (string.IsNullOrEmpty(toelichting))
        {
            return Uitkomst.Mislukt("Geef in een paar woorden aan waarom het in orde is.");
        }

        if (toelichting.Length > MaximaleTekst)
        {
            return Uitkomst.Mislukt($"De toelichting is te lang (maximaal {MaximaleTekst} tekens).");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var (signaal, fout) = await Controleer(db, dossierId, sleutel);
        if (signaal is null)
        {
            return Uitkomst.Mislukt(fout!);
        }

        if (await Bespreekpunt(db, dossierId, sleutel) is { } punt)
        {
            if (punt.Uitkomst is not null)
            {
                return Uitkomst.Mislukt("Dit signaal is al met de klant besproken; de uitkomst staat bij het bespreekpunt.");
            }

            db.Bespreekpunten.Remove(punt);
        }

        Zet(db, await db.Leespunten.SingleOrDefaultAsync(l => l.DossierId == dossierId && l.Sleutel == sleutel), dossierId, signaal, LeesAfhandeling.InOrde, toelichting, wie);
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.SignaalInOrde, dossierId, $"{signaal.Titel}: {toelichting}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Het signaal gaat mee naar de bespreking met de klant: er komt een bespreekpunt bij.</summary>
    public async Task<Uitkomst> NaarBespreking(Guid dossierId, string sleutel, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var (signaal, fout) = await Controleer(db, dossierId, sleutel);
        if (signaal is null)
        {
            return Uitkomst.Mislukt(fout!);
        }

        Zet(db, await db.Leespunten.SingleOrDefaultAsync(l => l.DossierId == dossierId && l.Sleutel == sleutel), dossierId, signaal, LeesAfhandeling.Bespreekpunt, null, wie);
        if (await Bespreekpunt(db, dossierId, sleutel) is null)
        {
            db.Bespreekpunten.Add(Nieuw(dossierId, signaal.Titel, signaal.Tekst, sleutel, await Volgende(db, dossierId), wie));
        }

        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.BespreekpuntToegevoegd, dossierId, signaal.Titel);
        return Uitkomst.Gelukt;
    }

    /// <summary>De afhandeling ongedaan maken; een nog niet besproken bespreekpunt uit het signaal vervalt ook.</summary>
    public async Task<Uitkomst> Heropen(Guid dossierId, string sleutel, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await IsGeparafeerd(db, dossierId))
        {
            return Uitkomst.Mislukt(Geparafeerd);
        }

        var leespunt = await db.Leespunten.SingleOrDefaultAsync(l => l.DossierId == dossierId && l.Sleutel == sleutel);
        if (leespunt is null)
        {
            return Uitkomst.Gelukt;
        }

        if (await Bespreekpunt(db, dossierId, sleutel) is { } punt)
        {
            if (punt.Uitkomst is not null)
            {
                return Uitkomst.Mislukt("Dit signaal is al met de klant besproken; de uitkomst staat bij het bespreekpunt.");
            }

            db.Bespreekpunten.Remove(punt);
        }

        db.Leespunten.Remove(leespunt);
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.SignaalHeropend, dossierId, leespunt.Titel);
        return Uitkomst.Gelukt;
    }

    /// <summary>Een eigen bespreekpunt, bijv. een vraag die bij het uitwerken opkwam.</summary>
    public async Task<Uitkomst<Guid>> VoegBespreekpuntToe(Guid dossierId, string? onderwerp, string? toelichting, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        onderwerp = onderwerp?.Trim();
        toelichting = string.IsNullOrWhiteSpace(toelichting) ? null : toelichting.Trim();
        if (string.IsNullOrEmpty(onderwerp))
        {
            return Uitkomst<Guid>.Mislukt("Vul het onderwerp in.");
        }

        if (onderwerp.Length > 300 || toelichting?.Length > MaximaleTekst)
        {
            return Uitkomst<Guid>.Mislukt("Het onderwerp (300) of de toelichting (4000 tekens) is te lang.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Dossiers.AnyAsync(d => d.Id == dossierId))
        {
            return Uitkomst<Guid>.Mislukt("Dit dossier bestaat niet (meer).");
        }

        var punt = Nieuw(dossierId, onderwerp, toelichting, null, await Volgende(db, dossierId), wie);
        db.Bespreekpunten.Add(punt);
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.BespreekpuntToegevoegd, dossierId, onderwerp);
        return Uitkomst<Guid>.Met(punt.Id);
    }

    /// <summary>Een bespreekpunt weghalen; komt het uit een signaal, dan staat dat signaal weer open.</summary>
    public async Task<Uitkomst> VerwijderBespreekpunt(Guid id, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var punt = await db.Bespreekpunten.SingleOrDefaultAsync(p => p.Id == id);
        if (punt is null)
        {
            return Uitkomst.Mislukt("Dit bespreekpunt bestaat niet (meer).");
        }

        if (punt.Uitkomst is not null)
        {
            return Uitkomst.Mislukt("Dit punt is al besproken; haal eerst de uitkomst weg.");
        }

        if (punt.Signaal is not null)
        {
            if (await IsGeparafeerd(db, punt.DossierId))
            {
                return Uitkomst.Mislukt(Geparafeerd);
            }

            await db.Leespunten.Where(l => l.DossierId == punt.DossierId && l.Sleutel == punt.Signaal).ExecuteDeleteAsync();
        }

        db.Bespreekpunten.Remove(punt);
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.BespreekpuntVerwijderd, punt.DossierId, punt.Onderwerp);
        return Uitkomst.Gelukt;
    }

    /// <summary>De signalen zoals ze nu uit de cijfers en het rapport volgen.</summary>
    private async Task<IReadOnlyList<Leessignaal>> Signalen(BoekerhoudenDbContext db, Guid dossierId)
    {
        var overzicht = await rubriceer.Overzicht(dossierId, metVoorstellen: false);
        if (overzicht is not { Schema: { } schema, Balans: { } balans })
        {
            return [];
        }

        var rapport = (await rapporten.Maak(dossierId, metVerklaring: false)) is { IsGelukt: true } r ? r.Waarde!.Rapport : null;
        var grondslagen = rapport?.Hoofdstukken.SelectMany(h => h.Blokken).OfType<GrondslagBlok>().ToList();
        var toegelicht = await db.DossierOnderdelen.AsNoTracking()
            .Where(o => o.DossierId == dossierId && o.Soort == OnderdeelSoort.Post && o.HoofdpostCode != null && o.Notitie != null && o.Notitie != "")
            .Select(o => o.HoofdpostCode!).ToListAsync();
        return Leessignalen.Bepaal(new LeesGegevens(
            RapportDienst.Soort(overzicht.Dossier.Soort),
            schema,
            balans,
            overzicht.Naam,
            grondslagen is { Count: > 0 } ? grondslagen : null,
            rapport?.Waarschuwingen ?? [],
            toegelicht.ToHashSet(StringComparer.Ordinal)));
    }

    private async Task<(Leessignaal? Signaal, string? Fout)> Controleer(BoekerhoudenDbContext db, Guid dossierId, string sleutel)
    {
        if (!await db.Dossiers.AnyAsync(d => d.Id == dossierId))
        {
            return (null, "Dit dossier bestaat niet (meer).");
        }

        if (await IsGeparafeerd(db, dossierId))
        {
            return (null, Geparafeerd);
        }

        var signaal = (await Signalen(db, dossierId)).FirstOrDefault(s => s.Sleutel == sleutel);
        return signaal is null ? (null, "Dit signaal is er niet meer; de cijfers of het rapport zijn intussen gewijzigd.") : (signaal, null);
    }

    private void Zet(BoekerhoudenDbContext db, DossierLeespunt? leespunt, Guid dossierId, Leessignaal signaal, LeesAfhandeling afhandeling, string? toelichting, Uitvoerder wie)
    {
        if (leespunt is null)
        {
            leespunt = new DossierLeespunt { DossierId = dossierId, Sleutel = signaal.Sleutel };
            db.Leespunten.Add(leespunt);
        }

        leespunt.Titel = signaal.Titel.Length <= 300 ? signaal.Titel : signaal.Titel[..300];
        leespunt.Afhandeling = afhandeling;
        leespunt.Toelichting = toelichting;
        leespunt.Door = wie.Naam;
        leespunt.Op = tijd.GetUtcNow();
    }

    private Bespreekpunt Nieuw(Guid dossierId, string onderwerp, string? toelichting, string? signaal, int volgorde, Uitvoerder wie) => new()
    {
        Id = Guid.NewGuid(),
        DossierId = dossierId,
        Onderwerp = onderwerp.Length <= 300 ? onderwerp : onderwerp[..300],
        Toelichting = toelichting,
        Signaal = signaal,
        Volgorde = volgorde,
        AangemaaktDoor = wie.Naam,
        AangemaaktOp = tijd.GetUtcNow(),
    };

    private static Task<Bespreekpunt?> Bespreekpunt(BoekerhoudenDbContext db, Guid dossierId, string sleutel) =>
        db.Bespreekpunten.SingleOrDefaultAsync(p => p.DossierId == dossierId && p.Signaal == sleutel);

    private static async Task<int> Volgende(BoekerhoudenDbContext db, Guid dossierId) =>
        (await db.Bespreekpunten.Where(p => p.DossierId == dossierId).MaxAsync(p => (int?)p.Volgorde) ?? -1) + 1;

    private static Task<bool> IsGeparafeerd(BoekerhoudenDbContext db, Guid dossierId) =>
        db.DossierOnderdelen.AnyAsync(o => o.DossierId == dossierId && o.Soort == OnderdeelSoort.Doorlezen && o.GereviewdDoor != null);

    private static BespreekpuntInfo Info(Bespreekpunt p) =>
        new(p.Id, p.Onderwerp, p.Toelichting, p.Signaal, p.Significant, p.Uitkomst, p.AangemaaktDoor, p.AangemaaktOp);

    private async Task Log(BoekerhoudenDbContext db, Uitvoerder wie, string actie, Guid dossierId, string details)
    {
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleAsync(d => d.Id == dossierId);
        await audit.Log(wie, actie, WerkprogrammaDienst.Onderwerp(dossier), details);
    }
}
