using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Vpb;

public enum Fiscaleregelsoort
{
    Kop,
    Regel,
    Subtotaal,
    Totaal,
}

/// <summary>
/// Een bedrag in de fiscale positie. <paramref name="Sleutel"/>: met de hand aan te passen (de totalen niet).
/// <paramref name="Berekend"/>: het berekende bedrag als het met de hand is aangepast, anders leeg.
/// </summary>
public sealed record Fiscalecel(decimal? Bedrag, string? Sleutel = null, decimal? Berekend = null, bool Percentage = false)
{
    public bool IsAangepast => Berekend is not null;
}

/// <summary>Een regel van de fiscale positie, met een bedrag (links) en een totaal (rechts), zoals op papier.</summary>
public sealed record Fiscaleregel(Fiscaleregelsoort Soort, string Omschrijving, Fiscalecel? Bedrag = null, Fiscalecel? Totaal = null);

/// <summary>Een rekening in een specificatie (niet of beperkt aftrekbaar), met het saldo over het jaar (kosten positief).</summary>
public sealed record Fiscalerekening(string Nummer, string Naam, decimal Bedrag);

/// <summary>
/// Wat de fiscale positie nodig heeft uit de administratie. <paramref name="Saldi"/>: per rekening de mutatie over het
/// jaar (debet positief); <paramref name="Betaald"/>: wat er in het jaar op de balansrekening(en) van de
/// vennootschapsbelasting is betaald; <paramref name="Investeringen"/>: de aanschafwaarde van de vaste activa die in het
/// jaar zijn aangeschaft.
/// </summary>
public sealed record Fiscalegegevens(int Jaar, Rekeningschema Schema, IReadOnlyDictionary<string, decimal> Saldi, decimal Betaald, decimal Investeringen);

/// <summary>
/// De fiscale positie van een BV over een jaar: van het resultaat voor belastingen via de fiscale correcties (bij: niet
/// en beperkt aftrekbare kosten e.d.; af: investeringsaftrek, deelnemingsvrijstelling e.d.) en de verliesverrekening naar
/// het belastbare bedrag, de vennootschapsbelasting per schijf (nihil bij verlies) en wat er na de voorlopige aanslagen
/// nog te betalen is. Elk ingevoerd getal en elk tarief is met de hand aan te passen; de totalen rekenen daarmee.
/// </summary>
public sealed class Fiscalepositie
{
    private static readonly System.Globalization.CultureInfo Nederlands = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    private Fiscalepositie(int jaar, Vpbtarieven tarieven, IReadOnlyList<Fiscaleregel> regels, IReadOnlyList<Fiscaleregel> uitgangspunten,
        IReadOnlyList<Fiscalerekening> nietAftrekbaar, IReadOnlyList<Fiscalerekening> beperkt, decimal vennootschapsbelasting, decimal teBetalen, decimal belastbaar)
    {
        (Jaar, Tarieven, Regels, Uitgangspunten, NietAftrekbaar, Beperkt) = (jaar, tarieven, regels, uitgangspunten, nietAftrekbaar, beperkt);
        (Vennootschapsbelasting, TeBetalen, Belastbaar) = (vennootschapsbelasting, teBetalen, belastbaar);
    }

    public int Jaar { get; }

    /// <summary>De tarieven zoals gebruikt (met de aanpassingen).</summary>
    public Vpbtarieven Tarieven { get; }

    public IReadOnlyList<Fiscaleregel> Regels { get; }

    /// <summary>Loonsom, investeringen, verrekenbare verliezen en de tarieven: alles waarmee gerekend is.</summary>
    public IReadOnlyList<Fiscaleregel> Uitgangspunten { get; }

    public IReadOnlyList<Fiscalerekening> NietAftrekbaar { get; }

    public IReadOnlyList<Fiscalerekening> Beperkt { get; }

    /// <summary>De vennootschapsbelasting over het jaar; nihil bij verlies.</summary>
    public decimal Vennootschapsbelasting { get; }

    /// <summary>Na de betaalde voorlopige aanslagen: positief = nog te betalen, negatief = terug te ontvangen.</summary>
    public decimal TeBetalen { get; }

    /// <summary>Het belastbare bedrag (negatief = verlies).</summary>
    public decimal Belastbaar { get; }

    public static Fiscalepositie Bereken(Fiscalegegevens gegevens, IReadOnlyDictionary<string, decimal>? aanpassingen = null)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        aanpassingen ??= new Dictionary<string, decimal>();
        var schema = gegevens.Schema;
        var basis = Vpbtarieven.Voor(gegevens.Jaar);
        var tarieven = basis.Met(aanpassingen);

        Fiscalecel Cel(string sleutel, decimal berekend, bool percentage = false) =>
            aanpassingen.TryGetValue(sleutel, out var b) ? new(b, sleutel, berekend, percentage) : new(berekend, sleutel, null, percentage);
        static Fiscalecel Vast(decimal bedrag) => new(bedrag);
        decimal Saldo(Rekening r) => gegevens.Saldi.GetValueOrDefault(r.Nummer);
        List<Fiscalerekening> Specificatie(Fiscaalsoort soort) =>
            [.. Fiscaleindeling.Rekeningen(schema, soort).Where(r => !r.IsBalans && Saldo(r) != 0).OrderBy(r => r.Nummer, Rekeningnummers.Volgorde)
                .Select(r => new Fiscalerekening(r.Nummer, r.Naam, Saldo(r)))];

        // Het resultaat voor belastingen: alle rekeningen van de winst-en-verliesrekening behalve de vennootschapsbelasting.
        var resultaat = -schema.Alle.Where(r => !r.IsBalans && Fiscaleindeling.Van(r) != Fiscaalsoort.Last).Sum(Saldo);
        var nietAftrekbaar = Specificatie(Fiscaalsoort.NietAftrekbaar);
        var beperkt = Specificatie(Fiscaalsoort.BeperktAftrekbaar);
        var loonsom = schema.Alle.Where(r => r.RgsCode?.StartsWith("WPerLes", StringComparison.Ordinal) == true).Sum(Saldo);

        // De uitgangspunten: met de hand aan te passen, en de tarieven van het jaar.
        var loonsomCel = Cel("vpb.loonsom", loonsom);
        var investeringenCel = Cel("vpb.investeringen", gegevens.Investeringen);
        var verliesCel = Cel("vpb.verlies", 0m);
        var uitgangspunten = new List<Fiscaleregel>
        {
            new(Fiscaleregelsoort.Regel, "Fiscale loonsom", loonsomCel),
            new(Fiscaleregelsoort.Regel, "Investeringen in het jaar (voor de investeringsaftrek)", investeringenCel),
            new(Fiscaleregelsoort.Regel, "Verrekenbare verliezen uit voorgaande jaren", verliesCel),
        };
        uitgangspunten.AddRange(Vpbtarieven.Velden.Select(v => new Fiscaleregel(Fiscaleregelsoort.Regel, v.Naam, Cel(v.Sleutel, basis.Waarde(v.Sleutel), v.Percentage))));

        var regels = new List<Fiscaleregel>();
        void Regel(string tekst, Fiscalecel? bedrag = null, Fiscalecel? totaal = null) => regels.Add(new(Fiscaleregelsoort.Regel, tekst, bedrag, totaal));
        void Kop(string tekst) => regels.Add(new(Fiscaleregelsoort.Kop, tekst));

        var resultaatCel = Cel("vpb.resultaat", resultaat);
        var correctie = Cel("vpb.correctie", 0m);
        Regel("Resultaat voor belastingen", totaal: resultaatCel);
        Regel("Correctie", totaal: correctie);
        var commercieel = (resultaatCel.Bedrag ?? 0m) + (correctie.Bedrag ?? 0m);
        regels.Add(new(Fiscaleregelsoort.Subtotaal, "Resultaat na correctie", Totaal: Vast(commercieel)));

        Kop("Bij:");
        var bij = new List<Fiscalecel>
        {
            Cel("vpb.nietaftrekbaar", nietAftrekbaar.Sum(r => r.Bedrag)),
            Cel("vpb.beperkt", tarieven.NietAftrekbaar(beperkt.Sum(r => r.Bedrag), loonsomCel.Bedrag ?? 0m)),
            Cel("vpb.bij.verschil", 0m),
            Cel("vpb.bij.desinvestering", 0m),
            Cel("vpb.bij.vrij", 0m),
        };
        Regel("Niet aftrekbare kosten", bij[0]);
        Regel("Beperkt aftrekbare kosten (niet aftrekbaar deel)", bij[1]);
        Regel("Verschillen commercieel - fiscaal", bij[2]);
        Regel("Desinvesteringsbijtelling", bij[3]);
        Regel("Overige bijtellingen", bij[4]);
        var totaalBij = bij.Sum(c => c.Bedrag ?? 0m);
        regels.Add(new(Fiscaleregelsoort.Subtotaal, "Totaal bij", Totaal: Vast(totaalBij)));

        Kop("Af:");
        var af = new List<Fiscalecel>
        {
            Cel("vpb.af.deelneming", 0m),
            Cel("vpb.af.vrijgesteld", 0m),
            Cel("vpb.af.kia", tarieven.Kia(investeringenCel.Bedrag ?? 0m)),
            Cel("vpb.af.giften", 0m),
            Cel("vpb.af.verschil", 0m),
            Cel("vpb.af.vrij", 0m),
        };
        Regel("Deelnemingsvrijstelling", af[0]);
        Regel("Vrijgestelde winstbestanddelen", af[1]);
        Regel("Investeringsaftrek", af[2]);
        Regel("Aftrekbare giften", af[3]);
        Regel("Verschillen commercieel - fiscaal", af[4]);
        Regel("Overige aftrekposten", af[5]);
        var totaalAf = af.Sum(c => c.Bedrag ?? 0m);
        regels.Add(new(Fiscaleregelsoort.Subtotaal, "Totaal af", Totaal: Vast(totaalAf)));

        var fiscaal = commercieel + totaalBij - totaalAf;
        regels.Add(new(Fiscaleregelsoort.Subtotaal, "Fiscaal resultaat", Totaal: Vast(fiscaal)));
        var verrekening = Cel("vpb.verliesverrekening", Math.Min(Math.Max(0m, verliesCel.Bedrag ?? 0m), Math.Max(0m, fiscaal)));
        Regel("Af: verrekening verliezen voorgaande jaren", totaal: verrekening);
        var belastbaar = fiscaal - (verrekening.Bedrag ?? 0m);
        regels.Add(new(Fiscaleregelsoort.Subtotaal, $"Belastbaar bedrag {gegevens.Jaar}", Totaal: Vast(belastbaar)));

        // Afgerond naar beneden op een veelvoud van € 5, zoals in de aangifte.
        var grondslag = belastbaar > 0 ? Math.Floor(belastbaar / 5m) * 5m : 0m;
        Regel("Bedrag waarover belasting wordt berekend", totaal: Vast(grondslag));
        var (laag, hoog) = tarieven.Belasting(grondslag);
        Kop("Berekende vennootschapsbelasting");
        Regel(string.Create(Nederlands, $"{tarieven.TariefLaag:0.##}% tot en met {tarieven.Schijfgrens:N0}"), Vast(laag));
        Regel(string.Create(Nederlands, $"{tarieven.TariefHoog:0.##}% over het meerdere"), Vast(hoog));
        var vpb = laag + hoog;
        regels.Add(new(Fiscaleregelsoort.Totaal, belastbaar <= 0 ? $"Vennootschapsbelasting {gegevens.Jaar}: nihil (verlies)" : $"Vennootschapsbelasting {gegevens.Jaar}",
            Totaal: Vast(vpb)));

        Kop("Balanspost vennootschapsbelasting");
        var betaald = Cel("vpb.betaald", gegevens.Betaald);
        Regel("Betaalde voorlopige aanslagen", totaal: betaald);
        var teBetalen = vpb - (betaald.Bedrag ?? 0m);
        regels.Add(new(Fiscaleregelsoort.Totaal, teBetalen >= 0 ? "Nog te betalen" : "Terug te ontvangen", Totaal: Vast(Math.Abs(teBetalen))));

        return new Fiscalepositie(gegevens.Jaar, tarieven, regels, uitgangspunten, nietAftrekbaar, beperkt, vpb, teBetalen, belastbaar);
    }

    /// <summary>Alle sleutels die met de hand zijn aan te passen.</summary>
    public IEnumerable<string> Aanpasbaar => Regels.Concat(Uitgangspunten).SelectMany(r => new[] { r.Bedrag, r.Totaal })
        .Where(c => c?.Sleutel is not null).Select(c => c!.Sleutel!);
}
