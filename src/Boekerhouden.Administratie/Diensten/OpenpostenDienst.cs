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
/// en telt altijd mee. Andere regels op crediteuren staan er los bij, zodat het totaal sluit op het grootboek.
/// </summary>
public sealed class OpenpostenDienst(Toegang toegang, PdfDienst pdf)
{
    /// <summary>De groep voor regels op crediteuren zonder leverancier.</summary>
    public const string ZonderRelatie = "Zonder leverancier";

    /// <summary>De lijst als PDF (A4 liggend), voor de download.</summary>
    public async Task<Uitkomst<byte[]>> CrediteurenPdf(Uitvoerder wie, Guid administratie, DateOnly peildatum, string? relatie = null, CancellationToken stoppen = default) =>
        await pdf.Maak(OpenpostenHtml.Maak(await Crediteuren(wie, administratie, peildatum, relatie)), stoppen);

    public async Task<Openpostenlijst> Crediteuren(Uitvoerder wie, Guid administratie, DateOnly peildatum, string? relatie = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var naam = await db.Administraties.AsNoTracking().Where(a => a.Id == administratie).Select(a => a.Naam).SingleAsync();
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var crediteuren = schema.MetFunctie(Rekeningfunctie.Crediteuren).Select(r => r.Nummer).ToList();

        var allePosten = await db.OpenstaandePosten.AsNoTracking()
            .Where(p => p.Soort == Factuursoort.Inkoop && (relatie == null || p.Relatie == relatie))
            .ToListAsync();
        var posten = allePosten.Where(p => p.Datum <= peildatum).ToList();
        var factuurboekingen = allePosten.Select(p => p.BoekingId).ToList();
        var boekingIds = posten.Select(p => p.BoekingId).ToList();
        var boekingen = await db.Boekingen.AsNoTracking().Where(b => boekingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Dagboek, b.Boekstuk, b.Omschrijving }).ToDictionaryAsync(b => b.Id);

        // Alle andere regels op crediteuren: betalingen, correcties en wat er zonder factuur op crediteuren staat.
        var andere = await (from r in db.GeldendeRegels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where crediteuren.Contains(r.Rekeningnummer) && !factuurboekingen.Contains(r.BoekingId) && (relatie == null || r.Relatie == relatie)
                            select new { r.Relatie, r.Factuurnummer, r.Bedrag, r.Omschrijving, b.Datum, b.Dagboek, b.Boekstuk, Boeking = b.Omschrijving, b.Volgnummer })
            .ToListAsync();
        var sleutels = posten.Select(p => (p.Relatie, p.Factuurnummer)).ToHashSet();
        var perFactuur = andere.Where(b => b.Relatie != null && b.Factuurnummer != null).ToLookup(b => (b.Relatie!, b.Factuurnummer!));
        var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);

        var regels = posten.Select(p =>
        {
            var alle = perFactuur[(p.Relatie, p.Factuurnummer)].ToList();
            // Wat de post zelf al vereffend is (afletteren, betalingen en correcties via het journaal, verrekenen) min die
            // regels in het journaal is de verrekening met een creditnota; die heeft geen eigen boeking. Een oudere
            // betaling die de post niet bijwerkte, maakt het verschil kleiner of tegengesteld: dan geen verrekening.
            var vereffend = p.Openstaand - p.Bedrag;
            var verschil = vereffend - alle.Sum(b => b.Bedrag);
            var verrekend = Math.Sign(verschil) == Math.Sign(vereffend) && Math.Abs(vereffend) > Math.Abs(vereffend - verschil) ? verschil : 0m;
            var betaald = alle.Where(b => b.Datum <= peildatum).Sum(b => b.Bedrag) + verrekend;
            var boeking = boekingen.GetValueOrDefault(p.BoekingId);
            return (p.Relatie, Post: new Openpost(p.Id, p.Datum, p.Vervaldatum, boeking?.Omschrijving ?? "", p.Factuurnummer, -p.Bedrag, betaald,
                peildatum.DayNumber - p.Datum.DayNumber, boeking?.Dagboek ?? "", boeking?.Boekstuk ?? "", p.DocumentId));
        });

        // Regels die niet bij een factuur op de peildatum horen (geen of een onbekend factuurnummer, een vooruitbetaling,
        // een factuur buiten de inkoop om): per factuurnummer bij elkaar, zonder nummer elk apart. Zo sluit de lijst op
        // het saldo crediteuren in het grootboek.
        var los = andere.Where(b => b.Datum <= peildatum && !(b.Relatie != null && b.Factuurnummer != null && sleutels.Contains((b.Relatie, b.Factuurnummer))))
            .GroupBy(b => (Relatie: b.Relatie ?? "", b.Factuurnummer, Boeking: b.Factuurnummer == null ? b.Volgnummer : 0))
            .Select(g =>
            {
                var eerste = g.OrderBy(b => b.Datum).ThenBy(b => b.Volgnummer).First();
                return (Relatie: g.Key.Relatie, Post: new Openpost(Guid.Empty, eerste.Datum, eerste.Datum, eerste.Omschrijving ?? eerste.Boeking, eerste.Factuurnummer ?? "",
                    -g.Where(b => b.Bedrag < 0).Sum(b => b.Bedrag), g.Where(b => b.Bedrag > 0).Sum(b => b.Bedrag),
                    peildatum.DayNumber - eerste.Datum.DayNumber, eerste.Dagboek, eerste.Boekstuk, null));
            });

        return new Openpostenlijst(naam, peildatum, Factuursoort.Inkoop,
        [
            .. regels.Concat(los).Where(x => x.Post.Openstaand != 0).GroupBy(x => x.Relatie)
                .Select(g => new Openpostrelatie(g.Key, g.Key.Length == 0 ? ZonderRelatie : namen.GetValueOrDefault(g.Key) ?? g.Key, [.. g.Select(x => x.Post).OrderBy(p => p.Datum).ThenBy(p => p.Factuurnummer, StringComparer.Ordinal)]))
                .OrderBy(r => r.Relatie.Length == 0).ThenBy(r => r.Naam, StringComparer.CurrentCultureIgnoreCase),
        ]);
    }
}
