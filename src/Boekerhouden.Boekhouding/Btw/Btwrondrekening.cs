using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Btw;

/// <summary>Waar een getal in de rondrekening vandaan komt, om door te klikken naar de boekingen.</summary>
public enum Rondrekeningbron
{
    /// <summary>Grondslag of btw van één rubriek.</summary>
    Rubriek,

    /// <summary>5a: de btw van alle rubrieken.</summary>
    Verschuldigd,

    /// <summary>5b.</summary>
    Voorbelasting,

    /// <summary>5c: verschuldigd min voorbelasting.</summary>
    TeBetalen,

    /// <summary>De omzet in de aangifte: de grondslag van 1a–1c, 1e en 3a–3c.</summary>
    OmzetAangifte,

    /// <summary>De omzet in het grootboek: de rekeningen in de RGS-groep netto-omzet (WOmz).</summary>
    OmzetGrootboek,

    /// <summary>Wat er met een btw-code op de btw-rekeningen is geboekt.</summary>
    Btwrekeningen,
}

public enum Rondrekeningsoort
{
    Kop,
    Regel,
    Subtotaal,
    Totaal,
}

/// <summary>Wat een getal in de rondrekening is; <paramref name="Rubriek"/> en <paramref name="Post"/> alleen bij <see cref="Rondrekeningbron.Rubriek"/>.</summary>
public sealed record Rondrekeningsleutel(Rondrekeningbron Bron, string? Rubriek = null, Btwpost Post = Btwpost.Grondslag);

/// <summary>
/// Een regel van de rondrekening: per periode het getal (zoals aangegeven, of volgens de boekingen als de periode nog
/// niet is aangegeven), het totaal daarvan, het hele jaar volgens de administratie en de afwijking (administratie min
/// totaal: wat nog in een suppletie hoort).
/// </summary>
public sealed record Rondrekeningregel(
    Rondrekeningsoort Soort,
    string? Code,
    string Omschrijving,
    IReadOnlyList<decimal?> Perioden,
    decimal? Totaal,
    decimal? Administratie,
    decimal? Afwijking,
    Rondrekeningsleutel? Sleutel);

/// <summary>Een boekingsregel achter een getal van de rondrekening, met het teken van dat getal.</summary>
public sealed record Rondrekeningbronregel(Journaalpost Post, Journaalregel Regel, decimal Bedrag);

/// <summary>
/// De btw-rondrekening van een jaar, opgebouwd uit de rubrieken van de aangifte met de perioden naast elkaar: per periode
/// wat is aangegeven (of, als de periode nog niet is aangegeven, wat de boekingen geven), het totaal, het hele jaar
/// volgens de administratie en de afwijking. Daaronder de aansluiting met het grootboek: de omzet in de aangiften
/// tegenover de omzetrekeningen, en de btw tegenover wat er op de btw-rekeningen is geboekt.
/// </summary>
public sealed class Btwrondrekening
{
    /// <summary>De rubrieken die als omzet tellen (niet 1d privégebruik, niet 2a en 4a/4b: dat zijn inkopen).</summary>
    public static IReadOnlyList<string> Omzetrubrieken { get; } = ["1a", "1b", "1c", "1e", "3a", "3b", "3c"];

    /// <summary>De rubrieken met btw (bij 1e en 3a–3c alleen de omzet).</summary>
    public static IReadOnlyList<string> Btwrubrieken { get; } = ["1a", "1b", "1c", "1d", "2a", "4a", "4b"];

    private readonly IReadOnlyList<Journaalpost> posten;
    private readonly Rekeningschema schema;
    private readonly IReadOnlyList<BtwCode> codes;

    private Btwrondrekening(Btwjaar jaar, IReadOnlyList<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes)
    {
        (Jaar, this.posten, this.schema, this.codes) = (jaar, posten, schema, codes);
        Regels = Maak();
    }

    public Btwjaar Jaar { get; }

    public IReadOnlyList<Btwperiode> Perioden => Jaar.Perioden;

    public IReadOnlyList<Rondrekeningregel> Regels { get; }

    public static Btwrondrekening Maak(Btwjaar jaar, IReadOnlyList<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(jaar);
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(codes);
        return new Btwrondrekening(jaar, posten, schema, codes);
    }

    /// <summary>De boekingsregels achter een getal, van <paramref name="van"/> tot en met <paramref name="tot"/>.</summary>
    public IReadOnlyList<Rondrekeningbronregel> Bronnen(Rondrekeningsleutel sleutel, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        if (sleutel.Bron is Rondrekeningbron.OmzetGrootboek or Rondrekeningbron.Btwrekeningen)
        {
            var rekeningen = schema.Alle.Where(r => sleutel.Bron == Rondrekeningbron.OmzetGrootboek ? IsOmzet(r) : Btwaangifte.IsBtwrekening(r))
                .Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
            return [.. posten.Where(p => p.Datum >= van && p.Datum <= tot && p.Boekstuk != "begin")
                .SelectMany(p => p.Regels.Where(r => rekeningen.Contains(r.Rekeningnummer) && (sleutel.Bron == Rondrekeningbron.OmzetGrootboek || r.BtwCode is not null))
                    .Select(r => new Rondrekeningbronregel(p, r, -r.Bedrag)))];
        }

        return [.. Btwaangifte.Bijdragen(posten, schema, codes, van, tot).Where(b => sleutel.Bron switch
            {
                Rondrekeningbron.Rubriek => b.Rubriek == sleutel.Rubriek && b.Soort == sleutel.Post,
                Rondrekeningbron.Verschuldigd => b.Soort == Btwpost.Btw,
                Rondrekeningbron.Voorbelasting => b.Soort == Btwpost.Voorbelasting,
                Rondrekeningbron.TeBetalen => b.Soort != Btwpost.Grondslag,
                _ => b.Soort == Btwpost.Grondslag && Omzetrubrieken.Contains(b.Rubriek),
            })
            .Select(b => new Rondrekeningbronregel(b.Post, b.Regel, sleutel.Bron == Rondrekeningbron.TeBetalen && b.Soort == Btwpost.Voorbelasting ? -b.Bedrag : b.Bedrag))];
    }

    /// <summary>Een rekening in de RGS-groep netto-omzet.</summary>
    public static bool IsOmzet(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        return rekening.RgsCode?.StartsWith("WOmz", StringComparison.Ordinal) == true;
    }

    private List<Rondrekeningregel> Maak()
    {
        var regels = new List<Rondrekeningregel>();
        var jaarcijfers = Jaar.Jaaraangifte;

        // Per periode: wat is aangegeven, anders wat de boekingen nu geven.
        decimal Aangifte(Btwperiode p, Func<Aangifterubriek, decimal> rubriek, Func<string, bool> welke, Func<decimal, decimal, decimal> totaal)
        {
            var (rubrieken, voorbelasting) = p.Vastgelegd is { } v ? (v.Rubrieken, v.Voorbelasting) : (p.Berekend.Rubrieken, p.Berekend.Voorbelasting);
            return totaal(rubrieken.Where(r => welke(r.Rubriek)).Sum(rubriek), voorbelasting);
        }

        Rondrekeningregel Regel(Rondrekeningsoort soort, string? code, string omschrijving, Rondrekeningsleutel sleutel,
            Func<Aangifterubriek, decimal> rubriek, Func<string, bool> welke, Func<decimal, decimal, decimal> totaal)
        {
            var perioden = Perioden.Select(p => (decimal?)Aangifte(p, rubriek, welke, totaal)).ToList();
            var som = perioden.Sum() ?? 0m;
            var administratie = totaal(jaarcijfers.Rubrieken.Where(r => welke(r.Rubriek)).Sum(rubriek), jaarcijfers.Voorbelasting);
            return new(soort, code, omschrijving, perioden, som, administratie, administratie - som, sleutel);
        }

        static bool Leeg(Rondrekeningregel r) => r.Perioden.All(b => b is null or 0) && r.Administratie is null or 0;

        regels.Add(new(Rondrekeningsoort.Kop, null, "Omzet en grondslagen", [], null, null, null, null));
        regels.AddRange(Btwaangifte.Volgorde
            .Select(code => Regel(Rondrekeningsoort.Regel, code, Rubrieknaam(code), new(Rondrekeningbron.Rubriek, code, Btwpost.Grondslag),
                r => r.Grondslag, r => r == code, (s, _) => s))
            .Where(r => !Leeg(r)));
        regels.Add(new(Rondrekeningsoort.Kop, null, "Btw", [], null, null, null, null));
        regels.AddRange(Btwrubrieken
            .Select(code => Regel(Rondrekeningsoort.Regel, code, Rubrieknaam(code), new(Rondrekeningbron.Rubriek, code, Btwpost.Btw),
                r => r.Btw, r => r == code, (s, _) => s))
            .Where(r => !Leeg(r)));
        regels.Add(Regel(Rondrekeningsoort.Subtotaal, "5a", "Verschuldigde omzetbelasting", new(Rondrekeningbron.Verschuldigd), r => r.Btw, _ => true, (s, _) => s));
        regels.Add(Regel(Rondrekeningsoort.Regel, "5b", "Voorbelasting", new(Rondrekeningbron.Voorbelasting), r => r.Btw, _ => false, (_, v) => v));
        var teBetalen = Regel(Rondrekeningsoort.Totaal, "5c", "Te betalen", new(Rondrekeningbron.TeBetalen), r => r.Btw, _ => true, (s, v) => s - v);
        regels.Add(teBetalen);

        // De aansluiting met het grootboek, per periode; de administratie en afwijking staan al bij de rubrieken.
        var omzetAangifte = Regel(Rondrekeningsoort.Regel, null, "Omzet volgens de aangiften", new(Rondrekeningbron.OmzetAangifte),
            r => r.Grondslag, r => Omzetrubrieken.Contains(r), (s, _) => s);
        var omzetGrootboek = Perioden.Select(p => (decimal?)Grootboek(Rondrekeningbron.OmzetGrootboek, p.Periode.Van, p.Periode.Tot)).ToList();
        var btwrekeningen = Perioden.Select(p => (decimal?)Grootboek(Rondrekeningbron.Btwrekeningen, p.Periode.Van, p.Periode.Tot)).ToList();
        regels.Add(new(Rondrekeningsoort.Kop, null, "Aansluiting met het grootboek", [], null, null, null, null));
        regels.Add(omzetAangifte with { Administratie = null, Afwijking = null });
        regels.Add(new(Rondrekeningsoort.Regel, null, "Omzet volgens het grootboek", omzetGrootboek, omzetGrootboek.Sum(), null, null, new(Rondrekeningbron.OmzetGrootboek)));
        regels.Add(Verschil("Verschil omzet", omzetGrootboek, omzetAangifte.Perioden));
        regels.Add(teBetalen with { Soort = Rondrekeningsoort.Regel, Code = null, Omschrijving = "Btw volgens de aangiften", Administratie = null, Afwijking = null });
        regels.Add(new(Rondrekeningsoort.Regel, null, "Btw geboekt op de btw-rekeningen", btwrekeningen, btwrekeningen.Sum(), null, null, new(Rondrekeningbron.Btwrekeningen)));
        regels.Add(Verschil("Verschil btw", btwrekeningen, teBetalen.Perioden));
        return regels;
    }

    private static Rondrekeningregel Verschil(string omschrijving, IReadOnlyList<decimal?> grootboek, IReadOnlyList<decimal?> aangifte)
    {
        var perioden = grootboek.Zip(aangifte, (g, a) => (decimal?)((g ?? 0m) - (a ?? 0m))).ToList();
        return new(Rondrekeningsoort.Subtotaal, null, omschrijving, perioden, perioden.Sum(), null, null, null);
    }

    private decimal Grootboek(Rondrekeningbron bron, DateOnly van, DateOnly tot) => Bronnen(new(bron), van, tot).Sum(b => b.Bedrag);

    /// <summary>De korte naam van een rubriek, zoals op het aangifteformulier.</summary>
    public static string Rubrieknaam(string rubriek) => rubriek switch
    {
        "1a" => "Leveringen/diensten belast met hoog tarief",
        "1b" => "Leveringen/diensten belast met laag tarief",
        "1c" => "Leveringen/diensten belast met overige tarieven, behalve 0%",
        "1d" => "Privégebruik",
        "1e" => "Leveringen/diensten belast met 0% of niet bij u belast",
        "2a" => "Leveringen/diensten waarbij de heffing van omzetbelasting naar u is verlegd",
        "3a" => "Leveringen naar landen buiten de EU (uitvoer)",
        "3b" => "Leveringen naar of diensten in landen binnen de EU",
        "3c" => "Installatie/afstandsverkopen binnen de EU",
        "4a" => "Leveringen/diensten uit landen buiten de EU",
        "4b" => "Leveringen/diensten uit landen binnen de EU",
        _ => rubriek,
    };
}
