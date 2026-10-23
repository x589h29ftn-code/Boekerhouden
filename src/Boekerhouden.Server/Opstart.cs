using Boekerhouden.Data;
using Boekerhouden.Import;
using Boekerhouden.Server.Components;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;
using Boekerhouden.Server.Identiteit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server;

/// <summary>Configuratie van diensten en de request-pijplijn; gedeeld door Program en de integratietests.</summary>
public static class Opstart
{
    public const string Verbindingsnaam = "Boekerhouden";

    public static void ConfigureerDiensten(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddScoped<Doorverwijzer>();
        services.AddScoped<AuthenticationStateProvider, HervaliderendeAuthenticatieStatus>();

        var verbinding = builder.Configuration.GetConnectionString(Verbindingsnaam)
                         ?? throw new InvalidOperationException($"Verbindingsgegevens '{Verbindingsnaam}' ontbreken in appsettings.json.");
        services.AddDbContextFactory<BoekerhoudenDbContext>(o => o.UseNpgsql(verbinding));

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "Boekerhouden";
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
            .AddEntityFrameworkStores<BoekerhoudenDbContext>()
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
        services.AddSingleton<KlantDienst>();
        services.AddSingleton<DossierDienst>();
        services.AddSingleton<ImportDienst>();
        services.AddSingleton<GebruikerDienst>();
        services.AddSingleton<RgsDienst>();
        services.AddSingleton<RubriceerDienst>();
        services.AddSingleton<SjabloonDienst>();
        services.AddSingleton<CorrectieDienst>();
        services.Configure<PdfOpties>(builder.Configuration.GetSection("Pdf"));
        services.AddSingleton<PdfDienst>();
    }

    public static void ConfigureerPijplijn(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/fout", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/niet-gevonden", createScopeForStatusCodePages: true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<WachtwoordWijzigenVerplicht>();
        app.UseAntiforgery();

        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        app.MapPost("/account/uitloggen", async (SignInManager<Gebruiker> signIn, AuditDienst audit, HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await audit.Log(context.User.AlsUitvoerder(), AuditActies.Uitgelogd);
            }

            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect("~/inloggen");
        });

        ExportEindpunten.Map(app);

        app.MapGet("/importsjabloon.xlsx", ([FromQuery] int? boekjaar, [FromQuery] string? klant) =>
        {
            var stream = new MemoryStream();
            ImportSjabloonGenerator.Schrijf(stream, boekjaar, klant);
            stream.Position = 0;
            var naam = boekjaar is null ? "importsjabloon.xlsx" : $"importsjabloon-{boekjaar}.xlsx";
            return TypedResults.File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", naam);
        });
    }

    /// <summary>Werkt de database bij naar de laatste versie en zorgt dat de vaste rollen bestaan.</summary>
    public static async Task InitialiseerDatabase(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoekerhoudenDbContext>();
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
