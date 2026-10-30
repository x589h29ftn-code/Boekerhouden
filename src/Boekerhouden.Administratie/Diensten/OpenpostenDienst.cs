using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Eén openstaande post op de peildatum: de factuur (of creditnota) met het bedrag, wat er tot en met de peildatum
/// betaald of verrekend is, wat er openstaat en hoe oud de post is. Bedragen zoals op de factuur: een factuur positief,
/// een creditnota negatief.
/// </summary>
/// <param name="Ouderdom">Dagen tussen de factuurdatum en de peildatum.</param>
public sealed record Openpost(
    Guid Id,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    string Factuurnummer,
    decimal Factuurbedrag,
    decimal Betaald,
    int Ouderdom,
    string Dagboek,
    string Boekstuk,
    Guid? Document)
{
    public decimal Openstaand => Factuurbedrag - Betaald;
}

/// <summary>De openstaande posten van één relatie, met de totalen.</summary>
public sealed record Openpostrelatie(string Relatie, string Naam, IReadOnlyList<Openpost> Posten)
{
    public decimal Factuurbedrag => Posten.Sum(p => p.Factuurbedrag);

    public decimal Betaald => Posten.Sum(p => p.Betaald);

    public decimal Openstaand => Posten.Sum(p => p.Openstaand);
}

/// <summary>De openstaande posten op een peildatum, per relatie, met het totaal generaal.</summary>
public sealed record Openpostenlijst(string Administratie, DateOnly Peildatum, Factuursoort Soort, IReadOnlyList<Openpostrelatie> Relaties)
{
    public decimal Factuurbedrag => Relaties.Sum(r => r.Factuurbedrag);

    public decimal Betaald => Relaties.Sum(r => r.Betaald);

    public decimal Openstaand => Relaties.Sum(r => r.Openstaand);
}

/// <summary>
/// De lijst openstaande posten (crediteuren) op een peildatum. Wat op die datum openstond, volgt uit het journaal:
/// de facturen tot en met de peildatum, min de betalingen tot en met de peildatum (de regels op crediteuren met dezelfde
/// leverancier en hetzelfde factuurnummer). Een verrekening van een creditnota met een factuur heeft geen eigen datum
/// en telt altijd mee.
/// </summary>
public sealed class OpenpostenDienst(Toegang toegang, PdfDienst pdf)
{
    /// <summary>De lijst als PDF (A4 liggend), voor de download.</summary>
    public async Task<Uitkomst<byte[]>> CrediteurenPdf(Uitvoerder wie, Guid administratie, DateOnly peildatum, string? relatie = null, CancellationToken stoppen = default) =>
        await pdf.Maak(OpenpostenHtml.Maak(await Crediteuren(wie, administratie, peildatum, relatie)), stoppen);

    public async Task<Openpostenlijst> Crediteuren(Uitvoerder wie, Guid administratie, DateOnly peildatum, string? relatie = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var naam = await db.Administraties.AsNoTracking().Where(a => a.Id == administratie).Select(a => a.Naam).SingleAsync();
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var crediteuren = schema.MetFunctie(Rekeningfunctie.Crediteuren).Select(r => r.Nummer).ToList();

        var posten = await db.OpenstaandePosten.AsNoTracking()
            .Where(p => p.Soort == Factuursoort.Inkoop && p.Datum <= peildatum && (relatie == null || p.Relatie == relatie))
            .ToListAsync();
        var boekingIds = posten.Select(p => p.BoekingId).ToList();
        var boekingen = await db.Boekingen.AsNoTracking().Where(b => boekingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Dagboek, b.Boekstuk, b.Omschrijving }).ToDictionaryAsync(b => b.Id);

        // Alle regels op crediteuren met een factuurnummer, behalve die van de facturen zelf: de betalingen.
        var betalingen = await (from r in db.GeldendeRegels
                                join b in db.Boekingen on r.BoekingId equals b.Id
                                where crediteuren.Contains(r.Rekeningnummer) && r.Relatie != null && r.Factuurnummer != null && !boekingIds.Contains(r.BoekingId)
                                select new { r.Relatie, r.Factuurnummer, r.Bedrag, b.Datum }).ToListAsync();
        var perFactuur = betalingen.ToLookup(b => (b.Relatie!, b.Factuurnummer!));
        var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);

        var regels = posten.Select(p =>
        {
            var alle = perFactuur[(p.Relatie, p.Factuurnummer)].ToList();
            var verrekend = (p.Openstaand - p.Bedrag) - alle.Sum(b => b.Bedrag);
            var betaald = alle.Where(b => b.Datum <= peildatum).Sum(b => b.Bedrag) + verrekend;
            var boeking = boekingen.GetValueOrDefault(p.BoekingId);
            return (p.Relatie, Post: new Openpost(p.Id, p.Datum, p.Vervaldatum, boeking?.Omschrijving ?? "", p.Factuurnummer, -p.Bedrag, betaald,
                peildatum.DayNumber - p.Datum.DayNumber, boeking?.Dagboek ?? "", boeking?.Boekstuk ?? "", p.DocumentId));
        }).Where(x => x.Post.Openstaand != 0);

        return new Openpostenlijst(naam, peildatum, Factuursoort.Inkoop,
        [
            .. regels.GroupBy(x => x.Relatie)
                .Select(g => new Openpostrelatie(g.Key, namen.GetValueOrDefault(g.Key) ?? g.Key, [.. g.Select(x => x.Post).OrderBy(p => p.Datum).ThenBy(p => p.Factuurnummer, StringComparer.Ordinal)]))
                .OrderBy(r => r.Naam, StringComparer.CurrentCultureIgnoreCase),
        ]);
    }
}
