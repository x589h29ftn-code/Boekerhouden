namespace Boekerhouden.Domain.Rgs;

/// <summary>Eén RGS-versie als boom: opzoeken, kinderen, voorouders en de selectie per soort entiteit.</summary>
public sealed class RgsSchema
{
    private readonly Dictionary<string, RgsCode> codes;
    private readonly Dictionary<string, List<RgsCode>> kinderen;
    private readonly bool heeftFilters;

    public RgsSchema(string versie, IEnumerable<RgsCode> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        Versie = versie;
        this.codes = new Dictionary<string, RgsCode>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            this.codes.TryAdd(code.Code, code);
        }

        kinderen = this.codes.Values
            .Where(c => c.Ouder is not null)
            .GroupBy(c => c.Ouder!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Volgorde).ToList(), StringComparer.Ordinal);
        heeftFilters = this.codes.Values.Any(c => c.Kiesfilters.Count > 0);
    }

    public string Versie { get; }

    public int Aantal => codes.Count;

    public IEnumerable<RgsCode> Alle => codes.Values.OrderBy(c => c.Volgorde);

    public RgsCode? Zoek(string? code) => code is not null && codes.TryGetValue(code, out var c) ? c : null;

    public IReadOnlyList<RgsCode> Kinderen(string code) =>
        kinderen.TryGetValue(code, out var lijst) ? lijst : [];

    /// <summary>De ouders van een code, van direct boven tot niveau 1.</summary>
    public IEnumerable<RgsCode> Voorouders(string code)
    {
        var huidige = Zoek(code);
        while (huidige?.Ouder is { } ouder && Zoek(ouder) is { } o)
        {
            yield return o;
            huidige = o;
        }
    }

    /// <summary>De hoofdposten (niveau 2): eerst de balans, dan de W&amp;V, in de officiële volgorde.</summary>
    public IEnumerable<RgsCode> Hoofdposten => codes.Values.Where(c => c.Niveau == 2).OrderBy(c => c.Volgorde);

    /// <summary>
    /// De codes (niveau 1–4) die bij de selectie horen, plus hun voorouders, zodat de boom altijd compleet is.
    /// Heeft het werkboek geen filterkolommen, dan doen alle actieve codes mee.
    /// </summary>
    public HashSet<string> Selecteer(RgsSelectie selectie)
    {
        ArgumentNullException.ThrowIfNull(selectie);
        var gekozen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes.Values)
        {
            var doetMee = heeftFilters ? selectie.Selecteert(code) : !code.Inactief && code.Niveau <= 4;
            if (doetMee && gekozen.Add(code.Code))
            {
                foreach (var ouder in Voorouders(code.Code))
                {
                    gekozen.Add(ouder.Code);
                }
            }
        }

        return gekozen;
    }

    /// <summary>
    /// Een code waaraan een grootboekrekening gekoppeld kan worden: niveau 3 of 4. Een mutatie (niveau 5) wordt
    /// teruggebracht naar zijn grootboekrekening (niveau 4). Onbekend, inactief of te grof (niveau 1–2) geeft null.
    /// </summary>
    public RgsCode? Koppelbaar(string? code)
    {
        var c = Zoek(code);
        while (c is { Niveau: > 4 } && Zoek(c.Ouder) is { } ouder)
        {
            c = ouder;
        }

        return c is { Niveau: 3 or 4, Inactief: false } ? c : null;
    }
}
