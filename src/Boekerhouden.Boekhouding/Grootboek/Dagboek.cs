namespace Boekerhouden.Boekhouding.Grootboek;

public enum Dagboeksoort
{
    Inkoop,
    Verkoop,
    Bank,
    Kas,
    Memoriaal,
}

/// <summary>Een dagboek. Een bank- of kasdagboek hoort bij één grootboekrekening (de bank of de kas).</summary>
public sealed record Dagboek(string Code, string Naam, Dagboeksoort Soort, string? Rekeningnummer = null);
