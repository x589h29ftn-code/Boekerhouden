using Boekerhouden.Administratie.Data;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// Voor een beheerder is tweestapsverificatie verplicht (beveiliging B1): zolang die niet aan staat, kan hij alleen
/// naar de pagina om hem in te stellen. Statische bestanden, uitloggen en passkeys blijven bereikbaar.
/// </summary>
public sealed class TweestapVerplicht(RequestDelegate volgende)
{
    public const string Pad = "/account/beveiliging";

    private static readonly string[] Vrij = [Pad, "/account/uitloggen", "/account/passkey", WachtwoordWijzigenVerplicht.Pad, "/_framework", "/_blazor", "/_content"];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var gebruiker = context.User;
        var verplicht = gebruiker.Identity?.IsAuthenticated == true
                        && gebruiker.IsInRole(Rollen.Beheerder)
                        && !gebruiker.HasClaim(AdministratieClaims.TweestapAan, "true");
        var pad = context.Request.Path;
        if (verplicht && !Path.HasExtension(pad.Value) && HttpMethods.IsGet(context.Request.Method)
            && !Vrij.Any(v => pad.StartsWithSegments(v, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(Pad);
            return Task.CompletedTask;
        }

        return volgende(context);
    }
}
