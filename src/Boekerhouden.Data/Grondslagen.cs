namespace Boekerhouden.Data;

/// <summary>De grondslagen waarop de jaarrekening van een BV is opgesteld.</summary>
public enum Verslaggeving
{
    /// <summary>Titel 9 Boek 2 BW en de Richtlijnen voor de jaarverslaggeving voor kleine rechtspersonen (RJk).</summary>
    Commercieel,

    /// <summary>Fiscale grondslagen (art. 2:396 lid 6 BW, Besluit fiscale waarderingsgrondslagen).</summary>
    Fiscaal,
}

/// <summary>Wanneer een grondslag standaard in de jaarrekening staat.</summary>
public enum GrondslagOpname
{
    /// <summary>Altijd, bijv. de algemene grondslagen.</summary>
    Altijd,

    /// <summary>Als een van de gekoppelde RGS-posten een saldo heeft (dit jaar of vorig jaar).</summary>
    BijPost,

    /// <summary>
    /// Standaard dicht; de gebruiker zet hem aan (bijv. foutenherstel of stelselwijziging). Met RGS-codes staat hij
    /// open als een van die posten een saldo heeft (bijv. bijzondere waardeverminderingen).
    /// </summary>
    Optioneel,
}

/// <summary>
/// Een grondslag in de kantoorbibliotheek (Beheer › Grondslagen): een tekst voor commerciële en een voor fiscale
/// grondslagen. Een lege tekst betekent dat de grondslag bij die variant niet voorkomt (bijv. latente belastingen
/// bij fiscale grondslagen). Per sleutel kunnen er versies zijn vanaf een boekjaar: een aanpassing geldt vanaf dat jaar,
/// eerdere jaren houden hun tekst.
/// </summary>
public sealed class GrondslagSjabloon : IJaarversie
{
    public string Sleutel { get; set; } = string.Empty;

    /// <inheritdoc />
    public int VanafBoekjaar { get; set; }

    /// <summary>De paragraaf, bijv. "Grondslagen voor de waardering van activa en passiva".</summary>
    public string Groep { get; set; } = string.Empty;

    public string Titel { get; set; } = string.Empty;

    public string? TekstCommercieel { get; set; }

    public string? TekstFiscaal { get; set; }

    public GrondslagOpname Opname { get; set; }

    /// <summary>De RGS-codes (komma-gescheiden) waarbij de grondslag hoort, bijv. "BMva".</summary>
    public string Codes { get; set; } = string.Empty;

    /// <summary>Een keuze geldt alleen voor dat jaar (bijv. foutenherstel) en rolt niet door naar het volgende jaar.</summary>
    public bool Jaargebonden { get; set; }

    public int Volgorde { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>
/// Een keuze in een dossier voor een grondslag: wel of niet in de jaarrekening, en eventueel een eigen tekst. Zonder
/// keuze in dit dossier geldt die van het laatste eerdere jaar (behalve bij jaargebonden grondslagen).
/// </summary>
public sealed class DossierGrondslag
{
    public Guid DossierId { get; set; }

    public string Sleutel { get; set; } = string.Empty;

    /// <summary>Null = de standaard (bij post of altijd: wel; optioneel: niet).</summary>
    public bool? Opgenomen { get; set; }

    /// <summary>Null = de tekst uit de bibliotheek.</summary>
    public string? Tekst { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}
