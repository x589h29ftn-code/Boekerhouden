using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Administratie.Data;

public sealed class Gebruiker : IdentityUser<Guid>
{
    public string Naam { get; set; } = string.Empty;

    public string Initialen { get; set; } = string.Empty;

    public bool Actief { get; set; } = true;

    /// <summary>Na aanmaken of resetten door de beheerder moet de gebruiker eerst een eigen wachtwoord kiezen.</summary>
    public bool MoetWachtwoordWijzigen { get; set; }
}

/// <summary>De rollen in het pakket zelf; de rechten in een administratie staan in <see cref="Lidmaatschap"/>.</summary>
public static class Rollen
{
    /// <summary>Gebruikers en leden beheren, het wijzigingslog inzien. Geeft geen inzage in de cijfers.</summary>
    public const string Beheerder = "Beheerder";

    /// <summary>Medewerker van het kantoor: mag administraties aanmaken (en wordt daar lid met de rol Kantoor).</summary>
    public const string Kantoor = "Kantoor";

    public static readonly IReadOnlyList<string> Alle = [Beheerder, Kantoor];
}

/// <summary>De rol van een gebruiker in één administratie.</summary>
public enum Lidmaatschapsrol
{
    /// <summary>Alleen overzichten en documenten bekijken.</summary>
    Inzien,

    /// <summary>Documenten inlezen, factureren, de bank bijwerken.</summary>
    Ondernemer,

    /// <summary>Alles, ook memoriaal, rekeningschema en periodes.</summary>
    Kantoor,
}

/// <summary>De administratie van één onderneming.</summary>
public sealed class Administratie
{
    public Guid Id { get; set; }

    public string Naam { get; set; } = string.Empty;

    public Ondernemingsvorm Ondernemingsvorm { get; set; }

    public string? KvkNummer { get; set; }

    public string? BtwNummer { get; set; }

    public DateTimeOffset AangemaaktOp { get; set; }

    public string AangemaaktDoor { get; set; } = string.Empty;
}

public sealed class Lidmaatschap
{
    public Guid AdministratieId { get; set; }

    public Guid GebruikerId { get; set; }

    public Lidmaatschapsrol Rol { get; set; }
}

/// <summary>Gegevens die bij één administratie horen; de database geeft alleen die van de geopende administratie.</summary>
public interface IVanAdministratie
{
    Guid AdministratieId { get; }
}

public sealed class Grootboekrekening : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Nummer { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public Rekeningsoort Soort { get; set; }

    public string? RgsCode { get; set; }

    public Rekeningfunctie Functie { get; set; }

    public Rekening NaarDomein() => new(Nummer, Naam, Soort, RgsCode, Functie);
}

public sealed class Dagboekdefinitie : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Naam { get; set; } = string.Empty;

    public Dagboeksoort Soort { get; set; }

    public string? Rekeningnummer { get; set; }

    public Dagboek NaarDomein() => new(Code, Naam, Soort, Rekeningnummer);
}

/// <summary>
/// Een geboekte journaalpost. Wordt nooit gewijzigd of verwijderd (de database weigert dat); corrigeren gaat met een
/// tegenboeking. Volgnummer en hash vormen de keten (<see cref="Journaalketen"/>).
/// </summary>
public sealed class Boeking : IVanAdministratie
{
    public Guid Id { get; set; }

    public Guid AdministratieId { get; set; }

    public long Volgnummer { get; set; }

    public string Dagboek { get; set; } = string.Empty;

    public string Boekstuk { get; set; } = string.Empty;

    public DateOnly Datum { get; set; }

    public string Omschrijving { get; set; } = string.Empty;

    public DateTimeOffset GeboektOp { get; set; }

    public Guid? GeboektDoorId { get; set; }

    public string GeboektDoor { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string VorigeHash { get; set; } = string.Empty;

    public List<Boekingsregel> Regels { get; set; } = [];

    public Journaalpost NaarDomein() => new(Dagboek, Boekstuk, Datum, Omschrijving,
        [.. Regels.OrderBy(r => r.Regelnummer).Select(r => new Journaalregel(r.Rekeningnummer, r.Bedrag, r.Omschrijving, r.BtwCode, r.Relatie, r.Factuurnummer))]);
}

public sealed class Boekingsregel : IVanAdministratie
{
    public long Id { get; set; }

    public Guid BoekingId { get; set; }

    public Guid AdministratieId { get; set; }

    public int Regelnummer { get; set; }

    public string Rekeningnummer { get; set; } = string.Empty;

    public decimal Bedrag { get; set; }

    public string? Omschrijving { get; set; }

    public string? BtwCode { get; set; }

    public string? Relatie { get; set; }

    public string? Factuurnummer { get; set; }
}

/// <summary>Eén regel in het wijzigingslog; wordt nooit gewijzigd of verwijderd (de database weigert dat).</summary>
public sealed class AuditRegel
{
    public long Id { get; set; }

    public DateTimeOffset Tijdstip { get; set; }

    public Guid? GebruikerId { get; set; }

    public string Gebruiker { get; set; } = string.Empty;

    /// <summary>De administratie waarin het gebeurde, of leeg (inloggen, gebruikersbeheer).</summary>
    public Guid? AdministratieId { get; set; }

    public string Actie { get; set; } = string.Empty;

    public string? Onderwerp { get; set; }

    public string? Details { get; set; }
}
