namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// Beveiligingsheaders op elk antwoord (beveiliging §4.7): de pagina mag niet in een frame (clickjacking), de browser
/// raadt geen bestandstypen, verwijzingen gaan niet naar buiten, en camera, microfoon en locatie staan uit. De
/// Content-Security-Policy staat alleen bronnen van de eigen site toe; Blazor heeft inline-stijlen en een websocket
/// nodig.
/// </summary>
public sealed class Beveiligingsheaders(RequestDelegate volgende)
{
    public const string Beleid =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "connect-src 'self' ws: wss:; font-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = Beleid;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        return volgende(context);
    }
}
