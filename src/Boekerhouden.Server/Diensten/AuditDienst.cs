using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>Schrijft het wijzigingslog. Regels worden alleen toegevoegd, nooit gewijzigd of verwijderd.</summary>
public sealed class AuditDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, TimeProvider tijd)
{
    public async Task Log(Uitvoerder wie, string actie, string? onderwerp = null, string? details = null)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.AuditRegels.Add(new AuditRegel
        {
            Tijdstip = tijd.GetUtcNow(),
            GebruikerId = wie.Id,
            Gebruiker = wie.Naam,
            Actie = actie,
            Onderwerp = onderwerp,
            Details = details,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AuditRegel>> Recent(string? zoek = null, int aantal = 500)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.AuditRegels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zoek))
        {
            var patroon = $"%{zoek.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Gebruiker, patroon)
                                     || EF.Functions.ILike(a.Actie, patroon)
                                     || (a.Onderwerp != null && EF.Functions.ILike(a.Onderwerp, patroon)));
        }

        return await query.OrderByDescending(a => a.Id).Take(aantal).ToListAsync();
    }
}

/// <summary>Vaste omschrijvingen van handelingen in het wijzigingslog.</summary>
public static class AuditActies
{
    public const string Ingelogd = "Ingelogd";
    public const string InloggenMislukt = "Inloggen mislukt";
    public const string AccountGeblokkeerd = "Account geblokkeerd";
    public const string Uitgelogd = "Uitgelogd";
    public const string WachtwoordGewijzigd = "Wachtwoord gewijzigd";
    public const string EersteBeheerderAangemaakt = "Eerste beheerder aangemaakt";
    public const string GebruikerAangemaakt = "Gebruiker aangemaakt";
    public const string GebruikerGewijzigd = "Gebruiker gewijzigd";
    public const string GebruikerGedeactiveerd = "Gebruiker gedeactiveerd";
    public const string GebruikerGeactiveerd = "Gebruiker geactiveerd";
    public const string WachtwoordGereset = "Wachtwoord gereset";
    public const string KlantAangemaakt = "Klant aangemaakt";
    public const string KlantGewijzigd = "Klant gewijzigd";
    public const string DossierAangemaakt = "Dossier aangemaakt";
    public const string SaldibalansGeimporteerd = "Saldibalans geïmporteerd";
}
