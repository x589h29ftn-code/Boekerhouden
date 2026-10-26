using Boekerhouden.Boekhouding.Btw;

namespace Boekerhouden.Boekhouding.Facturen;

public enum Factuursoort
{
    Inkoop,
    Verkoop,
}

/// <summary>
/// Eén regel van een factuur: bedrag exclusief btw op een grootboekrekening (kosten of omzet). De btw wordt berekend,
/// tenzij hij is opgegeven (bijv. zoals herkend op de factuur van de leverancier).
/// </summary>
/// <param name="Bedrag">Exclusief btw; negatief bij een creditfactuur.</param>
public sealed record Factuurregel(string Omschrijving, string Rekeningnummer, string BtwCode, decimal Bedrag, decimal? Btw = null);

/// <summary>Een inkoop- of verkoopfactuur, zoals die geboekt wordt.</summary>
public sealed record Factuur(
    Factuursoort Soort,
    string Relatie,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Factuurregel> Regels,
    string? Betalingskenmerk = null)
{
    public decimal TotaalExclusief => Regels.Sum(r => r.Bedrag);

    /// <summary>De btw op de factuur (zonder verlegde btw, die betaalt de ondernemer niet aan de leverancier).</summary>
    public decimal TotaalBtw(IEnumerable<BtwCode> codes) => Regels.Sum(r => BtwOpFactuur(r, codes));

    public decimal Totaal(IEnumerable<BtwCode> codes) => TotaalExclusief + TotaalBtw(codes);

    internal static decimal BtwVan(Factuurregel regel, IEnumerable<BtwCode> codes)
    {
        var code = BtwCodes.Zoek(codes, regel.BtwCode) ?? throw new ArgumentException($"Btw-code {regel.BtwCode} bestaat niet.", nameof(regel));
        return regel.Btw ?? code.Bereken(regel.Bedrag);
    }

    internal static decimal BtwOpFactuur(Factuurregel regel, IEnumerable<BtwCode> codes)
    {
        var code = BtwCodes.Zoek(codes, regel.BtwCode) ?? throw new ArgumentException($"Btw-code {regel.BtwCode} bestaat niet.", nameof(regel));
        return code.Verlegd ? 0m : BtwVan(regel, codes);
    }
}
