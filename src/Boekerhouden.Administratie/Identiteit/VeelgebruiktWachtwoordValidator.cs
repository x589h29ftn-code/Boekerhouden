using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>Weigert wachtwoorden met een veelgebruikt woord, of met de naam of het e-mailadres van de gebruiker.</summary>
public sealed class VeelgebruiktWachtwoordValidator : IPasswordValidator<Gebruiker>
{
    private static readonly string[] Verboden =
    [
        "wachtwoord", "password", "welkom", "welcome", "qwerty", "azerty", "123456", "654321", "abcdef",
        "boekerhouden", "administratie", "admin", "beheerder", "geheim", "letmein", "zomer", "winter", "lente", "herfst",
    ];

    public Task<IdentityResult> ValidateAsync(UserManager<Gebruiker> manager, Gebruiker user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var persoonlijk = new List<string>();
        if (user.Email?.Split('@')[0] is { Length: >= 4 } lokaal)
        {
            persoonlijk.Add(lokaal);
        }

        persoonlijk.AddRange(user.Naam.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(d => d.Length >= 4));

        if (Verboden.Any(w => password.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Fout("Het wachtwoord bevat een veelgebruikt woord of reeks. Kies iets dat minder voor de hand ligt."));
        }

        if (persoonlijk.Any(w => password.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Fout("Het wachtwoord mag je naam of e-mailadres niet bevatten."));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    private static IdentityResult Fout(string tekst) =>
        IdentityResult.Failed(new IdentityError { Code = "VeelgebruiktWachtwoord", Description = tekst });
}
