using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>
/// Review van het werkprogramma (docs §10.7 en vraag C6): reviewpunten per onderdeel, en per onderdeel "gereviewd"
/// door een reviewer of accountant, als alle stappen klaar zijn en er geen open punten meer zijn.
/// </summary>
public sealed class ReviewDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public const string AlleenReviewers = "Alleen een reviewer of accountant kan een onderdeel als gereviewd aftekenen.";

    /// <summary>Een reviewpunt plaatsen. Was het onderdeel al gereviewd, dan vervalt die review.</summary>
    public async Task<Uitkomst<Guid>> PlaatsPunt(Guid onderdeelId, Guid? stapId, string tekst, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        tekst = tekst?.Trim() ?? string.Empty;
        if (tekst.Length is 0 or > 4000)
        {
            return Uitkomst<Guid>.Mislukt("Beschrijf het reviewpunt (hoogstens 4000 tekens).");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var onderdeel = await db.DossierOnderdelen.SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        if (stapId is { } id && !await db.DossierStappen.AnyAsync(s => s.Id == id && s.OnderdeelId == onderdeelId))
        {
            return Uitkomst<Guid>.Mislukt("Deze stap hoort niet bij dit onderdeel.");
        }

        var punt = new Reviewpunt
        {
            Id = Guid.NewGuid(),
            DossierId = onderdeel.DossierId,
            OnderdeelId = onderdeelId,
            StapId = stapId,
            Tekst = tekst,
            Door = wie.Naam,
            Op = tijd.GetUtcNow(),
        };
        db.Reviewpunten.Add(punt);
        var vervallen = onderdeel.GereviewdDoor is not null;
        onderdeel.GereviewdDoor = null;
        onderdeel.GereviewdOp = null;
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        await audit.Log(wie, AuditActies.ReviewpuntGeplaatst, WerkprogrammaDienst.Onderwerp(dossier),
            $"{onderdeel.Titel}: {tekst}{(vervallen ? " (review vervallen)" : null)}");
        return Uitkomst<Guid>.Met(punt.Id);
    }

    public async Task<Uitkomst> LosOp(Guid puntId, string? reactie, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (reactie?.Length > 4000)
        {
            return Uitkomst.Mislukt("Een reactie is hoogstens 4000 tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var punt = await db.Reviewpunten.SingleOrDefaultAsync(p => p.Id == puntId);
        if (punt is null)
        {
            return Uitkomst.Mislukt("Dit reviewpunt bestaat niet (meer).");
        }

        punt.Opgelost = true;
        punt.Reactie = string.IsNullOrWhiteSpace(reactie) ? null : reactie.Trim();
        punt.OpgelostDoor = wie.Naam;
        punt.OpgelostOp = tijd.GetUtcNow();
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.ReviewpuntOpgelost, punt, punt.Reactie);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> Heropen(Guid puntId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var punt = await db.Reviewpunten.SingleOrDefaultAsync(p => p.Id == puntId);
        if (punt is null)
        {
            return Uitkomst.Mislukt("Dit reviewpunt bestaat niet (meer).");
        }

        punt.Opgelost = false;
        punt.OpgelostDoor = null;
        punt.OpgelostOp = null;
        var onderdeel = await db.DossierOnderdelen.SingleAsync(o => o.Id == punt.OnderdeelId);
        onderdeel.GereviewdDoor = null;
        onderdeel.GereviewdOp = null;
        await db.SaveChangesAsync();
        await Log(db, wie, AuditActies.ReviewpuntHeropend, punt, null);
        return Uitkomst.Gelukt;
    }

    /// <summary>Een onderdeel aftekenen als gereviewd. Daarna is het vergrendeld tot de review wordt teruggezet.</summary>
    public async Task<Uitkomst> Reviewen(Guid onderdeelId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await MagReviewen(db, wie))
        {
            return Uitkomst.Mislukt(AlleenReviewers);
        }

        var onderdeel = await db.DossierOnderdelen.Include(o => o.Stappen).SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        if (onderdeel.Soort == OnderdeelSoort.Doorlezen && !await IsAccountant(db, wie))
        {
            return Uitkomst.Mislukt(AlleenAccountant);
        }

        var open = onderdeel.Stappen.Count(s => !s.Verwijderd && s.Status == StapStatus.Open);
        var punten = await db.Reviewpunten.CountAsync(p => p.OnderdeelId == onderdeelId && !p.Opgelost);
        if (onderdeel.GereviewdDoor is not null || open > 0 || punten > 0)
        {
            return Uitkomst.Mislukt(onderdeel.GereviewdDoor is not null ? "Het onderdeel is al gereviewd."
                : open > 0 ? $"Nog {open} stap(pen) open; die moeten eerst gereed of niet van toepassing zijn."
                : $"Er staan nog {punten} reviewpunt(en) open.");
        }

        onderdeel.GereviewdDoor = wie.Naam;
        onderdeel.GereviewdOp = tijd.GetUtcNow();
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        await audit.Log(wie, AuditActies.OnderdeelGereviewd, WerkprogrammaDienst.Onderwerp(dossier), onderdeel.Titel);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> ReviewTerug(Guid onderdeelId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await MagReviewen(db, wie))
        {
            return Uitkomst.Mislukt("Alleen een reviewer of accountant kan een review terugzetten.");
        }

        var onderdeel = await db.DossierOnderdelen.SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel?.GereviewdDoor is null)
        {
            return Uitkomst.Mislukt("Dit onderdeel is niet gereviewd.");
        }

        if (onderdeel.Soort == OnderdeelSoort.Doorlezen && !await IsAccountant(db, wie))
        {
            return Uitkomst.Mislukt(AlleenAccountant);
        }

        onderdeel.GereviewdDoor = null;
        onderdeel.GereviewdOp = null;
        await db.SaveChangesAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == onderdeel.DossierId);
        await audit.Log(wie, AuditActies.ReviewTeruggezet, WerkprogrammaDienst.Onderwerp(dossier), onderdeel.Titel);
        return Uitkomst.Gelukt;
    }

    public const string AlleenAccountant = "Alleen een accountant kan het doorlezen paraferen of de paraaf terugzetten.";

    private static Task<bool> IsAccountant(BoekerhoudenDbContext db, Uitvoerder wie) =>
        wie.Id is not { } id
            ? Task.FromResult(false)
            : (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               where ur.UserId == id && r.Name == Rollen.Accountant
               select ur).AnyAsync();

    /// <summary>Heeft de gebruiker de rol reviewer of accountant?</summary>
    internal static Task<bool> MagReviewen(BoekerhoudenDbContext db, Uitvoerder wie) =>
        wie.Id is not { } id
            ? Task.FromResult(false)
            : (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               where ur.UserId == id && (r.Name == Rollen.Reviewer || r.Name == Rollen.Accountant)
               select ur).AnyAsync();

    private async Task Log(BoekerhoudenDbContext db, Uitvoerder wie, string actie, Reviewpunt punt, string? reactie)
    {
        var onderdeel = await db.DossierOnderdelen.SingleAsync(o => o.Id == punt.OnderdeelId);
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == punt.DossierId);
        await audit.Log(wie, actie, WerkprogrammaDienst.Onderwerp(dossier), $"{onderdeel.Titel}: {punt.Tekst}{(reactie is null ? null : $" – {reactie}")}");
    }
}
