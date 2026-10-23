namespace Boekerhouden.Import;

/// <summary>Vaste namen en posities van het standaard importsjabloon (zie docs/onderzoek, §3.3).</summary>
public static class ImportSjabloon
{
    public const string WerkbladNaam = "Saldibalans";
    public const string ToelichtingWerkbladNaam = "Toelichting";

    public const string LabelBoekjaar = "Boekjaar";
    public const string LabelKlant = "Klant";

    public const string KolomGrootboeknummer = "Grootboeknummer";
    public const string KolomOmschrijving = "Omschrijving";
    public const string KolomBedrag = "Bedrag";
    public const string KolomRgsCode = "RGS-code";

    /// <summary>
    /// Kolomnamen die ook worden herkend, zodat gangbare exports (bijv. "Rekening, Omschr, Saldo")
    /// zonder aanpassing kunnen worden ingelezen. Vergelijking zonder hoofdletters, spaties, punten en dubbele punten.
    /// </summary>
    public static readonly IReadOnlyList<string> SynoniemenGrootboeknummer =
        ["grootboeknummer", "grootboekrekening", "grootboek", "rekening", "rekeningnummer", "rekeningnr", "reknr", "nummer", "nr"];

    public static readonly IReadOnlyList<string> SynoniemenOmschrijving =
        ["omschrijving", "omschr", "naam", "rekeningnaam", "grootboekomschrijving"];

    public static readonly IReadOnlyList<string> SynoniemenBedrag = ["bedrag", "saldo", "eindsaldo"];

    /// <summary>Rijnummer (1-based) van de kolomkoppen in het gegenereerde sjabloon.</summary>
    public const int KopRij = 4;
}
