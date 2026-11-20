using System.Reflection;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Het rekeningschema waarmee een nieuwe administratie begint.</summary>
public enum Schemakeuze
{
    /// <summary>Het schema van Sifer (per rechtsvorm), elke rekening met een RGS-code.</summary>
    Sifer,

    /// <summary>Standaard schema OVPA: het uitgebreide schema van het kantoor, met btw-code per rekening.</summary>
    Ovpa,
}

/// <summary>
/// De rekeningschema's om uit te kiezen bij een nieuwe administratie. Een meegeleverd schema staat als CSV in
/// <c>Grootboek/Schemas</c> (nummer;naam;soort;rgs;functie;btw, btw in de codes van Sifer); de RGS-code staat erbij
/// waar die vaststaat en is per rekening aan te vullen.
/// </summary>
public static class Rekeningschemas
{
    public static IReadOnlyList<(Schemakeuze Keuze, string Naam)> Keuzes { get; } =
    [
        (Schemakeuze.Sifer, "Sifer"),
        (Schemakeuze.Ovpa, "Standaard schema OVPA"),
    ];

    public static string Naam(Schemakeuze keuze) => Keuzes.First(k => k.Keuze == keuze).Naam;

    public static IReadOnlyList<Rekening> Rekeningen(Schemakeuze keuze, Ondernemingsvorm vorm) => keuze switch
    {
        Schemakeuze.Ovpa => Ovpa.Value,
        _ => StandaardRekeningschema.Rekeningen(vorm),
    };

    /// <summary>
    /// De RGS-codes om uit te kiezen bij een rekening: de hoofdposten en de codes van de meegeleverde schema's (Sifer per
    /// rechtsvorm en OVPA) en van het eigen schema, met de naam van de rekening erbij. Alleen codes die in een schema
    /// staan: geen verzonnen codes. Gesorteerd op code; balans (B…) en resultaat (W…) door elkaar.
    /// </summary>
    public static IReadOnlyList<(string Code, string Naam)> Rgskeuze(Rekeningschema? eigen = null)
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, naam, _) in RgsHoofdposten.Posten)
        {
            codes.TryAdd(code, naam);
        }

        var schemas = Keuzes.SelectMany(k => k.Keuze == Schemakeuze.Sifer
            ? Enum.GetValues<Ondernemingsvorm>().SelectMany(v => Rekeningen(k.Keuze, v))
            : Rekeningen(k.Keuze, Ondernemingsvorm.BV));
        foreach (var r in (eigen?.Alle ?? []).Concat(schemas).Where(r => !string.IsNullOrWhiteSpace(r.RgsCode)))
        {
            codes.TryAdd(r.RgsCode!, r.Naam);
        }

        return [.. codes.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => (c.Key, c.Value))];
    }

    /// <summary>De dagboeken; bank en kas op de bank- en kasrekening van het schema.</summary>
    public static IReadOnlyList<Dagboek> Dagboeken(Schemakeuze keuze)
    {
        if (keuze == Schemakeuze.Sifer)
        {
            return StandaardRekeningschema.Dagboeken;
        }

        var schema = new Rekeningschema(Rekeningen(keuze, Ondernemingsvorm.BV));
        var bank = schema.MetFunctie(Rekeningfunctie.Bank).First().Nummer;
        var kas = schema.MetFunctie(Rekeningfunctie.Kas).First().Nummer;
        return [.. StandaardRekeningschema.Dagboeken.Select(d => d.Soort switch
        {
            Dagboeksoort.Bank => d with { Rekeningnummer = bank },
            Dagboeksoort.Kas => d with { Rekeningnummer = kas },
            _ => d,
        })];
    }

    private static readonly Lazy<IReadOnlyList<Rekening>> Ovpa = new(() => Lees("ovpa.csv"));

    private static List<Rekening> Lees(string bestand)
    {
        var assembly = typeof(Rekeningschemas).Assembly;
        var naam = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + bestand, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(naam)!;
        using var lezer = new StreamReader(stream);
        var rekeningen = new List<Rekening>();
        while (lezer.ReadLine() is { } regel)
        {
            if (regel.Length == 0 || regel.StartsWith('#'))
            {
                continue;
            }

            var d = regel.Split(';');
            rekeningen.Add(new Rekening(
                d[0],
                d[1],
                Enum.Parse<Rekeningsoort>(d[2]),
                d[3].Length > 0 ? d[3] : null,
                d[4].Length > 0 ? Enum.Parse<Rekeningfunctie>(d[4]) : Rekeningfunctie.Geen,
                d[5].Length > 0 ? d[5] : null));
        }

        return rekeningen;
    }
}
