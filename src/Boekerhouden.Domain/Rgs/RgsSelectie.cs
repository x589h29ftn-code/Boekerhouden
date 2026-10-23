namespace Boekerhouden.Domain.Rgs;

/// <summary>
/// Welke RGS-codes bij een soort entiteit horen, volgens de werkwijze in het werkboek (tabblad Recap):
/// kies een basisset (kiesfilter) en filter daarna de onderdelen weg die niet van toepassing zijn (wegfilters).
/// Inactieve codes en mutaties (niveau 5) vallen altijd af.
/// </summary>
public sealed record RgsSelectie(string Naam, string Kiesfilter, IReadOnlyList<string> Wegfilters)
{
    private static readonly string[] Sectoren = ["Agro", "Bank", "OZW-Coop-Sticht-FWO", "WoCo"];

    /// <summary>BV: basisset, zonder de posten specifiek voor een eenmanszaak/VOF en zonder sectoren.</summary>
    public static RgsSelectie BV { get; } = new("BV", "Basis", ["EZ/VOF", .. Sectoren]);

    /// <summary>Eenmanszaak (en VOF): de EZ/VOF-set, zonder de posten specifiek voor een BV en zonder sectoren.</summary>
    public static RgsSelectie Eenmanszaak { get; } = new("Eenmanszaak", "EZ/VOF", ["BV", .. Sectoren]);

    public bool Selecteert(RgsCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return !code.Inactief
               && code.Niveau <= 4
               && code.Kiesfilters.Contains(Kiesfilter)
               && !Wegfilters.Any(code.Wegfilters.Contains);
    }
}
