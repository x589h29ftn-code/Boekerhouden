namespace Boekerhouden.Domain.Rgs;

public enum Saldokant
{
    Debet,
    Credit,
}

/// <summary>
/// Eén code uit het Referentie GrootboekSchema (RGS), zoals in het officiële werkboek. Niveau 1 is balans of W&amp;V,
/// 2 de hoofdrubriek (hoofdpost), 3 de rubriek (subpost), 4 de grootboekrekening en 5 de mutatie.
/// </summary>
/// <param name="Omschrijving">De verkorte omschrijving; in de boom geeft de ouder de context.</param>
/// <param name="Kant">De normale saldokant (D/C); leeg bij niveau 1.</param>
/// <param name="Omslagcode">De code waar een saldo aan de andere kant heen gaat (bijv. bank credit → schulden aan banken).</param>
/// <param name="Volgorde">Volgorde in het werkboek (= de officiële sortering).</param>
/// <param name="Kiesfilters">Kiesfilters waarin de code zit (bijv. "Basis", "EZ/VOF").</param>
/// <param name="Wegfilters">Wegfilters waarin de code zit: specifiek voor dat gebruik (bijv. "BV", "Agro").</param>
public sealed record RgsCode(
    string Code,
    int Niveau,
    string? Ouder,
    string Omschrijving,
    string OmschrijvingLang,
    Saldokant? Kant,
    string? Omslagcode,
    int Volgorde,
    string? Referentienummer,
    bool Inactief,
    IReadOnlySet<string> Kiesfilters,
    IReadOnlySet<string> Wegfilters)
{
    /// <summary>Hoort de code bij de balans (B) of bij de winst-en-verliesrekening (W)?</summary>
    public bool IsBalans => Code.StartsWith('B');

    /// <summary>Staat een saldo aan de normale kant van deze code? Een saldo van nul telt als normaal.</summary>
    public bool IsNormaleKant(decimal saldo) =>
        saldo == 0 || Kant is null || (Kant == Saldokant.Debet ? saldo > 0 : saldo < 0);
}
