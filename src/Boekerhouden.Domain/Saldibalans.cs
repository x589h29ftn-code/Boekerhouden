namespace Boekerhouden.Domain;

/// <summary>
/// De saldibalans van één boekjaar: per grootboekrekening het saldo per einde boekjaar.
/// Tekenconventie: debet is positief, credit is negatief.
/// </summary>
public sealed class Saldibalans
{
    public Saldibalans(int boekjaar, string? klant, IReadOnlyList<SaldibalansRegel> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);

        var dubbel = regels.GroupBy(r => r.Grootboeknummer, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dubbel is not null)
        {
            throw new ArgumentException($"Grootboeknummer {dubbel.Key} komt meerdere keren voor.", nameof(regels));
        }

        Boekjaar = boekjaar;
        Klant = klant;
        Regels = regels;
    }

    public int Boekjaar { get; }

    /// <summary>De klant zoals vermeld in het importbestand (ter controle), indien ingevuld.</summary>
    public string? Klant { get; }

    public IReadOnlyList<SaldibalansRegel> Regels { get; }

    public decimal TotaalDebet => Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);

    public decimal TotaalCredit => -Regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag);

    /// <summary>Som van alle bedragen. Bij een correcte saldibalans is dit nihil.</summary>
    public decimal Saldo => Regels.Sum(r => r.Bedrag);

    public bool IsInEvenwicht => Saldo == 0m;
}
