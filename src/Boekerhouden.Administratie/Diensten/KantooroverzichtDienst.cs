using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Facturen;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Wat er in een administratie te doen is: inkoopfacturen en bankmutaties te verwerken (met de oudste mutatie), tot
/// wanneer de bank is ingelezen, verkoopconcepten, vervallen debiteuren en tot wanneer de perioden zijn afgesloten.
/// </summary>
public sealed record Werkstand(
    Guid Administratie,
    int Inkoop,
    int Bank,
    DateOnly? OudsteBank,
    DateOnly? AfschriftTot,
    int Concepten,
    int Vervallen,
    decimal VervallenBedrag,
    DateOnly? AfgeslotenTot)
{
    public bool Leeg => Inkoop == 0 && Bank == 0 && Concepten == 0 && Vervallen == 0;
}

/// <summary>Een soort werk in de werklijst van de startpagina.</summary>
public enum Werksoort
{
    Inkoop,
    Bank,
    Verkoop,
    Debiteuren,
}

/// <summary>
/// Het kantooroverzicht op de startpagina: per administratie de <see cref="Werkstand"/>, in een paar queries over alle
/// administraties van de gebruiker tegelijk (ook bij honderden administraties snel). Alleen administraties waarvan de
/// gebruiker lid is (<see cref="Toegang.OpenOverzicht"/>); gearchiveerde administraties tellen niet mee.
/// </summary>
public sealed class KantooroverzichtDienst(Toegang toegang, TimeProvider tijd)
{
    public async Task<IReadOnlyDictionary<Guid, Werkstand>> Stand(Uitvoerder wie)
    {
        var (db, leden) = await toegang.OpenOverzicht(wie);
        await using (db)
        {
            var actief = await db.Administraties.AsNoTracking().Where(a => a.GearchiveerdOp == null)
                .Select(a => new { a.Id, a.AfgeslotenTot }).ToListAsync();
            var ids = actief.Where(a => leden.ContainsKey(a.Id)).Select(a => a.Id).ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, Werkstand>();
            }

            var vandaag = DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);
            var inkoop = await db.Documenten.IgnoreQueryFilters().Where(d => ids.Contains(d.AdministratieId) && d.Status == Documentstatus.TeVerwerken)
                .GroupBy(d => d.AdministratieId).Select(g => new { g.Key, Aantal = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Aantal);
            var bank = await db.Banktransacties.IgnoreQueryFilters().Where(t => ids.Contains(t.AdministratieId) && t.Status == Banktransactiestatus.TeVerwerken)
                .GroupBy(t => t.AdministratieId).Select(g => new { g.Key, Aantal = g.Count(), Oudste = g.Min(t => t.Datum) }).ToDictionaryAsync(g => g.Key);
            var afschriften = await db.Afschriften.IgnoreQueryFilters().Where(a => ids.Contains(a.AdministratieId) && a.Einddatum != null)
                .GroupBy(a => a.AdministratieId).Select(g => new { g.Key, Tot = g.Max(a => a.Einddatum) }).ToDictionaryAsync(g => g.Key, g => g.Tot);
            var concepten = await db.Verkoopdocumenten.IgnoreQueryFilters().Where(v => ids.Contains(v.AdministratieId) && v.Status == Verkoopstatus.Concept)
                .GroupBy(v => v.AdministratieId).Select(g => new { g.Key, Aantal = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Aantal);

            // Bedragen optellen in het geheugen: SQLite kan geen decimals optellen.
            var vervallen = (await db.OpenstaandePosten.IgnoreQueryFilters()
                    .Where(p => ids.Contains(p.AdministratieId) && p.Soort == Factuursoort.Verkoop && p.Openstaand != 0 && p.Vervaldatum < vandaag)
                    .Select(p => new { p.AdministratieId, p.Openstaand }).ToListAsync())
                .GroupBy(p => p.AdministratieId).ToDictionary(g => g.Key, g => (Aantal: g.Count(), Bedrag: g.Sum(p => p.Openstaand)));

            return actief.Where(a => leden.ContainsKey(a.Id)).ToDictionary(a => a.Id, a => new Werkstand(
                a.Id,
                inkoop.GetValueOrDefault(a.Id),
                bank.TryGetValue(a.Id, out var b) ? b.Aantal : 0,
                b?.Oudste,
                afschriften.GetValueOrDefault(a.Id),
                concepten.GetValueOrDefault(a.Id),
                vervallen.TryGetValue(a.Id, out var v) ? v.Aantal : 0,
                v.Bedrag,
                a.AfgeslotenTot));
        }
    }
}
