using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <param name="Relevant">Rekeningen met saldo (na correcties) in het boekjaar of het vorige jaar.</param>
/// <param name="StappenKlaar">Stappen die gereed of niet van toepassing zijn (alleen als het werkprogramma loopt).</param>
/// <param name="Signalen">Wat aandacht vraagt, in gewone taal.</param>
public sealed record DossierSignalen(
    Guid DossierId,
    int Relevant,
    int Gekoppeld,
    int StappenKlaar,
    int StappenTotaal,
    int OpenPunten,
    IReadOnlyList<string> Signalen)
{
    /// <summary>Gekoppeld %, naar beneden afgerond (zoals bij het rubriceren).</summary>
    public int Percentage => Relevant == 0 ? 100 : Gekoppeld * 100 / Relevant;
}

/// <summary>
/// Signalen voor de klantenmonitor (docs §5.2, S1): per dossier wat nog aandacht vraagt. Berekend voor veel dossiers
/// tegelijk, zodat de klantenlijst snel blijft.
/// </summary>
public sealed class SignaalDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory)
{
    public async Task<IReadOnlyDictionary<Guid, DossierSignalen>> VoorDossiers(IReadOnlyCollection<Guid> dossierIds)
    {
        ArgumentNullException.ThrowIfNull(dossierIds);
        if (dossierIds.Count == 0)
        {
            return new Dictionary<Guid, DossierSignalen>();
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var dossiers = await db.Dossiers.AsNoTracking().Where(d => dossierIds.Contains(d.Id))
            .Select(d => new { d.Id, d.KlantId, d.Boekjaar, d.SoortOpdracht, d.WerkprogrammaVersie })
            .ToListAsync();
        var klanten = dossiers.Select(d => d.KlantId).Distinct().ToList();
        var saldi = await DossierSaldi.Haal(db, [.. dossiers.Select(d => new DossierSleutel(d.KlantId, d.Boekjaar, d.SoortOpdracht))]);
        var gekoppeld = (await db.Koppelingen.AsNoTracking().Where(k => klanten.Contains(k.KlantId)).Select(k => k.GrootboekrekeningId).ToListAsync()).ToHashSet();
        var ingelezen = (await db.ImportBatches.AsNoTracking().Where(b => klanten.Contains(b.KlantId) && b.Actief)
            .Select(b => new { b.KlantId, b.Boekjaar }).ToListAsync()).ToHashSet();
        var stappen = (await db.DossierStappen.AsNoTracking()
                .Join(db.DossierOnderdelen.AsNoTracking(), s => s.OnderdeelId, o => o.Id, (s, o) => new { o.DossierId, s.Status, s.Verwijderd, s.Controle })
                .Where(x => dossierIds.Contains(x.DossierId) && !x.Verwijderd)
                .ToListAsync())
            .ToLookup(x => x.DossierId);
        var punten = (await db.Reviewpunten.AsNoTracking().Where(p => dossierIds.Contains(p.DossierId) && !p.Opgelost)
                .GroupBy(p => p.DossierId).Select(g => new { DossierId = g.Key, Aantal = g.Count() }).ToListAsync())
            .ToDictionary(x => x.DossierId, x => x.Aantal);

        var resultaat = new Dictionary<Guid, DossierSignalen>();
        foreach (var d in dossiers)
        {
            var dossierSaldi = saldi[new DossierSleutel(d.KlantId, d.Boekjaar, d.SoortOpdracht)];
            var relevant = dossierSaldi.Where(s => (s.Value.Huidig ?? 0) != 0 || (s.Value.Vorig ?? 0) != 0).Select(s => s.Key).ToList();
            var aantalGekoppeld = relevant.Count(gekoppeld.Contains);
            var dossierStappen = stappen[d.Id].ToList();
            var klaar = dossierStappen.Count(s => s.Status != StapStatus.Open);
            var open = punten.GetValueOrDefault(d.Id);

            var signalen = new List<string>();
            if (!ingelezen.Contains(new { d.KlantId, d.Boekjaar }))
            {
                signalen.Add($"Nog geen saldibalans {d.Boekjaar} ingelezen");
            }

            if (relevant.Count > aantalGekoppeld)
            {
                signalen.Add($"{relevant.Count - aantalGekoppeld} rekening(en) met saldo niet gekoppeld");
            }

            if (open > 0)
            {
                signalen.Add($"{open} open reviewpunt(en)");
            }

            if (d.WerkprogrammaVersie is not null
                && dossierStappen.Any(s => s.Controle == StapControle.MaterielePostenGekozen && s.Status == StapStatus.Open))
            {
                signalen.Add("Beoordeling posten nog niet afgetekend");
            }

            resultaat[d.Id] = new DossierSignalen(d.Id, relevant.Count, aantalGekoppeld, klaar, dossierStappen.Count, open, signalen);
        }

        return resultaat;
    }
}
