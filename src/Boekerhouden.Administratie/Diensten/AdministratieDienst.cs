using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

public sealed record AdministratieInvoer(string Naam, Ondernemingsvorm Ondernemingsvorm, string? KvkNummer, string? BtwNummer);

/// <summary>Een administratie zoals de gebruiker hem ziet, met zijn eigen rol erin.</summary>
public sealed record AdministratieKop(Guid Id, string Naam, Ondernemingsvorm Ondernemingsvorm, string? KvkNummer, string? BtwNummer, Lidmaatschapsrol Rol)
{
    public bool Mag(Recht recht) => Toegang.Mag(Rol, recht);
}

/// <summary>Administraties aanmaken en de administraties van een gebruiker.</summary>
public sealed class AdministratieDienst(IDbContextFactory<AdministratieDbContext> dbFactory, Toegang toegang, AuditDienst audit, TimeProvider tijd)
{
    /// <summary>De administraties waarvan de gebruiker lid is.</summary>
    public async Task<IReadOnlyList<AdministratieKop>> Lijst(Uitvoerder wie, string? zoek = null)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = from l in db.Lidmaatschappen
                    join a in db.Administraties on l.AdministratieId equals a.Id
                    where l.GebruikerId == wie.Id
                    select new { a, l.Rol };
        if (!string.IsNullOrWhiteSpace(zoek))
        {
            var patroon = $"%{zoek.Trim()}%";
            query = db.Database.IsNpgsql() ? query.Where(x => EF.Functions.ILike(x.a.Naam, patroon)) : query.Where(x => EF.Functions.Like(x.a.Naam, patroon));
        }

        var lijst = await query.OrderBy(x => x.a.Naam).ToListAsync();
        return [.. lijst.Select(x => Kop(x.a, x.Rol))];
    }

    /// <summary>De administratie met de rol van de gebruiker, of leeg als die niet bestaat of hij er geen lid van is.</summary>
    public async Task<AdministratieKop?> Open(Uitvoerder wie, Guid id)
    {
        if (await toegang.Rol(wie, id) is not { } rol)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var administratie = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == id);
        return Kop(administratie, rol);
    }

    /// <summary>
    /// Maakt een administratie aan met het standaardrekeningschema en de standaarddagboeken. De maker wordt lid met
    /// de rol Kantoor.
    /// </summary>
    public async Task<Uitkomst<Guid>> Maak(AdministratieInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Vul de naam in.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.KvkNummer) && !(invoer.KvkNummer.Trim().Length == 8 && invoer.KvkNummer.Trim().All(char.IsAsciiDigit)))
        {
            fouten.Add("Een KvK-nummer heeft 8 cijfers.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.BtwNummer) && !BtwNummer.IsGeldig(invoer.BtwNummer))
        {
            fouten.Add("Het btw-nummer is niet geldig (bijv. NL123456789B01).");
        }

        if (wie.Id is null)
        {
            fouten.Add("Log eerst in.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        var id = Guid.NewGuid();
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Geopend = id;
        db.Administraties.Add(new Data.Administratie
        {
            Id = id,
            Naam = invoer.Naam.Trim(),
            Ondernemingsvorm = invoer.Ondernemingsvorm,
            KvkNummer = Leeg(invoer.KvkNummer),
            BtwNummer = string.IsNullOrWhiteSpace(invoer.BtwNummer) ? null : BtwNummer.Normaliseer(invoer.BtwNummer),
            AangemaaktOp = tijd.GetUtcNow(),
            AangemaaktDoor = wie.Naam,
        });
        db.Lidmaatschappen.Add(new Lidmaatschap { AdministratieId = id, GebruikerId = wie.Id!.Value, Rol = Lidmaatschapsrol.Kantoor });
        db.Rekeningen.AddRange(StandaardRekeningschema.Rekeningen(invoer.Ondernemingsvorm).Select(r => new Grootboekrekening
        {
            Id = Guid.NewGuid(),
            AdministratieId = id,
            Nummer = r.Nummer,
            Naam = r.Naam,
            Soort = r.Soort,
            RgsCode = r.RgsCode,
            Functie = r.Functie,
        }));
        db.Dagboeken.AddRange(StandaardRekeningschema.Dagboeken.Select(d => new Dagboekdefinitie
        {
            Id = Guid.NewGuid(),
            AdministratieId = id,
            Code = d.Code,
            Naam = d.Naam,
            Soort = d.Soort,
            Rekeningnummer = d.Rekeningnummer,
        }));
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.AdministratieAangemaakt, invoer.Naam.Trim(), invoer.Ondernemingsvorm.ToString(), id);
        return Uitkomst<Guid>.Met(id);
    }

    /// <summary>Alle administraties met hun leden, voor de beheerder: alleen namen, geen cijfers.</summary>
    public async Task<IReadOnlyList<AdministratieMetLeden>> AlleMetLeden()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var administraties = await db.Administraties.AsNoTracking().OrderBy(a => a.Naam).ToListAsync();
        var leden = await (from l in db.Lidmaatschappen
                           join g in db.Users on l.GebruikerId equals g.Id
                           select new { l.AdministratieId, g.Id, g.Naam, l.Rol }).ToListAsync();
        return [.. administraties.Select(a => new AdministratieMetLeden(a.Id, a.Naam,
            [.. leden.Where(l => l.AdministratieId == a.Id).OrderBy(l => l.Naam).Select(l => new Lid(l.Id, l.Naam, l.Rol))]))];
    }

    /// <summary>Maakt een gebruiker lid van een administratie (of wijzigt zijn rol); alleen voor de beheerder.</summary>
    public async Task<Uitkomst> ZetLid(Guid administratie, Guid gebruiker, Lidmaatschapsrol rol, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Administraties.AnyAsync(a => a.Id == administratie) || await db.Users.FindAsync(gebruiker) is not { } lid)
        {
            return Uitkomst.Mislukt("De administratie of de gebruiker bestaat niet (meer).");
        }

        var bestaand = await db.Lidmaatschappen.FindAsync(administratie, gebruiker);
        if (bestaand is null)
        {
            db.Lidmaatschappen.Add(new Lidmaatschap { AdministratieId = administratie, GebruikerId = gebruiker, Rol = rol });
        }
        else
        {
            bestaand.Rol = rol;
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.LidToegevoegd, lid.Email, rol.ToString(), administratie);
        return Uitkomst.Gelukt;
    }

    private static AdministratieKop Kop(Data.Administratie a, Lidmaatschapsrol rol) =>
        new(a.Id, a.Naam, a.Ondernemingsvorm, a.KvkNummer, a.BtwNummer, rol);

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
}

public sealed record Lid(Guid GebruikerId, string Naam, Lidmaatschapsrol Rol);

public sealed record AdministratieMetLeden(Guid Id, string Naam, IReadOnlyList<Lid> Leden);

/// <summary>Een geopende administratie en wie hem geopend heeft (voor de schermen).</summary>
public sealed record Geopend(AdministratieKop Kop, Uitvoerder Wie);
