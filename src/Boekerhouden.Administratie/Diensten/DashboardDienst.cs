using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat er op het dashboard van een administratie kan staan.</summary>
public enum Widget
{
    /// <summary>Saldo per bank- en kasrekening volgens het grootboek, met het laatste afschrift.</summary>
    Liquide,

    /// <summary>Te betalen (of terug te vragen) btw: de btw-rekeningen tot en met de afgelopen maand of het afgelopen kwartaal.</summary>
    Btw,

    /// <summary>Inkoopfacturen die nog verwerkt moeten worden.</summary>
    Inkoop,

    /// <summary>Bankmutaties die nog verwerkt moeten worden.</summary>
    Bankmutaties,

    Debiteuren,

    Crediteuren,

    /// <summary>Omzet, inkoop (of alle kosten) en resultaat per maand van het jaar, als grafiek.</summary>
    Maandgrafiek,

    /// <summary>Omzet, kosten en resultaat van dit jaar tot vandaag, naast dezelfde periode vorig jaar.</summary>
    Kerncijfers,

    /// <summary>Conceptfacturen en terugkerende facturen die gemaakt moeten worden.</summary>
    Verkoop,

    /// <summary>De saldo-aansluitingen die niet sluiten.</summary>
    Aansluitingen,

    /// <summary>Het saldo van alle bank- en kasrekeningen samen aan het eind van elke maand, de laatste twaalf maanden.</summary>
    Liquiditeit,
}

/// <summary>Een widget op het dashboard, met de keuze die erbij hoort (btw per maand of kwartaal; grafiek met inkoop of alle kosten).</summary>
public sealed record Widgetkeuze(Widget Soort, string? Optie = null)
{
    public const string Maand = "maand";
    public const string Kwartaal = "kwartaal";
    public const string Inkoop = "inkoop";
    public const string Kosten = "kosten";

    /// <summary>De keuzes per widget, de eerste is de standaard.</summary>
    public static IReadOnlyList<string> Opties(Widget soort) => soort switch
    {
        Widget.Btw => [Kwartaal, Maand],
        Widget.Maandgrafiek => [Inkoop, Kosten],
        _ => [],
    };

    /// <summary>De keuze, of de standaard als er niets (geldigs) is gekozen.</summary>
    public string? Geldig => Opties(Soort) is { Count: > 0 } opties ? opties.Contains(Optie ?? "", StringComparer.Ordinal) ? Optie : opties[0] : null;

    public override string ToString() => Geldig is { } optie ? $"{Soort.ToString().ToLowerInvariant()}:{optie}" : Soort.ToString().ToLowerInvariant();

    /// <summary>
    /// Leest een opgeslagen indeling (<c>liquide,btw:maand</c>); onbekende widgets en dubbele vallen weg, zodat een
    /// widget die later verdwijnt geen fout geeft.
    /// </summary>
    public static IReadOnlyList<Widgetkeuze> Lees(string? tekst)
    {
        var keuzes = new List<Widgetkeuze>();
        foreach (var deel in (tekst ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (naam, optie) = deel.IndexOf(':', StringComparison.Ordinal) is var i and >= 0 ? (deel[..i], deel[(i + 1)..]) : (deel, null);
            if (Enum.TryParse<Widget>(naam, ignoreCase: true, out var soort) && Enum.IsDefined(soort) && keuzes.All(k => k.Soort != soort))
            {
                var keuze = new Widgetkeuze(soort, optie);
                keuzes.Add(keuze with { Optie = keuze.Geldig });
            }
        }

        return keuzes;
    }

    public static string Schrijf(IEnumerable<Widgetkeuze> keuzes) => string.Join(',', keuzes.Select(k => k.ToString()));
}

/// <summary>Saldo van een bank- of kasrekening.</summary>
/// <param name="Afschrift">Datum en eindsaldo van het laatst ingelezen afschrift (alleen bij een bank met IBAN).</param>
public sealed record Liquiderekening(string Dagboek, string Naam, string Rekening, decimal Saldo, (DateOnly Datum, decimal Saldo)? Afschrift);

/// <summary>
/// De btw: het saldo van de btw-rekeningen tot en met de afgelopen periode (credit = te betalen) en wat er in de lopende
/// periode al bij komt.
/// </summary>
public sealed record Btwstand(Aangifteperiode Afgelopen, decimal Saldo, Aangifteperiode Lopend, decimal Lopende)
{
    /// <summary>Te betalen positief, terug te vragen negatief.</summary>
    public decimal TeBetalen => -Saldo;
}

/// <summary>Openstaande posten van debiteuren of crediteuren, als positieve bedragen.</summary>
public sealed record Openstaand(int Aantal, decimal Totaal, int AantalVervallen, decimal Vervallen);

/// <summary>Omzet, kosten en resultaat van 1 januari tot een datum.</summary>
public sealed record Periodecijfers(DateOnly Van, DateOnly Tot, decimal Omzet, decimal Kosten, decimal Resultaat);

/// <summary>
/// Het saldo van de bank- en kasrekeningen aan het eind van een maand (voor de lopende maand: vandaag), samen en per
/// rekening.
/// </summary>
public sealed record Liquiditeitsmaand(DateOnly Maand, DateOnly Tot, decimal Saldo, IReadOnlyList<(string Naam, decimal Saldo)> PerRekening);

/// <summary>
/// Alles wat het dashboard laat zien, altijd vanaf vandaag gerekend (los van het jaar dat elders in het pakket open
/// staat); een onderdeel is leeg als de widget niet gekozen is.
/// </summary>
public sealed record Dashboard(
    DateOnly Vandaag,
    IReadOnlyList<Liquiderekening>? Liquide = null,
    Btwstand? Btw = null,
    (int Aantal, decimal Totaal)? Inkoop = null,
    (int Aantal, int MogelijkDubbel, DateOnly? Oudste)? Bankmutaties = null,
    Openstaand? Debiteuren = null,
    Openstaand? Crediteuren = null,
    (int Jaar, IReadOnlyList<Maandcijfer> Maanden)? Maandgrafiek = null,
    (Periodecijfers Dit, Periodecijfers Vorig)? Kerncijfers = null,
    (int Concepten, int Terugkerend)? Verkoop = null,
    (int Totaal, IReadOnlyList<string> SluitenNiet)? Aansluitingen = null,
    IReadOnlyList<Liquiditeitsmaand>? Liquiditeit = null);

/// <summary>
/// Het dashboard van een administratie: per gebruiker een eigen keuze en volgorde van widgets, en de cijfers daarvoor.
/// Alles alleen-lezen (recht <see cref="Recht.Lezen"/>); de indeling is van de gebruiker zelf.
/// </summary>
public sealed class DashboardDienst(
    Toegang toegang,
    GrootboekDienst grootboek,
    BankDienst bank,
    InkoopDienst inkoop,
    AbonnementDienst abonnementen,
    AansluitingDienst aansluitingen,
    AuditDienst audit,
    TimeProvider tijd)
{
    /// <summary>De indeling voor wie nog niets heeft gekozen.</summary>
    public static IReadOnlyList<Widgetkeuze> Standaard { get; } =
    [
        new(Widget.Liquide),
        new(Widget.Bankmutaties),
        new(Widget.Inkoop),
        new(Widget.Btw, Widgetkeuze.Kwartaal),
        new(Widget.Maandgrafiek, Widgetkeuze.Inkoop),
        new(Widget.Liquiditeit),
        new(Widget.Debiteuren),
        new(Widget.Crediteuren),
        new(Widget.Kerncijfers),
    ];

    private DateOnly Vandaag => DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<Widgetkeuze>> Indeling(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var opgeslagen = wie.Id is { } gebruiker
            ? await db.Dashboardindelingen.AsNoTracking().Where(d => d.GebruikerId == gebruiker).Select(d => d.Widgets).FirstOrDefaultAsync()
            : null;
        return opgeslagen is null ? Standaard : Widgetkeuze.Lees(opgeslagen);
    }

    /// <summary>Bewaart de indeling van de gebruiker zelf; een lege lijst mag (dan staat er niets).</summary>
    public async Task<Uitkomst> BewaarIndeling(Uitvoerder wie, Guid administratie, IReadOnlyList<Widgetkeuze> keuzes)
    {
        ArgumentNullException.ThrowIfNull(keuzes);
        if (wie.Id is not { } gebruiker)
        {
            return Uitkomst.Mislukt("Alleen een gebruiker heeft een eigen dashboard.");
        }

        if (keuzes.Any(k => !Enum.IsDefined(k.Soort)) || keuzes.DistinctBy(k => k.Soort).Count() != keuzes.Count)
        {
            return Uitkomst.Mislukt("Elke widget kan één keer op het dashboard.");
        }

        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var tekst = Widgetkeuze.Schrijf(keuzes);
        var indeling = await db.Dashboardindelingen.FirstOrDefaultAsync(d => d.GebruikerId == gebruiker);
        if (indeling is null)
        {
            db.Dashboardindelingen.Add(indeling = new Dashboardindeling { AdministratieId = administratie, GebruikerId = gebruiker });
        }

        indeling.Widgets = tekst;
        indeling.GewijzigdOp = tijd.GetUtcNow();
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DashboardGewijzigd, details: tekst, administratie: administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>De cijfers voor de gekozen widgets.</summary>
    public async Task<Dashboard> Laad(Uitvoerder wie, Guid administratie, IReadOnlyList<Widgetkeuze> keuzes)
    {
        ArgumentNullException.ThrowIfNull(keuzes);
        var vandaag = Vandaag;
        var dashboard = new Dashboard(vandaag);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        foreach (var keuze in keuzes)
        {
            dashboard = keuze.Soort switch
            {
                Widget.Liquide => dashboard with { Liquide = await Liquide(wie, administratie, db, dagboeken, vandaag) },
                Widget.Liquiditeit => dashboard with { Liquiditeit = await Liquiditeit(db, schema, dagboeken, vandaag) },
                Widget.Btw => dashboard with { Btw = await Btw(db, schema, keuze.Geldig == Widgetkeuze.Maand ? Aangiftefrequentie.Maand : Aangiftefrequentie.Kwartaal, vandaag) },
                Widget.Inkoop => dashboard with { Inkoop = await TeVerwerkenInkoop(wie, administratie) },
                Widget.Bankmutaties => dashboard with { Bankmutaties = await TeVerwerkenBank(db) },
                Widget.Debiteuren => dashboard with { Debiteuren = await Open(db, Factuursoort.Verkoop, vandaag) },
                Widget.Crediteuren => dashboard with { Crediteuren = await Open(db, Factuursoort.Inkoop, vandaag) },
                Widget.Maandgrafiek => dashboard with { Maandgrafiek = (vandaag.Year, await Maandgrafiek(wie, administratie, vandaag.Year)) },
                Widget.Kerncijfers => dashboard with { Kerncijfers = await Kerncijfers(wie, administratie, vandaag) },
                Widget.Verkoop => dashboard with
                {
                    Verkoop = (await db.Verkoopdocumenten.CountAsync(v => v.Status == Verkoopstatus.Concept), await abonnementen.TeMaken(wie, administratie, vandaag)),
                },
                Widget.Aansluitingen => dashboard with { Aansluitingen = await Aansluitingen(wie, administratie) },
                _ => dashboard,
            };
        }

        return dashboard;
    }

    private static async Task<Dictionary<string, decimal>> Saldi(AdministratieDbContext db, IReadOnlyCollection<string> rekeningen, DateOnly? van, DateOnly tot)
    {
        var regels = await (from r in db.GeldendeRegels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where rekeningen.Contains(r.Rekeningnummer) && b.Datum <= tot && (van == null || b.Datum >= van)
                            select new { r.Rekeningnummer, r.Bedrag }).ToListAsync();
        return regels.GroupBy(r => r.Rekeningnummer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag), StringComparer.Ordinal);
    }

    /// <summary>De bank- en kasdagboeken met hun rekening.</summary>
    private static List<Dagboek> Liquidedagboeken(IReadOnlyList<Dagboek> dagboeken) =>
        [.. dagboeken.Where(d => d.Soort is Dagboeksoort.Bank or Dagboeksoort.Kas && d.Rekeningnummer is not null)];

    private async Task<IReadOnlyList<Liquiderekening>> Liquide(Uitvoerder wie, Guid administratie, AdministratieDbContext db, IReadOnlyList<Dagboek> dagboeken, DateOnly vandaag)
    {
        var liquide = Liquidedagboeken(dagboeken);
        var saldi = await Saldi(db, [.. liquide.Select(d => d.Rekeningnummer!)], null, vandaag);
        var standen = (await bank.Stand(wie, administratie)).ToDictionary(s => s.Dagboek, StringComparer.Ordinal);

        // Een kas zonder boekingen laten we weg; een bank altijd (ook als er nog niets is ingelezen).
        return
        [
            .. liquide
                .Where(d => d.Soort == Dagboeksoort.Bank || saldi.ContainsKey(d.Rekeningnummer!))
                .Select(d => new Liquiderekening(d.Code, d.Naam, d.Rekeningnummer!, saldi.GetValueOrDefault(d.Rekeningnummer!),
                    standen.GetValueOrDefault(d.Code) is { Tot: { } datum, Saldo: { } saldo } ? (datum, saldo) : null)),
        ];
    }

    private static async Task<Btwstand> Btw(AdministratieDbContext db, Rekeningschema schema, Aangiftefrequentie frequentie, DateOnly vandaag)
    {
        var rekeningen = schema.Alle.Where(r => r.Functie is Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw).Select(r => r.Nummer).ToList();
        var afgelopen = Aangifteperiode.Afgelopen(vandaag, frequentie);
        var lopend = afgelopen.Volgende;
        var saldo = (await Saldi(db, rekeningen, null, afgelopen.Tot)).Values.Sum();
        var erbij = (await Saldi(db, rekeningen, lopend.Van, lopend.Tot)).Values.Sum();
        return new Btwstand(afgelopen, saldo, lopend, -erbij);
    }

    private async Task<(int, decimal)> TeVerwerkenInkoop(Uitvoerder wie, Guid administratie)
    {
        var lijst = await inkoop.Lijst(wie, administratie, Documentstatus.TeVerwerken);
        return (lijst.Count, lijst.Sum(d => d.Totaal ?? 0m));
    }

    private static async Task<(int, int, DateOnly?)> TeVerwerkenBank(AdministratieDbContext db)
    {
        var open = await db.Banktransacties.AsNoTracking().Where(t => t.Status == Banktransactiestatus.TeVerwerken)
            .Select(t => new { t.Datum, t.MogelijkDubbel }).ToListAsync();
        return (open.Count, open.Count(t => t.MogelijkDubbel), open.Count == 0 ? null : open.Min(t => t.Datum));
    }

    private static async Task<Openstaand> Open(AdministratieDbContext db, Factuursoort soort, DateOnly vandaag)
    {
        var posten = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == soort && p.Openstaand != 0)
            .Select(p => new { p.Openstaand, p.Vervaldatum }).ToListAsync();
        var vervallen = posten.Where(p => p.Vervaldatum < vandaag).ToList();
        return new Openstaand(posten.Count, Math.Abs(posten.Sum(p => p.Openstaand)), vervallen.Count, Math.Abs(vervallen.Sum(p => p.Openstaand)));
    }

    /// <summary>
    /// De saldi van alle bank- en kasrekeningen (de rekeningen van de bank- en kasdagboeken en die met de functie bank
    /// of kas) aan het eind van elk van de laatste twaalf maanden, de lopende maand tot en met vandaag.
    /// </summary>
    private static async Task<IReadOnlyList<Liquiditeitsmaand>> Liquiditeit(AdministratieDbContext db, Rekeningschema schema, IReadOnlyList<Dagboek> dagboeken, DateOnly vandaag)
    {
        var rekeningen = Liquidedagboeken(dagboeken).Select(d => d.Rekeningnummer!)
            .Concat(schema.Alle.Where(r => r.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Kas).Select(r => r.Nummer))
            .Distinct(StringComparer.Ordinal).ToList();
        var regels = await (from r in db.GeldendeRegels
                            join b in db.Boekingen on r.BoekingId equals b.Id
                            where rekeningen.Contains(r.Rekeningnummer) && b.Datum <= vandaag
                            select new { r.Rekeningnummer, b.Datum, r.Bedrag }).ToListAsync();
        var eerste = new DateOnly(vandaag.Year, vandaag.Month, 1).AddMonths(-11);
        var namen = rekeningen.ToDictionary(r => r, r => schema.Zoek(r)?.Naam ?? r, StringComparer.Ordinal);
        return
        [
            .. Enumerable.Range(0, 12).Select(i =>
            {
                var maand = eerste.AddMonths(i);
                var tot = i == 11 ? vandaag : maand.AddMonths(1).AddDays(-1);
                var per = rekeningen
                    .Where(r => regels.Any(x => x.Rekeningnummer == r && x.Datum <= tot))
                    .Select(r => (Naam: namen[r], Saldo: regels.Where(x => x.Rekeningnummer == r && x.Datum <= tot).Sum(x => x.Bedrag)))
                    .ToList();
                return new Liquiditeitsmaand(maand, tot, per.Sum(r => r.Saldo), per);
            }),
        ];
    }

    private async Task<IReadOnlyList<Maandcijfer>> Maandgrafiek(Uitvoerder wie, Guid administratie, int jaar) =>
        Maandcijfers.Uit(await grootboek.Maandoverzicht(wie, administratie, new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31)));

    /// <summary>Van 1 januari tot en met vandaag, naast dezelfde periode vorig jaar.</summary>
    private async Task<(Periodecijfers, Periodecijfers)> Kerncijfers(Uitvoerder wie, Guid administratie, DateOnly vandaag)
    {
        var vorigTot = vandaag.Month == 2 && vandaag.Day == 29 ? new DateOnly(vandaag.Year - 1, 2, 28) : vandaag.AddYears(-1);
        return (await Periode(wie, administratie, new DateOnly(vandaag.Year, 1, 1), vandaag),
            await Periode(wie, administratie, new DateOnly(vandaag.Year - 1, 1, 1), vorigTot));
    }

    private async Task<Periodecijfers> Periode(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        var maanden = Maandcijfers.Uit(await grootboek.Maandoverzicht(wie, administratie, van, tot));
        return new Periodecijfers(van, tot, maanden.Sum(m => m.Omzet), maanden.Sum(m => m.Kosten), maanden.Sum(m => m.Resultaat));
    }

    private async Task<(int, IReadOnlyList<string>)> Aansluitingen(Uitvoerder wie, Guid administratie)
    {
        var lijst = await aansluitingen.Aansluitingen(wie, administratie);
        return (lijst.Count, [.. lijst.Where(a => !a.Sluit).Select(a => a.Onderwerp)]);
    }
}
