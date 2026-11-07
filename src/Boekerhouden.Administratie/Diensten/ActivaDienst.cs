using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;
using ActivumEntiteit = Boekerhouden.Administratie.Data.Activum;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat de gebruiker van een actief invult (zie <see cref="Boekhouding.Activa.Activum"/>).</summary>
public sealed record ActivumInvoer(
    string Omschrijving,
    DateOnly Aanschafdatum,
    decimal Aanschafwaarde,
    decimal Restwaarde,
    DateOnly BeginAfschrijving,
    int Looptijd,
    Afschrijvingsmethode Methode,
    string Activarekening,
    string Afschrijvingsrekening,
    string Kostenrekening,
    decimal AfgeschrevenVoorBegin = 0m,
    string? Bron = null,
    string? Notitie = null)
{
    public Boekhouding.Activa.Activum NaarDomein() =>
        new(Omschrijving.Trim(), Aanschafdatum, Aanschafwaarde, Restwaarde, BeginAfschrijving, Looptijd, Methode, Activarekening.Trim(), Afschrijvingsrekening.Trim(),
            Kostenrekening.Trim(), AfgeschrevenVoorBegin);
}

/// <summary>Een actief in de lijst, met de stand van nu (na gedeeltelijke desinvesteringen).</summary>
/// <param name="Afgeschreven">Cumulatieve afschrijving nu (ook vóór de eerste maand, min wat met een desinvestering wegging).</param>
/// <param name="PerMaand">De afschrijving van de volgende maand volgens het plan; 0 als hij klaar is.</param>
/// <param name="Achterstand">Maanden tot en met de vorige maand die nog niet geboekt zijn.</param>
/// <param name="Gedesinvesteerd">Voor een deel gedesinvesteerd (verkocht, ingeruild of gesloopt).</param>
public sealed record Activaregel(
    Guid Id, int Nummer, string Omschrijving, DateOnly Aanschafdatum, decimal Aanschafwaarde, decimal Afgeschreven, decimal Restwaarde, decimal PerMaand,
    string Activarekening, DateOnly? BuitenGebruikOp, int Achterstand, bool Gedesinvesteerd = false)
{
    public decimal Boekwaarde => BuitenGebruikOp is null ? Aanschafwaarde - Afgeschreven : 0m;
}

/// <summary>Een desinvestering van een actief zoals geboekt.</summary>
/// <param name="Deel">In procenten.</param>
/// <param name="Tegenboeking">Teruggedraaid met deze boeking; leeg = geldt.</param>
/// <param name="Laatste">De laatste die nog geldt: alleen die kan worden teruggedraaid.</param>
public sealed record Desinvesteringsregel(
    long Id, DateOnly Datum, Desinvesteringssoort Soort, decimal Deel, decimal Aanschaf, decimal Afschrijving, decimal Opbrengst, string Boekstuk,
    string? Tegenboeking, bool Laatste)
{
    public decimal Boekwaarde => Aanschaf - Afschrijving;

    public decimal Resultaat => Opbrengst - Boekwaarde;
}

/// <summary>Een actief met zijn plan; <see cref="Vast"/> als er al geboekt is (aanschaf en rekeningen liggen dan vast).</summary>
/// <param name="Boekstukken">Per geboekte maand de boeking, bijv. "MEM 2026-0040".</param>
/// <param name="Aanschafwaarde">Nu, na gedeeltelijke desinvesteringen.</param>
/// <param name="Afgeschreven">Cumulatieve afschrijving nu.</param>
/// <param name="Restwaarde">Restwaarde nu.</param>
/// <param name="TeBoeken">De maanden tot en met de vorige maand die nog niet geboekt zijn.</param>
public sealed record Activumdetail(
    Guid Id, int Nummer, ActivumInvoer Invoer, IReadOnlyList<Afschrijvingsmaand> Plan, IReadOnlyDictionary<DateOnly, string> Boekstukken,
    DateOnly? BuitenGebruikOp, IReadOnlyList<Desinvesteringsregel> Desinvesteringen, bool Vast, DateTimeOffset AangemaaktOp, string AangemaaktDoor,
    decimal Aanschafwaarde, decimal Afgeschreven, decimal Restwaarde, int TeBoeken)
{
    public decimal Boekwaarde => Aanschafwaarde - Afgeschreven;

    /// <summary>De afschrijving van de eerste maand die nog niet geboekt is.</summary>
    public decimal PerMaand => BuitenGebruikOp is null ? Plan.FirstOrDefault(m => !m.Geboekt)?.Bedrag ?? 0m : 0m;
}

/// <summary>Wat de gebruiker bij desinvesteren invult.</summary>
/// <param name="Deel">In procenten: 100 = het hele actief.</param>
/// <param name="Opbrengst">Zonder btw; 0 bij sloop.</param>
/// <param name="Opbrengstrekening">Waar de opbrengst nu op staat (een tussenrekening van de verkoop- of inkoopfactuur).</param>
/// <param name="Resultaatrekening">Voor de boekwinst of het boekverlies (winst-en-verliesrekening).</param>
/// <param name="EerstAfschrijven">De afschrijvingen tot en met de maand van de desinvestering eerst boeken (per maand).</param>
public sealed record Desinvesteringsinvoer(
    DateOnly Datum, Desinvesteringssoort Soort, decimal Deel, decimal Opbrengst, string? Opbrengstrekening, string? Resultaatrekening, bool EerstAfschrijven = true);

/// <summary>Een regel van de boeking zoals hij zal worden: rekening met naam, omschrijving en bedrag (debet positief).</summary>
public sealed record Voorstelregel(string Rekening, string Naam, string? Omschrijving, decimal Bedrag);

/// <summary>Wat een desinvestering zal boeken, zonder iets te wijzigen.</summary>
/// <param name="EerstAfschrijven">De afschrijvingen per maand die eerst nog geboekt moeten worden.</param>
/// <param name="Fouten">Waarom het (nog) niet kan; leeg = boeken kan.</param>
public sealed record Desinvesteringsvoorstel(
    IReadOnlyList<Afschrijvingsvoorstel> EerstAfschrijven, Desinvesteringsbedragen? Bedragen, string Omschrijving, IReadOnlyList<Voorstelregel> Boeking, IReadOnlyList<string> Fouten);

/// <summary>Een aanschaf op een activarekening die nog niet als actief is opgenomen, met een voorstel.</summary>
public sealed record Activakandidaat(string Boekstuk, DateOnly Datum, string Omschrijving, string Rekening, decimal Bedrag, ActivumInvoer Voorstel);

/// <summary>Eén afschrijvingsboeking die <see cref="ActivaDienst.Afschrijven"/> zou maken: de datum, hoeveel activa en het bedrag.</summary>
public sealed record Afschrijvingsvoorstel(DateOnly Datum, string Omschrijving, int Activa, decimal Bedrag);

/// <summary>Een actief in de activastaat.</summary>
public sealed record Activastaatpost(Guid Id, int Nummer, string Omschrijving, Activastaatregel Regel);

/// <summary>
/// De activa op één activarekening in de activastaat, met het totaal en het saldo in het grootboek aan het eind van het
/// jaar (aanschaf op de activarekening, cumulatieve afschrijving als positief bedrag).
/// </summary>
public sealed record Activastaatgroep(
    string Activarekening, string Naam, string Afschrijvingsrekening, IReadOnlyList<Activastaatpost> Posten, Activastaatregel Totaal,
    decimal GrootboekAanschaf, decimal GrootboekAfschrijving)
{
    public bool Sluit => GrootboekAanschaf == Totaal.AanschafEind && GrootboekAfschrijving == Totaal.AfschrijvingEind;
}

/// <summary>
/// Vaste activa (Boekhouden, Vaste activa): de activakaart met het afschrijvingsplan, afschrijvingen boeken in het
/// memoriaal (per maand, kwartaal of jaar), desinvesteren (verkoop, inruil, sloop, ook voor een deel) met het boekresultaat, de activastaat met de
/// aansluiting op het grootboek, en voorstellen uit aanschaffingen die al op een activarekening geboekt zijn.
/// Inzien voor iedereen met toegang; wijzigen en boeken alleen het kantoor (recht Boekhouden).
/// </summary>
public sealed class ActivaDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd)
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    private DateOnly Vandaag => DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<Activaregel>> Lijst(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var activa = await db.Activa.AsNoTracking().OrderBy(a => a.Nummer).ToListAsync();
        var geboekt = await Geboekt(db);
        var stappen = await Stappen(db);
        var vorigeMaand = VorigeMaand;
        return [.. activa.Select(a =>
        {
            var g = geboekt.GetValueOrDefault(a.Id) ?? [];
            var d = stappen.GetValueOrDefault(a.Id) ?? [];
            var plan = Afschrijvingsplan.Maak(a.NaarDomein(), g, d);
            var (aanschaf, afgeschreven, rest) = a.BuitenGebruikOp is null ? Stand(a, g, d) : (a.Aanschafwaarde, a.AfgeschrevenVoorBegin + g.Values.Sum(), 0m);
            var open = a.BuitenGebruikOp is null ? plan.Where(m => !m.Geboekt).ToList() : [];
            return new Activaregel(a.Id, a.Nummer, a.Omschrijving, a.Aanschafdatum, aanschaf, afgeschreven, rest, open.FirstOrDefault()?.Bedrag ?? 0m,
                a.Activarekening, a.BuitenGebruikOp, open.Count(m => m.Maand <= vorigeMaand), d.Count > 0);
        })];
    }

    private DateOnly VorigeMaand => new DateOnly(Vandaag.Year, Vandaag.Month, 1).AddMonths(-1);

    public async Task<Activumdetail?> Open(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Activa.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) is not { } a)
        {
            return null;
        }

        var rijen = await db.Afschrijvingen.AsNoTracking().Where(x => x.ActivumId == id).ToListAsync();
        var desinvesteringen = await db.Desinvesteringen.AsNoTracking().Where(x => x.ActivumId == id).OrderBy(x => x.Datum).ThenBy(x => x.Id).ToListAsync();
        var geboekt = rijen.ToDictionary(r => r.Maand, r => r.Bedrag);
        var stappen = desinvesteringen.Where(x => x.Tegenboeking is null).Select(x => x.NaarStap()).ToList();
        var plan = Afschrijvingsplan.Maak(a.NaarDomein(), geboekt, stappen);
        if (a.BuitenGebruikOp is { } weg)
        {
            plan = [.. plan.Where(m => m.Geboekt || m.Maand <= weg)];
        }

        var laatste = desinvesteringen.LastOrDefault(x => x.Tegenboeking is null)?.Id;
        var (aanschaf, afgeschreven, rest) = Stand(a, geboekt, stappen);
        return new Activumdetail(a.Id, a.Nummer, Invoer(a), plan, rijen.ToDictionary(r => r.Maand, r => r.Boekstuk), a.BuitenGebruikOp,
            [.. desinvesteringen.Select(x => new Desinvesteringsregel(x.Id, x.Datum, x.Soort, x.Deel, x.Aanschaf, x.Afschrijving, x.Opbrengst, x.Boekstuk, x.Tegenboeking, x.Id == laatste))],
            rijen.Count > 0 || desinvesteringen.Count > 0, a.AangemaaktOp, a.AangemaaktDoor, aanschaf, afgeschreven, rest,
            a.BuitenGebruikOp is null ? plan.Count(m => !m.Geboekt && m.Maand <= VorigeMaand) : 0);
    }

    /// <summary>Aanschafwaarde, cumulatieve afschrijving en restwaarde nu: na de geboekte afschrijvingen en desinvesteringen.</summary>
    private static (decimal Aanschaf, decimal Afgeschreven, decimal Restwaarde) Stand(ActivumEntiteit a, IReadOnlyDictionary<DateOnly, decimal> geboekt, IReadOnlyList<Desinvesteringsstap> stappen) =>
        (a.Aanschafwaarde - stappen.Sum(d => d.Aanschaf), a.AfgeschrevenVoorBegin + geboekt.Values.Sum() - stappen.Sum(d => d.Afschrijving), a.Restwaarde - stappen.Sum(d => d.Restwaarde));

    /// <summary>Per actief de desinvesteringen die gelden (niet teruggedraaid).</summary>
    private static async Task<Dictionary<Guid, List<Desinvesteringsstap>>> Stappen(AdministratieDbContext db) =>
        (await db.Desinvesteringen.AsNoTracking().Where(d => d.Tegenboeking == null).OrderBy(d => d.Datum).ToListAsync())
            .GroupBy(d => d.ActivumId).ToDictionary(g => g.Key, g => g.Select(d => d.NaarStap()).ToList());

    private static ActivumInvoer Invoer(ActivumEntiteit a) =>
        new(a.Omschrijving, a.Aanschafdatum, a.Aanschafwaarde, a.Restwaarde, a.BeginAfschrijving, a.Looptijd, a.Methode, a.Activarekening, a.Afschrijvingsrekening,
            a.Kostenrekening, a.AfgeschrevenVoorBegin, a.Bron, a.Notitie);

    /// <summary>Per actief de geboekte afschrijvingen per maand.</summary>
    private static async Task<Dictionary<Guid, Dictionary<DateOnly, decimal>>> Geboekt(AdministratieDbContext db) =>
        (await db.Afschrijvingen.AsNoTracking().Select(x => new { x.ActivumId, x.Maand, x.Bedrag }).ToListAsync())
            .GroupBy(x => x.ActivumId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Maand, x => x.Bedrag));

    /// <summary>
    /// Nieuw of gewijzigd. Zijn er al afschrijvingen geboekt, dan liggen aanschaf, begin, methode en rekeningen vast;
    /// omschrijving, restwaarde, looptijd en notitie kunnen nog (wat er nog af moet, gaat over de resterende maanden).
    /// </summary>
    public async Task<Uitkomst<Guid>> Bewaar(Uitvoerder wie, Guid administratie, Guid? id, ActivumInvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var domein = invoer.NaarDomein();
        var fouten = domein.Controleer(schema).ToList();
        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        ActivumEntiteit? a = null;
        if (id is { } bestaand)
        {
            if (await db.Activa.FirstOrDefaultAsync(x => x.Id == bestaand) is not { } gevonden)
            {
                return Uitkomst<Guid>.Mislukt("Dit actief bestaat niet (meer).");
            }

            a = gevonden;
            var geboekt = await db.Afschrijvingen.AsNoTracking().Where(x => x.ActivumId == bestaand).ToDictionaryAsync(x => x.Maand, x => x.Bedrag);
            var stappen = (await Stappen(db)).GetValueOrDefault(bestaand) ?? [];
            var oud = a.NaarDomein();
            if (geboekt.Count > 0 || stappen.Count > 0 || a.BuitenGebruikOp is not null)
            {
                if (oud with { Omschrijving = domein.Omschrijving, Restwaarde = domein.Restwaarde, Looptijd = domein.Looptijd } != domein)
                {
                    return Uitkomst<Guid>.Mislukt("Er zijn al afschrijvingen geboekt: aanschaf, begin, methode en rekeningen liggen vast. Omschrijving, restwaarde en looptijd kunnen nog.");
                }

                if (a.BuitenGebruikOp is not null && (domein.Restwaarde != oud.Restwaarde || domein.Looptijd != oud.Looptijd))
                {
                    return Uitkomst<Guid>.Mislukt("Dit actief is buiten gebruik; alleen de omschrijving en de notitie kunnen nog.");
                }

                if (domein.Afschrijfbaar - domein.AfgeschrevenVoorBegin - geboekt.Values.Sum() - stappen.Sum(d => d.Aanschaf - d.Afschrijving - d.Restwaarde) < 0)
                {
                    return Uitkomst<Guid>.Mislukt("Er is al meer afgeschreven dan de aanschafwaarde min deze restwaarde.");
                }
            }
        }

        if (a is null)
        {
            var nummer = (await db.Activa.Select(x => (int?)x.Nummer).MaxAsync() ?? 0) + 1;
            a = new ActivumEntiteit { Id = Guid.NewGuid(), AdministratieId = administratie, Nummer = nummer, AangemaaktOp = tijd.GetUtcNow(), AangemaaktDoor = wie.Naam };
            db.Activa.Add(a);
        }

        (a.Omschrijving, a.Aanschafdatum, a.Aanschafwaarde, a.Restwaarde, a.BeginAfschrijving, a.Looptijd, a.Methode) =
            (domein.Omschrijving, domein.Aanschafdatum, domein.Aanschafwaarde, domein.Restwaarde, domein.EersteMaand, domein.Methode == Afschrijvingsmethode.Geen ? 0 : domein.Looptijd, domein.Methode);
        (a.Activarekening, a.Afschrijvingsrekening, a.Kostenrekening, a.AfgeschrevenVoorBegin) =
            (domein.Activarekening, domein.Afschrijvingsrekening, domein.Kostenrekening, domein.AfgeschrevenVoorBegin);
        (a.Bron, a.Notitie) = (Leeg(invoer.Bron, 100), Leeg(invoer.Notitie, 1000));
        await db.SaveChangesAsync();
        await audit.Log(wie, id is null ? AuditActies.ActivumToegevoegd : AuditActies.ActivumGewijzigd, $"{a.Nummer} {a.Omschrijving}",
            $"{a.Aanschafwaarde:0.00}, {a.Looptijd} maanden, rest {a.Restwaarde:0.00}", administratie);
        return Uitkomst<Guid>.Met(a.Id);
    }

    /// <summary>Verwijdert een actief; alleen als er nog niets voor geboekt is.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid id)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Activa.FirstOrDefaultAsync(x => x.Id == id) is not { } a)
        {
            return Uitkomst.Mislukt("Dit actief bestaat niet (meer).");
        }

        if (a.BuitenGebruikOp is not null || await db.Afschrijvingen.AnyAsync(x => x.ActivumId == id) || await db.Desinvesteringen.AnyAsync(x => x.ActivumId == id))
        {
            return Uitkomst.Mislukt("Voor dit actief is al geboekt; verwijderen kan niet meer.");
        }

        db.Activa.Remove(a);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ActivumVerwijderd, $"{a.Nummer} {a.Omschrijving}", null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Welke afschrijvingsboekingen <see cref="Afschrijven"/> zou maken: alle maanden tot en met <paramref name="tot"/>
    /// die nog niet geboekt zijn, per hele periode (maand, kwartaal of jaar) waarvan het eind niet na de maand van
    /// <paramref name="tot"/> ligt.
    /// </summary>
    /// <param name="alleen">Alleen dit actief (de knop op de activakaart); leeg = alle activa.</param>
    public async Task<IReadOnlyList<Afschrijvingsvoorstel>> TeAfschrijven(Uitvoerder wie, Guid administratie, DateOnly tot, Afschrijvingsfrequentie frequentie, Guid? alleen = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await Perioden(db, tot, frequentie, alleen)).Select(Voorstel)];
    }

    private static Afschrijvingsvoorstel Voorstel(Afschrijvingsperiode p) =>
        new(p.Datum, p.Omschrijving, p.Maanden.Select(m => m.Activum.Id).Distinct().Count(), p.Maanden.Sum(m => m.Bedrag));

    private sealed record Teboeken(ActivumEntiteit Activum, DateOnly Maand, decimal Bedrag);

    private sealed record Afschrijvingsperiode(DateOnly Datum, string Omschrijving, IReadOnlyList<Teboeken> Maanden);

    private static async Task<IReadOnlyList<Afschrijvingsperiode>> Perioden(AdministratieDbContext db, DateOnly tot, Afschrijvingsfrequentie frequentie, Guid? alleen = null)
    {
        var eind = Afsluiting.Maandeinde(tot);
        var activa = await db.Activa.Where(a => a.BuitenGebruikOp == null && a.Methode != Afschrijvingsmethode.Geen && (alleen == null || a.Id == alleen))
            .OrderBy(a => a.Nummer).ToListAsync();
        var geboekt = await Geboekt(db);
        var stappen = await Stappen(db);
        var maanden = activa.SelectMany(a => Afschrijvingsplan.Maak(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? [], stappen.GetValueOrDefault(a.Id))
                .Where(m => !m.Geboekt && m.Bedrag != 0 && Afschrijvingsplan.Periodeeinde(m.Maand, frequentie) <= eind)
                .Select(m => new Teboeken(a, m.Maand, m.Bedrag)))
            .ToList();
        return [.. maanden.GroupBy(m => Afschrijvingsplan.Periodeeinde(m.Maand, frequentie)).OrderBy(g => g.Key)
            .Select(g => new Afschrijvingsperiode(g.Key, $"Afschrijvingen {Periode(g.Key, frequentie)}", [.. g]))];
    }

    private static string Periode(DateOnly einde, Afschrijvingsfrequentie frequentie) => frequentie switch
    {
        Afschrijvingsfrequentie.Kwartaal => $"{(einde.Month + 2) / 3}e kwartaal {einde.Year}",
        Afschrijvingsfrequentie.Jaar => einde.Year.ToString(CultureInfo.InvariantCulture),
        _ => einde.ToString("MMMM yyyy", Nederlands),
    };

    /// <summary>
    /// Boekt de afschrijvingen tot en met <paramref name="tot"/> in het memoriaal: per periode één boeking op de laatste
    /// dag, met per actief de kosten (debet) tegen de cumulatieve afschrijving (credit). Niet in een afgesloten periode.
    /// Geeft het aantal boekingen.
    /// </summary>
    public async Task<Uitkomst<int>> Afschrijven(Uitvoerder wie, Guid administratie, DateOnly tot, Afschrijvingsfrequentie frequentie, Guid? alleen = null)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (Memoriaal(dagboeken) is not { } memoriaal)
        {
            return Uitkomst<int>.Mislukt("Deze administratie heeft geen memoriaal.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var perioden = await Perioden(db, tot, frequentie, alleen);
        if (perioden.Count == 0)
        {
            return Uitkomst<int>.Mislukt($"Er is tot en met {Weergave.Datum(Afsluiting.Maandeinde(tot))} niets meer af te schrijven.");
        }

        if (await BoekAfschrijvingen(db, administratie, memoriaal, schema, dagboeken, perioden, wie) is { Count: > 0 } fouten)
        {
            return Uitkomst<int>.Mislukt([.. fouten]);
        }

        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.AfschrijvingenGeboekt, $"t/m {Weergave.Datum(perioden[^1].Datum)}",
            $"{perioden.Count} {(perioden.Count == 1 ? "boeking" : "boekingen")}; {perioden.Sum(p => p.Maanden.Sum(m => m.Bedrag)):0.00}", administratie);
        return Uitkomst<int>.Met(perioden.Count);
    }

    private static Dagboek? Memoriaal(IReadOnlyList<Dagboek> dagboeken) =>
        dagboeken.Where(d => d.Soort == Dagboeksoort.Memoriaal).OrderBy(d => d.Code != "MEM").FirstOrDefault();

    /// <summary>Boekt de perioden in het memoriaal (binnen de transactie van de aanroeper); geeft de fouten, leeg = gelukt.</summary>
    private async Task<IReadOnlyList<string>> BoekAfschrijvingen(
        AdministratieDbContext db, Guid administratie, Dagboek memoriaal, Rekeningschema schema, IReadOnlyList<Dagboek> dagboeken, IReadOnlyList<Afschrijvingsperiode> perioden, Uitvoerder wie)
    {
        if (await GrootboekDienst.Afgesloten(db, administratie, [.. perioden.Select(p => p.Datum)]) is { } afgesloten)
        {
            return [afgesloten];
        }

        foreach (var periode in perioden)
        {
            var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, memoriaal.Code, periode.Datum.Year);
            var regels = periode.Maanden.GroupBy(m => m.Activum).SelectMany(g =>
            {
                var bedrag = g.Sum(m => m.Bedrag);
                var omschrijving = Kort($"Afschrijving {g.Key.Nummer} {g.Key.Omschrijving}");
                return new[] { new Journaalregel(g.Key.Kostenrekening, bedrag, omschrijving), new Journaalregel(g.Key.Afschrijvingsrekening, -bedrag, omschrijving) };
            });
            var post = new Journaalpost(memoriaal.Code, boekstuk, periode.Datum, periode.Omschrijving, [.. regels]);
            if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } fouten)
            {
                return fouten;
            }

            await grootboek.VoegToeAanKeten(db, administratie, post, wie);
            foreach (var m in periode.Maanden)
            {
                db.Afschrijvingen.Add(new Afschrijving { AdministratieId = administratie, ActivumId = m.Activum.Id, Maand = m.Maand, Bedrag = m.Bedrag, Boekstuk = $"{memoriaal.Code} {boekstuk}" });
            }

            await db.SaveChangesAsync();
        }

        return [];
    }

    /// <summary>
    /// Wat een desinvestering zou boeken: eerst de afschrijvingen die nog open staan tot en met de maand (per maand),
    /// dan de bedragen (naar rato bij een deel) en de memoriaalboeking. Wijzigt niets; met de fouten als het nog niet kan.
    /// </summary>
    public async Task<Desinvesteringsvoorstel> VoorstelDesinvestering(Uitvoerder wie, Guid administratie, Guid id, Desinvesteringsinvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return (await Desinvestering(db, administratie, id, invoer)).Voorstel;
    }

    private sealed record Desinvesteringsplan(Desinvesteringsvoorstel Voorstel, ActivumEntiteit? Activum, IReadOnlyList<Afschrijvingsperiode> Eerst, Dagboek? Memoriaal);

    private static async Task<Desinvesteringsplan> Desinvestering(AdministratieDbContext db, Guid administratie, Guid id, Desinvesteringsinvoer invoer)
    {
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        var fouten = new List<string>();
        if (await db.Activa.FirstOrDefaultAsync(x => x.Id == id) is not { } a)
        {
            return new(new([], null, "", [], ["Dit actief bestaat niet (meer)."]), null, [], null);
        }

        var memoriaal = Memoriaal(dagboeken);
        if (memoriaal is null)
        {
            fouten.Add("Deze administratie heeft geen memoriaal.");
        }

        if (a.BuitenGebruikOp is not null)
        {
            fouten.Add("Dit actief is al helemaal gedesinvesteerd.");
        }

        if (invoer.Datum < a.Aanschafdatum)
        {
            fouten.Add("Desinvesteren kan niet vóór de aanschaf.");
        }

        if (invoer.Deel is <= 0 or > 100)
        {
            fouten.Add("Het deel ligt tussen 0 en 100%.");
        }

        var opbrengst = invoer.Soort == Desinvesteringssoort.Sloop ? 0m : invoer.Opbrengst;
        if (opbrengst < 0)
        {
            fouten.Add("De opbrengst is 0 of meer.");
        }

        var resultaatrekening = schema.Zoek(invoer.Resultaatrekening?.Trim());
        if (resultaatrekening is not { IsBalans: false, Functie: Rekeningfunctie.Geen })
        {
            fouten.Add("Kies voor het boekresultaat een rekening in de winst-en-verliesrekening.");
        }

        var opbrengstrekening = opbrengst == 0 ? null : schema.Zoek(invoer.Opbrengstrekening?.Trim());
        if (opbrengst != 0 && opbrengstrekening is not { Functie: Rekeningfunctie.Geen })
        {
            fouten.Add(invoer.Soort == Desinvesteringssoort.Inruil
                ? "Kies de rekening waarop de inruilwaarde nu staat (geen bank, debiteuren, crediteuren of btw)."
                : "Kies de rekening waarop de opbrengst nu staat (geen bank, debiteuren, crediteuren of btw).");
        }

        // Eerst de afschrijvingen tot en met de maand van de desinvestering.
        var geboekt = await db.Afschrijvingen.AsNoTracking().Where(x => x.ActivumId == id).ToDictionaryAsync(x => x.Maand, x => x.Bedrag);
        var stappen = (await Stappen(db)).GetValueOrDefault(id) ?? [];
        var maand = new DateOnly(invoer.Datum.Year, invoer.Datum.Month, 1);
        var eerst = a.BuitenGebruikOp is null ? await Perioden(db, invoer.Datum, Afschrijvingsfrequentie.Maand, id) : [];
        if (eerst.Count > 0 && !invoer.EerstAfschrijven)
        {
            fouten.Add($"Boek eerst de afschrijvingen tot en met {maand.ToString("MMMM yyyy", Nederlands)}.");
        }

        var (aanschaf, afgeschreven, rest) = Stand(a, geboekt, stappen);
        afgeschreven += eerst.Sum(p => p.Maanden.Sum(m => m.Bedrag));
        if (await GrootboekDienst.Afgesloten(db, administratie, invoer.Datum) is { } afgesloten)
        {
            fouten.Add(afgesloten);
        }

        if (fouten.Count > 0 && (aanschaf <= 0 || invoer.Deel is <= 0 or > 100))
        {
            return new(new([.. eerst.Select(Voorstel)], null, "", [], fouten), a, eerst, memoriaal);
        }

        var bedragen = Boekhouding.Activa.Desinvestering.Bereken(aanschaf, afgeschreven, rest, invoer.Deel / 100m, opbrengst);
        var omschrijving = Kort(Boekhouding.Activa.Desinvestering.Omschrijving(invoer.Soort, invoer.Deel / 100m, a.Nummer, a.Omschrijving));
        IReadOnlyList<Voorstelregel> boeking = [];
        if (resultaatrekening is not null && (opbrengst == 0 || opbrengstrekening is not null))
        {
            boeking = [.. Boekhouding.Activa.Desinvestering.Regels(a.NaarDomein(), bedragen, opbrengstrekening?.Nummer, resultaatrekening.Nummer, omschrijving)
                .Select(r => new Voorstelregel(r.Rekeningnummer, schema.Zoek(r.Rekeningnummer)?.Naam ?? r.Rekeningnummer, r.Omschrijving, r.Bedrag))];
        }

        return new(new([.. eerst.Select(Voorstel)], bedragen, omschrijving, boeking, fouten), a, eerst, memoriaal);
    }

    /// <summary>
    /// Desinvesteert een actief (geheel of een deel): verkoop, inruil of sloop. Boekt zo nodig eerst de afschrijvingen
    /// tot en met die maand (per maand), dan één memoriaalboeking op de datum die het deel van de aanschafwaarde en de
    /// cumulatieve afschrijving van de balans haalt, de opbrengst van zijn rekening haalt en het boekresultaat boekt.
    /// Bij een deel gaat het plan verder met de rest.
    /// </summary>
    public async Task<Uitkomst> Desinvesteer(Uitvoerder wie, Guid administratie, Guid id, Desinvesteringsinvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var plan = await Desinvestering(db, administratie, id, invoer);
        if (plan.Voorstel.Fouten.Count > 0 || plan.Voorstel.Bedragen is not { } bedragen || plan.Activum is not { } a || plan.Memoriaal is not { } memoriaal)
        {
            return Uitkomst.Mislukt([.. plan.Voorstel.Fouten.DefaultIfEmpty("Deze desinvestering kan niet worden geboekt.")]);
        }

        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (plan.Eerst.Count > 0 && await BoekAfschrijvingen(db, administratie, memoriaal, schema, dagboeken, plan.Eerst, wie) is { Count: > 0 } afschrijffouten)
        {
            return Uitkomst.Mislukt([.. afschrijffouten]);
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, memoriaal.Code, invoer.Datum.Year);
        var post = new Journaalpost(memoriaal.Code, boekstuk, invoer.Datum, plan.Voorstel.Omschrijving,
            [.. plan.Voorstel.Boeking.Select(r => new Journaalregel(r.Rekening, r.Bedrag, r.Omschrijving))]);
        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        var opbrengst = invoer.Soort == Desinvesteringssoort.Sloop ? null : invoer.Opbrengstrekening?.Trim();
        db.Desinvesteringen.Add(new ActivumDesinvestering
        {
            AdministratieId = administratie,
            ActivumId = a.Id,
            Datum = invoer.Datum,
            Soort = invoer.Soort,
            Deel = invoer.Deel,
            Aanschaf = bedragen.Aanschaf,
            Afschrijving = bedragen.Afschrijving,
            Restwaarde = bedragen.Restwaarde,
            Opbrengst = bedragen.Opbrengst,
            Opbrengstrekening = bedragen.Opbrengst == 0 ? null : opbrengst,
            Resultaatrekening = invoer.Resultaatrekening!.Trim(),
            Boekstuk = $"{memoriaal.Code} {boekstuk}",
            GeboektOp = tijd.GetUtcNow(),
            GeboektDoor = wie.Naam,
        });
        if (invoer.Deel == 100)
        {
            a.BuitenGebruikOp = invoer.Datum;
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.ActivumGedesinvesteerd, $"{a.Nummer} {a.Omschrijving}",
            $"{post.Omschrijving}; {Weergave.Datum(invoer.Datum)}; boekwaarde {bedragen.Boekwaarde:0.00}, opbrengst {bedragen.Opbrengst:0.00}, resultaat {bedragen.Resultaat:0.00}; {memoriaal.Code} {boekstuk}"
            + (plan.Eerst.Count > 0 ? $"; eerst {plan.Eerst.Count} afschrijvingsboeking(en)" : ""), administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Draait de laatste desinvestering van een actief terug: een tegenboeking op dezelfde datum; het actief staat weer
    /// zoals ervoor (de afschrijvingen die eerst zijn geboekt, blijven staan).
    /// </summary>
    public async Task<Uitkomst> TerugdraaienDesinvestering(Uitvoerder wie, Guid administratie, long desinvestering)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await db.Desinvesteringen.FirstOrDefaultAsync(d => d.Id == desinvestering && d.Tegenboeking == null) is not { } d)
        {
            return Uitkomst.Mislukt("Deze desinvestering bestaat niet (meer) of is al teruggedraaid.");
        }

        if (await db.Desinvesteringen.AnyAsync(x => x.ActivumId == d.ActivumId && x.Tegenboeking == null && (x.Datum > d.Datum || (x.Datum == d.Datum && x.Id > d.Id))))
        {
            return Uitkomst.Mislukt("Draai eerst de latere desinvestering van dit actief terug.");
        }

        if (await db.Afschrijvingen.AnyAsync(x => x.ActivumId == d.ActivumId && x.Maand > d.Datum))
        {
            return Uitkomst.Mislukt("Na deze desinvestering is er al afgeschreven; die afschrijvingen gaan uit van wat er toen over was.");
        }

        if (await GrootboekDienst.GeldendeBoeking(db, d.Boekstuk) is not { Regels.Count: > 0 } boeking)
        {
            return Uitkomst.Mislukt($"Boeking {d.Boekstuk} bestaat niet meer.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, boeking.Datum) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, boeking.Dagboek, boeking.Datum.Year);
        var tegen = boeking.NaarDomein().Tegenboeking(boekstuk, boeking.Datum, Kort($"Terugdraaien {d.Boekstuk}: {boeking.Omschrijving}"));
        (await grootboek.VoegToeAanKeten(db, administratie, tegen, wie)).Herstelt = boeking.Id;
        d.Tegenboeking = $"{boeking.Dagboek} {boekstuk}";
        var a = await db.Activa.SingleAsync(x => x.Id == d.ActivumId);
        a.BuitenGebruikOp = null;
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.DesinvesteringTeruggedraaid, $"{a.Nummer} {a.Omschrijving}", $"{d.Boekstuk}; tegenboeking {d.Tegenboeking}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>De rekeningen van de vorige desinvestering in deze administratie, als voorstel (opbrengst, resultaat).</summary>
    public async Task<(string? Opbrengst, string? Resultaat)> VorigeDesinvesteringsrekeningen(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var vorige = await db.Desinvesteringen.AsNoTracking().OrderByDescending(d => d.Id).Select(d => new { d.Opbrengstrekening, d.Resultaatrekening }).FirstOrDefaultAsync();
        return (vorige?.Opbrengstrekening, vorige?.Resultaatrekening);
    }

    /// <summary>
    /// De activastaat van een jaar per activarekening: begin, investeringen, afschrijvingen, desinvesteringen en eind,
    /// uit de geboekte afschrijvingen, met het saldo in het grootboek op 31 december ter controle.
    /// </summary>
    public async Task<IReadOnlyList<Activastaatgroep>> Activastaat(Uitvoerder wie, Guid administratie, int jaar)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var activa = await db.Activa.AsNoTracking().OrderBy(a => a.Nummer).ToListAsync();
        var geboekt = await Geboekt(db);
        var stappen = await Stappen(db);
        var eind = new DateOnly(jaar, 12, 31);
        var groepen = new List<Activastaatgroep>();
        foreach (var groep in activa.GroupBy(a => (a.Activarekening, a.Afschrijvingsrekening)).OrderBy(g => g.Key.Activarekening, Rekeningnummers.Volgorde))
        {
            var posten = groep.Select(a => (a, Regel: Boekhouding.Activa.Activastaat.Regel(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? [], stappen.GetValueOrDefault(a.Id) ?? [], jaar)))
                .Where(p => p.Regel is not null).Select(p => new Activastaatpost(p.a.Id, p.a.Nummer, p.a.Omschrijving, p.Regel!)).ToList();
            if (posten.Count == 0)
            {
                continue;
            }

            var (aanschaf, afschrijving) = groep.Key;
            groepen.Add(new Activastaatgroep(aanschaf, schema.Zoek(aanschaf)?.Naam ?? aanschaf, afschrijving, posten,
                posten.Aggregate(Activastaatregel.Nul, (som, p) => som + p.Regel),
                await Saldo(db, aanschaf, eind), -await Saldo(db, afschrijving, eind)));
        }

        return groepen;
    }

    private static async Task<decimal> Saldo(AdministratieDbContext db, string rekening, DateOnly tot) =>
        (await db.GeldendeRegels.AsNoTracking().Where(r => r.Rekeningnummer == rekening && db.Boekingen.Any(b => b.Id == r.BoekingId && b.Datum <= tot))
            .Select(r => r.Bedrag).ToListAsync()).Sum();

    /// <summary>
    /// Aanschaffingen die al op een activarekening staan (debet, bijv. via de inkoopfactuur of de beginbalans) en nog
    /// niet als actief zijn opgenomen, met een voorstel: omschrijving, datum, bedrag en de rekeningen die erbij horen.
    /// Een activarekening is een balansrekening met een RGS-code voor de aanschafwaarde van vaste activa (eindigt op
    /// <c>Vvp</c>), of een rekening die al bij een actief in gebruik is.
    /// </summary>
    public async Task<IReadOnlyList<Activakandidaat>> Kandidaten(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var activa = await db.Activa.AsNoTracking().ToListAsync();
        var rekeningen = schema.Alle.Where(r => IsActivarekening(r) || activa.Any(a => a.Activarekening == r.Nummer)).Select(r => r.Nummer).ToList();
        if (rekeningen.Count == 0)
        {
            return [];
        }

        var desinvesteringen = await db.Desinvesteringen.AsNoTracking().Select(d => new { d.Boekstuk, d.Tegenboeking }).ToListAsync();
        var bronnen = activa.Select(a => a.Bron).Concat(desinvesteringen.Select(d => d.Boekstuk)).Concat(desinvesteringen.Select(d => d.Tegenboeking)).OfType<string>().ToHashSet();
        var regels = await db.GeldendeRegels.AsNoTracking().Where(r => rekeningen.Contains(r.Rekeningnummer) && r.Bedrag > 0)
            .Join(db.Boekingen.AsNoTracking(), r => r.BoekingId, b => b.Id, (r, b) => new { b.Dagboek, b.Boekstuk, b.Datum, b.Omschrijving, Regel = r.Omschrijving, r.Rekeningnummer, r.Bedrag, b.Herstelt })
            .ToListAsync();
        return [.. regels.Where(r => r.Herstelt is null && !bronnen.Contains($"{r.Dagboek} {r.Boekstuk}"))
            .OrderByDescending(r => r.Datum)
            .Select(r =>
            {
                var omschrijving = Kort(string.IsNullOrWhiteSpace(r.Regel) ? r.Omschrijving : r.Regel);
                var (afschrijving, kosten, looptijd) = Rekeningen(schema, activa, r.Rekeningnummer);
                var voorstel = new ActivumInvoer(omschrijving, r.Datum, r.Bedrag, 0m, new DateOnly(r.Datum.Year, r.Datum.Month, 1), looptijd, Afschrijvingsmethode.Lineair,
                    r.Rekeningnummer, afschrijving ?? "", kosten ?? "", 0m, $"{r.Dagboek} {r.Boekstuk}");
                return new Activakandidaat($"{r.Dagboek} {r.Boekstuk}", r.Datum, omschrijving, r.Rekeningnummer, r.Bedrag, voorstel);
            })];
    }

    /// <summary>
    /// De activarekeningen van de administratie (inventaris, gebouwen, vervoer- en transportmiddelen, immateriële vaste
    /// activa: RGS <c>BMva…Vvp</c> of <c>BIva…Vvp</c>, plus rekeningen die al bij een actief horen): bij een regel daarop
    /// biedt het pakket aan om direct te activeren.
    /// </summary>
    public async Task<IReadOnlySet<string>> Activarekeningen(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var gebruikt = await db.Activa.AsNoTracking().Select(a => a.Activarekening).Distinct().ToListAsync();
        return schema.Alle.Where(r => IsActivarekening(r) || gebruikt.Contains(r.Nummer)).Select(r => r.Nummer).ToHashSet();
    }

    /// <summary>De boekingen (dagboek en boekstuk) met een aanschaf op een activarekening die nog geen actief is.</summary>
    public async Task<IReadOnlySet<string>> TeActiveren(Uitvoerder wie, Guid administratie) =>
        (await Kandidaten(wie, administratie)).Select(k => k.Boekstuk).ToHashSet();

    /// <summary>De activa die uit deze boeking (bijv. "INK 2026-0012") zijn gemaakt.</summary>
    public async Task<IReadOnlyList<(Guid Id, int Nummer, string Omschrijving)>> UitBoeking(Uitvoerder wie, Guid administratie, string boekstuk)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await db.Activa.AsNoTracking().Where(a => a.Bron == boekstuk).OrderBy(a => a.Nummer).Select(a => new { a.Id, a.Nummer, a.Omschrijving }).ToListAsync())
            .Select(a => (a.Id, a.Nummer, a.Omschrijving))];
    }

    /// <summary>
    /// Direct activeren: van elke aanschaf op een activarekening in deze boeking een actief met het voorstel (rekeningen
    /// uit RGS of het vorige actief op die rekening, looptijd idem, afschrijven vanaf de maand van aanschaf). Looptijd en
    /// restwaarde zijn daarna op de kaart aan te passen zolang er nog niet is afgeschreven.
    /// </summary>
    public async Task<Uitkomst<IReadOnlyList<Guid>>> Activeer(Uitvoerder wie, Guid administratie, string boekstuk)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var kandidaten = (await Kandidaten(wie, administratie)).Where(k => k.Boekstuk == boekstuk).ToList();
        if (kandidaten.Count == 0)
        {
            return Uitkomst<IReadOnlyList<Guid>>.Mislukt($"In {boekstuk} staat geen aanschaf op een activarekening die nog geen actief is.");
        }

        var ids = new List<Guid>();
        foreach (var k in kandidaten)
        {
            var uitkomst = await Bewaar(wie, administratie, null, k.Voorstel);
            if (!uitkomst.IsGelukt)
            {
                return Uitkomst<IReadOnlyList<Guid>>.Mislukt([$"{k.Omschrijving} ({k.Rekening}): {string.Join(" ", uitkomst.Fouten)}"]);
            }

            ids.Add(uitkomst.Waarde);
        }

        return Uitkomst<IReadOnlyList<Guid>>.Met(ids);
    }

    /// <summary>De afschrijvings- en kostenrekening en looptijd die bij een activarekening passen (voor het formulier).</summary>
    public async Task<(string? Afschrijving, string? Kosten, int Looptijd)> Rekeningvoorstel(Uitvoerder wie, Guid administratie, string activarekening)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        return Rekeningen(schema, await db.Activa.AsNoTracking().ToListAsync(), activarekening.Trim());
    }

    /// <summary>Een rekening voor de aanschafwaarde van vaste activa (RGS: <c>BMva…Vvp</c> of <c>BIva…Vvp</c>).</summary>
    private static bool IsActivarekening(Rekening r) =>
        r.IsBalans && r.RgsCode is { Length: > 4 } rgs && (rgs.StartsWith("BMva", StringComparison.Ordinal) || rgs.StartsWith("BIva", StringComparison.Ordinal)) && rgs.EndsWith("Vvp", StringComparison.Ordinal);

    /// <summary>
    /// De afschrijvings- en kostenrekening en de looptijd die bij een activarekening horen: zoals bij het laatste actief
    /// op die rekening, anders uit RGS (<c>BMvaBeiVvp</c> → <c>BMvaBeiCae</c> en <c>WAfsAmvBei</c>), met 60 maanden.
    /// </summary>
    internal static (string? Afschrijving, string? Kosten, int Looptijd) Rekeningen(Rekeningschema schema, IReadOnlyList<ActivumEntiteit> activa, string activarekening)
    {
        if (activa.Where(a => a.Activarekening == activarekening).OrderByDescending(a => a.Nummer).FirstOrDefault() is { } eerder)
        {
            return (eerder.Afschrijvingsrekening, eerder.Kostenrekening, eerder.Looptijd > 0 ? eerder.Looptijd : 60);
        }

        if (schema.Zoek(activarekening)?.RgsCode is not { Length: > 7 } rgs || !rgs.EndsWith("Vvp", StringComparison.Ordinal))
        {
            return (null, null, 60);
        }

        var stam = rgs[..^3];
        var soort = rgs[4..7];
        var kosten = (rgs.StartsWith("BIva", StringComparison.Ordinal) ? "WAfsAiv" : "WAfsAmv") + soort;
        return (schema.Alle.FirstOrDefault(r => r.RgsCode == stam + "Cae")?.Nummer, schema.Alle.FirstOrDefault(r => r.RgsCode == kosten)?.Nummer, 60);
    }

    /// <summary>Hoeveel activa afschrijvingen hebben tot en met <paramref name="tot"/> die nog niet geboekt zijn (voor het afsluiten van een periode).</summary>
    internal static async Task<int> Achterstallig(AdministratieDbContext db, DateOnly tot)
    {
        var maand = new DateOnly(tot.Year, tot.Month, 1);
        var activa = await db.Activa.AsNoTracking().Where(a => a.BuitenGebruikOp == null && a.Methode != Afschrijvingsmethode.Geen).ToListAsync();
        var geboekt = await Geboekt(db);
        var stappen = await Stappen(db);
        return activa.Count(a => Afschrijvingsplan.Maak(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? [], stappen.GetValueOrDefault(a.Id)).Any(m => !m.Geboekt && m.Bedrag != 0 && m.Maand <= maand));
    }

    private static string Kort(string tekst) => tekst.Length > 200 ? tekst[..200] : tekst;

    private static string? Leeg(string? tekst, int lengte) =>
        string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim() is var t && t.Length > lengte ? t[..lengte] : tekst.Trim();
}
