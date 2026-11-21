using Boekerhouden.Administratie.Data;
using Boekerhouden.Domain;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat een zoekresultaat is; de volgorde is die van de groepen in het zoekvenster.</summary>
public enum Zoeksoort
{
    Actie,
    Pagina,
    Relatie,
    Rekening,
    Boeking,
    Verkoopfactuur,
    Activum,
    Administratie,
}

/// <summary>Een gevonden item: wat het is, één regel tekst, details erachter en waar het heen gaat.</summary>
public sealed record Zoekresultaat(Zoeksoort Soort, string Titel, string? Detail, string Url);

/// <summary>
/// Het zoekvenster (Ctrl+K): pagina's, relaties, rekeningen, boekingen (boekstuk, omschrijving, factuurnummer of
/// bedrag), verkoopfacturen en vaste activa in de geopende administratie, en de administraties waarvan de gebruiker lid
/// is. Alleen via <see cref="Toegang"/>: zonder lidmaatschap vindt hij in een administratie niets.
/// </summary>
public sealed class ZoekDienst(Toegang toegang, AdministratieDienst administraties)
{
    /// <summary>Het aantal resultaten per soort.</summary>
    public const int PerSoort = 6;

    public async Task<IReadOnlyList<Zoekresultaat>> Zoek(Uitvoerder wie, string? tekst, Guid? administratie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var zoek = tekst?.Trim() ?? string.Empty;
        if (zoek.Length < 2)
        {
            return [];
        }

        var resultaten = new List<Zoekresultaat>();
        if (administratie is { } id && await administraties.Open(wie, id) is { } kop)
        {
            resultaten.AddRange(Acties(kop, zoek));
            resultaten.AddRange(Paginas(kop, zoek));
            await using var db = await toegang.Open(wie, id, Recht.Lezen);
            resultaten.AddRange(await InAdministratie(db, id, zoek));
        }

        resultaten.AddRange((await administraties.Lijst(wie, zoek)).Take(PerSoort)
            .Select(a => new Zoekresultaat(Zoeksoort.Administratie, a.Naam, Weergave.Tekst(a.Ondernemingsvorm), $"/a/{a.Id}")));
        return [.. resultaten.OrderBy(r => r.Soort)];
    }

    /// <summary>Iets doen in plaats van iets vinden: naam, trefwoorden, waar het gebeurt en het recht dat nodig is.</summary>
    private sealed record Zoekactie(string Naam, string Trefwoorden, string Pad, Recht Recht, string Detail);

    private static readonly Zoekactie[] Zoekacties =
    [
        new("Inkoopfacturen inlezen", "factuur facturen inkoop inlezen uploaden pdf bon scan", "/inkoop", Recht.Invoeren, "Inkoop"),
        new("Verkoopfacturen inlezen", "factuur facturen verkoop inlezen uploaden ander pakket", "/verkoop", Recht.Invoeren, "Verkoop"),
        new("Nieuwe verkoopfactuur", "factuur maken nieuw nieuwe verkoop klant", "/verkoop/nieuw", Recht.Invoeren, "Facturen maken"),
        new("Terugkerende factuur", "factuur terugkerend abonnement herhalen elke maand", "/verkoop/terugkerend", Recht.Invoeren, "Facturen maken"),
        new("Bankafschrift inlezen", "bank afschrift afschriften inlezen mt940 camt rekeningafschrift", "/bank", Recht.Invoeren, "Bank"),
        new("Nieuwe memoriaalboeking", "boeken boeking memoriaal journaalpost nieuw nieuwe", "/memoriaal", Recht.Boekhouden, "Memoriaal"),
        new("Loonjournaal importeren", "loon loonjournaal salaris importeren inlezen", "/memoriaal", Recht.Boekhouden, "Memoriaal"),
        new("Btw-aangifte doen", "btw aangifte omzetbelasting kwartaal maand vastleggen", "/btw", Recht.Lezen, "Btw-aangifte"),
        new("Periode afsluiten", "periode maand afsluiten blokkeren vastzetten", "/perioden", Recht.Boekhouden, "Perioden"),
        new("Auditfile maken", "auditfile xaf exporteren accountant belastingdienst", "/kolommenbalans", Recht.Lezen, "Kolommenbalans"),
        new("Beginbalans inlezen", "beginbalans jaarrekening openingsbalans inlezen", "/beginbalans", Recht.Boekhouden, "Instellingen"),
        new("Nieuwe relatie", "relatie klant leverancier nieuw nieuwe toevoegen", "/relaties", Recht.Invoeren, "Relaties"),
        new("Nieuwe regel", "regel automatisch boeken herkennen nieuw nieuwe", "/regels", Recht.Boekhouden, "Regels"),
        new("Nieuw actief", "actief activa investering afschrijven nieuw", "/activa", Recht.Boekhouden, "Vaste activa"),
    ];

    /// <summary>
    /// De acties waar elk woord van de zoektekst (vanaf drie letters) het begin van een woord van de naam of de
    /// trefwoorden is; alleen wat de gebruiker in deze administratie mag.
    /// </summary>
    private static IEnumerable<Zoekresultaat> Acties(AdministratieKop kop, string zoek)
    {
        var woorden = zoek.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToList();
        if (woorden.Count == 0)
        {
            return [];
        }

        return Zoekacties
            .Where(a => kop.Mag(a.Recht))
            .Where(a =>
            {
                var tokens = $"{a.Naam} {a.Trefwoorden}".ToLowerInvariant().Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
                return woorden.All(w => tokens.Any(t => t.StartsWith(w, StringComparison.Ordinal)));
            })
            .Select(a => new Zoekresultaat(Zoeksoort.Actie, a.Naam, a.Detail, $"/a/{kop.Id}{a.Pad}"))
            .Take(PerSoort);
    }

    private static IEnumerable<Zoekresultaat> Paginas(AdministratieKop kop, string zoek) =>
        Administratiemenu.AllePaginas()
            .Where(x => x.Item.Zichtbaar(kop))
            .Where(x => x.Item.Naam.Contains(zoek, StringComparison.OrdinalIgnoreCase) || $"{x.Groep} {x.Item.Naam}".Contains(zoek, StringComparison.OrdinalIgnoreCase))
            .Select(x => new Zoekresultaat(Zoeksoort.Pagina, x.Item.Naam, x.Groep, $"/a/{kop.Id}{x.Item.Pad}"))
            .Take(PerSoort);

    // ToLower().Contains() wordt in SQL lower(..) LIKE '%..%' (met de juiste escapes), op PostgreSQL en SQLite.
#pragma warning disable CA1304, CA1311, CA1862
    private static async Task<IEnumerable<Zoekresultaat>> InAdministratie(AdministratieDbContext db, Guid id, string zoek)
    {
        var klein = zoek.ToLowerInvariant();
        var resultaten = new List<Zoekresultaat>();

        var relaties = await db.Relaties.AsNoTracking()
            .Where(r => r.Code.ToLower().Contains(klein) || r.Naam.ToLower().Contains(klein))
            .OrderBy(r => r.Naam).Take(PerSoort).Select(r => new { r.Code, r.Naam, r.Plaats }).ToListAsync();
        resultaten.AddRange(relaties.Select(r => new Zoekresultaat(Zoeksoort.Relatie, r.Naam, string.Join(" · ", new[] { r.Code, r.Plaats }.Where(t => !string.IsNullOrEmpty(t))),
            $"/a/{id}/relaties/{Uri.EscapeDataString(r.Code)}")));

        var rekeningen = await db.Rekeningen.AsNoTracking()
            .Where(r => r.Nummer.StartsWith(zoek) || r.Naam.ToLower().Contains(klein))
            .OrderBy(r => r.Nummer).Take(PerSoort).Select(r => new { r.Nummer, r.Naam }).ToListAsync();
        resultaten.AddRange(rekeningen.Select(r => new Zoekresultaat(Zoeksoort.Rekening, $"{r.Nummer} {r.Naam}", "grootboekkaart",
            $"/a/{id}/grootboek?rekening={Uri.EscapeDataString(r.Nummer)}")));

        // Boekingen: op boekstuk, omschrijving, factuurnummer of (als de tekst een bedrag is) een bedrag op een regel.
        decimal? bedrag = BedragParser.TryParse(zoek, out var b) && b != 0m ? Math.Abs(b) : null;
        var opRegel = db.GeldendeRegels.Where(r => (r.Factuurnummer != null && r.Factuurnummer.ToLower().Contains(klein))
            || (bedrag != null && (r.Bedrag == bedrag || r.Bedrag == -bedrag))).Select(r => r.BoekingId);
        var boekingen = await db.Geldend.AsNoTracking().Include(x => x.Regels)
            .Where(x => x.Boekstuk.ToLower().Contains(klein) || x.Omschrijving.ToLower().Contains(klein) || opRegel.Contains(x.Id))
            .OrderByDescending(x => x.Datum).ThenByDescending(x => x.Volgnummer).Take(PerSoort).ToListAsync();
        resultaten.AddRange(boekingen.Select(x => new Zoekresultaat(Zoeksoort.Boeking, $"{x.Dagboek} {x.Boekstuk} {x.Omschrijving}".TrimEnd(),
            $"{Weergave.Datum(x.Datum)} · {Weergave.Bedrag(x.Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag))}",
            $"/a/{id}/boeking/{Uri.EscapeDataString(x.Dagboek)}/{Uri.EscapeDataString(x.Boekstuk)}")));

        var facturen = await db.Verkoopdocumenten.AsNoTracking()
            .Where(v => (v.Nummer != null && v.Nummer.ToLower().Contains(klein)) || v.Omschrijving.ToLower().Contains(klein) || v.Relatie.ToLower().Contains(klein))
            .OrderByDescending(v => v.Datum).Take(PerSoort).Select(v => new { v.Id, v.Nummer, v.Omschrijving, v.Relatie, v.Datum }).ToListAsync();
        resultaten.AddRange(facturen.Select(v => new Zoekresultaat(Zoeksoort.Verkoopfactuur, $"{v.Nummer ?? "concept"} {v.Omschrijving}".TrimEnd(),
            $"{Weergave.Datum(v.Datum)} · {v.Relatie}", $"/a/{id}/verkoop/{v.Id}")));

        var activa = await db.Activa.AsNoTracking()
            .Where(a => a.Omschrijving.ToLower().Contains(klein))
            .OrderBy(a => a.Nummer).Take(PerSoort).Select(a => new { a.Id, a.Nummer, a.Omschrijving, a.Aanschafdatum }).ToListAsync();
        resultaten.AddRange(activa.Select(a => new Zoekresultaat(Zoeksoort.Activum, a.Omschrijving, $"nummer {a.Nummer} · {Weergave.Datum(a.Aanschafdatum)}",
            $"/a/{id}/activa/{a.Id}")));
        return resultaten;
    }
#pragma warning restore CA1304, CA1311, CA1862
}
