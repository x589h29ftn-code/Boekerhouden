using Boekerhouden.Data;
using Boekerhouden.Domain.Correcties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>Een dossier zoals de saldi het kennen: klant, boekjaar en soort opdracht.</summary>
internal readonly record struct DossierSleutel(Guid KlantId, int Boekjaar, SoortOpdracht Soort);

/// <summary>
/// De saldi per grootboekrekening voor een dossier: de saldibalans van de klant plus de correcties in het dossier, en
/// hetzelfde voor het vorige jaar (de vergelijkende cijfers, met de correcties uit het dossier van vorig jaar).
/// </summary>
internal static class DossierSaldi
{
    public static async Task<Dictionary<Guid, GecorrigeerdSaldo>> Haal(BoekerhoudenDbContext db, Guid klantId, int boekjaar, SoortOpdracht soort)
    {
        var sleutel = new DossierSleutel(klantId, boekjaar, soort);
        return (await Haal(db, [sleutel]))[sleutel];
    }

    /// <summary>De saldi van meerdere dossiers tegelijk, met twee query's (voor overzichten zoals de klantenlijst).</summary>
    public static async Task<Dictionary<DossierSleutel, Dictionary<Guid, GecorrigeerdSaldo>>> Haal(BoekerhoudenDbContext db, IReadOnlyCollection<DossierSleutel> dossiers)
    {
        var klanten = dossiers.Select(d => d.KlantId).Distinct().ToList();
        var import = await (from s in db.Saldi.AsNoTracking()
                            join b in db.ImportBatches.AsNoTracking() on s.ImportBatchId equals b.Id
                            where klanten.Contains(b.KlantId) && b.Actief
                            select new { b.KlantId, b.Boekjaar, s.GrootboekrekeningId, s.Bedrag })
            .ToListAsync();
        var correcties = await (from r in db.Correctieregels.AsNoTracking()
                                join c in db.Correctieboekingen.AsNoTracking() on r.CorrectieboekingId equals c.Id
                                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                                where klanten.Contains(d.KlantId)
                                select new { d.KlantId, d.Boekjaar, d.SoortOpdracht, r.GrootboekrekeningId, r.Bedrag })
            .ToListAsync();

        var importPerJaar = import.ToLookup(r => (r.KlantId, r.Boekjaar));
        var correctiesPerDossier = correcties.ToLookup(r => new DossierSleutel(r.KlantId, r.Boekjaar, r.SoortOpdracht));

        static Dictionary<Guid, decimal> Som(IEnumerable<(Guid Id, decimal Bedrag)> regels) =>
            regels.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag));

        return dossiers.Distinct().ToDictionary(
            d => d,
            d => GecorrigeerdSaldo.Bereken(
                Som(importPerJaar[(d.KlantId, d.Boekjaar)].Select(r => (r.GrootboekrekeningId, r.Bedrag))),
                Som(correctiesPerDossier[d].Select(r => (r.GrootboekrekeningId, r.Bedrag))),
                Som(importPerJaar[(d.KlantId, d.Boekjaar - 1)].Select(r => (r.GrootboekrekeningId, r.Bedrag))),
                Som(correctiesPerDossier[d with { Boekjaar = d.Boekjaar - 1 }].Select(r => (r.GrootboekrekeningId, r.Bedrag)))));
    }
}
