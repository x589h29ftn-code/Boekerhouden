using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record DossierDetail(
    Guid Id,
    string Naam,
    int Boekjaar,
    SoortOpdracht Soort,
    DossierStatus Status,
    Guid KlantId,
    string Klantnummer,
    string KlantNaam,
    Rechtsvorm Rechtsvorm,
    Verslaggeving? Grondslag = null);

public sealed class DossierDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    /// <summary>De gebruikelijke opdracht per rechtsvorm: BV → jaarrekening samenstellen, eenmanszaak → winstaangifte.</summary>
    public static SoortOpdracht StandaardSoort(Rechtsvorm rechtsvorm) =>
        rechtsvorm == Rechtsvorm.BV ? SoortOpdracht.SamenstellenJaarrekening : SoortOpdracht.WinstaangifteOpstellen;

    public async Task<IReadOnlyList<DossierSamenvatting>> VoorKlant(Guid klantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Dossiers.AsNoTracking()
            .Where(d => d.KlantId == klantId)
            .OrderByDescending(d => d.Boekjaar)
            .ThenBy(d => d.SoortOpdracht)
            .Select(d => new DossierSamenvatting(d.Id, d.Naam, d.Boekjaar, d.SoortOpdracht, d.Status))
            .ToListAsync();
    }

    public async Task<DossierDetail?> Haal(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Dossiers.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DossierDetail(d.Id, d.Naam, d.Boekjaar, d.SoortOpdracht, d.Status,
                d.KlantId, d.Klant!.Klantnummer, d.Klant.Naam, d.Klant.Rechtsvorm, d.Grondslag))
            .SingleOrDefaultAsync();
    }

    /// <param name="grondslag">Bij een jaarrekening: commerciële of fiscale grondslagen; null = die van het laatste eerdere jaar (of later kiezen).</param>
    public async Task<Uitkomst<Guid>> Maak(Guid klantId, int boekjaar, SoortOpdracht soort, Uitvoerder wie, Verslaggeving? grondslag = null)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var klant = await db.Klanten.SingleOrDefaultAsync(k => k.Id == klantId);
        if (klant is null)
        {
            return Uitkomst<Guid>.Mislukt("De klant bestaat niet (meer).");
        }

        if (boekjaar is < 2000 or > 2100)
        {
            return Uitkomst<Guid>.Mislukt("Vul een geldig boekjaar in, bijvoorbeeld 2025.");
        }

        if (await db.Dossiers.AnyAsync(d => d.KlantId == klantId && d.Boekjaar == boekjaar && d.SoortOpdracht == soort))
        {
            return Uitkomst<Guid>.Mislukt($"Er bestaat al een dossier '{Weergave.Tekst(soort)}' voor {boekjaar}.");
        }

        var nu = tijd.GetUtcNow();
        var heeftSaldibalans = await db.ImportBatches.AnyAsync(b => b.KlantId == klantId && b.Boekjaar == boekjaar && b.Actief);
        var dossier = new Dossier
        {
            Id = Guid.NewGuid(),
            KlantId = klantId,
            Boekjaar = boekjaar,
            SoortOpdracht = soort,
            Status = heeftSaldibalans ? DossierStatus.InUitvoering : DossierStatus.Nieuw,
            Naam = soort == SoortOpdracht.SamenstellenJaarrekening ? $"Jaarrekening {boekjaar}" : $"Winstaangifte {boekjaar}",
            Grondslag = soort != SoortOpdracht.SamenstellenJaarrekening ? null
                : grondslag ?? await db.Dossiers.Where(d => d.KlantId == klantId && d.SoortOpdracht == soort && d.Boekjaar < boekjaar && d.Grondslag != null)
                    .OrderByDescending(d => d.Boekjaar).Select(d => d.Grondslag).FirstOrDefaultAsync(),
            AangemaaktOp = nu,
            GewijzigdOp = nu,
            GewijzigdDoor = wie.Naam,
        };
        db.Dossiers.Add(dossier);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DossierAangemaakt, $"{KlantDienst.Onderwerp(klant)} – {dossier.Naam}",
            dossier.Grondslag is { } gekozen ? Weergave.Tekst(gekozen) : null);
        return Uitkomst<Guid>.Met(dossier.Id);
    }
}
