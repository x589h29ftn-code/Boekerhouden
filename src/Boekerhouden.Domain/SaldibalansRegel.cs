namespace Boekerhouden.Domain;

/// <summary>Eén grootboekrekening in een saldibalans. Debet is positief, credit is negatief.</summary>
public sealed record SaldibalansRegel
{
    public SaldibalansRegel(string grootboeknummer, string omschrijving, decimal bedrag, string? rgsCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grootboeknummer);
        ArgumentException.ThrowIfNullOrWhiteSpace(omschrijving);

        Grootboeknummer = grootboeknummer;
        Omschrijving = omschrijving;
        Bedrag = bedrag;
        RgsCode = rgsCode;
    }

    public string Grootboeknummer { get; }

    public string Omschrijving { get; }

    public decimal Bedrag { get; }

    /// <summary>Optionele RGS-referentiecode, bijvoorbeeld <c>BLimBanRba</c>.</summary>
    public string? RgsCode { get; }
}
