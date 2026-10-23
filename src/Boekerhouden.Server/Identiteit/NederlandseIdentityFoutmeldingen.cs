using Microsoft.AspNetCore.Identity;

namespace Boekerhouden.Server.Identiteit;

/// <summary>Foutmeldingen van ASP.NET Core Identity in het Nederlands.</summary>
public sealed class NederlandseIdentityFoutmeldingen : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Fout(nameof(DefaultError), "Er is een onbekende fout opgetreden.");

    public override IdentityError ConcurrencyFailure() => Fout(nameof(ConcurrencyFailure), "Iemand anders heeft dit tegelijk gewijzigd. Probeer het opnieuw.");

    public override IdentityError PasswordMismatch() => Fout(nameof(PasswordMismatch), "Het wachtwoord is onjuist.");

    public override IdentityError InvalidToken() => Fout(nameof(InvalidToken), "De link of code is ongeldig of verlopen.");

    public override IdentityError InvalidUserName(string? userName) => Fout(nameof(InvalidUserName), $"Gebruikersnaam '{userName}' is ongeldig.");

    public override IdentityError InvalidEmail(string? email) => Fout(nameof(InvalidEmail), $"E-mailadres '{email}' is ongeldig.");

    public override IdentityError DuplicateUserName(string userName) => Fout(nameof(DuplicateUserName), $"Er bestaat al een gebruiker met e-mailadres '{userName}'.");

    public override IdentityError DuplicateEmail(string email) => Fout(nameof(DuplicateEmail), $"Er bestaat al een gebruiker met e-mailadres '{email}'.");

    public override IdentityError InvalidRoleName(string? role) => Fout(nameof(InvalidRoleName), $"Rol '{role}' is ongeldig.");

    public override IdentityError DuplicateRoleName(string role) => Fout(nameof(DuplicateRoleName), $"Rol '{role}' bestaat al.");

    public override IdentityError UserAlreadyHasPassword() => Fout(nameof(UserAlreadyHasPassword), "De gebruiker heeft al een wachtwoord.");

    public override IdentityError UserLockoutNotEnabled() => Fout(nameof(UserLockoutNotEnabled), "Blokkeren is voor deze gebruiker niet ingeschakeld.");

    public override IdentityError UserAlreadyInRole(string role) => Fout(nameof(UserAlreadyInRole), $"De gebruiker heeft de rol '{role}' al.");

    public override IdentityError UserNotInRole(string role) => Fout(nameof(UserNotInRole), $"De gebruiker heeft de rol '{role}' niet.");

    public override IdentityError PasswordTooShort(int length) => Fout(nameof(PasswordTooShort), $"Het wachtwoord moet minstens {length} tekens lang zijn.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Fout(nameof(PasswordRequiresUniqueChars), $"Het wachtwoord moet minstens {uniqueChars} verschillende tekens bevatten.");

    public override IdentityError PasswordRequiresNonAlphanumeric() => Fout(nameof(PasswordRequiresNonAlphanumeric), "Het wachtwoord moet minstens één leesteken of symbool bevatten.");

    public override IdentityError PasswordRequiresDigit() => Fout(nameof(PasswordRequiresDigit), "Het wachtwoord moet minstens één cijfer bevatten.");

    public override IdentityError PasswordRequiresLower() => Fout(nameof(PasswordRequiresLower), "Het wachtwoord moet minstens één kleine letter bevatten.");

    public override IdentityError PasswordRequiresUpper() => Fout(nameof(PasswordRequiresUpper), "Het wachtwoord moet minstens één hoofdletter bevatten.");

    public override IdentityError RecoveryCodeRedemptionFailed() => Fout(nameof(RecoveryCodeRedemptionFailed), "De herstelcode is ongeldig.");

    public override IdentityError LoginAlreadyAssociated() => Fout(nameof(LoginAlreadyAssociated), "Deze externe login is al gekoppeld aan een gebruiker.");

    private static IdentityError Fout(string code, string tekst) => new() { Code = code, Description = tekst };
}
