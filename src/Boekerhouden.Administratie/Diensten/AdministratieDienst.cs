using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

public sealed record AdministratieInvoer(string Naam, Ondernemingsvorm Ondernemingsvorm, string? KvkNummer, string? BtwNummer);

/// <summary>Een administratie zoals de gebruiker hem ziet, met zijn eigen rol erin.</summary>
/// <param name="Gearchiveerd">Gearchiveerd: alleen inzien (<see cref="Mag"/> geeft dan alleen Lezen).</param>
public sealed record AdministratieKop(Guid Id, string Naam, Ondernemingsvorm Ondernemingsvorm, string? KvkNummer, string? BtwNummer, Lidmaatschapsrol Rol,
    DateTimeOffset? Gearchiveerd = null)
{
    public bool Mag(Recht recht) => Toegang.Mag(Rol, recht) && (Gearchiveerd is null || recht == Recht.Lezen);
}

/// <summary>Wat er bij het verwijderen van een administratie bekend moet zijn.</summary>
/// <param name="Bewaarplicht">De datum tot wanneer de laatste boeking bewaard moet worden (7 jaar na het eind van het boekjaar), of leeg zonder boekingen.</param>
public sealed record Verwijdervoorwaarden(string Naam, bool Gearchiveerd, int Boekingen, DateOnly? Bewaarplicht);

/// <summary>Administraties aanmaken en de administraties van een gebruiker.</summary>
public sealed class AdministratieDienst(IDbContextFactory<AdministratieDbContext> dbFactory, Toegang toegang, AuditDienst audit, TimeProvider tijd, KantoorDienst kantoor)
{
    /// <summary>De administraties waarvan de gebruiker lid is; gearchiveerde alleen als daarom gevraagd wordt.</summary>
    public async Task<IReadOnlyList<AdministratieKop>> Lijst(Uitvoerder wie, string? zoek = null, bool ookGearchiveerd = false)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = from l in db.Lidmaatschappen
                    join a in db.Administraties on l.AdministratieId equals a.Id
                    where l.GebruikerId == wie.Id && (ookGearchiveerd || a.GearchiveerdOp == null)
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

    /// <summary>
    /// Archiveert een administratie (einde van de klantrelatie): daarna alleen nog inzien, en niet meer in de gewone
    /// lijst. Voor het kantoor (rol Kantoor in de administratie).
    /// </summary>
    public async Task<Uitkomst> Archiveer(Uitvoerder wie, Guid id)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (await toegang.Rol(wie, id) is not Lidmaatschapsrol.Kantoor)
        {
            throw new GeenToegangException();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var a = await db.Administraties.SingleAsync(x => x.Id == id);
        if (a.GearchiveerdOp is not null)
        {
            return Uitkomst.Mislukt("Deze administratie is al gearchiveerd.");
        }

        (a.GearchiveerdOp, a.GearchiveerdDoor) = (tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.AdministratieGearchiveerd, a.Naam, null, id);
        return Uitkomst.Gelukt;
    }

    /// <summary>Haalt een gearchiveerde administratie terug: weer gewoon in gebruik.</summary>
    public async Task<Uitkomst> Herstel(Uitvoerder wie, Guid id)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (await toegang.Rol(wie, id) is not Lidmaatschapsrol.Kantoor)
        {
            throw new GeenToegangException();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var a = await db.Administraties.SingleAsync(x => x.Id == id);
        if (a.GearchiveerdOp is null)
        {
            return Uitkomst.Mislukt("Deze administratie is niet gearchiveerd.");
        }

        (a.GearchiveerdOp, a.GearchiveerdDoor) = (null, null);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.AdministratieHersteld, a.Naam, null, id);
        return Uitkomst.Gelukt;
    }

    /// <summary>Wat het scherm moet weten voordat een administratie verwijderd wordt (alleen de beheerder).</summary>
    public async Task<Verwijdervoorwaarden> Voorwaarden(Uitvoerder wie, Guid id)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (await toegang.Rol(wie, id) is null || !await kantoor.IsBeheerder(wie))
        {
            throw new GeenToegangException();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Geopend = id;
        var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == id);
        var aantal = await db.Boekingen.CountAsync();
        var laatste = await db.Boekingen.MaxAsync(b => (DateOnly?)b.Datum);
        return new Verwijdervoorwaarden(a.Naam, a.GearchiveerdOp is not null, aantal, laatste is { } d ? new DateOnly(d.Year + 7, 12, 31) : null);
    }

    /// <summary>
    /// Verwijdert een administratie met alles erin (boekingen, documenten, relaties, instellingen), alleen door de
    /// beheerder. Een administratie met boekingen moet eerst gearchiveerd zijn; de naam moet ter bevestiging getypt
    /// worden, en loopt de fiscale bewaarplicht (7 jaar) nog, dan moet dat apart bevestigd zijn. Het wijzigingslog blijft
    /// bewaard, met de verwijdering erin.
    /// </summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid id, string bevestigNaam, bool bewaarplichtGezien)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var voorwaarden = await Voorwaarden(wie, id);
        if (!string.Equals(bevestigNaam?.Trim(), voorwaarden.Naam, StringComparison.Ordinal))
        {
            return Uitkomst.Mislukt("Typ de naam van de administratie precies over om te bevestigen.");
        }

        if (voorwaarden.Boekingen > 0 && !voorwaarden.Gearchiveerd)
        {
            return Uitkomst.Mislukt("Archiveer de administratie eerst: een administratie met boekingen gaat eerst naar het archief.");
        }

        if (voorwaarden.Bewaarplicht is { } tot && tot >= DateOnly.FromDateTime(tijd.GetUtcNow().UtcDateTime) && !bewaarplichtGezien)
        {
            return Uitkomst.Mislukt($"De fiscale bewaarplicht loopt nog tot {Weergave.Datum(tot)}: bevestig dat je de administratie toch wilt verwijderen.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        db.Geopend = id;
        await using var transactie = await db.Database.BeginTransactionAsync();
        db.VerwijderdeAdministraties.Add(new VerwijderdeAdministratie { AdministratieId = id, Naam = voorwaarden.Naam, VerwijderdOp = tijd.GetUtcNow(), VerwijderdDoor = wie.Naam });
        await db.SaveChangesAsync();
        foreach (var type in Verwijdervolgorde(db))
        {
            await (Task<int>)typeof(AdministratieDienst).GetMethod(nameof(VerwijderAlles), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type).Invoke(null, [db, id])!;
        }

        await db.Lidmaatschappen.Where(l => l.AdministratieId == id).ExecuteDeleteAsync();
        await db.Administraties.Where(a => a.Id == id).ExecuteDeleteAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.AdministratieVerwijderd, voorwaarden.Naam, $"{voorwaarden.Boekingen} boekingen", id);
        return Uitkomst.Gelukt;
    }

    private static Task<int> VerwijderAlles<T>(AdministratieDbContext db, Guid id)
        where T : class, IVanAdministratie => db.Set<T>().Where(e => e.AdministratieId == id).ExecuteDeleteAsync();

    /// <summary>
    /// De soorten gegevens van een administratie in de volgorde waarin ze weg kunnen: wat naar iets anders verwijst eerst
    /// (bijv. boekingsregels voor boekingen, banktransacties voor afschriften).
    /// </summary>
    private static List<Type> Verwijdervolgorde(AdministratieDbContext db)
    {
        var soorten = db.Model.GetEntityTypes().Where(t => typeof(IVanAdministratie).IsAssignableFrom(t.ClrType)).ToList();
        var volgorde = new List<Type>();
        var gedaan = new HashSet<Type>();
        void Bezoek(Microsoft.EntityFrameworkCore.Metadata.IEntityType t, HashSet<Type> bezig)
        {
            if (gedaan.Contains(t.ClrType) || !bezig.Add(t.ClrType))
            {
                return;
            }

            // Eerst alles wat naar deze soort verwijst.
            foreach (var afhankelijk in soorten.Where(s => s != t && s.GetForeignKeys().Any(k => k.PrincipalEntityType == t)))
            {
                Bezoek(afhankelijk, bezig);
            }

            gedaan.Add(t.ClrType);
            volgorde.Add(t.ClrType);
        }

        foreach (var soort in soorten)
        {
            Bezoek(soort, []);
        }

        return volgorde;
    }

    private static AdministratieKop Kop(Data.Administratie a, Lidmaatschapsrol rol) =>
        new(a.Id, a.Naam, a.Ondernemingsvorm, a.KvkNummer, a.BtwNummer, rol, a.GearchiveerdOp);

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
}

public sealed record Lid(Guid GebruikerId, string Naam, Lidmaatschapsrol Rol);

public sealed record AdministratieMetLeden(Guid Id, string Naam, IReadOnlyList<Lid> Leden);

/// <summary>Een geopende administratie en wie hem geopend heeft (voor de schermen).</summary>
public sealed record Geopend(AdministratieKop Kop, Uitvoerder Wie);
