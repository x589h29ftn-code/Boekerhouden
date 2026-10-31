using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een terugkerende factuur zoals de gebruiker hem invult.</summary>
/// <param name="Omschrijving">De omschrijving van elke factuur, met variabelen als <c>#periode</c>.</param>
/// <param name="Automatisch">Elke factuur meteen definitief maken; anders een concept om na te kijken.</param>
/// <param name="Verdelen">De omzet van elke factuur verdelen over zijn periode (via <paramref name="Verdeelrekening"/>).</param>
public sealed record Abonnementinvoer(
    string Naam,
    string Relatie,
    Herhaling Herhaling,
    DateOnly Begin,
    DateOnly? Einde,
    string Omschrijving,
    IReadOnlyList<Verkoopregel> Regels,
    bool Automatisch = false,
    bool Verdelen = false,
    string? Verdeelrekening = null,
    string? Referentie = null,
    string? Tekst = null,
    Guid? LayoutId = null,
    bool Actief = true);

/// <summary>Een terugkerende factuur in de lijst.</summary>
/// <param name="Bedrag">Het bedrag exclusief btw van elke factuur.</param>
/// <param name="Gemaakt">Hoeveel facturen er al van gemaakt zijn.</param>
/// <param name="TeMaken">Hoeveel er tot en met vandaag nog gemaakt moeten worden.</param>
public sealed record Abonnementregel(
    Guid Id,
    string Naam,
    string Relatie,
    string Klant,
    Herhaling Herhaling,
    DateOnly? VolgendeDatum,
    decimal Bedrag,
    bool Automatisch,
    bool Actief,
    int Gemaakt,
    int TeMaken);

/// <summary>Wat er bij het maken van de terugkerende facturen gebeurd is.</summary>
/// <param name="Facturen">De gemaakte facturen: id, en het nummer als hij definitief is.</param>
public sealed record Aanmaakverslag(IReadOnlyList<(Guid Id, string? Nummer)> Facturen, IReadOnlyList<string> Meldingen)
{
    public int Definitief => Facturen.Count(f => f.Nummer is not null);
}

/// <summary>
/// Terugkerende verkoopfacturen: huur, abonnementen, vaste diensten. Per abonnement de klant, de regels (met variabelen
/// als <c>#periode</c>) en de planning (<see cref="Abonnementsplan"/>). <see cref="Maak"/> maakt alle facturen tot en
/// met een datum: als concept, of meteen definitief; eventueel met de omzet verdeeld over de periode van de factuur.
/// Een factuur die niet definitief kan (bijv. een ontbrekend adres), blijft als concept staan met de reden.
/// </summary>
public sealed class AbonnementDienst(Toegang toegang, VerkoopDienst verkoop, AuditDienst audit, TimeProvider tijd)
{
    private static readonly JsonSerializerOptions Json = new();

    private DateOnly Vandaag => DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<Abonnementregel>> Lijst(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);
        var vandaag = Vandaag;
        return
        [
            .. (await db.Abonnementen.AsNoTracking().ToListAsync()).Select(a =>
            {
                var plan = Plan(a);
                return new Abonnementregel(a.Id, a.Naam, a.Relatie, namen.GetValueOrDefault(a.Relatie, a.Relatie), a.Herhaling, a.Actief ? plan.Volgende(a.Volgende) : null,
                    Regels(a).Sum(r => r.Bedrag), a.Automatisch, a.Actief, a.Volgende, a.Actief ? plan.TeMaken(a.Volgende, vandaag).Count : 0);
            })
            .OrderBy(a => !a.Actief).ThenBy(a => a.VolgendeDatum ?? DateOnly.MaxValue).ThenBy(a => a.Naam, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>Hoeveel facturen er tot en met de peildatum gemaakt moeten worden.</summary>
    public async Task<int> TeMaken(Uitvoerder wie, Guid administratie, DateOnly? peildatum = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dag = peildatum ?? Vandaag;
        return (await db.Abonnementen.AsNoTracking().Where(a => a.Actief).ToListAsync()).Sum(a => Plan(a).TeMaken(a.Volgende, dag).Count);
    }

    public async Task<(Abonnementinvoer Invoer, int Gemaakt)?> Open(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Abonnementen.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) is { } a ? (Invoer(a), a.Volgende) : null;
    }

    /// <summary>Een terugkerende factuur met de klant en regels van een bestaande factuur, beginnend een maand later.</summary>
    public async Task<Abonnementinvoer?> VanFactuur(Uitvoerder wie, Guid administratie, Guid factuur)
    {
        if (await verkoop.Open(wie, administratie, factuur) is not { } f)
        {
            return null;
        }

        return new Abonnementinvoer($"{f.Factuur.Klant.Naam} {f.Factuur.Omschrijving}".Trim(), f.Invoer.Relatie, Herhaling.Maand, f.Factuur.Datum.AddMonths(1), null,
            f.Invoer.Omschrijving, [.. f.Factuur.Regels.Where(r => r.Aantal > 0)], Referentie: f.Invoer.Referentie, Tekst: f.Invoer.Tekst, LayoutId: f.Invoer.LayoutId);
    }

    public async Task<Uitkomst<Guid>> Bewaar(Uitvoerder wie, Guid administratie, Guid? id, Abonnementinvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Geef de terugkerende factuur een naam (bijv. Huur kantoor).");
        }

        if (!await db.Relaties.AnyAsync(r => r.Code == invoer.Relatie))
        {
            fouten.Add("Kies een klant.");
        }

        if (invoer.Regels.Count == 0)
        {
            fouten.Add("De factuur heeft geen regels.");
        }
        else if (invoer.Regels.Count > VerkoopDienst.MaximumRegels)
        {
            fouten.Add($"Een factuur heeft hoogstens {VerkoopDienst.MaximumRegels} regels.");
        }

        fouten.AddRange(new Abonnementsplan(invoer.Begin, invoer.Herhaling, invoer.Einde).Controleer());
        if (invoer.Verdelen)
        {
            var (schema, _) = await GrootboekDienst.Inrichting(db);
            if (schema.Zoek(invoer.Verdeelrekening ?? "") is not { IsBalans: true })
            {
                fouten.Add("Kies voor het verdelen een balansrekening, bijv. vooruitontvangen omzet.");
            }
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        Abonnement abonnement;
        if (id is { } bestaand)
        {
            if (await db.Abonnementen.FirstOrDefaultAsync(a => a.Id == bestaand) is not { } gevonden)
            {
                return Uitkomst<Guid>.Mislukt("Deze terugkerende factuur bestaat niet (meer).");
            }

            abonnement = gevonden;
        }
        else
        {
            abonnement = new Abonnement { Id = Guid.NewGuid(), AdministratieId = administratie, AangemaaktOp = tijd.GetUtcNow(), AangemaaktDoor = wie.Naam };
            db.Abonnementen.Add(abonnement);
        }

        abonnement.Naam = Kort(invoer.Naam.Trim(), 200);
        abonnement.Relatie = invoer.Relatie;
        (abonnement.Herhaling, abonnement.Begin, abonnement.Einde) = (invoer.Herhaling, invoer.Begin, invoer.Einde);
        abonnement.Omschrijving = Kort(invoer.Omschrijving?.Trim() ?? "", 500);
        abonnement.Regels = JsonSerializer.Serialize(invoer.Regels, Json);
        (abonnement.Referentie, abonnement.Tekst, abonnement.LayoutId) = (Leeg(invoer.Referentie), Leeg(invoer.Tekst), invoer.LayoutId);
        (abonnement.Verdelen, abonnement.Verdeelrekening) = (invoer.Verdelen, invoer.Verdelen ? invoer.Verdeelrekening : null);
        (abonnement.Automatisch, abonnement.Actief) = (invoer.Automatisch, invoer.Actief);
        await db.SaveChangesAsync();
        await audit.Log(wie, id is null ? AuditActies.AbonnementToegevoegd : AuditActies.AbonnementGewijzigd, abonnement.Naam,
            $"{abonnement.Relatie}; {abonnement.Herhaling} vanaf {abonnement.Begin:dd-MM-yyyy}{(abonnement.Actief ? null : "; gestopt")}", administratie);
        return Uitkomst<Guid>.Met(abonnement.Id);
    }

    /// <summary>Verwijdert een terugkerende factuur; de facturen die er al van gemaakt zijn, blijven.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Abonnementen.FirstOrDefaultAsync(a => a.Id == id) is not { } abonnement)
        {
            return Uitkomst.Mislukt("Deze terugkerende factuur bestaat niet (meer).");
        }

        foreach (var document in await db.Verkoopdocumenten.Where(v => v.AbonnementId == id).ToListAsync())
        {
            document.AbonnementId = null;
        }

        db.Abonnementen.Remove(abonnement);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.AbonnementVerwijderd, abonnement.Naam, null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Maakt alle facturen van de actieve terugkerende facturen tot en met de peildatum (standaard vandaag). Twee keer
    /// achter elkaar maakt geen dubbele facturen: het volgnummer gaat in dezelfde transactie omhoog, met de
    /// administratie vergrendeld.
    /// </summary>
    public async Task<Uitkomst<Aanmaakverslag>> Maak(Uitvoerder wie, Guid administratie, DateOnly? peildatum = null, Guid? alleen = null)
    {
        var dag = peildatum ?? Vandaag;
        var context = await verkoop.Context(wie, administratie);
        var gemaakt = new List<(Guid Id, bool Automatisch, string Naam)>();
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            await using var transactie = await db.Database.BeginTransactionAsync();
            await db.Vergrendel(administratie);
            var abonnementen = await db.Abonnementen.Where(a => a.Actief && (alleen == null || a.Id == alleen)).ToListAsync();
            var relaties = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code);
            foreach (var a in abonnementen)
            {
                if (!relaties.TryGetValue(a.Relatie, out var relatie))
                {
                    continue;
                }

                var plan = Plan(a);
                foreach (var n in plan.TeMaken(a.Volgende, dag))
                {
                    var datum = plan.Datum(n);
                    var (van, tot) = plan.Periode(n);
                    var termijn = relatie.Betalingstermijn ?? context.Betalingstermijn;
                    var invoer = new Verkoopinvoer(a.Relatie, datum, datum.AddDays(termijn), plan.Vul(a.Omschrijving, n),
                        [.. Regels(a).Select(r => r with { Omschrijving = plan.Vul(r.Omschrijving, n) })],
                        a.Referentie, a.Tekst is { } t ? plan.Vul(t, n) : null,
                        a.Verdelen && a.Verdeelrekening is { } rekening ? new Periodeverdeling(van, tot, rekening) : null, a.LayoutId);
                    var document = VerkoopDienst.NieuwConcept(administratie, invoer, relatie, wie, tijd.GetUtcNow(), a.Id);
                    db.Verkoopdocumenten.Add(document);
                    gemaakt.Add((document.Id, a.Automatisch, a.Naam));
                    a.Volgende = n + 1;
                    a.LaatstGemaaktOp = tijd.GetUtcNow();
                }
            }

            await db.SaveChangesAsync();
            await transactie.CommitAsync();
        }

        var facturen = new List<(Guid Id, string? Nummer)>();
        var meldingen = new List<string>();
        foreach (var (id, automatisch, naam) in gemaakt)
        {
            string? nummer = null;
            if (automatisch)
            {
                var definitief = await verkoop.MaakDefinitief(wie, administratie, id);
                if (definitief.IsGelukt)
                {
                    nummer = definitief.Waarde;
                }
                else
                {
                    meldingen.Add($"{naam}: als concept bewaard, want {string.Join(" ", definitief.Fouten).TrimEnd('.').ToLowerInvariant()}.");
                }
            }

            facturen.Add((id, nummer));
        }

        if (gemaakt.Count > 0)
        {
            await audit.Log(wie, AuditActies.AbonnementenGemaakt, $"{gemaakt.Count} facturen", $"t/m {dag:dd-MM-yyyy}; {facturen.Count(f => f.Nummer is not null)} definitief", administratie);
        }

        return Uitkomst<Aanmaakverslag>.Met(new Aanmaakverslag(facturen, meldingen));
    }

    private static Abonnementsplan Plan(Abonnement a) => new(a.Begin, a.Herhaling, a.Einde);

    private static List<Verkoopregel> Regels(Abonnement a) =>
        JsonSerializer.Deserialize<List<Verkoopregel>>(a.Regels, Json) ?? [];

    private static Abonnementinvoer Invoer(Abonnement a) =>
        new(a.Naam, a.Relatie, a.Herhaling, a.Begin, a.Einde, a.Omschrijving, Regels(a), a.Automatisch, a.Verdelen, a.Verdeelrekening, a.Referentie, a.Tekst, a.LayoutId, a.Actief);

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
}
