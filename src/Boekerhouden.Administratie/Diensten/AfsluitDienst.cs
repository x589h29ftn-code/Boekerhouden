using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Een stap bij het afsluiten van een periode: wat, of hij gedaan is, wat er nog ligt (één regel) en waar je hem doet.
/// </summary>
public sealed record Afsluitstap(string Naam, bool Klaar, string? Nog, string Pad, string Icoon);

/// <summary>
/// Periode afsluiten als stappenplan: de vaste volgorde van het maandwerk tot en met een datum. Bank bij, inkoop en
/// verkoop verwerkt, afschrijvingen geboekt (als er vaste activa zijn), aansluitingen die sluiten en de btw-aangiften
/// vastgelegd (als de administratie btw-plichtig is); daarna afsluiten. Alleen lezen: afsluiten zelf doet
/// <see cref="PeriodeDienst.SluitAf"/>, en mag ook als niet alles klaar is.
/// </summary>
public sealed class AfsluitDienst(Toegang toegang, BankDienst bank, AansluitingDienst aansluitingen, BtwaangifteDienst btw)
{
    public async Task<IReadOnlyList<Afsluitstap>> Stappen(Uitvoerder wie, Guid administratie, DateOnly tot)
    {
        var stappen = new List<Afsluitstap>();
        bool btwPlichtig;
        await using (var db = await toegang.Open(wie, administratie, Recht.Lezen))
        {
            btwPlichtig = await db.Administraties.Where(a => a.Id == administratie).Select(a => a.BtwPlichtig).SingleAsync();

            var banken = await bank.Stand(wie, administratie);
            var teVerwerken = await db.Banktransacties.CountAsync(t => t.Status == Banktransactiestatus.TeVerwerken && t.Datum <= tot);
            var achter = banken.Where(b => b.Tot is not { } t || t < tot).ToList();
            var ingelezen = banken.Any(b => b.Tot is not null);
            stappen.Add(new($"Bank bij tot en met {Weergave.Datum(tot)}", ingelezen && achter.Count == 0 && teVerwerken == 0,
                !ingelezen ? "nog geen bankafschrift ingelezen"
                    : string.Join(", ", new[]
                    {
                        achter.Count > 0 ? string.Join(", ", achter.Select(b => $"{b.Naam} t/m {(b.Tot is { } t ? Weergave.Datum(t) : "nog niets")}")) : null,
                        teVerwerken > 0 ? $"{teVerwerken} {(teVerwerken == 1 ? "mutatie" : "mutaties")} te verwerken" : null,
                    }.Where(x => x is not null)),
                "/bank", "bank"));

            var inkoop = await db.Documenten.CountAsync(d => d.Status == Documentstatus.TeVerwerken);
            stappen.Add(new("Inkoop verwerkt", inkoop == 0, inkoop > 0 ? $"{inkoop} {(inkoop == 1 ? "factuur" : "facturen")} te verwerken" : null, "/inkoop", "shopping-cart-simple"));

            var verkoop = await db.Verkoopdocumenten.CountAsync(v => v.Status == Verkoopstatus.Concept && v.Datum <= tot);
            stappen.Add(new("Verkoop verwerkt", verkoop == 0, verkoop > 0 ? $"{verkoop} {(verkoop == 1 ? "factuur" : "facturen")} nog niet definitief of geboekt" : null,
                verkoop > 0 && await db.Verkoopdocumenten.AnyAsync(v => v.Status == Verkoopstatus.Concept && v.Datum <= tot && !v.Ingelezen) ? "/facturen" : "/verkoop", "storefront"));

            if (await db.Activa.AnyAsync())
            {
                var activa = await ActivaDienst.Achterstallig(db, tot);
                stappen.Add(new("Afschrijvingen geboekt", activa == 0, activa > 0 ? $"{activa} {(activa == 1 ? "actief" : "activa")} nog af te schrijven" : null, "/activa", "buildings"));
            }
        }

        var open = (await aansluitingen.Aansluitingen(wie, administratie)).Where(a => !a.Sluit).ToList();
        stappen.Add(new("Aansluitingen sluiten", open.Count == 0, open.Count > 0 ? $"sluit niet: {string.Join(", ", open.Select(a => a.Onderwerp))}" : null, "/aansluitingen", "checks"));

        if (btwPlichtig)
        {
            var nietAangegeven = (await btw.Jaar(wie, administratie, tot.Year)).Jaar.Perioden
                .Where(p => p.Periode.Tot <= tot && p.Vastgelegd is null).Select(p => p.Periode.Naam).ToList();
            stappen.Add(new("Btw-aangifte vastgelegd", nietAangegeven.Count == 0, nietAangegeven.Count > 0 ? $"nog niet vastgelegd: {string.Join(", ", nietAangegeven)}" : null, "/btw", "percent"));
        }

        return stappen;
    }
}
