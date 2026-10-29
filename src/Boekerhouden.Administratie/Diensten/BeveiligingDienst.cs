using System.Globalization;
using System.Text;
using Boekerhouden.Administratie.Data;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De authenticator-app van een gebruiker: de sleutel om in te stellen, en of hij aan staat.</summary>
public sealed record AuthenticatorStatus(bool Aan, string? Sleutel, string? Uri, int HerstelcodesOver);

public sealed record PasskeyRegel(string Id, string Naam, DateTimeOffset Aangemaakt);

/// <summary>
/// Tweestapsverificatie (beveiliging B1): een authenticator-app (TOTP) met herstelcodes, en passkeys. Elke handeling
/// krijgt een eigen scope (een Blazor-circuit leeft lang) en komt in het wijzigingslog.
/// </summary>
public sealed class BeveiligingDienst(IServiceScopeFactory scopeFactory, AuditDienst audit)
{
    public const string Uitgever = "Boekerhouden Administratie";
    public const int AantalHerstelcodes = 10;

    public async Task<AuthenticatorStatus> Authenticator(Guid gebruiker)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        if (g.TwoFactorEnabled)
        {
            return new AuthenticatorStatus(true, null, null, await users.CountRecoveryCodesAsync(g));
        }

        var sleutel = await users.GetAuthenticatorKeyAsync(g);
        if (string.IsNullOrEmpty(sleutel))
        {
            await users.ResetAuthenticatorKeyAsync(g);
            sleutel = await users.GetAuthenticatorKeyAsync(g);
        }

        return new AuthenticatorStatus(false, Groepeer(sleutel!), Uri(g.Email!, sleutel!), 0);
    }

    /// <summary>Zet de authenticator aan na een juiste code uit de app; geeft de herstelcodes (eenmalig te tonen).</summary>
    public async Task<Uitkomst<IReadOnlyList<string>>> ZetAan(Guid gebruiker, string code)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        var schoon = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (!await users.VerifyTwoFactorTokenAsync(g, users.Options.Tokens.AuthenticatorTokenProvider, schoon))
        {
            return Uitkomst<IReadOnlyList<string>>.Mislukt("De code klopt niet. Kijk of de tijd op je telefoon goed staat en probeer de volgende code.");
        }

        await users.SetTwoFactorEnabledAsync(g, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(g, AantalHerstelcodes))!.ToList();
        await audit.Log(new Uitvoerder(g.Id, g.Naam), AuditActies.TweestapAan);
        return Uitkomst<IReadOnlyList<string>>.Met(codes);
    }

    /// <summary>Zet de authenticator uit; niet voor een beheerder (voor hem is tweestapsverificatie verplicht).</summary>
    public async Task<Uitkomst> ZetUit(Guid gebruiker)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        if (await users.IsInRoleAsync(g, Rollen.Beheerder))
        {
            return Uitkomst.Mislukt("Voor een beheerder is tweestapsverificatie verplicht.");
        }

        await users.SetTwoFactorEnabledAsync(g, false);
        await users.ResetAuthenticatorKeyAsync(g);
        await audit.Log(new Uitvoerder(g.Id, g.Naam), AuditActies.TweestapUit);
        return Uitkomst.Gelukt;
    }

    public async Task<IReadOnlyList<string>> NieuweHerstelcodes(Guid gebruiker)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(g, AantalHerstelcodes))!.ToList();
        await audit.Log(new Uitvoerder(g.Id, g.Naam), AuditActies.HerstelcodesVernieuwd);
        return codes;
    }

    public async Task<IReadOnlyList<PasskeyRegel>> Passkeys(Guid gebruiker)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        return [.. (await users.GetPasskeysAsync(g)).OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyRegel(Base64Url(p.CredentialId), p.Name ?? "Passkey", p.CreatedAt))];
    }

    /// <summary>Slaat een passkey op na een geslaagde registratie (attestatie) in de browser.</summary>
    public async Task<Uitkomst> VoegPasskeyToe(Guid gebruiker, UserPasskeyInfo passkey, string? naam)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        passkey.Name = string.IsNullOrWhiteSpace(naam) ? $"Passkey {passkey.CreatedAt.ToLocalTime():dd-MM-yyyy}" : naam.Trim()[..Math.Min(100, naam.Trim().Length)];
        var resultaat = await users.AddOrUpdatePasskeyAsync(g, passkey);
        if (!resultaat.Succeeded)
        {
            return Uitkomst.Mislukt([.. resultaat.Errors.Select(e => e.Description)]);
        }

        await audit.Log(new Uitvoerder(g.Id, g.Naam), AuditActies.PasskeyToegevoegd, passkey.Name);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> VerwijderPasskey(Guid gebruiker, string id)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        var g = await users.FindByIdAsync(gebruiker.ToString()) ?? throw new InvalidOperationException("Gebruiker bestaat niet.");
        byte[] credentialId;
        try
        {
            credentialId = Convert.FromBase64String(id.Replace('-', '+').Replace('_', '/').PadRight((id.Length + 3) / 4 * 4, '='));
        }
        catch (FormatException)
        {
            return Uitkomst.Mislukt("Deze passkey bestaat niet (meer).");
        }

        if (await users.GetPasskeyAsync(g, credentialId) is null)
        {
            return Uitkomst.Mislukt("Deze passkey bestaat niet (meer).");
        }

        var resultaat = await users.RemovePasskeyAsync(g, credentialId);
        if (!resultaat.Succeeded)
        {
            return Uitkomst.Mislukt("Deze passkey bestaat niet (meer).");
        }

        await audit.Log(new Uitvoerder(g.Id, g.Naam), AuditActies.PasskeyVerwijderd);
        return Uitkomst.Gelukt;
    }

    /// <summary>De otpauth-URI die de authenticator-app via de QR-code inleest.</summary>
    internal static string Uri(string email, string sleutel) =>
        string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{System.Uri.EscapeDataString(Uitgever)}:{System.Uri.EscapeDataString(email)}?secret={sleutel}&issuer={System.Uri.EscapeDataString(Uitgever)}&digits=6");

    /// <summary>De QR-code als SVG (zonder script of plaatje van buiten, dus binnen de Content-Security-Policy).</summary>
    public static string QrSvg(string tekst)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(tekst, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var grootte = matrix.Count;
        var pad = new StringBuilder();
        for (var y = 0; y < grootte; y++)
        {
            for (var x = 0; x < grootte; x++)
            {
                if (matrix[y][x])
                {
                    pad.Append(CultureInfo.InvariantCulture, $"M{x},{y}h1v1h-1z");
                }
            }
        }

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {grootte} {grootte}\" width=\"200\" height=\"200\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR-code\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path d=\"{pad}\" fill=\"#000\"/></svg>";
    }

    private static string Groepeer(string sleutel)
    {
        var resultaat = new StringBuilder();
        for (var i = 0; i < sleutel.Length; i += 4)
        {
            resultaat.Append(sleutel.AsSpan(i, Math.Min(4, sleutel.Length - i))).Append(' ');
        }

        return resultaat.ToString().TrimEnd().ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
