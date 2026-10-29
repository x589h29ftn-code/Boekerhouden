using System.Net;
using System.Text.RegularExpressions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Inloggen via het formulier, zoals een browser zonder JavaScript.</summary>
internal static partial class Aanmelden
{
    public static async Task<HttpResponseMessage> LogIn(HttpClient client, string email, string wachtwoord, string terugNaar)
    {
        var url = new Uri($"/inloggen?ReturnUrl={Uri.EscapeDataString(terugNaar)}", UriKind.Relative);
        var formulier = await client.GetStringAsync(url);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(formulier),
            ["_handler"] = "inloggen",
            ["Invoer.Email"] = email,
            ["Invoer.Wachtwoord"] = wachtwoord,
        }));
    }

    /// <summary>Inloggen met wachtwoord en daarna de code uit de authenticator-app.</summary>
    public static async Task<HttpResponseMessage> LogInMetCode(HttpClient client, string email, string wachtwoord, string sleutel, string terugNaar)
    {
        var eerste = await LogIn(client, email, wachtwoord, terugNaar);
        if (eerste.Doel() is not { } doel || !doel.StartsWith("/inloggen/tweede-stap", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Geen tweede stap gevraagd, maar {eerste.StatusCode} {eerste.Doel()}.");
        }

        return await TweedeStap(client, doel, code: Totp.Code(sleutel));
    }

    public static async Task<HttpResponseMessage> TweedeStap(HttpClient client, string pad, string? code = null, string? herstelcode = null)
    {
        var url = new Uri(pad, UriKind.Relative);
        var formulier = await client.GetStringAsync(url);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(formulier),
            ["_handler"] = "tweede-stap",
            ["Invoer.Code"] = code ?? string.Empty,
            ["Invoer.Herstelcode"] = herstelcode ?? string.Empty,
        }));
    }

    public static string Token(string html) => TokenPatroon().Match(html) is { Success: true } m
        ? WebUtility.HtmlDecode(m.Groups[1].Value)
        : throw new InvalidOperationException("Geen antiforgery-token in de pagina.");

    public static string? Doel(this HttpResponseMessage antwoord) =>
        antwoord.Headers.Location is not { } uri ? null : uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPatroon();
}
