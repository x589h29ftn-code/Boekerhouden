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

    public static string Token(string html) => TokenPatroon().Match(html) is { Success: true } m
        ? WebUtility.HtmlDecode(m.Groups[1].Value)
        : throw new InvalidOperationException("Geen antiforgery-token in de pagina.");

    public static string? Doel(this HttpResponseMessage antwoord) =>
        antwoord.Headers.Location is not { } uri ? null : uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPatroon();
}
