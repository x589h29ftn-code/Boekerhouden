using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record KlantInvoer(string Klantnummer, string Naam, Rechtsvorm Rechtsvorm, string? KvkNummer, string? Plaats);

public sealed record DossierSamenvatting(Guid Id, string Naam, int Boekjaar, SoortOpdracht Soort, DossierStatus Status);

public sealed record KlantRegel(
    Guid Id,
    string Klantnummer,
    string Naam,
    Rechtsvorm Rechtsvorm,
    string? Plaats,
    DossierSamenvatting? LaatsteDossier,
    DateTimeOffset? LaatsteImport,
    DateTimeOffset GewijzigdOp,
    string GewijzigdDoor);

public sealed class KlantDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public async Task<IReadOnlyList<KlantRegel>> Lijst(string? zoek = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Klanten.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zoek))
        {
            var patroon = $"%{zoek.Trim()}%";
            query = query.Where(k => EF.Functions.ILike(k.Naam, patroon) || EF.Functions.ILike(k.Klantnummer, patroon));
        }

        return await query
            .OrderBy(k => k.Naam)
            .Select(k => new KlantRegel(
                k.Id,
                k.Klantnummer,
                k.Naam,
                k.Rechtsvorm,
                k.Plaats,
                k.Dossiers
                    .OrderByDescending(d => d.Boekjaar)
                    .Select(d => new DossierSamenvatting(d.Id, d.Naam, d.Boekjaar, d.SoortOpdracht, d.Status))
                    .FirstOrDefault(),
                db.ImportBatches.Where(b => b.KlantId == k.Id).Max(b => (DateTimeOffset?)b.GeimporteerdOp),
                k.GewijzigdOp,
                k.GewijzigdDoor))
            .ToListAsync();
    }

    public async Task<Klant?> Haal(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Klanten.AsNoTracking().SingleOrDefaultAsync(k => k.Id == id);
    }

    public async Task<Uitkomst<Guid>> Maak(KlantInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = Valideer(invoer);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (fouten.Count == 0 && await db.Klanten.AnyAsync(k => k.Klantnummer == invoer.Klantnummer.Trim()))
        {
            fouten.Add($"Klantnummer {invoer.Klantnummer.Trim()} bestaat al.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        var nu = tijd.GetUtcNow();
        var klant = new Klant { Id = Guid.NewGuid(), AangemaaktOp = nu };
        Vul(klant, invoer, wie, nu);
        db.Klanten.Add(klant);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KlantAangemaakt, Onderwerp(klant));
        return Uitkomst<Guid>.Met(klant.Id);
    }

    public async Task<Uitkomst> Wijzig(Guid id, KlantInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = Valideer(invoer);
        await using var db = await dbFactory.CreateDbContextAsync();
        var klant = await db.Klanten.SingleOrDefaultAsync(k => k.Id == id);
        if (klant is null)
        {
            return Uitkomst.Mislukt("De klant bestaat niet (meer).");
        }

        if (fouten.Count == 0 && await db.Klanten.AnyAsync(k => k.Id != id && k.Klantnummer == invoer.Klantnummer.Trim()))
        {
            fouten.Add($"Klantnummer {invoer.Klantnummer.Trim()} bestaat al.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        Vul(klant, invoer, wie, tijd.GetUtcNow());
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KlantGewijzigd, Onderwerp(klant));
        return Uitkomst.Gelukt;
    }

    internal static string Onderwerp(Klant klant) => $"{klant.Klantnummer} {klant.Naam}";

    private static List<string> Valideer(KlantInvoer invoer)
    {
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Klantnummer))
        {
            fouten.Add("Vul een klantnummer in.");
        }

        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Vul de naam van de klant in.");
        }

        var kvk = invoer.KvkNummer?.Trim();
        if (!string.IsNullOrEmpty(kvk) && (kvk.Length != 8 || !kvk.All(char.IsAsciiDigit)))
        {
            fouten.Add("Een KvK-nummer bestaat uit 8 cijfers.");
        }

        return fouten;
    }

    private static void Vul(Klant klant, KlantInvoer invoer, Uitvoerder wie, DateTimeOffset nu)
    {
        klant.Klantnummer = invoer.Klantnummer.Trim();
        klant.Naam = invoer.Naam.Trim();
        klant.Rechtsvorm = invoer.Rechtsvorm;
        klant.KvkNummer = string.IsNullOrWhiteSpace(invoer.KvkNummer) ? null : invoer.KvkNummer.Trim();
        klant.Plaats = string.IsNullOrWhiteSpace(invoer.Plaats) ? null : invoer.Plaats.Trim();
        klant.GewijzigdOp = nu;
        klant.GewijzigdDoor = wie.Naam;
    }
}
