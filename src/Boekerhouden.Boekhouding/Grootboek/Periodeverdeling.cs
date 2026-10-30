namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een kostenregel van de factuur die over de maanden wordt verdeeld (bedrag met teken, zoals geboekt).</summary>
public sealed record Verdeelregel(string Rekeningnummer, decimal Bedrag, string? Omschrijving = null);

/// <summary>
/// Een factuur over een periode (bijv. een jaarabonnement van 1-9-2026 t/m 31-8-2027) transitorisch verdelen: de
/// factuur gaat met de kosten op de balans (<see cref="Rekening"/>, bijv. vooruitbetaalde kosten), en per maand van de
/// periode gaat een gelijk deel naar de kostenrekening. Elke maand die de periode raakt, telt mee; de bedragen zijn op
/// de cent afgerond en de laatste maand krijgt het restje, zodat het totaal precies sluit. De btw blijft bij de factuur.
/// </summary>
public sealed record Periodeverdeling(DateOnly Van, DateOnly Tot, string Rekening)
{
    /// <summary>Meer dan vijf jaar verdelen is vrijwel altijd een tikfout.</summary>
    public const int MaximumMaanden = 60;

    /// <summary>De eerste dag van elke maand die de periode raakt.</summary>
    public IReadOnlyList<DateOnly> Maanden =>
        Tot < Van ? [] : [.. Enumerable.Range(0, (Tot.Year - Van.Year) * 12 + Tot.Month - Van.Month + 1).Select(i => new DateOnly(Van.Year, Van.Month, 1).AddMonths(i))];

    public IReadOnlyList<string> Controleer()
    {
        var fouten = new List<string>();
        if (Tot < Van)
        {
            fouten.Add("De periode eindigt vóór het begin.");
        }
        else if (Maanden.Count > MaximumMaanden)
        {
            fouten.Add($"Een verdeling over meer dan {MaximumMaanden} maanden kan niet.");
        }

        if (string.IsNullOrWhiteSpace(Rekening))
        {
            fouten.Add("Kies de rekening voor de verdeling (bijv. vooruitbetaalde kosten).");
        }

        return fouten;
    }

    /// <summary>Een bedrag in <paramref name="delen"/> gelijke delen op de cent; het laatste deel krijgt het restje.</summary>
    public static IReadOnlyList<decimal> Verdeel(decimal bedrag, int delen)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delen, 1);
        var deel = Math.Round(bedrag / delen, 2, MidpointRounding.AwayFromZero);
        return [.. Enumerable.Repeat(deel, delen - 1), bedrag - deel * (delen - 1)];
    }

    /// <summary>
    /// Per maand de regels: elke kostenregel naar zijn rekening (een gelijk deel), en het totaal daarvan af van de
    /// verdeelrekening. Leeg als er niets te verdelen is.
    /// </summary>
    public IReadOnlyList<(DateOnly Maand, IReadOnlyList<Verdeelregel> Regels)> PerMaand(IReadOnlyList<Verdeelregel> kosten)
    {
        ArgumentNullException.ThrowIfNull(kosten);
        var maanden = Maanden;
        if (maanden.Count == 0 || kosten.Count == 0)
        {
            return [];
        }

        var delen = kosten.Select(k => Verdeel(k.Bedrag, maanden.Count)).ToList();
        return
        [
            .. maanden.Select((maand, i) =>
            {
                var regels = kosten.Select((k, j) => k with { Bedrag = delen[j][i] }).Where(r => r.Bedrag != 0).ToList();
                regels.Add(new Verdeelregel(Rekening, -regels.Sum(r => r.Bedrag)));
                return (maand, (IReadOnlyList<Verdeelregel>)regels);
            }),
        ];
    }
}
