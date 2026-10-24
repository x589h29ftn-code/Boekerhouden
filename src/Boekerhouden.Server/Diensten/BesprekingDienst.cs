using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record BesprekingInfo(
    DateOnly? Datum,
    string? Aanwezigen,
    string? Notitie,
    string? ErkendDoor,
    DateOnly? ErkendOp,
    Erkenningswijze? Wijze,
    IReadOnlyList<BespreekpuntInfo> Punten,
    bool Vergrendeld)
{
    public int Open => Punten.Count(p => !p.IsBesproken);

    public bool IsErkend => ErkendDoor is not null && ErkendOp is not null;
}

/// <summary>Alles voor het bespreekverslag (PDF).</summary>
public sealed record Bespreekverslag(DossierDetail Dossier, Kantoorinstellingen Kantoor, BesprekingInfo Bespreking, string Door, DateTimeOffset Op);

/// <summary>
/// De bespreking met de klant (plakje 7c): datum en aanwezigen, de uitkomst per bespreekpunt (het bespreekverslag) en
/// de erkenning van de verantwoordelijkheid door het bestuur of de ondernemer.
/// </summary>
public sealed class BesprekingDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, KantoorDienst kantoor, AuditDienst audit, TimeProvider tijd)
{
    public const string Vergrendeld = "De bespreking is gereviewd. Zet de review eerst terug om iets te wijzigen.";

    public async Task<BesprekingInfo?> Overzicht(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Dossiers.AnyAsync(d => d.Id == dossierId))
        {
            return null;
        }

        var b = await db.Besprekingen.AsNoTracking().SingleOrDefaultAsync(x => x.DossierId == dossierId);
        var punten = await db.Bespreekpunten.AsNoTracking().Where(p => p.DossierId == dossierId)
            .OrderBy(p => p.Volgorde).ThenBy(p => p.AangemaaktOp).ToListAsync();
        return new BesprekingInfo(b?.Datum, b?.Aanwezigen, b?.Notitie, b?.ErkendDoor, b?.ErkendOp, b?.Wijze,
            [.. punten.Select(p => new BespreekpuntInfo(p.Id, p.Onderwerp, p.Toelichting, p.Signaal, p.Significant, p.Uitkomst, p.AangemaaktDoor, p.AangemaaktOp))],
            await IsVergrendeld(db, dossierId));
    }

    public async Task<Bespreekverslag?> Verslag(Guid dossierId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null || await Overzicht(dossierId) is not { } bespreking)
        {
            return null;
        }

        return new Bespreekverslag(
            new DossierDetail(dossier.Id, dossier.Naam, dossier.Boekjaar, dossier.SoortOpdracht, dossier.Status, dossier.KlantId,
                dossier.Klant!.Klantnummer, dossier.Klant.Naam, dossier.Klant.Rechtsvorm),
            await kantoor.Haal(), bespreking, wie.Naam, tijd.GetUtcNow());
    }

    public async Task<Uitkomst> BewaarBespreking(Guid dossierId, DateOnly? datum, string? aanwezigen, string? notitie, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        (aanwezigen, notitie) = (Leeg(aanwezigen), Leeg(notitie));
        if (aanwezigen?.Length > 500 || notitie?.Length > DoorleesDienst.MaximaleTekst)
        {
            return Uitkomst.Mislukt("De aanwezigen (500) of de notitie (4000 tekens) is te lang.");
        }

        if (datum > Vandaag())
        {
            return Uitkomst.Mislukt("De datum van de bespreking ligt in de toekomst.");
        }

        return await Wijzig(dossierId, wie, AuditActies.BesprekingVastgelegd, b =>
        {
            (b.Datum, b.Aanwezigen, b.Notitie) = (datum, aanwezigen, notitie);
            return datum is { } d ? $"Bespreking {Weergave.Datum(d)}" : "Bespreking zonder datum";
        });
    }

    public async Task<Uitkomst> BewaarErkenning(Guid dossierId, string? door, DateOnly? op, Erkenningswijze? wijze, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        door = Leeg(door);
        if (door?.Length > 200)
        {
            return Uitkomst.Mislukt("De naam is te lang (maximaal 200 tekens).");
        }

        if (op > Vandaag())
        {
            return Uitkomst.Mislukt("De datum van de erkenning ligt in de toekomst.");
        }

        return await Wijzig(dossierId, wie, AuditActies.ErkenningVastgelegd, b =>
        {
            (b.ErkendDoor, b.ErkendOp, b.Wijze) = (door, op, wijze);
            return $"{door ?? "(niemand)"}{(op is { } d ? $", {Weergave.Datum(d)}" : null)}{(wijze is { } w ? $", {Weergave.Tekst(w)}" : null)}";
        });
    }

    /// <summary>De uitkomst van een bespreekpunt; leeg = (nog) niet besproken.</summary>
    public async Task<Uitkomst> BewaarUitkomst(Guid puntId, string? uitkomst, bool significant, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        uitkomst = Leeg(uitkomst);
        if (uitkomst?.Length > DoorleesDienst.MaximaleTekst)
        {
            return Uitkomst.Mislukt($"De uitkomst is te lang (maximaal {DoorleesDienst.MaximaleTekst} tekens).");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var punt = await db.Bespreekpunten.SingleOrDefaultAsync(p => p.Id == puntId);
        if (punt is null)
        {
            return Uitkomst.Mislukt("Dit bespreekpunt bestaat niet (meer).");
        }

        if (await IsVergrendeld(db, punt.DossierId))
        {
            return Uitkomst.Mislukt(Vergrendeld);
        }

        (punt.Uitkomst, punt.Significant, punt.GewijzigdDoor, punt.GewijzigdOp) = (uitkomst, significant, wie.Naam, tijd.GetUtcNow());
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleAsync(d => d.Id == punt.DossierId);
        await audit.Log(wie, AuditActies.UitkomstVastgelegd, WerkprogrammaDienst.Onderwerp(dossier),
            $"{punt.Onderwerp}: {uitkomst ?? "(niet besproken)"}{(significant ? " (significant)" : null)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Waarom de stap "bespreekpunten besproken" nog niet gereed kan; null = het kan.</summary>
    public async Task<string?> NietBesproken(Guid dossierId)
    {
        var b = await Overzicht(dossierId);
        return b is null ? "Dit dossier bestaat niet (meer)."
            : b.Datum is null ? "Vul eerst de datum van de bespreking in."
            : b.Open > 0 ? $"Nog {b.Open} bespreekpunt(en) zonder uitkomst."
            : null;
    }

    /// <summary>Waarom de stap "erkenning" nog niet gereed kan; null = het kan.</summary>
    public async Task<string?> NietErkend(Guid dossierId) =>
        await Overzicht(dossierId) is { IsErkend: true } ? null : "Leg eerst vast wie de verantwoordelijkheid heeft erkend en op welke datum.";

    private async Task<Uitkomst> Wijzig(Guid dossierId, Uitvoerder wie, string actie, Func<Bespreking, string> wijzig)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst.Mislukt("Dit dossier bestaat niet (meer).");
        }

        if (await IsVergrendeld(db, dossierId))
        {
            return Uitkomst.Mislukt(Vergrendeld);
        }

        var b = await db.Besprekingen.SingleOrDefaultAsync(x => x.DossierId == dossierId);
        if (b is null)
        {
            b = new Bespreking { DossierId = dossierId };
            db.Besprekingen.Add(b);
        }

        var details = wijzig(b);
        (b.GewijzigdDoor, b.GewijzigdOp) = (wie.Naam, tijd.GetUtcNow());
        await db.SaveChangesAsync();
        await audit.Log(wie, actie, WerkprogrammaDienst.Onderwerp(dossier), details);
        return Uitkomst.Gelukt;
    }

    private DateOnly Vandaag() => DateOnly.FromDateTime(tijd.GetLocalNow().Date);

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    private static Task<bool> IsVergrendeld(BoekerhoudenDbContext db, Guid dossierId) =>
        db.DossierOnderdelen.AnyAsync(o => o.DossierId == dossierId && o.Soort == OnderdeelSoort.Bespreking && o.GereviewdDoor != null);
}
