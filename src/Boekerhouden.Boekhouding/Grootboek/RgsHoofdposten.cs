using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// De hoofdposten (niveau 1 en 2) van RGS 3.8, voor balans en W&amp;V zonder het volledige werkboek. Een RGS-code
/// begint met de code van zijn hoofdpost (bijv. <c>BLimBanRba</c> → <c>BLim</c>), dus de groepering werkt voor elke
/// rekening. Een hoofdpost die hier niet staat, komt onder "Niet gekoppeld"; met het werkboek erbij gaat het altijd goed.
/// </summary>
public static class RgsHoofdposten
{
    private static readonly (string Code, string? Ouder, string Naam, Saldokant? Kant)[] Codes =
    [
        ("B", null, "BALANS", null),
        ("BIva", "B", "Immateriële vaste activa", Saldokant.Debet),
        ("BMva", "B", "Materiële vaste activa", Saldokant.Debet),
        ("BVrd", "B", "Voorraden", Saldokant.Debet),
        ("BVor", "B", "Vorderingen", Saldokant.Debet),
        ("BLim", "B", "Liquide middelen", Saldokant.Debet),
        ("BEiv", "B", "Groepsvermogen - Eigen vermogen - Kapitaal", Saldokant.Credit),
        ("BLas", "B", "Langlopende schulden", Saldokant.Credit),
        ("BSch", "B", "Kortlopende schulden", Saldokant.Credit),
        ("W", null, "WINST- EN VERLIESREKENING", null),
        ("WOmz", "W", "Netto-omzet", Saldokant.Credit),
        ("WKpr", "W", "Kostprijs van de omzet", Saldokant.Debet),
        ("WPer", "W", "Lasten uit hoofde van personeelsbeloningen", Saldokant.Debet),
        ("WAfs", "W", "Afschrijvingen op immateriële en materiële vaste activa", Saldokant.Debet),
        ("WBed", "W", "Overige bedrijfskosten", Saldokant.Debet),
        ("WFbe", "W", "Financiële baten en lasten", Saldokant.Credit),
        ("WBel", "W", "Belastingen", Saldokant.Debet),
        ("WMfo", "W", "Mutatie fiscale oudedagsreserve", Saldokant.Debet),
    ];

    /// <summary>De hoofdposten van niveau 2 (bijv. <c>BMva</c> materiële vaste activa), in de volgorde van balans en W&amp;V.</summary>
    public static IReadOnlyList<(string Code, string Naam, Saldokant Kant)> Posten { get; } =
        [.. Codes.Where(c => c.Ouder is not null).Select(c => (c.Code, c.Naam, c.Kant!.Value))];

    public static RgsSchema Schema { get; } = new("3.8 (hoofdposten)", Codes.Select((c, i) =>
        new RgsCode(c.Code, c.Ouder is null ? 1 : 2, c.Ouder, c.Naam, c.Naam, c.Kant, null, i, null, false, new HashSet<string>(), new HashSet<string>())));
}
