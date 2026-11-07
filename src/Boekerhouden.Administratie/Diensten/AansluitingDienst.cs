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

    /// <summary>Een activarekening (aanschafwaarde) of afschrijvingsrekening tegen de vaste activa.</summary>
    Activa,
}

/// <summary>Wat er bij de controle van een bankmutatie tegen het bankdagboek niet klopt.</summary>
public enum Bankverschilsoort
{
    /// <summary>De mutatie is geboekt, maar zijn boeking staat niet (meer) op de bank: verwijderd of vervallen.</summary>
    NietInGrootboek,

    /// <summary>De boeking van de mutatie staat met een ander bedrag op de bank.</summary>
    Afwijkend,

    /// <summary>Een boeking op de bank in de periode van de afschriften zonder bankmutatie (bijv. memoriaal of tegenboeking).</summary>
    ZonderMutatie,
}

/// <summary>
/// Eén verschil tussen de ingelezen bankmutaties en de bank in het grootboek.
/// </summary>
/// <param name="Bedrag">Het bedrag van de mutatie (bij <see cref="Bankverschilsoort.ZonderMutatie"/>: van de boeking op de bank).</param>
/// <param name="Grootboek">Wat er in het grootboek bij deze mutatie op de bank staat.</param>
/// <param name="Boekstuk">Dagboek en boekstuk, bijv. "BNK 2026-0003".</param>
public sealed record Bankverschil(Bankverschilsoort Soort, DateOnly Datum, decimal Bedrag, decimal Grootboek, string Omschrijving, string? Boekstuk, Guid? Mutatie);

/// <summary>
/// Eén saldo-aansluiting: het saldo in het grootboek tegenover de bron (het bankafschrift, de openstaande posten),
/// of alleen het saldo als er niets tegenover staat (vraagposten horen 0 te zijn, de btw is wat te betalen is).
/// </summary>
/// <param name="Grootboek">Het saldo in het grootboek (debet positief, credit negatief).</param>
/// <param name="Bron">Het saldo volgens de bron in dezelfde tekenconventie; leeg = geen bron.</param>
/// <param name="Toelichting">Bijv. "2 mutaties nog te verwerken" of de datum van het afschrift.</param>
/// <param name="Verschillen">Bij een bank: de mutaties die niet (goed) in het bankdagboek staan, en boekingen op de bank zonder mutatie.</param>
public sealed record Aansluiting(Aansluitingsoort Soort, string Onderwerp, string? Rekening, decimal Grootboek, decimal? Bron, string? Toelichting, string? Dagboek = null,
    IReadOnlyList<Bankverschil>? Verschillen = null)
{
    /// <summary>Grootboek min bron; bij vraagposten het saldo zelf (dat hoort 0 te zijn).</summary>
    public decimal? Verschil => Bron is { } bron ? Grootboek - bron : Soort == Aansluitingsoort.Vraagposten ? Grootboek : null;

    /// <summary>Bankmutaties die niet (goed) in het grootboek staan; een boeking zonder mutatie is ter informatie.</summary>
    public IEnumerable<Bankverschil> Ontbrekend => Verschillen?.Where(v => v.Soort != Bankverschilsoort.ZonderMutatie) ?? [];

    public bool Sluit => Verschil is null or 0m && !Ontbrekend.Any();
}

/// <summary>
/// De saldo-aansluitingen van een administratie, zoals het nu staat: elke bank op het laatste ingelezen afschrift,
/// crediteuren en debiteuren op de openstaande posten, de vaste activa op de activa- en afschrijvingsrekeningen, de
/// vraagposten (horen leeg te zijn) en de btw-rekeningen.
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
            var verschillen = await Bankcontrole(db, dagboek.Code, rekening,
                afschriften.Where(a => a.Dagboek == dagboek.Code).Min(a => a.Begindatum) ?? DateOnly.MinValue, tot);
            var toelichting = $"afschrift t/m {Weergave.Datum(tot)}" + (open > 0 ? $", {open} {(open == 1 ? "mutatie" : "mutaties")} te verwerken" : "")
                + (verschillen.Count(v => v.Soort != Bankverschilsoort.ZonderMutatie) is > 0 and var mis ? $", {mis} {(mis == 1 ? "mutatie" : "mutaties")} niet in het bankdagboek" : "");
            lijst.Add(new Aansluiting(Aansluitingsoort.Bank, dagboek.Naam, rekening, grootboek.Sum(), laatste.Eindsaldo, toelichting, dagboek.Code, verschillen));
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

        // Vaste activa: elke activarekening op de aanschafwaarde van de activa, elke afschrijvingsrekening op de
        // cumulatieve afschrijving (na afschrijvingen en desinvesteringen).
        foreach (var r in await ActivaDienst.Aansluiting(db, schema))
        {
            var bedragen = await db.GeldendeRegels.AsNoTracking().Where(x => x.Rekeningnummer == r.Rekening).Select(x => x.Bedrag).ToListAsync();
            lijst.Add(new Aansluiting(Aansluitingsoort.Activa, r.Onderwerp, r.Rekening, bedragen.Sum(), r.Bron, r.Toelichting));
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

    /// <summary>
    /// Vergelijkt elke geboekte bankmutatie tot en met <paramref name="tot"/> met zijn boeking: staat die nog met
    /// hetzelfde bedrag op de bankrekening? Een boeking die vervallen of verwijderd is, of met een ander bedrag op de
    /// bank staat, komt in de lijst. Daarnaast de boekingen op de bank na het begin van het eerste afschrift die bij
    /// geen mutatie horen (de beginbalans valt daarvoor). Mutaties die nog te verwerken zijn, staan al in de toelichting.
    /// </summary>
    private static async Task<IReadOnlyList<Bankverschil>> Bankcontrole(AdministratieDbContext db, string dagboek, string rekening, DateOnly begin, DateOnly tot)
    {
        var mutaties = await db.Banktransacties.AsNoTracking()
            .Where(t => t.Dagboek == dagboek && t.Status == Banktransactiestatus.Geboekt && t.Datum <= tot)
            .Select(t => new { t.Id, t.Datum, t.Bedrag, t.TegenNaam, t.Omschrijving, t.BoekingId }).ToListAsync();
        var gekoppeld = mutaties.Where(m => m.BoekingId is not null).Select(m => m.BoekingId!.Value).Distinct().ToList();
        // Op de bank: per geldende boeking het saldo van de bankregels (optellen in het geheugen; SQLite telt geen decimals op).
        var opBank = (await (from r in db.GeldendeRegels
                             join b in db.Boekingen on r.BoekingId equals b.Id
                             where r.Rekeningnummer == rekening && (gekoppeld.Contains(b.Id) || (b.Datum > begin && b.Datum <= tot))
                             select new { b.Id, b.Dagboek, b.Boekstuk, b.Datum, b.Omschrijving, r.Bedrag }).ToListAsync())
            .GroupBy(r => r.Id).ToDictionary(g => g.Key, g => (g.First().Dagboek, g.First().Boekstuk, g.First().Datum, g.First().Omschrijving, Bedrag: g.Sum(r => r.Bedrag)));
        var stukken = (await db.Boekingen.AsNoTracking().Where(b => gekoppeld.Contains(b.Id)).Select(b => new { b.Id, b.Dagboek, b.Boekstuk }).ToListAsync())
            .ToDictionary(b => b.Id, b => $"{b.Dagboek} {b.Boekstuk}");

        var verschillen = new List<Bankverschil>();
        foreach (var groep in mutaties.GroupBy(m => m.BoekingId))
        {
            var gevonden = groep.Key is { } id && opBank.TryGetValue(id, out var b) ? b.Bedrag : 0m;
            if (gevonden == groep.Sum(m => m.Bedrag))
            {
                continue;
            }

            var stuk = groep.Key is { } sleutel ? stukken.GetValueOrDefault(sleutel) : null;
            verschillen.AddRange(groep.Select(m => new Bankverschil(gevonden == 0 ? Bankverschilsoort.NietInGrootboek : Bankverschilsoort.Afwijkend, m.Datum, m.Bedrag,
                gevonden, Tekst(m.TegenNaam, m.Omschrijving), stuk, m.Id)));
        }

        var bijMutatie = gekoppeld.ToHashSet();
        verschillen.AddRange(opBank.Where(b => !bijMutatie.Contains(b.Key) && b.Value.Bedrag != 0)
            .Select(b => new Bankverschil(Bankverschilsoort.ZonderMutatie, b.Value.Datum, b.Value.Bedrag, b.Value.Bedrag, b.Value.Omschrijving, $"{b.Value.Dagboek} {b.Value.Boekstuk}", null)));
        return [.. verschillen.OrderBy(v => v.Datum).ThenBy(v => v.Bedrag)];
    }

    private static string Tekst(string? naam, string omschrijving) =>
        string.Join(" - ", new[] { naam, omschrijving }.Where(t => !string.IsNullOrWhiteSpace(t)));
}
