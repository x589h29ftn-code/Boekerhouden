using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Vpb;

/// <summary>Hoe een grootboekrekening meetelt in de fiscale positie.</summary>
public enum Fiscaalsoort
{
    /// <summary>Gewoon aftrekbaar (of een balansrekening zonder rol).</summary>
    Gewoon,

    /// <summary>Niet aftrekbaar: boetes, kleding, steekpenningen e.d.; telt helemaal bij.</summary>
    NietAftrekbaar,

    /// <summary>Beperkt aftrekbaar (gemengde kosten): representatie, relatiegeschenken, congressen, reis- en verblijfkosten.</summary>
    BeperktAftrekbaar,

    /// <summary>De vennootschapsbelasting in de winst-en-verliesrekening (de last): telt niet mee in het resultaat voor belastingen.</summary>
    Last,

    /// <summary>De balansrekening van de vennootschapsbelasting: betalingen erop zijn de betaalde (voorlopige) aanslagen.</summary>
    Balans,
}

/// <summary>
/// De fiscale indeling van de rekeningen: wat er bij de rekening is ingesteld, en anders automatisch op naam en RGS-code
/// (boetes niet aftrekbaar; representatie, relatiegeschenken, congressen en reis- en verblijfkosten beperkt aftrekbaar;
/// RGS-groep belastingen (WBel) als de last; een balansrekening met "vennootschapsbelasting" in de naam als de balanspost).
/// </summary>
public static class Fiscaleindeling
{
    private static readonly string[] BeperktInNaam = ["representatie", "relatiegeschenk", "reis- en verblijf", "congres"];

    public static Fiscaalsoort Van(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        if (rekening.Fiscaal is { } ingesteld)
        {
            return ingesteld;
        }

        var naam = rekening.Naam.ToLowerInvariant();
        var rgs = rekening.RgsCode ?? "";
        if (rekening.IsBalans)
        {
            return naam.Contains("vennootschapsbelasting", StringComparison.Ordinal) ? Fiscaalsoort.Balans : Fiscaalsoort.Gewoon;
        }

        if (rgs.StartsWith("WBel", StringComparison.Ordinal) || naam.Contains("vennootschapsbelasting", StringComparison.Ordinal))
        {
            return Fiscaalsoort.Last;
        }

        if (naam.Contains("boete", StringComparison.Ordinal))
        {
            return Fiscaalsoort.NietAftrekbaar;
        }

        // Op naam: de RGS-groepen zijn te grof (reclame en representatie delen er een). Vergoedingen aan personeel
        // (RGS WPer) vallen onder de werkkostenregeling, niet hieronder.
        var beperkt = !rgs.StartsWith("WPer", StringComparison.Ordinal) && BeperktInNaam.Any(w => naam.Contains(w, StringComparison.Ordinal));
        return beperkt ? Fiscaalsoort.BeperktAftrekbaar : Fiscaalsoort.Gewoon;
    }

    /// <summary>De rekeningen van het schema met deze fiscale soort.</summary>
    public static IEnumerable<Rekening> Rekeningen(Rekeningschema schema, Fiscaalsoort soort)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Alle.Where(r => Van(r) == soort);
    }
}
