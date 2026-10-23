using System.Security.Cryptography;
using Boekerhouden.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record GebruikerInvoer(string Email, string Naam, string Initialen, string? Titel, IReadOnlyCollection<string> Rollen);

public sealed record GebruikerRegel(
    Guid Id,
    string Email,
    string Naam,
    string Initialen,
    string? Titel,
    bool Actief,
    bool Geblokkeerd,
    bool MoetWachtwoordWijzigen,
    IReadOnlyList<string> Rollen);

/// <summary>
/// Gebruikersbeheer door de beheerder. Elke handeling krijgt een eigen scope (en dus een verse UserManager en
/// DbContext), omdat een Blazor-circuit lang leeft.
/// </summary>
public sealed class GebruikerDienst(IServiceScopeFactory scopeFactory, AuditDienst audit)
{
    private const string Letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<bool> EersteBeheerderNodig()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoekerhoudenDbContext>();
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

        var fouten = Valideer(invoer, rollenVerplicht: false);
        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = NieuweGebruiker(invoer);
        var resultaat = await users.CreateAsync(gebruiker, wachtwoord);
        if (!resultaat.Succeeded)
        {
            return Mislukt(resultaat);
        }

        await users.AddToRolesAsync(gebruiker, Rollen.Alle);
        await audit.Log(new Uitvoerder(gebruiker.Id, gebruiker.Naam), AuditActies.EersteBeheerderAangemaakt, gebruiker.Email);
        return Uitkomst.Gelukt;
    }

    public async Task<IReadOnlyList<GebruikerRegel>> Lijst()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoekerhoudenDbContext>();
        var rollenPerGebruiker = await (from ur in db.UserRoles
                                        join r in db.Roles on ur.RoleId equals r.Id
                                        select new { ur.UserId, r.Name })
            .ToListAsync();
        var gebruikers = await db.Users.AsNoTracking().OrderBy(g => g.Naam).ToListAsync();
        var nu = DateTimeOffset.UtcNow;
        return
        [
            .. gebruikers.Select(g => new GebruikerRegel(
                g.Id,
                g.Email ?? string.Empty,
                g.Naam,
                g.Initialen,
                g.Titel,
                g.Actief,
                g.Actief && g.LockoutEnd > nu,
                g.MoetWachtwoordWijzigen,
                [.. Rollen.Alle.Where(r => rollenPerGebruiker.Any(x => x.UserId == g.Id && x.Name == r))])),
        ];
    }

    /// <summary>Maakt een gebruiker aan met een tijdelijk wachtwoord, dat bij de eerste keer inloggen moet worden gewijzigd.</summary>
    public async Task<Uitkomst<string>> Maak(GebruikerInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = Valideer(invoer, rollenVerplicht: true);
        if (fouten.Count > 0)
        {
            return Uitkomst<string>.Mislukt([.. fouten]);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = NieuweGebruiker(invoer);
        gebruiker.MoetWachtwoordWijzigen = true;
        var wachtwoord = TijdelijkWachtwoord();
        var resultaat = await users.CreateAsync(gebruiker, wachtwoord);
        if (!resultaat.Succeeded)
        {
            return Uitkomst<string>.Mislukt([.. Teksten(resultaat)]);
        }

        await users.AddToRolesAsync(gebruiker, invoer.Rollen.Distinct());
        await audit.Log(wie, AuditActies.GebruikerAangemaakt, gebruiker.Email, $"Rollen: {string.Join(", ", invoer.Rollen)}");
        return Uitkomst<string>.Met(wachtwoord);
    }

    public async Task<Uitkomst> Wijzig(Guid id, GebruikerInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = Valideer(invoer, rollenVerplicht: true);
        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.FindByIdAsync(id.ToString());
        if (gebruiker is null)
        {
            return Uitkomst.Mislukt("De gebruiker bestaat niet (meer).");
        }

        var huidigeRollen = await users.GetRolesAsync(gebruiker);
        var wordtGeenBeheerder = huidigeRollen.Contains(Rollen.Beheerder) && !invoer.Rollen.Contains(Rollen.Beheerder);
        if (wordtGeenBeheerder && id == wie.Id)
        {
            return Uitkomst.Mislukt("Je kunt je eigen beheerdersrol niet intrekken. Laat een andere beheerder dat doen.");
        }

        if (wordtGeenBeheerder && gebruiker.Actief && await AantalActieveBeheerders(users) <= 1)
        {
            return Uitkomst.Mislukt("Dit is de laatste actieve beheerder; die rol kan niet worden ingetrokken.");
        }

        gebruiker.Naam = invoer.Naam.Trim();
        gebruiker.Initialen = invoer.Initialen.Trim();
        gebruiker.Titel = string.IsNullOrWhiteSpace(invoer.Titel) ? null : invoer.Titel.Trim();
        gebruiker.Email = invoer.Email.Trim();
        gebruiker.UserName = invoer.Email.Trim();
        var resultaat = await users.UpdateAsync(gebruiker);
        if (!resultaat.Succeeded)
        {
            return Mislukt(resultaat);
        }

        var erbij = invoer.Rollen.Except(huidigeRollen).ToList();
        var eraf = huidigeRollen.Except(invoer.Rollen).ToList();
        if (erbij.Count > 0)
        {
            await users.AddToRolesAsync(gebruiker, erbij);
        }

        if (eraf.Count > 0)
        {
            await users.RemoveFromRolesAsync(gebruiker, eraf);
        }

        if (erbij.Count > 0 || eraf.Count > 0)
        {
            // Andere rechten: de gebruiker moet opnieuw inloggen.
            await users.UpdateSecurityStampAsync(gebruiker);
        }

        await audit.Log(wie, AuditActies.GebruikerGewijzigd, gebruiker.Email, $"Rollen: {string.Join(", ", invoer.Rollen)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// (De)activeert een gebruiker. Een gedeactiveerde gebruiker kan niet meer inloggen en wordt binnen een minuut
    /// uitgelogd; de gebruiker blijft bestaan, zodat zijn naam in dossiers en het wijzigingslog herleidbaar blijft.
    /// </summary>
    public async Task<Uitkomst> ZetActief(Guid id, bool actief, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.FindByIdAsync(id.ToString());
        if (gebruiker is null)
        {
            return Uitkomst.Mislukt("De gebruiker bestaat niet (meer).");
        }

        if (!actief)
        {
            if (id == wie.Id)
            {
                return Uitkomst.Mislukt("Je kunt jezelf niet deactiveren.");
            }

            if (gebruiker.Actief && await users.IsInRoleAsync(gebruiker, Rollen.Beheerder) && await AantalActieveBeheerders(users) <= 1)
            {
                return Uitkomst.Mislukt("Dit is de laatste actieve beheerder; die kan niet worden gedeactiveerd.");
            }
        }

        gebruiker.Actief = actief;
        gebruiker.LockoutEnabled = true;
        gebruiker.LockoutEnd = actief ? null : DateTimeOffset.MaxValue;
        gebruiker.AccessFailedCount = 0;
        var resultaat = await users.UpdateAsync(gebruiker);
        if (!resultaat.Succeeded)
        {
            return Mislukt(resultaat);
        }

        await users.UpdateSecurityStampAsync(gebruiker);
        await audit.Log(wie, actief ? AuditActies.GebruikerGeactiveerd : AuditActies.GebruikerGedeactiveerd, gebruiker.Email);
        return Uitkomst.Gelukt;
    }

    /// <summary>Geeft een nieuw tijdelijk wachtwoord en heft een blokkade na te veel inlogpogingen op.</summary>
    public async Task<Uitkomst<string>> ResetWachtwoord(Guid id, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var gebruiker = await users.FindByIdAsync(id.ToString());
        if (gebruiker is null)
        {
            return Uitkomst<string>.Mislukt("De gebruiker bestaat niet (meer).");
        }

        if (!gebruiker.Actief)
        {
            return Uitkomst<string>.Mislukt("Activeer de gebruiker eerst.");
        }

        var wachtwoord = TijdelijkWachtwoord();
        var token = await users.GeneratePasswordResetTokenAsync(gebruiker);
        var resultaat = await users.ResetPasswordAsync(gebruiker, token, wachtwoord);
        if (!resultaat.Succeeded)
        {
            return Uitkomst<string>.Mislukt([.. Teksten(resultaat)]);
        }

        gebruiker.MoetWachtwoordWijzigen = true;
        gebruiker.LockoutEnd = null;
        gebruiker.AccessFailedCount = 0;
        await users.UpdateAsync(gebruiker);
        await audit.Log(wie, AuditActies.WachtwoordGereset, gebruiker.Email);
        return Uitkomst<string>.Met(wachtwoord);
    }

    /// <summary>Een tijdelijk wachtwoord van 4 × 4 tekens zonder verwarrende tekens (0/O, 1/l/I), dat aan het wachtwoordbeleid voldoet.</summary>
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

    private static async Task<int> AantalActieveBeheerders(UserManager<Gebruiker> users) =>
        (await users.GetUsersInRoleAsync(Rollen.Beheerder)).Count(g => g.Actief);

    private static Gebruiker NieuweGebruiker(GebruikerInvoer invoer) => new()
    {
        Id = Guid.NewGuid(),
        UserName = invoer.Email.Trim(),
        Email = invoer.Email.Trim(),
        EmailConfirmed = true,
        Naam = invoer.Naam.Trim(),
        Initialen = invoer.Initialen.Trim(),
        Titel = string.IsNullOrWhiteSpace(invoer.Titel) ? null : invoer.Titel.Trim(),
        LockoutEnabled = true,
    };

    private static List<string> Valideer(GebruikerInvoer invoer, bool rollenVerplicht)
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

        if (rollenVerplicht && invoer.Rollen.Count == 0)
        {
            fouten.Add("Kies minstens één rol.");
        }

        if (invoer.Rollen.Any(r => !Rollen.Alle.Contains(r)))
        {
            fouten.Add("Onbekende rol.");
        }

        return fouten;
    }

    private static IEnumerable<string> Teksten(IdentityResult resultaat) => resultaat.Errors.Select(e => e.Description);

    private static Uitkomst Mislukt(IdentityResult resultaat) => Uitkomst.Mislukt([.. Teksten(resultaat)]);
}
