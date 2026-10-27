using System.Security.Cryptography;
using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

public sealed record GebruikerInvoer(string Email, string Naam, string Initialen, IReadOnlyCollection<string> Rollen);

public sealed record GebruikerRegel(Guid Id, string Email, string Naam, bool Actief, bool MoetWachtwoordWijzigen, IReadOnlyList<string> Rollen);

/// <summary>Gebruikers van het administratiepakket. Elke handeling krijgt een eigen scope (een Blazor-circuit leeft lang).</summary>
public sealed class GebruikerDienst(IServiceScopeFactory scopeFactory, AuditDienst audit)
{
    private const string Letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<bool> EersteBeheerderNodig()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        return !await db.Users.AnyAsync();
    }

    /// <summary>Maakt bij een lege database de eerste gebruiker aan, met alle rollen. Daarna kan dit niet meer.</summary>
    public async Task<Uitkomst> MaakEersteBeheerder(GebruikerInvoer invoer, string wachtwoord)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        if (!await EersteBeheerderNodig())
        {
            return Uitkomst.Mislukt("Er is al een beheerder. Vraag die om een account.");
        }

        var fouten = Valideer(invoer);
        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = Nieuw(invoer);
        var resultaat = await users.CreateAsync(gebruiker, wachtwoord);
        if (!resultaat.Succeeded)
        {
            return Uitkomst.Mislukt([.. resultaat.Errors.Select(e => e.Description)]);
        }

        await users.AddToRolesAsync(gebruiker, Rollen.Alle);
        await audit.Log(new Uitvoerder(gebruiker.Id, gebruiker.Naam), AuditActies.EersteBeheerderAangemaakt, gebruiker.Email);
        return Uitkomst.Gelukt;
    }

    /// <summary>Maakt een gebruiker aan met een tijdelijk wachtwoord, dat bij de eerste keer inloggen moet worden gewijzigd.</summary>
    public async Task<Uitkomst<string>> Maak(GebruikerInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = Valideer(invoer);
        if (fouten.Count > 0)
        {
            return Uitkomst<string>.Mislukt([.. fouten]);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = Nieuw(invoer);
        gebruiker.MoetWachtwoordWijzigen = true;
        var wachtwoord = TijdelijkWachtwoord();
        var resultaat = await users.CreateAsync(gebruiker, wachtwoord);
        if (!resultaat.Succeeded)
        {
            return Uitkomst<string>.Mislukt([.. resultaat.Errors.Select(e => e.Description)]);
        }

        if (invoer.Rollen.Count > 0)
        {
            await users.AddToRolesAsync(gebruiker, invoer.Rollen.Distinct());
        }

        await audit.Log(wie, AuditActies.GebruikerAangemaakt, gebruiker.Email, $"Rollen: {string.Join(", ", invoer.Rollen)}");
        return Uitkomst<string>.Met(wachtwoord);
    }

    public async Task<IReadOnlyList<GebruikerRegel>> Lijst()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        var rollen = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
        var gebruikers = await db.Users.AsNoTracking().OrderBy(g => g.Naam).ToListAsync();
        return [.. gebruikers.Select(g => new GebruikerRegel(g.Id, g.Email ?? string.Empty, g.Naam, g.Actief, g.MoetWachtwoordWijzigen,
            [.. rollen.Where(r => r.UserId == g.Id).Select(r => r.Name!)]))];
    }

    /// <summary>Een tijdelijk wachtwoord van 4 × 4 tekens zonder verwarrende tekens, dat aan het wachtwoordbeleid voldoet.</summary>
    internal static string TijdelijkWachtwoord()
    {
        while (true)
        {
            var tekens = RandomNumberGenerator.GetItems<char>(Letters, 16);
            if (tekens.Any(char.IsUpper) && tekens.Any(char.IsLower) && tekens.Any(char.IsDigit) && tekens.Distinct().Count() >= 8)
            {
                var s = new string(tekens);
                return $"{s[..4]}-{s[4..8]}-{s[8..12]}-{s[12..]}";
            }
        }
    }

    private static Gebruiker Nieuw(GebruikerInvoer invoer) => new()
    {
        Id = Guid.NewGuid(),
        UserName = invoer.Email.Trim(),
        Email = invoer.Email.Trim(),
        EmailConfirmed = true,
        Naam = invoer.Naam.Trim(),
        Initialen = invoer.Initialen.Trim(),
        LockoutEnabled = true,
    };

    private static List<string> Valideer(GebruikerInvoer invoer)
    {
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Vul de naam in.");
        }

        if (string.IsNullOrWhiteSpace(invoer.Initialen))
        {
            fouten.Add("Vul de initialen in.");
        }

        if (string.IsNullOrWhiteSpace(invoer.Email) || !invoer.Email.Contains('@', StringComparison.Ordinal))
        {
            fouten.Add("Vul een geldig e-mailadres in; dat is ook de gebruikersnaam.");
        }

        if (invoer.Rollen.Any(r => !Rollen.Alle.Contains(r)))
        {
            fouten.Add("Onbekende rol.");
        }

        return fouten;
    }
}
