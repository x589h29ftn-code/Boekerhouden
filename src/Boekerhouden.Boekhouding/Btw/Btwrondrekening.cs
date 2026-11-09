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

    /// <summary>Voorbelasting naar herkomst: <see cref="Rondrekeningsleutel.Rubriek"/> 2a, 4a of 4b (verlegd), of leeg voor de rest.</summary>
    VoorbelastingVan,
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

/// <summary>Een regel van de jaaropstelling omzetbelasting: omzet (grondslag) en btw in twee kolommen.</summary>
public sealed record Jaaropstellingregel(Rondrekeningsoort Soort, string? Code, string Omschrijving, Jaaropstellingcel? Omzet, Jaaropstellingcel? Btw);

/// <summary>
/// Een bedrag in de jaaropstelling. <paramref name="Bron"/>: door te klikken naar de boekingen. <paramref name="Sleutel"/>:
/// met de hand aan te passen (de totalen niet). <paramref name="Berekend"/>: het bedrag volgens de administratie als het
/// met de hand is aangepast, anders leeg.
/// </summary>
public sealed record Jaaropstellingcel(decimal? Bedrag, Rondrekeningsleutel? Bron = null, string? Sleutel = null, decimal? Berekend = null)
{
    public bool IsAangepast => Berekend is not null;
}

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
                .SelectMany(p => p.Regels.Where(r => rekeningen.Contains(r.Rekeningnummer)
                    && (sleutel.Bron == Rondrekeningbron.OmzetGrootboek || r.BtwCode is not null || Btwaangifte.IsPrivegebruikregel(p, r, schema)))
                    .Select(r => new Rondrekeningbronregel(p, r, -r.Bedrag)))];
        }

        return [.. Btwaangifte.Bijdragen(posten, schema, codes, van, tot).Where(b => sleutel.Bron switch
            {
                Rondrekeningbron.Rubriek => b.Rubriek == sleutel.Rubriek && b.Soort == sleutel.Post,
                Rondrekeningbron.Verschuldigd => b.Soort == Btwpost.Btw,
                Rondrekeningbron.Voorbelasting => b.Soort == Btwpost.Voorbelasting,
                Rondrekeningbron.TeBetalen => b.Soort != Btwpost.Grondslag,
                Rondrekeningbron.VoorbelastingVan => b.Soort == Btwpost.Voorbelasting && Herkomst(b) == sleutel.Rubriek,
                _ => b.Soort == Btwpost.Grondslag && Omzetrubrieken.Contains(b.Rubriek),
            })
            .Select(b => new Rondrekeningbronregel(b.Post, b.Regel, sleutel.Bron == Rondrekeningbron.TeBetalen && b.Soort == Btwpost.Voorbelasting ? -b.Bedrag : b.Bedrag))];
    }

    /// <summary>
    /// De jaaropstelling omzetbelasting: het jaar volgens de administratie onder elkaar met omzet en btw in twee
    /// kolommen; de omzet aangesloten op de netto-omzet in de winst-en-verliesrekening, de voorbelasting naar herkomst,
    /// de afdrachten per periode (maand of kwartaal, zoals de administratie aangifte doet) en wat er overblijft: de suppletie.
    /// Elk ingevoerd getal (niet de totalen) is met de hand aan te passen; <paramref name="aanpassingen"/> per
    /// <see cref="Jaaropstellingcel.Sleutel"/>. De totalen rekenen met de aangepaste bedragen.
    /// </summary>
    public IReadOnlyList<Jaaropstellingregel> Jaaropstelling(IReadOnlyDictionary<string, decimal>? aanpassingen = null)
    {
        aanpassingen ??= new Dictionary<string, decimal>();
        var j = Jaar.Jaaraangifte;
        var (van, tot) = (new DateOnly(Jaar.Jaar, 1, 1), new DateOnly(Jaar.Jaar, 12, 31));
        var regels = new List<Jaaropstellingregel>();

        // Een cel die met de hand is aan te passen: het berekende bedrag, of het aangepaste.
        Jaaropstellingcel Cel(string sleutel, decimal? berekend, Rondrekeningsleutel? bron = null) =>
            aanpassingen.TryGetValue(sleutel, out var bedrag) ? new(bedrag, bron, sleutel, berekend ?? 0m) : new(berekend, bron, sleutel);
        static Jaaropstellingcel Vast(decimal? bedrag, Rondrekeningsleutel? bron = null) => new(bedrag, bron);
        void Kop(string tekst) => regels.Add(new(Rondrekeningsoort.Kop, null, tekst, null, null));
        void Regel(Rondrekeningsoort soort, string? code, string tekst, Jaaropstellingcel? omzet, Jaaropstellingcel? btw) =>
            regels.Add(new(soort, code, tekst, omzet, btw));
        (Jaaropstellingcel Omzet, Jaaropstellingcel? Btw) Rubriek(string code, string tekst, bool metBtw = true)
        {
            var omzet = Cel($"{code}.omzet", j.Rubriek(code).Grondslag, new(Rondrekeningbron.Rubriek, code, Btwpost.Grondslag));
            var btw = metBtw ? Cel($"{code}.btw", j.Rubriek(code).Btw, new(Rondrekeningbron.Rubriek, code, Btwpost.Btw)) : null;
            Regel(Rondrekeningsoort.Regel, code, tekst, omzet, btw);
            return (omzet, btw);
        }

        var cellen = new Dictionary<string, (Jaaropstellingcel Omzet, Jaaropstellingcel? Btw)>(StringComparer.Ordinal);
        Kop("Omzet");
        cellen["1a"] = Rubriek("1a", "Omzet hoog tarief");
        cellen["1b"] = Rubriek("1b", "Omzet laag tarief");
        cellen["1c"] = Rubriek("1c", "Omzet overige tarieven");
        cellen["1d"] = Rubriek("1d", "Privégebruik");
        cellen["1e"] = Rubriek("1e", "Omzet nultarief of niet bij u belast", metBtw: false);
        Kop("Verleggingsregeling");
        cellen["2a"] = Rubriek("2a", "Inkoop heffing verlegd");
        Kop("Omzet naar het buitenland");
        cellen["3a"] = Rubriek("3a", "Omzet buiten de EU (uitvoer)", metBtw: false);
        cellen["3b"] = Rubriek("3b", "Omzet binnen de EU", metBtw: false);
        cellen["3c"] = Rubriek("3c", "Installatie/afstandsverkopen binnen de EU", metBtw: false);

        // De omzet uit de rubrieken aansluiten op de netto-omzet in het grootboek; wat overblijft zijn de overige verschillen.
        decimal Omzet(string code) => cellen[code].Omzet.Bedrag ?? 0m;
        var grootboek = Grootboek(Rondrekeningbron.OmzetGrootboek, van, tot);
        Kop("Overige mutaties");
        var geenOmzet = new[] { Vast(-Omzet("2a")), Vast(-Omzet("1d")) };
        Regel(Rondrekeningsoort.Regel, null, "Inkoop verlegd is geen omzet", geenOmzet[0], null);
        Regel(Rondrekeningsoort.Regel, null, "Privégebruik is geen omzet", geenOmzet[1], null);
        var handmatig = new[] { Cel("tefactureren.omzet", null), Cel("divers.omzet", null) };
        Regel(Rondrekeningsoort.Regel, null, "Mutatie te factureren", handmatig[0], null);
        Regel(Rondrekeningsoort.Regel, null, "Diverse mutaties", handmatig[1], null);
        var gerekend = cellen.Values.Sum(c => c.Omzet.Bedrag ?? 0m) + geenOmzet.Sum(c => c.Bedrag ?? 0m) + handmatig.Sum(c => c.Bedrag ?? 0m);
        Regel(Rondrekeningsoort.Regel, null, "Overige verschillen", Vast(grootboek - gerekend), null);
        Regel(Rondrekeningsoort.Subtotaal, null, "Netto-omzet volgens de winst-en-verliesrekening", Vast(grootboek, new(Rondrekeningbron.OmzetGrootboek)), null);

        Kop("Inkoop vanuit het buitenland");
        cellen["4a"] = Rubriek("4a", "Inkoop van buiten de EU (invoer)");
        cellen["4b"] = Rubriek("4b", "Inkoop van binnen de EU");
        var verschuldigd = cellen.Values.Sum(c => c.Btw?.Bedrag ?? 0m);
        Regel(Rondrekeningsoort.Subtotaal, "5a", "Verschuldigde omzetbelasting", null, Vast(verschuldigd, new(Rondrekeningbron.Verschuldigd)));

        Kop("Voorbelasting");
        var bijdragen = Btwaangifte.Bijdragen(posten, schema, codes, van, tot).Where(b => b.Soort == Btwpost.Voorbelasting).ToList();
        var voorbelasting = new List<Jaaropstellingcel>();
        foreach (var (herkomst, sleutel, tekst) in new (string?, string, string)[]
        {
            (null, "5b", "Voorbelasting"), (null, "5b.suppletie", "Suppletie omzetbelasting vorig jaar"), ("2a", "5b.2a", "Inkoop heffing verlegd"),
            ("4a", "5b.4a", "Inkoop van buiten de EU (invoer)"), ("4b", "5b.4b", "Inkoop van binnen de EU"),
        })
        {
            var berekend = sleutel == "5b.suppletie" ? (decimal?)null : bijdragen.Where(b => Herkomst(b) == herkomst).Sum(b => b.Bedrag);
            var cel = Cel(sleutel, berekend, berekend is null ? null : new(Rondrekeningbron.VoorbelastingVan, herkomst));
            voorbelasting.Add(cel);
            Regel(Rondrekeningsoort.Regel, "5b", tekst, cel, null);
        }

        var totaalVoorbelasting = voorbelasting.Sum(c => c.Bedrag ?? 0m);
        var teBetalen = verschuldigd - totaalVoorbelasting;
        Regel(Rondrekeningsoort.Subtotaal, "5b", "Totaal voorbelasting", null, Vast(totaalVoorbelasting, new(Rondrekeningbron.Voorbelasting)));
        Regel(Rondrekeningsoort.Totaal, "5c", teBetalen >= 0 ? "Te betalen omzetbelasting" : "Terug te vragen omzetbelasting", null, Vast(teBetalen, new(Rondrekeningbron.TeBetalen)));

        // De afdrachten: wat per periode is aangegeven, in de frequentie van de administratie. Een periode die nog niet is
        // vastgelegd maar met de hand een bedrag heeft, telt als aangegeven.
        Kop("Afdrachten omzetbelasting");
        var vastgelegd = Jaar.Vastgelegd.ToList();
        var aangegeven = 0m;
        var nietAangegeven = 0m;
        foreach (var p in Perioden)
        {
            vastgelegd.Remove(p.Vastgelegd!);
            var cel = Cel($"afdracht.{p.Periode.Van:yyyy-MM-dd}", p.Vastgelegd?.TeBetalen);
            var open = cel.Bedrag is null;
            Regel(Rondrekeningsoort.Regel, null, open ? $"{p.Periode.Naam} (nog niet aangegeven)" : p.Periode.Naam, cel, null);
            aangegeven += cel.Bedrag ?? 0m;
            nietAangegeven += open ? p.Berekend.TeBetalen : 0m;
        }

        // Aangegeven in een andere frequentie (de frequentie is in het jaar gewijzigd).
        foreach (var v in vastgelegd.OrderBy(v => v.Van))
        {
            Regel(Rondrekeningsoort.Regel, null, $"{v.Van:dd-MM-yyyy} t/m {v.Tot:dd-MM-yyyy}", Vast(v.TeBetalen), null);
            aangegeven += v.TeBetalen;
        }

        Regel(Rondrekeningsoort.Subtotaal, null, "Totaal aangegeven", null, Vast(aangegeven));
        if (nietAangegeven != 0)
        {
            Regel(Rondrekeningsoort.Regel, null, "Nog niet aangegeven", null, Vast(nietAangegeven));
        }

        var suppletie = teBetalen - aangegeven - nietAangegeven;
        Regel(Rondrekeningsoort.Totaal, null, $"Suppletie omzetbelasting {Jaar.Jaar}: {suppletie switch { > 0 => "te betalen", < 0 => "terug te vragen", _ => "geen" }}",
            null, Vast(Math.Abs(suppletie)));

        // De balanspost: het saldo van de btw-rekeningen aan het eind van het jaar (een schuld positief).
        var balans = Btwaansluiting.Bereken(posten, schema, codes, van, tot);
        Kop($"Balanspost omzetbelasting per {tot:dd-MM-yyyy}");
        Regel(Rondrekeningsoort.Regel, null, "Saldo btw-rekeningen volgens het grootboek", null, Vast(balans.Saldo));
        if (balans.Verschil != 0)
        {
            Regel(Rondrekeningsoort.Regel, null, "Waarvan niet uit aangiften of betalingen", null, Vast(balans.Verschil));
        }

        return regels;
    }

    /// <summary>Alle sleutels van de jaaropstelling die met de hand zijn aan te passen.</summary>
    public IEnumerable<string> Aanpasbaar => Jaaropstelling().SelectMany(r => new[] { r.Omzet, r.Btw }).Where(c => c?.Sleutel is not null).Select(c => c!.Sleutel!);

    private static string? Herkomst(Btwbijdrage b) => b.Herkomst is "2a" or "4a" or "4b" ? b.Herkomst : null;

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
