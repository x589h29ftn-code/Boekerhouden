using System.Security.Claims;
using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// De gebruiker van de Windows-app op één pc: wie de app start, is deze gebruiker (beheerder en kantoor), zonder
/// wachtwoord of tweestapsverificatie. Het wijzigingslog noemt de naam van de Windows-gebruiker.
/// </summary>
public static class LokaleGebruiker
{
    public const string Email = "lokaal@boekerhouden.local";

    /// <summary>Maakt de gebruiker zo nodig aan en geeft hem terug zoals een ingelogde gebruiker.</summary>
    public static async Task<ClaimsPrincipal> Zorg(IServiceProvider diensten, string naam)
    {
        ArgumentNullException.ThrowIfNull(diensten);
        ArgumentException.ThrowIfNullOrWhiteSpace(naam);
        await using var scope = diensten.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.FindByEmailAsync(Email);
        if (gebruiker is null)
        {
            gebruiker = new Gebruiker
            {
                Id = Guid.NewGuid(),
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                Naam = naam,
                Initialen = Initialen(naam),
                Actief = true,
            };
            Controleer(await users.CreateAsync(gebruiker));
        }
        else if (gebruiker.Naam != naam)
        {
            gebruiker.Naam = naam;
            gebruiker.Initialen = Initialen(naam);
            Controleer(await users.UpdateAsync(gebruiker));
        }

        foreach (var rol in Rollen.Alle)
        {
            if (!await users.IsInRoleAsync(gebruiker, rol))
            {
                Controleer(await users.AddToRoleAsync(gebruiker, rol));
            }
        }

        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<Gebruiker>>();
        var principal = await factory.CreateAsync(gebruiker);
        return new ClaimsPrincipal(new ClaimsIdentity(principal.Claims, "Lokaal", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static string Initialen(string naam) =>
        string.Concat(naam.Split([' ', '.', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(2));

    private static void Controleer(IdentityResult uitkomst)
    {
        if (!uitkomst.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", uitkomst.Errors.Select(e => e.Description)));
        }
    }
}

/// <summary>De lokale gebruiker is altijd "ingelogd" (Windows-app).</summary>
public sealed class LokaleAuthenticatie(ClaimsPrincipal gebruiker) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(gebruiker));
}
