using Boekerhouden.Administratie.Components;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
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
