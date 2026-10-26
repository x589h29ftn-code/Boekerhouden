namespace Boekerhouden.Boekhouding.Bank;

/// <summary>
/// Eén mutatie op een bankafschrift. Een ontvangst is positief, een betaling negatief (zoals op de bankrekening in
/// het grootboek).
/// </summary>
/// <param name="Betalingskenmerk">Het gestructureerde betalingskenmerk (bijv. 16 cijfers), als de betaler dat gebruikte.</param>
/// <param name="EndToEndId">De referentie van de betaler die de hele betaling meegaat, of leeg.</param>
public sealed record Bankmutatie(
    DateOnly Datum,
    decimal Bedrag,
    string Omschrijving,
    string? TegenIban = null,
    string? TegenNaam = null,
    string? Betalingskenmerk = null,
    string? EndToEndId = null)
{
    public bool IsOntvangst => Bedrag > 0;

    /// <summary>
    /// Een sleutel om dezelfde mutatie bij opnieuw inlezen te herkennen: datum, bedrag, tegenrekening, referenties
    /// en omschrijving. Twee echt verschillende mutaties met precies dezelfde gegevens krijgen een volgnummer.
    /// </summary>
    public string Sleutel(string iban, int volgnummer = 0) =>
        string.Join('|', iban, Datum.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
            Bedrag.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), TegenIban, Betalingskenmerk, EndToEndId, Omschrijving, volgnummer);
}

/// <summary>Een bankafschrift: de mutaties van één rekening tussen een begin- en een eindsaldo.</summary>
public sealed record Bankafschrift(
    string Iban,
    string? Nummer,
    string Valuta,
    DateOnly? Begindatum,
    decimal Beginsaldo,
    DateOnly? Einddatum,
    decimal Eindsaldo,
    IReadOnlyList<Bankmutatie> Mutaties)
{
    /// <summary>Beginsaldo plus mutaties min eindsaldo; nul als het afschrift klopt.</summary>
    public decimal Verschil => Beginsaldo + Mutaties.Sum(m => m.Bedrag) - Eindsaldo;

    public bool SluitAan => Verschil == 0;
}

/// <summary>Een fout in een bankbestand, met het regelnummer als dat bekend is.</summary>
public sealed class BankbestandException : Exception
{
    public BankbestandException()
        : base("Het bankbestand is niet te lezen.")
    {
    }

    public BankbestandException(string melding)
        : base(melding)
    {
    }

    public BankbestandException(string melding, Exception binnen)
        : base(melding, binnen)
    {
    }
}
