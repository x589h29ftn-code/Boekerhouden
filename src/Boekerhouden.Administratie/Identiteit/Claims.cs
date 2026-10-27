using System.Security.Claims;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Administratie.Identiteit;

public static class AdministratieClaims
{
    public const string Naam = "naam";
    public const string Initialen = "initialen";
    public const string MoetWachtwoordWijzigen = "moet_wachtwoord_wijzigen";

    public static string WeergaveNaam(this ClaimsPrincipal gebruiker)
    {
        ArgumentNullException.ThrowIfNull(gebruiker);
        return gebruiker.FindFirstValue(Naam) ?? gebruiker.Identity?.Name ?? "Onbekend";
    }

    public static string WeergaveInitialen(this ClaimsPrincipal gebruiker)
    {
        ArgumentNullException.ThrowIfNull(gebruiker);
        if (gebruiker.FindFirstValue(Initialen) is { Length: > 0 } initialen)
        {
            return initialen;
        }

        var woorden = gebruiker.WeergaveNaam().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(woorden.Length > 1 ? [woorden[0][0], woorden[^1][0]] : woorden.Select(w => w[0]).Take(1)).ToUpperInvariant();
    }

    /// <summary>De ingelogde gebruiker als uitvoerder van een handeling (voor het wijzigingslog en de rechten).</summary>
    public static Uitvoerder AlsUitvoerder(this ClaimsPrincipal gebruiker)
    {
        ArgumentNullException.ThrowIfNull(gebruiker);
        var id = Guid.TryParse(gebruiker.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : (Guid?)null;
        return new Uitvoerder(id, gebruiker.WeergaveNaam());
    }
}

/// <summary>Zet naam en "moet wachtwoord wijzigen" in het inlogcookie, zodat schermen en middleware die direct kunnen lezen.</summary>
public sealed class GebruikerClaimsFactory(
    UserManager<Gebruiker> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Gebruiker, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Gebruiker user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identiteit = await base.GenerateClaimsAsync(user);
        identiteit.AddClaim(new Claim(AdministratieClaims.Naam, user.Naam));
        identiteit.AddClaim(new Claim(AdministratieClaims.Initialen, user.Initialen));
        if (user.MoetWachtwoordWijzigen)
        {
            identiteit.AddClaim(new Claim(AdministratieClaims.MoetWachtwoordWijzigen, "true"));
        }

        return identiteit;
    }
}
