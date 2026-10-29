namespace Boekerhouden.Boekhouding.Btw;

/// <summary>Een btw-tarief vanaf een datum, bijv. hoog 21% vanaf 1 oktober 2012.</summary>
public sealed record Btwtariefregel(Tarief Tarief, DateOnly Vanaf, decimal Percentage);

/// <summary>
/// De btw-tarieven door de tijd. Op een datum geldt per tarief de regel met de laatste ingangsdatum op of vóór die datum.
/// Zo kan een nieuw percentage (bijv. hoog 21,15% vanaf een datum) worden toegevoegd zonder dat eerdere facturen
/// anders worden berekend.
/// </summary>
public sealed class Btwtarieven
{
    private readonly List<Btwtariefregel> regels;

    public Btwtarieven(IEnumerable<Btwtariefregel> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        this.regels = [.. regels.OrderBy(r => r.Tarief).ThenBy(r => r.Vanaf)];
    }

    /// <summary>De Nederlandse tarieven: hoog 19% en vanaf 1-10-2012 21%, laag 6% en vanaf 1-1-2019 9%.</summary>
    public static Btwtarieven Standaard { get; } = new(
    [
        new(Tarief.Hoog, new DateOnly(2001, 1, 1), 19m),
        new(Tarief.Hoog, new DateOnly(2012, 10, 1), 21m),
        new(Tarief.Laag, new DateOnly(2001, 1, 1), 6m),
        new(Tarief.Laag, new DateOnly(2019, 1, 1), 9m),
        new(Tarief.Nul, new DateOnly(2001, 1, 1), 0m),
    ]);

    public IReadOnlyList<Btwtariefregel> Regels => regels;

    /// <summary>Het percentage van een tarief op een datum; geen btw (en een tarief zonder regels) is 0%.</summary>
    public decimal Percentage(Tarief tarief, DateOnly datum) =>
        tarief == Tarief.Geen ? 0m : regels.LastOrDefault(r => r.Tarief == tarief && r.Vanaf <= datum)?.Percentage
            ?? regels.FirstOrDefault(r => r.Tarief == tarief)?.Percentage ?? 0m;

    /// <summary>Wat er mis is met de tarieven: een percentage buiten 0–100, of twee regels op dezelfde datum.</summary>
    public static IReadOnlyList<string> Controleer(IEnumerable<Btwtariefregel> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        var lijst = regels.ToList();
        var fouten = new List<string>();
        foreach (var r in lijst.Where(r => r.Percentage is < 0 or >= 100))
        {
            fouten.Add($"Tarief {r.Tarief.ToString().ToLowerInvariant()} vanaf {r.Vanaf:dd-MM-yyyy}: {Btwpercentage.Tekst(r.Percentage)} kan niet.");
        }

        foreach (var dubbel in lijst.GroupBy(r => (r.Tarief, r.Vanaf)).Where(g => g.Count() > 1))
        {
            fouten.Add($"Tarief {dubbel.Key.Tarief.ToString().ToLowerInvariant()} heeft twee percentages vanaf {dubbel.Key.Vanaf:dd-MM-yyyy}.");
        }

        if (lijst.Any(r => r.Tarief == Tarief.Geen))
        {
            fouten.Add("Het tarief 'geen btw' heeft geen percentage.");
        }

        return fouten;
    }
}

/// <summary>De btw-codes met de tarieven: de codes zoals ze op een datum gelden (met het percentage van die datum).</summary>
public sealed class Btwtabel(IReadOnlyList<BtwCode> codes, Btwtarieven tarieven)
{
    public static Btwtabel Standaard { get; } = new(BtwCodes.Definities, Btwtarieven.Standaard);

    public IReadOnlyList<BtwCode> Codes { get; } = codes ?? throw new ArgumentNullException(nameof(codes));

    public Btwtarieven Tarieven { get; } = tarieven ?? throw new ArgumentNullException(nameof(tarieven));

    /// <summary>De codes met het percentage dat op <paramref name="datum"/> geldt (bijv. de factuurdatum).</summary>
    public IReadOnlyList<BtwCode> Op(DateOnly datum) => [.. Codes.Select(c => c with { Percentage = Tarieven.Percentage(c.Tarief, datum) })];
}
