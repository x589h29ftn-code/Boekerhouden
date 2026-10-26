namespace Boekerhouden.Boekhouding.Relaties;

/// <summary>Een leverancier of klant. Een relatie kan meerdere bankrekeningen hebben.</summary>
/// <param name="Code">Eigen code, bijv. <c>KPN</c> of <c>D10023</c>.</param>
/// <param name="Tegenrekening">De standaard grootboekrekening, bijv. telefoonkosten bij een telecomleverancier.</param>
public sealed record Relatie(
    string Code,
    string Naam,
    IReadOnlyList<string> Ibans,
    string? BtwNummer = null,
    string? KvkNummer = null,
    string? Tegenrekening = null)
{
    public bool HeeftIban(string? iban) => iban is not null && Ibans.Any(i => Iban.Normaliseer(i) == Iban.Normaliseer(iban));
}
