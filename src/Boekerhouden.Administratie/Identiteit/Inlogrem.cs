using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Boekerhouden.Administratie.Identiteit;

/// <summary>
/// Een rem op inloggen per IP-adres (beveiliging §4): hoogstens <see cref="Pogingen"/> keer in
/// <see cref="Venster"/> een inlogformulier versturen (wachtwoord, tweede stap, passkey, eerste gebruiker). Naast de
/// blokkade per account (5 pogingen) houdt dit het uitproberen van één wachtwoord op veel accounts tegen. Verkeer van
/// de eigen pc (loopback, bijv. tijdens het ontwikkelen) telt niet; achter een proxy op dezelfde server (Caddy) geeft
/// <see cref="ForwardedHeaders"/> het echte adres van de bezoeker door.
/// </summary>
public static class Inlogrem
{
    public const int Pogingen = 30;

    public static readonly TimeSpan Venster = TimeSpan.FromMinutes(5);

    private static readonly string[] Paden = ["/inloggen", "/eerste-gebruiker", "/account/passkey"];

    public static void Voegtoe(IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Sleutel(context) is { } ip
                ? RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = Pogingen, Window = Venster, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter(string.Empty));
            o.OnRejected = async (afgewezen, stoppen) =>
            {
                afgewezen.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await afgewezen.HttpContext.Response.WriteAsync("Te veel pogingen om in te loggen vanaf dit adres. Probeer het over een paar minuten opnieuw.", stoppen);
            };
        });

    /// <summary>Het IP-adres als het verzoek een inlogformulier verstuurt van buiten de eigen pc; anders leeg (geen rem).</summary>
    internal static string? Sleutel(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || context.Connection.RemoteIpAddress is not { } ip || IPAddress.IsLoopback(ip))
        {
            return null;
        }

        var pad = context.Request.Path.Value ?? string.Empty;
        return Paden.Any(p => pad.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? ip.ToString() : null;
    }

    /// <summary>Achter een proxy op dezelfde server: het adres en https van de bezoeker (alleen van een proxy op loopback).</summary>
    public static ForwardedHeadersOptions Proxy() => new() { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
}
