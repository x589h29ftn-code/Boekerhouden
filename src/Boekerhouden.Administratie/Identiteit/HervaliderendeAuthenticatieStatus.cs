using System.Security.Claims;
using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// Controleert elke minuut of de ingelogde gebruiker nog geldig is (security stamp). Wordt een gebruiker
/// gedeactiveerd, krijgt hij andere rollen of een nieuw wachtwoord, dan wordt een open scherm uitgelogd.
/// </summary>
public sealed class HervaliderendeAuthenticatieStatus(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticationState);
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.GetUserAsync(authenticationState.User);
        if (gebruiker is null || !gebruiker.Actief)
        {
            return false;
        }

        var stempel = authenticationState.User.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        return stempel == await users.GetSecurityStampAsync(gebruiker);
    }
}
