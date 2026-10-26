namespace Boekerhouden.Boekhouding.Regels;

public enum Verwerkingssoort
{
    Inkoopfactuur,
    Bankmutatie,
}

/// <summary>
/// Wat de regels en het leergeheugen van een inkoopfactuur of bankmutatie weten.
/// </summary>
/// <param name="Relatie">De code van de herkende relatie, of leeg.</param>
/// <param name="Naam">De naam zoals op de factuur of het afschrift.</param>
/// <param name="Bedrag">Het totaal inclusief btw; bij een bankmutatie met teken (betaling negatief).</param>
/// <param name="Btwpercentage">Het btw-tarief zoals herkend op de factuur, of leeg (onbekend of meerdere tarieven).</param>
public sealed record Verwerkingsgegevens(
    Verwerkingssoort Soort,
    string? Relatie,
    string? Naam,
    decimal Bedrag,
    string? Omschrijving = null,
    string? TegenIban = null,
    decimal? Btwpercentage = null);
