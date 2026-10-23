using Boekerhouden.Data;
using Boekerhouden.Domain.Correcties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>
/// De saldi per grootboekrekening voor een dossier: de saldibalans van de klant plus de correcties in het dossier, en
/// hetzelfde voor het vorige jaar (de vergelijkende cijfers, met de correcties uit het dossier van vorig jaar).
/// </summary>
internal static class DossierSaldi
{
    public static async Task<Dictionary<Guid, GecorrigeerdSaldo>> Haal(BoekerhoudenDbContext db, Guid klantId, int boekjaar, SoortOpdracht soort)
    {
        var import = await (from s in db.Saldi.AsNoTracking()
                            join b in db.ImportBatches.AsNoTracking() on s.ImportBatchId equals b.Id
                            where b.KlantId == klantId && b.Actief && (b.Boekjaar == boekjaar || b.Boekjaar == boekjaar - 1)
                            select new { s.GrootboekrekeningId, b.Boekjaar, s.Bedrag })
            .ToListAsync();
        var correcties = await (from r in db.Correctieregels.AsNoTracking()
                                join c in db.Correctieboekingen.AsNoTracking() on r.CorrectieboekingId equals c.Id
                                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                                where d.KlantId == klantId && d.SoortOpdracht == soort && (d.Boekjaar == boekjaar || d.Boekjaar == boekjaar - 1)
                                select new { r.GrootboekrekeningId, d.Boekjaar, r.Bedrag })
            .ToListAsync();

        Dictionary<Guid, decimal> Som<T>(IEnumerable<T> regels, Func<T, Guid> id, Func<T, decimal> bedrag) =>
            regels.GroupBy(id).ToDictionary(g => g.Key, g => g.Sum(bedrag));

        return GecorrigeerdSaldo.Bereken(
            Som(import.Where(r => r.Boekjaar == boekjaar), r => r.GrootboekrekeningId, r => r.Bedrag),
            Som(correcties.Where(r => r.Boekjaar == boekjaar), r => r.GrootboekrekeningId, r => r.Bedrag),
            Som(import.Where(r => r.Boekjaar == boekjaar - 1), r => r.GrootboekrekeningId, r => r.Bedrag),
            Som(correcties.Where(r => r.Boekjaar == boekjaar - 1), r => r.GrootboekrekeningId, r => r.Bedrag));
    }
}
