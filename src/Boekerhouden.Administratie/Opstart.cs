using Boekerhouden.Administratie.Components;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Documenten;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie;

/// <summary>Configuratie van diensten en de request-pijplijn; gedeeld door Program en de integratietests.</summary>
public static class Opstart
{
    public const string Verbindingsnaam = "Administratie";

    public static void ConfigureerDiensten(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddRazorComponents().AddInteractiveServerComponents();

        // X-Frame-Options en frame-ancestors zet Beveiligingsheaders zelf (strenger: DENY / 'none').
        services.AddAntiforgery(o => o.SuppressXFrameOptionsHeader = true);
        services.AddCascadingAuthenticationState();
        services.AddScoped<Doorverwijzer>();
        services.AddScoped<AuthenticationStateProvider, HervaliderendeAuthenticatieStatus>();

        var verbinding = builder.Configuration.GetConnectionString(Verbindingsnaam)
                         ?? throw new InvalidOperationException($"Verbindingsgegevens '{Verbindingsnaam}' ontbreken in appsettings.json.");
        services.AddDbContextFactory<AdministratieDbContext>(o => o.UseNpgsql(verbinding));

        // De sleutels voor cookies en tokens staan in de database, zodat inloggen een herstart overleeft en de sleutels
        // niet als los bestand op de server rondslingeren (beveiliging §4.7).
        var sleutels = services.AddDataProtection().SetApplicationName("Boekerhouden.Administratie").PersistKeysToDbContext<AdministratieDbContext>();
        if (OperatingSystem.IsWindows())
        {
            // Op de server-pc zijn de sleutels in de database bovendien versleuteld met de Windows-machinesleutel.
            sleutels.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "Boekerhouden.Administratie";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.LoginPath = "/inloggen";
            o.LogoutPath = "/account/uitloggen";
            o.AccessDeniedPath = "/geen-toegang";
            o.ExpireTimeSpan = TimeSpan.FromHours(10);
            o.SlidingExpiration = true;
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddIdentityCore<Gebruiker>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AdministratieDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<NederlandseIdentityFoutmeldingen>()
            .AddPasswordValidator<VeelgebruiktWachtwoordValidator>()
            .AddClaimsPrincipalFactory<GebruikerClaimsFactory>();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Rollen.Beheerder, p => p.RequireRole(Rollen.Beheerder));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditDienst>();
        services.AddSingleton<GebruikerDienst>();
        services.AddSingleton<Toegang>();
        services.AddSingleton<AdministratieDienst>();
        services.AddSingleton<GrootboekDienst>();
        services.AddSingleton<BeveiligingDienst>();
        services.AddSingleton<InkoopDienst>();

        // Virusscan vóór het openen van elk document (besluit B3). Zonder instelling: op de server fail closed, in
        // ontwikkeling (zonder ClamAV) zonder scan.
        if (builder.Configuration["Virusscan:Adres"] is { Length: > 0 } adres)
        {
            services.AddSingleton<IVirusscanner>(ClamAvScanner.Van(adres));
        }
        else if (!builder.Environment.IsDevelopment())
        {
            services.AddSingleton<IVirusscanner, OntbrekendeVirusscanner>();
        }
    }

    public static void ConfigureerPijplijn(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/fout", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseMiddleware<Beveiligingsheaders>();
        app.UseStatusCodePagesWithReExecute("/niet-gevonden", createScopeForStatusCodePages: true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<WachtwoordWijzigenVerplicht>();
        app.UseMiddleware<TweestapVerplicht>();
        app.UseAntiforgery();

        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);

        app.MapPost("/account/uitloggen", async (SignInManager<Gebruiker> signIn, AuditDienst audit, HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await audit.Log(context.User.AlsUitvoerder(), AuditActies.Uitgelogd);
            }

            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect("~/inloggen");
        });

        // Een ingelezen document bekijken: alleen voor leden, een PDF in de pdf-viewer van de browser (in een sandbox,
        // zonder scripts), al het andere als download (beveiliging §4.5).
        app.MapGet("/a/{administratie:guid}/documenten/{document:guid}", async (Guid administratie, Guid document, HttpContext context, InkoopDienst inkoop) =>
        {
            (byte[] Inhoud, string Inhoudstype, string Bestandsnaam)? bestand;
            try
            {
                bestand = await inkoop.Inhoud(context.User.AlsUitvoerder(), administratie, document);
            }
            catch (GeenToegangException)
            {
                bestand = null;
            }

            if (bestand is not { } b)
            {
                return Results.NotFound("Dit document bestaat niet of je hebt er geen toegang toe.");
            }

            var pdf = b.Inhoud.AsSpan(0, Math.Min(b.Inhoud.Length, 1024)).IndexOf("%PDF-"u8) >= 0;
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; object-src 'self'; style-src 'unsafe-inline'";
            return pdf
                ? Results.File(b.Inhoud, "application/pdf", enableRangeProcessing: true)
                : Results.File(b.Inhoud, "application/octet-stream", b.Bestandsnaam);
        });

        // Passkeys: de opties voor de browser (navigator.credentials.create/get). De uitdaging staat in een cookie;
        // het antwoord van de browser gaat met het formulier mee (Beveiliging en Inloggen).
        app.MapPost("/account/passkey/opties-nieuw", async (HttpContext context, UserManager<Gebruiker> users, SignInManager<Gebruiker> signIn, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest("Ongeldig verzoek.");
            }

            var gebruiker = await users.GetUserAsync(context.User);
            if (gebruiker is null)
            {
                return Results.Unauthorized();
            }

            var opties = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = gebruiker.Id.ToString(), Name = gebruiker.Email!, DisplayName = gebruiker.Naam });
            return Results.Content(opties, "application/json");
        });
        app.MapPost("/account/passkey/opties-inloggen", async (HttpContext context, SignInManager<Gebruiker> signIn, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest("Ongeldig verzoek.");
            }

            // Zonder gebruiker: de browser biedt de passkeys aan die bij deze site horen.
            var opties = await signIn.MakePasskeyRequestOptionsAsync(null);
            return Results.Content(opties, "application/json");
        }).AllowAnonymous();
    }

    /// <summary>Werkt de database bij naar de laatste versie en zorgt dat de vaste rollen bestaan.</summary>
    public static async Task InitialiseerDatabase(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        await db.Database.MigrateAsync();

        var rollen = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var rol in Rollen.Alle)
        {
            if (!await rollen.RoleExistsAsync(rol))
            {
                await rollen.CreateAsync(new IdentityRole<Guid>(rol) { Id = Guid.NewGuid() });
            }
        }
    }
}
