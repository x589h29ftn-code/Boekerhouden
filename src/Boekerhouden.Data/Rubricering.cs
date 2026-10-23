namespace Boekerhouden.Data;

/// <summary>Een ingelezen versie van het RGS-werkboek. Precies één versie is actief.</summary>
public sealed class RgsVersie
{
    public Guid Id { get; set; }

    /// <summary>Bijv. "3.8".</summary>
    public string Versie { get; set; } = string.Empty;

    public string Bestandsnaam { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int AantalCodes { get; set; }

    public bool Actief { get; set; }

    public DateTimeOffset IngelezenOp { get; set; }

    public string IngelezenDoor { get; set; } = string.Empty;

    public List<RgsCodeRegel> Codes { get; set; } = [];
}

/// <summary>Eén RGS-code van een versie, zoals in het hoofdblad van het werkboek.</summary>
public sealed class RgsCodeRegel
{
    public long Id { get; set; }

    public Guid RgsVersieId { get; set; }

    public string Code { get; set; } = string.Empty;

    public int Niveau { get; set; }

    public string? Ouder { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public string OmschrijvingLang { get; set; } = string.Empty;

    /// <summary>"D", "C" of leeg (niveau 1).</summary>
    public string? Kant { get; set; }

    public string? Omslagcode { get; set; }

    public int Volgorde { get; set; }

    public string? Referentienummer { get; set; }

    public bool Inactief { get; set; }

    public List<string> Kiesfilters { get; set; } = [];

    public List<string> Wegfilters { get; set; } = [];
}

public enum KoppelBron
{
    /// <summary>Door een gebruiker gekoppeld (slepen of kiezen). Wordt nooit automatisch overschreven.</summary>
    Handmatig,

    /// <summary>Voorstel dat een gebruiker heeft bevestigd. Wordt nooit automatisch overschreven.</summary>
    Voorstel,

    /// <summary>Automatisch via het kantoorsjabloon (zelfde rekeningnummer en vergelijkbare omschrijving).</summary>
    Sjabloon,

    /// <summary>Automatisch via de RGS-code uit het importbestand.</summary>
    Bestand,
}

/// <summary>
/// De koppeling van een grootboekrekening van de klant aan een RGS-code (niveau 3 of 4), eventueel via een eigen post.
/// Hoort bij de klant en geldt voor alle boekjaren.
/// </summary>
public sealed class Koppeling
{
    public Guid Id { get; set; }

    public Guid KlantId { get; set; }

    public Guid GrootboekrekeningId { get; set; }

    /// <summary>De RGS-code; bij een eigen post de code waaronder die post hangt.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid? EigenPostId { get; set; }

    /// <summary>De RGS-versie waaruit de code komt.</summary>
    public string RgsVersie { get; set; } = string.Empty;

    public KoppelBron Bron { get; set; }

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}

/// <summary>
/// Een post die het kantoor voor een klant onder een RGS-code toevoegt (bijv. "Overige vorderingen 1"), zodat de
/// jaarrekening een eigen indeling kan krijgen. Voor aangifte en SBR telt de post als de RGS-code erboven.
/// </summary>
public sealed class KlantEigenPost
{
    public Guid Id { get; set; }

    public Guid KlantId { get; set; }

    public string OuderCode { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public int Volgorde { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;
}

/// <summary>Een eigen naam voor een RGS-post bij één klant (voor de jaarrekening). Gaat voor de kantoornaam.</summary>
public sealed class KlantPostNaam
{
    public Guid KlantId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;
}

/// <summary>De standaardnaam van het kantoor voor een RGS-post, voor alle klanten.</summary>
public sealed class KantoorPostNaam
{
    public string Code { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;
}

/// <summary>Kantoorsjabloon: een rekeningnummer dat bij het kantoor altijd aan dezelfde RGS-code hoort.</summary>
public sealed class SjabloonRegel
{
    public Guid Id { get; set; }

    public string Rekeningnummer { get; set; } = string.Empty;

    /// <summary>De omschrijving waarmee de regel is vastgelegd; automatisch koppelen gebeurt alleen bij een vergelijkbare omschrijving.</summary>
    public string Omschrijving { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public DateTimeOffset GewijzigdOp { get; set; }

    public string GewijzigdDoor { get; set; } = string.Empty;
}
