using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Waar een aansluiting over gaat; bepaalt waar de regel naartoe linkt.</summary>
public enum Aansluitingsoort
{
    Bank,
    Crediteuren,
    Debiteuren,
    Vraagposten,
    Btw,
}

/// <summary>
/// Eén saldo-aansluiting: het saldo in het grootboek tegenover de bron (het bankafschrift, de openstaande posten),
/// of alleen het saldo als er niets tegenover staat (vraagposten horen 0 te zijn, de btw is wat te betalen is).
/// </summary>
/// <param name="Grootboek">Het saldo in het grootboek (debet positief, credit negatief).</param>
/// <param name="Bron">Het saldo volgens de bron in dezelfde tekenconventie; leeg = geen bron.</param>
/// <param name="Toelichting">Bijv. "2 mutaties nog te verwerken" of de datum van het afschrift.</param>
public sealed record Aansluiting(Aansluitingsoort Soort, string Onderwerp, string? Rekening, decimal Grootboek, decimal? Bron, string? Toelichting, string? Dagboek = null)
{
    /// <summary>Grootboek min bron; bij vraagposten het saldo zelf (dat hoort 0 te zijn).</summary>
    public decimal? Verschil => Bron is { } bron ? Grootboek - bron : Soort == Aansluitingsoort.Vraagposten ? Grootboek : null;

    public bool Sluit => Verschil is null or 0m;
}

/// <summary>
/// De saldo-aansluitingen van een administratie, zoals het nu staat: elke bank op het laatste ingelezen afschrift,
/// crediteuren en debiteuren op de openstaande posten, de vraagposten (horen leeg te zijn) en de btw-rekeningen.
/// </summary>
public sealed class AansluitingDienst(Toegang toegang)
{
    public async Task<IReadOnlyList<Aansluiting>> Aansluitingen(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        // Optellen in het geheugen: SQLite telt geen decimals op.
        var nodig = schema.Alle.Where(r => r.Functie is not Rekeningfunctie.Geen).Select(r => r.Nummer).ToList();
        var saldi = (await db.GeldendeRegels.AsNoTracking().Where(r => nodig.Contains(r.Rekeningnummer)).Select(r => new { r.Rekeningnummer, r.Bedrag }).ToListAsync())
            .GroupBy(r => r.Rekeningnummer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag), StringComparer.Ordinal);
        decimal Saldo(IEnumerable<Rekening> rekeningen) => rekeningen.Sum(r => saldi.GetValueOrDefault(r.Nummer));
        string? Nummers(IEnumerable<Rekening> rekeningen) => string.Join(", ", rekeningen.Select(r => r.Nummer)) is { Length: > 0 } t ? t : null;
        var lijst = new List<Aansluiting>();

        // Elke bank op het eindsaldo van het laatste afschrift; het grootboek tot en met die datum.
        var afschriften = await db.Afschriften.AsNoTracking().Where(a => a.Einddatum != null).ToListAsync();
        foreach (var dagboek in dagboeken.Where(d => d.Soort == Dagboeksoort.Bank && d.Rekeningnummer is not null))
        {
            var rekening = dagboek.Rekeningnummer!;
            if (afschriften.Where(a => a.Dagboek == dagboek.Code).OrderBy(a => a.Einddatum).ThenBy(a => a.IngelezenOp).LastOrDefault() is not { } laatste)
            {
                lijst.Add(new Aansluiting(Aansluitingsoort.Bank, dagboek.Naam, rekening, saldi.GetValueOrDefault(rekening), null, "nog geen afschrift ingelezen", dagboek.Code));
                continue;
            }

            var tot = laatste.Einddatum!.Value;
            var grootboek = await (from r in db.GeldendeRegels
                                   join b in db.Geldend on r.BoekingId equals b.Id
                                   where r.Rekeningnummer == rekening && b.Datum <= tot
                                   select r.Bedrag).ToListAsync();
            var open = await db.Banktransacties.AsNoTracking().CountAsync(t => t.Dagboek == dagboek.Code && t.Status == Banktransactiestatus.TeVerwerken && t.Datum <= tot);
            var toelichting = $"afschrift t/m {Weergave.Datum(tot)}" + (open > 0 ? $", {open} {(open == 1 ? "mutatie" : "mutaties")} te verwerken" : "");
            lijst.Add(new Aansluiting(Aansluitingsoort.Bank, dagboek.Naam, rekening, grootboek.Sum(), laatste.Eindsaldo, toelichting, dagboek.Code));
        }

        // Crediteuren en debiteuren op de openstaande posten (in de tekenconventie van het grootboek).
        foreach (var (soort, functie, factuursoort, naam) in new[]
                 {
                     (Aansluitingsoort.Crediteuren, Rekeningfunctie.Crediteuren, Factuursoort.Inkoop, "Crediteuren"),
                     (Aansluitingsoort.Debiteuren, Rekeningfunctie.Debiteuren, Factuursoort.Verkoop, "Debiteuren"),
                 })
        {
            var rekeningen = schema.MetFunctie(functie).ToList();
            var posten = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == factuursoort).Select(p => p.Openstaand).ToListAsync();
            if (rekeningen.Count == 0 && posten.Count == 0)
            {
                continue;
            }

            var aantal = posten.Count(p => p != 0);
            lijst.Add(new Aansluiting(soort, naam, Nummers(rekeningen), Saldo(rekeningen), posten.Sum(),
                $"{aantal} openstaande {(aantal == 1 ? "post" : "posten")}"));
        }

        if (schema.MetFunctie(Rekeningfunctie.Vraagposten).ToList() is { Count: > 0 } vraag)
        {
            lijst.Add(new Aansluiting(Aansluitingsoort.Vraagposten, "Vraagposten", Nummers(vraag), Saldo(vraag), null, null));
        }

        foreach (var (functie, naam) in new[] { (Rekeningfunctie.Voorbelasting, "Voorbelasting"), (Rekeningfunctie.AfTeDragenBtw, "Af te dragen btw") })
        {
            if (schema.MetFunctie(functie).ToList() is { Count: > 0 } btw)
            {
                lijst.Add(new Aansluiting(Aansluitingsoort.Btw, naam, Nummers(btw), Saldo(btw), null, null));
            }
        }

        return lijst;
    }
}
