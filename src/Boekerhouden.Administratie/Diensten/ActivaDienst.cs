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

/// <summary>Een actief in de lijst.</summary>
/// <param name="Afgeschreven">Totaal geboekt (ook vóór de eerste maand).</param>
/// <param name="PerMaand">De afschrijving van de volgende maand volgens het plan; 0 als hij klaar is.</param>
/// <param name="Achterstand">Maanden tot en met de vorige maand die nog niet geboekt zijn.</param>
public sealed record Activaregel(
    Guid Id, int Nummer, string Omschrijving, DateOnly Aanschafdatum, decimal Aanschafwaarde, decimal Afgeschreven, decimal PerMaand,
    string Activarekening, DateOnly? BuitenGebruikOp, int Achterstand)
{
    public decimal Boekwaarde => BuitenGebruikOp is null ? Aanschafwaarde - Afgeschreven : 0m;
}

/// <summary>Een actief met zijn plan; <see cref="Vast"/> als er al afschrijvingen geboekt zijn (aanschaf en rekeningen liggen dan vast).</summary>
/// <param name="Boekstukken">Per geboekte maand de boeking, bijv. "MEM 2026-0040".</param>
public sealed record Activumdetail(
    Guid Id, int Nummer, ActivumInvoer Invoer, IReadOnlyList<Afschrijvingsmaand> Plan, IReadOnlyDictionary<DateOnly, string> Boekstukken,
    DateOnly? BuitenGebruikOp, decimal? Opbrengst, string? Desinvesteringsboeking, bool Vast, DateTimeOffset AangemaaktOp, string AangemaaktDoor);

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
/// memoriaal (per maand, kwartaal of jaar), buiten gebruik stellen met het boekresultaat, de activastaat met de
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
        var vorigeMaand = new DateOnly(Vandaag.Year, Vandaag.Month, 1).AddMonths(-1);
        return [.. activa.Select(a =>
        {
            var plan = Afschrijvingsplan.Maak(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? []);
            var afgeschreven = a.AfgeschrevenVoorBegin + plan.Where(m => m.Geboekt).Sum(m => m.Bedrag);
            var open = a.BuitenGebruikOp is null ? plan.Where(m => !m.Geboekt).ToList() : [];
            return new Activaregel(a.Id, a.Nummer, a.Omschrijving, a.Aanschafdatum, a.Aanschafwaarde, afgeschreven, open.FirstOrDefault()?.Bedrag ?? 0m,
                a.Activarekening, a.BuitenGebruikOp, open.Count(m => m.Maand <= vorigeMaand));
        })];
    }

    public async Task<Activumdetail?> Open(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Activa.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) is not { } a)
        {
            return null;
        }

        var rijen = await db.Afschrijvingen.AsNoTracking().Where(x => x.ActivumId == id).ToListAsync();
        var plan = Afschrijvingsplan.Maak(a.NaarDomein(), rijen.ToDictionary(r => r.Maand, r => r.Bedrag));
        if (a.BuitenGebruikOp is { } weg)
        {
            plan = [.. plan.Where(m => m.Geboekt || m.Maand <= weg)];
        }

        return new Activumdetail(a.Id, a.Nummer, Invoer(a), plan, rijen.ToDictionary(r => r.Maand, r => r.Boekstuk), a.BuitenGebruikOp, a.Opbrengst,
            a.Desinvesteringsboeking, rijen.Count > 0, a.AangemaaktOp, a.AangemaaktDoor);
    }

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
            var oud = a.NaarDomein();
            if (geboekt.Count > 0 || a.BuitenGebruikOp is not null)
            {
                if (oud with { Omschrijving = domein.Omschrijving, Restwaarde = domein.Restwaarde, Looptijd = domein.Looptijd } != domein)
                {
                    return Uitkomst<Guid>.Mislukt("Er zijn al afschrijvingen geboekt: aanschaf, begin, methode en rekeningen liggen vast. Omschrijving, restwaarde en looptijd kunnen nog.");
                }

                if (a.BuitenGebruikOp is not null && (domein.Restwaarde != oud.Restwaarde || domein.Looptijd != oud.Looptijd))
                {
                    return Uitkomst<Guid>.Mislukt("Dit actief is buiten gebruik; alleen de omschrijving en de notitie kunnen nog.");
                }

                if (domein.Afschrijfbaar - domein.AfgeschrevenVoorBegin - geboekt.Values.Sum() < 0)
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

        if (a.BuitenGebruikOp is not null || await db.Afschrijvingen.AnyAsync(x => x.ActivumId == id))
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
    public async Task<IReadOnlyList<Afschrijvingsvoorstel>> TeAfschrijven(Uitvoerder wie, Guid administratie, DateOnly tot, Afschrijvingsfrequentie frequentie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await Perioden(db, tot, frequentie)).Select(p => new Afschrijvingsvoorstel(p.Datum, p.Omschrijving, p.Maanden.Select(m => m.Activum.Id).Distinct().Count(), p.Maanden.Sum(m => m.Bedrag)))];
    }

    private sealed record Teboeken(ActivumEntiteit Activum, DateOnly Maand, decimal Bedrag);

    private sealed record Afschrijvingsperiode(DateOnly Datum, string Omschrijving, IReadOnlyList<Teboeken> Maanden);

    private static async Task<IReadOnlyList<Afschrijvingsperiode>> Perioden(AdministratieDbContext db, DateOnly tot, Afschrijvingsfrequentie frequentie)
    {
        var eind = Afsluiting.Maandeinde(tot);
        var activa = await db.Activa.Where(a => a.BuitenGebruikOp == null && a.Methode != Afschrijvingsmethode.Geen).OrderBy(a => a.Nummer).ToListAsync();
        var geboekt = await Geboekt(db);
        var maanden = activa.SelectMany(a => Afschrijvingsplan.Maak(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? [])
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
    public async Task<Uitkomst<int>> Afschrijven(Uitvoerder wie, Guid administratie, DateOnly tot, Afschrijvingsfrequentie frequentie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (dagboeken.Where(d => d.Soort == Dagboeksoort.Memoriaal).OrderBy(d => d.Code != "MEM").FirstOrDefault() is not { } memoriaal)
        {
            return Uitkomst<int>.Mislukt("Deze administratie heeft geen memoriaal.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var perioden = await Perioden(db, tot, frequentie);
        if (perioden.Count == 0)
        {
            return Uitkomst<int>.Mislukt($"Er is tot en met {Weergave.Datum(Afsluiting.Maandeinde(tot))} niets meer af te schrijven.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, [.. perioden.Select(p => p.Datum)]) is { } afgesloten)
        {
            return Uitkomst<int>.Mislukt(afgesloten);
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
                return Uitkomst<int>.Mislukt([.. fouten]);
            }

            await grootboek.VoegToeAanKeten(db, administratie, post, wie);
            foreach (var m in periode.Maanden)
            {
                db.Afschrijvingen.Add(new Afschrijving { AdministratieId = administratie, ActivumId = m.Activum.Id, Maand = m.Maand, Bedrag = m.Bedrag, Boekstuk = $"{memoriaal.Code} {boekstuk}" });
            }

            await db.SaveChangesAsync();
        }

        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.AfschrijvingenGeboekt, $"t/m {Weergave.Datum(perioden[^1].Datum)}",
            $"{perioden.Count} {(perioden.Count == 1 ? "boeking" : "boekingen")}; {perioden.Sum(p => p.Maanden.Sum(m => m.Bedrag)):0.00}", administratie);
        return Uitkomst<int>.Met(perioden.Count);
    }

    /// <summary>
    /// Stelt een actief buiten gebruik (verkoop, sloop): een memoriaalboeking op de datum die aanschaf en afschrijving
    /// van de balans haalt, de opbrengst van zijn rekening haalt en het boekresultaat boekt. De afschrijvingen tot en
    /// met die maand moeten eerst geboekt zijn.
    /// </summary>
    /// <param name="opbrengstrekening">Waar de opbrengst (zonder btw) nu op staat, bijv. een tussenrekening van de verkoopfactuur.</param>
    /// <param name="resultaatrekening">Waar de boekwinst of het boekverlies op komt (winst-en-verliesrekening).</param>
    public async Task<Uitkomst> BuitenGebruik(Uitvoerder wie, Guid administratie, Guid id, DateOnly datum, decimal opbrengst, string? opbrengstrekening, string resultaatrekening)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (await db.Activa.FirstOrDefaultAsync(x => x.Id == id) is not { } a)
        {
            return Uitkomst.Mislukt("Dit actief bestaat niet (meer).");
        }

        var fouten = new List<string>();
        if (a.BuitenGebruikOp is not null)
        {
            fouten.Add("Dit actief is al buiten gebruik.");
        }

        if (datum < a.Aanschafdatum)
        {
            fouten.Add("Buiten gebruik stellen kan niet vóór de aanschaf.");
        }

        if (opbrengst < 0)
        {
            fouten.Add("De opbrengst is 0 of meer.");
        }

        if (schema.Zoek(resultaatrekening?.Trim()) is not { IsBalans: false, Functie: Rekeningfunctie.Geen })
        {
            fouten.Add("Kies voor het boekresultaat een rekening in de winst-en-verliesrekening.");
        }

        if (opbrengst != 0 && schema.Zoek(opbrengstrekening?.Trim()) is not { Functie: Rekeningfunctie.Geen })
        {
            fouten.Add("Kies de rekening waarop de opbrengst nu staat (geen bank, debiteuren, crediteuren of btw).");
        }

        if (dagboeken.Where(d => d.Soort == Dagboeksoort.Memoriaal).OrderBy(d => d.Code != "MEM").FirstOrDefault() is not { } memoriaal)
        {
            fouten.Add("Deze administratie heeft geen memoriaal.");
            return Uitkomst.Mislukt([.. fouten]);
        }

        var geboekt = await db.Afschrijvingen.AsNoTracking().Where(x => x.ActivumId == id).ToDictionaryAsync(x => x.Maand, x => x.Bedrag);
        var maand = new DateOnly(datum.Year, datum.Month, 1);
        if (Afschrijvingsplan.Maak(a.NaarDomein(), geboekt).FirstOrDefault(m => !m.Geboekt && m.Maand <= maand) is { } open)
        {
            fouten.Add($"Boek eerst de afschrijvingen tot en met {maand.ToString("MMMM yyyy", Nederlands)} (vanaf {open.Maand.ToString("MMMM yyyy", Nederlands)} staat nog open).");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, datum) is { } afgesloten)
        {
            fouten.Add(afgesloten);
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        var afgeschreven = a.AfgeschrevenVoorBegin + geboekt.Values.Sum();
        var omschrijving = Kort($"Buiten gebruik {a.Nummer} {a.Omschrijving}");
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, memoriaal.Code, datum.Year);
        var post = new Journaalpost(memoriaal.Code, boekstuk, datum, omschrijving,
            Desinvestering.Regels(a.NaarDomein(), afgeschreven, opbrengst, opbrengstrekening?.Trim(), resultaatrekening!.Trim(), omschrijving));
        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        (a.BuitenGebruikOp, a.Opbrengst, a.Desinvesteringsboeking) = (datum, opbrengst, $"{memoriaal.Code} {boekstuk}");
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        var boekwaarde = a.Aanschafwaarde - afgeschreven;
        await audit.Log(wie, AuditActies.ActivumBuitenGebruik, $"{a.Nummer} {a.Omschrijving}",
            $"{Weergave.Datum(datum)}; boekwaarde {boekwaarde:0.00}, opbrengst {opbrengst:0.00}; {memoriaal.Code} {boekstuk}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Maakt buiten gebruik stellen ongedaan: een tegenboeking van de desinvestering op dezelfde datum.</summary>
    public async Task<Uitkomst> WeerInGebruik(Uitvoerder wie, Guid administratie, Guid id)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Activa.FirstOrDefaultAsync(x => x.Id == id) is not { BuitenGebruikOp: not null, Desinvesteringsboeking: { } stuk } a)
        {
            return Uitkomst.Mislukt("Dit actief is niet buiten gebruik.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await GrootboekDienst.GeldendeBoeking(db, stuk) is not { Regels.Count: > 0 } boeking)
        {
            return Uitkomst.Mislukt($"Boeking {stuk} bestaat niet meer.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, boeking.Datum) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, boeking.Dagboek, boeking.Datum.Year);
        var tegen = boeking.NaarDomein().Tegenboeking(boekstuk, boeking.Datum, Kort($"Terugdraaien {stuk}: {boeking.Omschrijving}"));
        (await grootboek.VoegToeAanKeten(db, administratie, tegen, wie)).Herstelt = boeking.Id;
        (a.BuitenGebruikOp, a.Opbrengst, a.Desinvesteringsboeking) = (null, null, null);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.ActivumWeerInGebruik, $"{a.Nummer} {a.Omschrijving}", $"tegenboeking {boeking.Dagboek} {boekstuk}", administratie);
        return Uitkomst.Gelukt;
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
        var eind = new DateOnly(jaar, 12, 31);
        var groepen = new List<Activastaatgroep>();
        foreach (var groep in activa.GroupBy(a => (a.Activarekening, a.Afschrijvingsrekening)).OrderBy(g => g.Key.Activarekening, Rekeningnummers.Volgorde))
        {
            var posten = groep.Select(a => (a, Regel: Boekhouding.Activa.Activastaat.Regel(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? [], a.BuitenGebruikOp, jaar)))
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

        var bronnen = activa.Select(a => a.Bron).Concat(activa.Select(a => a.Desinvesteringsboeking)).OfType<string>().ToHashSet();
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
        return activa.Count(a => Afschrijvingsplan.Maak(a.NaarDomein(), geboekt.GetValueOrDefault(a.Id) ?? []).Any(m => !m.Geboekt && m.Maand <= maand));
    }

    private static string Kort(string tekst) => tekst.Length > 200 ? tekst[..200] : tekst;

    private static string? Leeg(string? tekst, int lengte) =>
        string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim() is var t && t.Length > lengte ? t[..lengte] : tekst.Trim();
}
