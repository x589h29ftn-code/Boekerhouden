using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Data;

/// <summary>Een medewerker die inlogt. De gebruikersnaam is het e-mailadres.</summary>
public sealed class Gebruiker : IdentityUser<Guid>
{
    public string Naam { get; set; } = string.Empty;

    public string Initialen { get; set; } = string.Empty;

    /// <summary>Bijvoorbeeld "AA" of "RA"; gebruikt bij ondertekening van rapporten.</summary>
    public string? Titel { get; set; }

    public bool Actief { get; set; } = true;

    /// <summary>Na aanmaken of resetten door de beheerder moet de gebruiker eerst een eigen wachtwoord kiezen.</summary>
    public bool MoetWachtwoordWijzigen { get; set; }
}
