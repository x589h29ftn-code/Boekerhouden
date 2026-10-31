using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Het inkoop- of verkoopboek over een periode (op factuurdatum): elke geboekte factuur en creditnota met de bedragen
/// zoals op de factuur en wat er nog openstaat, op datum en boekstuk. Een teruggedraaide factuur staat er niet meer in.
/// Bij inkoop verwijst <see cref="Factuurboekregel.Document"/> naar het ingelezen document, bij verkoop naar de gemaakte
/// verkoopfactuur (of het ingelezen document).
/// </summary>
internal static class Factuurboek
{
    public static async Task<IReadOnlyList<Factuurboekregel>> Lees(AdministratieDbContext db, Factuursoort soort, DateOnly van, DateOnly tot)
    {
        var posten = await db.OpenstaandePosten.AsNoTracking()
            .Where(p => p.Soort == soort && p.Datum >= van && p.Datum <= tot)
            .ToListAsync();
        var boekingIds = posten.Select(p => p.BoekingId).ToList();
        var boekingen = await db.Boekingen.AsNoTracking().Include(b => b.Regels).Where(b => boekingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);
        var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);
        var gemaakt = soort == Factuursoort.Verkoop
            ? await db.Verkoopdocumenten.AsNoTracking().Where(v => v.BoekingId != null && boekingIds.Contains(v.BoekingId.Value)).ToDictionaryAsync(v => v.BoekingId!.Value, v => v.Id)
            : [];
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        bool IsBtw(Boekingsregel r) => schema.Zoek(r.Rekeningnummer)?.Functie is Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw;

        // Inkoop: het bedrag van de post is negatief (te betalen); verkoop: positief (te ontvangen).
        var teken = soort == Factuursoort.Inkoop ? -1 : 1;
        return
        [
            .. posten.Select(p =>
            {
                var boeking = boekingen[p.BoekingId];
                var totaal = teken * p.Bedrag;
                var btw = -teken * boeking.Regels.Where(IsBtw).Sum(r => r.Bedrag);
                var document = gemaakt.TryGetValue(p.BoekingId, out var v) ? v : p.DocumentId;
                return new Factuurboekregel(p.Datum, boeking.Dagboek, boeking.Boekstuk, p.Relatie, namen.GetValueOrDefault(p.Relatie, p.Relatie),
                    p.Factuurnummer, boeking.Omschrijving, totaal - btw, btw, totaal, teken * p.Openstaand, document);
            })
            .OrderBy(r => r.Datum).ThenBy(r => r.Boekstuk, StringComparer.Ordinal),
        ];
    }
}
