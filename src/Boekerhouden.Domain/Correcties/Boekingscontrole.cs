namespace Boekerhouden.Domain.Correcties;

/// <summary>Eén regel van een correctieboeking: een bedrag op een grootboekrekening (debet positief, credit negatief).</summary>
public sealed record Boekingsregel(Guid RekeningId, decimal Bedrag);

/// <summary>
/// De controles op een correctieboeking (memoriaal). Een boeking moet altijd in evenwicht zijn (controle C08): de som
/// van de regels is nul, anders kan hij niet worden opgeslagen.
/// </summary>
public static class Boekingscontrole
{
    public const int MaximumRegels = 200;

    public static IReadOnlyList<string> Controleer(string? omschrijving, IReadOnlyList<Boekingsregel> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(omschrijving))
        {
            fouten.Add("Geef de boeking een omschrijving.");
        }

        if (regels.Count < 2)
        {
            fouten.Add("Een boeking heeft minstens twee regels nodig.");
        }

        if (regels.Count > MaximumRegels)
        {
            fouten.Add($"Een boeking heeft hoogstens {MaximumRegels} regels.");
        }

        var nul = regels.Select((r, i) => (r, i)).Where(x => x.r.Bedrag == 0).Select(x => x.i + 1).ToList();
        if (nul.Count > 0)
        {
            fouten.Add($"Regel {string.Join(", ", nul)} heeft geen bedrag.");
        }

        var centen = regels.Select((r, i) => (r, i)).Where(x => decimal.Round(x.r.Bedrag, 2) != x.r.Bedrag).Select(x => x.i + 1).ToList();
        if (centen.Count > 0)
        {
            fouten.Add($"Regel {string.Join(", ", centen)} heeft meer dan twee decimalen.");
        }

        var debet = regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);
        var credit = -regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag);
        if (debet != credit)
        {
            fouten.Add($"De boeking is niet in evenwicht: debet {Bedragen.Euro(debet)}, credit {Bedragen.Euro(credit)}, verschil {Bedragen.Euro(debet - credit)}.");
        }

        return fouten;
    }
}
