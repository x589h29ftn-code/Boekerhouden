namespace Boekerhouden.Import;

/// <summary>Codes van de importcontroles, gelijk aan de nummering in docs/onderzoek §3.3.</summary>
public static class ImportCodes
{
    /// <summary>Bestand leesbaar en kolomkoppen aanwezig.</summary>
    public const string Bestand = "I1";

    /// <summary>Boekjaar ingevuld en geldig.</summary>
    public const string Boekjaar = "I2";

    /// <summary>Grootboeknummer en omschrijving ingevuld; minstens één regel.</summary>
    public const string VerplichteVelden = "I3";

    /// <summary>Grootboeknummers uniek.</summary>
    public const string UniekeNummers = "I4";

    /// <summary>Bedragen geldig, hoogstens 2 decimalen.</summary>
    public const string GeldigeBedragen = "I5";

    /// <summary>Som van alle bedragen is nihil.</summary>
    public const string SaldoNihil = "I6";

    /// <summary>Klant in het bestand wijkt af van de gekozen klant (waarschuwing).</summary>
    public const string KlantAfwijkend = "I7";

    /// <summary>Er is al een saldibalans voor dit boekjaar; importeren vervangt die (waarschuwing).</summary>
    public const string BestaandeImport = "I9";

    /// <summary>Verschillen met de vorige import of het vorige jaar (informatie).</summary>
    public const string Verschillen = "I10";

    /// <summary>RGS-code heeft geen geldig formaat (waarschuwing; de code wordt genegeerd).</summary>
    public const string RgsFormaat = "I11";
}
