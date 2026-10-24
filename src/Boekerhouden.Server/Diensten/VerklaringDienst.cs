using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Identiteit;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <param name="Accountants">De actieve gebruikers met de rol accountant (om uit te kiezen), met naam en titel.</param>
/// <param name="Erkenning">De datum van de erkenning door het bestuur (uit de bespreking); de verklaring is niet eerder gedateerd.</param>
public sealed record VerklaringInfo(
    DateOnly? Datum,
    Guid? AccountantId,
    string? Toelichting,
    IReadOnlyList<(Guid Id, string Naam)> Accountants,
    DateOnly? Erkenning);

/// <summary>
/// De samenstellingsverklaring van een dossier (plakje 7d): wie tekent, de datum en een eigen toelichtende alinea.
/// De tekst zelf staat in het domein (<see cref="Samenstellingsverklaring"/>).
/// </summary>
public sealed class VerklaringDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public async Task<VerklaringInfo?> Overzicht(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Dossiers.AnyAsync(d => d.Id == dossierId))
        {
            return null;
        }

        var v = await db.Verklaringen.AsNoTracking().SingleOrDefaultAsync(x => x.DossierId == dossierId);
        return new VerklaringInfo(v?.Datum, v?.AccountantId, v?.Toelichting, await Accountants(db),
            await db.Besprekingen.Where(b => b.DossierId == dossierId).Select(b => b.ErkendOp).SingleOrDefaultAsync());
    }

    public async Task<Uitkomst> Bewaar(Guid dossierId, DateOnly? datum, Guid? accountantId, string? toelichting, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        toelichting = string.IsNullOrWhiteSpace(toelichting) ? null : toelichting.Trim();
        if (toelichting?.Length > DoorleesDienst.MaximaleTekst)
        {
            return Uitkomst.Mislukt($"De toelichting is te lang (maximaal {DoorleesDienst.MaximaleTekst} tekens).");
        }

        if (datum > DateOnly.FromDateTime(tijd.GetLocalNow().Date))
        {
            return Uitkomst.Mislukt("De datum van de verklaring ligt in de toekomst.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst.Mislukt("Dit dossier bestaat niet (meer).");
        }

        if (dossier.SoortOpdracht != SoortOpdracht.SamenstellenJaarrekening)
        {
            return Uitkomst.Mislukt("Alleen bij het samenstellen van een jaarrekening hoort een samenstellingsverklaring.");
        }

        var accountants = await Accountants(db);
        if (accountantId is { } id && accountants.All(a => a.Id != id))
        {
            return Uitkomst.Mislukt("Kies een actieve gebruiker met de rol accountant.");
        }

        var v = await db.Verklaringen.SingleOrDefaultAsync(x => x.DossierId == dossierId);
        if (v is null)
        {
            v = new DossierVerklaring { DossierId = dossierId };
            db.Verklaringen.Add(v);
        }

        (v.Datum, v.AccountantId, v.Toelichting, v.GewijzigdDoor, v.GewijzigdOp) = (datum, accountantId, toelichting, wie.Naam, tijd.GetUtcNow());
        await db.SaveChangesAsync();
        var naam = accountants.FirstOrDefault(a => a.Id == accountantId).Naam;
        await audit.Log(wie, AuditActies.VerklaringGewijzigd, WerkprogrammaDienst.Onderwerp(dossier),
            $"{(datum is { } d ? Weergave.Datum(d) : "zonder datum")}, {naam ?? "geen accountant"}{(toelichting is null ? null : ", met toelichting")}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Waarom de stap "datum verklaring" nog niet gereed kan; null = het kan.</summary>
    public async Task<string?> NietGereed(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var g = await Gegevens(db, dossierId, "", null, fiscaal: false);
        return Samenstellingsverklaring.Waarschuwingen(g).FirstOrDefault();
    }

    /// <summary>De gegevens voor de verklaring in het rapport.</summary>
    internal static async Task<VerklaringGegevens> Gegevens(BoekerhoudenDbContext db, Guid dossierId, string kantoor, string? plaats, bool fiscaal)
    {
        var v = await db.Verklaringen.AsNoTracking().SingleOrDefaultAsync(x => x.DossierId == dossierId);
        var accountant = v?.AccountantId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.Naam, u.Titel }).SingleOrDefaultAsync()
            : null;
        var erkenning = await db.Besprekingen.AsNoTracking().Where(b => b.DossierId == dossierId).Select(b => b.ErkendOp).SingleOrDefaultAsync();
        return new VerklaringGegevens(kantoor, plaats, accountant is null ? null : $"{accountant.Naam} {accountant.Titel}".Trim(), v?.Datum, fiscaal, erkenning, v?.Toelichting);
    }

    private static async Task<IReadOnlyList<(Guid Id, string Naam)>> Accountants(BoekerhoudenDbContext db) =>
        [.. (await (from ur in db.UserRoles
                    join r in db.Roles on ur.RoleId equals r.Id
                    join u in db.Users on ur.UserId equals u.Id
                    where r.Name == Rollen.Accountant && u.Actief
                    orderby u.Naam
                    select new { u.Id, u.Naam, u.Titel }).ToListAsync())
            .Select(a => (a.Id, $"{a.Naam} {a.Titel}".Trim()))];
}
