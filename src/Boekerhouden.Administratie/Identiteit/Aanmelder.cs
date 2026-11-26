using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// Inloggen (wachtwoord, passkey, tweede stap) alleen voor een gebruiker die actief is: een uitgezette gebruiker komt
/// er niet meer in, ook niet bij Beheer. Hij krijgt dezelfde melding als bij een verkeerd wachtwoord.
/// </summary>
public sealed class Aanmelder(
    UserManager<Gebruiker> gebruikers,
    IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<Gebruiker> claims,
    IOptions<IdentityOptions> opties,
    ILogger<SignInManager<Gebruiker>> logger,
    IAuthenticationSchemeProvider schemas,
    IUserConfirmation<Gebruiker> bevestiging)
    : SignInManager<Gebruiker>(gebruikers, context, claims, opties, logger, schemas, bevestiging)
{
    public override async Task<bool> CanSignInAsync(Gebruiker user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Actief && await base.CanSignInAsync(user);
    }
}
