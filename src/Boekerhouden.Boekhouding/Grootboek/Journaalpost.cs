namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Eén regel van een journaalpost: een bedrag op een grootboekrekening (debet positief, credit negatief). Een regel
/// met een btw-code op een gewone rekening is de grondslag; op een btw-rekening is het de btw zelf.
/// </summary>
public sealed record Journaalregel(
    string Rekeningnummer,
    decimal Bedrag,
    string? Omschrijving = null,
    string? BtwCode = null,
    string? Relatie = null,
    string? Factuurnummer = null);

/// <summary>Een journaalpost in een dagboek: altijd in evenwicht.</summary>
public sealed record Journaalpost(
    string Dagboek,
    string Boekstuk,
    DateOnly Datum,
    string Omschrijving,
    IReadOnlyList<Journaalregel> Regels)
{
    public decimal TotaalDebet => Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);
}
