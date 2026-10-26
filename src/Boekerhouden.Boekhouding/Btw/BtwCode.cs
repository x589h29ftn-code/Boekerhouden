namespace Boekerhouden.Boekhouding.Btw;

public enum BtwRichting
{
    Inkoop,
    Verkoop,
}

/// <summary>
/// Een btw-code: richting, percentage en de rubriek van de aangifte omzetbelasting.
/// </summary>
/// <param name="Rubriek">
/// De rubriek waarin de grondslag (en bij verkoop of verlegging de btw) wordt aangegeven, bijv. <c>1a</c> of <c>4b</c>;
/// leeg als de grondslag niet in de aangifte komt (zoals bij gewone inkoop: die btw staat alleen in 5b).
/// </param>
/// <param name="Verlegd">
/// Bij inkoop: de btw is naar de ondernemer verlegd (binnenland, EU, buiten de EU). Hij berekent de btw zelf,
/// draagt die af en trekt hem tegelijk af als voorbelasting.
/// </param>
public sealed record BtwCode(string Code, string Omschrijving, BtwRichting Richting, decimal Percentage, string? Rubriek, bool Verlegd = false)
{
    /// <summary>Komt er een btw-bedrag op de factuur zelf (dus niet bij 0%, vrijgesteld of verlegd)?</summary>
    public bool BtwOpFactuur => Percentage != 0 && !Verlegd;

    /// <summary>De btw over een grondslag, op centen afgerond (half van nul af).</summary>
    public decimal Bereken(decimal grondslag) => BtwBerekening.Btw(grondslag, Percentage);
}

/// <summary>Btw berekenen, ook terug uit een bedrag inclusief btw.</summary>
public static class BtwBerekening
{
    public static decimal Btw(decimal grondslag, decimal percentage) =>
        Math.Round(grondslag * percentage / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Splitst een bedrag inclusief btw in grondslag en btw; samen weer precies het bedrag.</summary>
    public static (decimal Grondslag, decimal Btw) UitInclusief(decimal inclusief, decimal percentage)
    {
        var btw = Math.Round(inclusief * percentage / (100m + percentage), 2, MidpointRounding.AwayFromZero);
        return (inclusief - btw, btw);
    }

    /// <summary>
    /// Klopt een btw-bedrag bij deze grondslag en dit percentage? Een cent verschil per regel mag, want leveranciers
    /// ronden per regel af; <paramref name="regels"/> is het aantal factuurregels.
    /// </summary>
    public static bool Klopt(decimal grondslag, decimal btw, decimal percentage, int regels = 1) =>
        Math.Abs(Btw(grondslag, percentage) - btw) <= 0.01m * Math.Max(1, regels);
}

/// <summary>De btw-codes die elke administratie standaard heeft (tarieven 2026).</summary>
public static class BtwCodes
{
    public static IReadOnlyList<BtwCode> Standaard { get; } =
    [
        new("V21", "Verkoop hoog tarief", BtwRichting.Verkoop, 21m, "1a"),
        new("V9", "Verkoop laag tarief", BtwRichting.Verkoop, 9m, "1b"),
        new("V0", "Verkoop 0%", BtwRichting.Verkoop, 0m, "1e"),
        new("VVL", "Verkoop, btw verlegd naar de afnemer", BtwRichting.Verkoop, 0m, "1e"),
        new("VEX", "Verkoop buiten de EU (uitvoer)", BtwRichting.Verkoop, 0m, "3a"),
        new("VEU", "Verkoop binnen de EU", BtwRichting.Verkoop, 0m, "3b"),
        new("VVR", "Verkoop vrijgesteld", BtwRichting.Verkoop, 0m, null),
        new("I21", "Inkoop hoog tarief", BtwRichting.Inkoop, 21m, null),
        new("I9", "Inkoop laag tarief", BtwRichting.Inkoop, 9m, null),
        new("I0", "Inkoop 0% of vrijgesteld", BtwRichting.Inkoop, 0m, null),
        new("IVL", "Inkoop, btw naar u verlegd", BtwRichting.Inkoop, 21m, "2a", Verlegd: true),
        new("IEX", "Inkoop buiten de EU", BtwRichting.Inkoop, 21m, "4a", Verlegd: true),
        new("IEU", "Inkoop binnen de EU", BtwRichting.Inkoop, 21m, "4b", Verlegd: true),
        new("GB", "Geen btw", BtwRichting.Inkoop, 0m, null),
    ];

    public static BtwCode? Zoek(IEnumerable<BtwCode> codes, string? code) =>
        code is null ? null : codes.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>De gewone code bij een richting en percentage, bijv. inkoop 21% → <c>I21</c>.</summary>
    public static BtwCode? Bij(IEnumerable<BtwCode> codes, BtwRichting richting, decimal percentage) =>
        codes.FirstOrDefault(c => c.Richting == richting && c.Percentage == percentage && !c.Verlegd);
}
