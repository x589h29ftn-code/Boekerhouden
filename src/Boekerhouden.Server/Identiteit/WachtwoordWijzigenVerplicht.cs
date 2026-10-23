namespace Boekerhouden.Server.Identiteit;

/// <summary>
/// Een gebruiker met een tijdelijk wachtwoord (nieuw of gereset) kan pas verder nadat hij een eigen wachtwoord
/// heeft gekozen. Statische bestanden, uitloggen en de wijzigpagina zelf blijven bereikbaar.
/// </summary>
public sealed class WachtwoordWijzigenVerplicht(RequestDelegate volgende)
{
    public const string Pad = "/wachtwoord-wijzigen";

    private static readonly string[] Vrij = [Pad, "/account/uitloggen", "/_framework", "/_blazor", "/_content", "/lib"];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var verplicht = context.User.Identity?.IsAuthenticated == true
                        && context.User.HasClaim(BoekerhoudenClaims.MoetWachtwoordWijzigen, "true");
        var pad = context.Request.Path;
        var isStatisch = Path.HasExtension(pad.Value);
        if (verplicht && !isStatisch && HttpMethods.IsGet(context.Request.Method)
            && !Vrij.Any(v => pad.StartsWithSegments(v, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(Pad);
            return Task.CompletedTask;
        }

        return volgende(context);
    }
}
