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
    public const string RgsIngelezen = "RGS ingelezen";
    public const string RgsGeactiveerd = "RGS-versie actief gemaakt";
    public const string Gekoppeld = "Rekeningen gekoppeld";
    public const string Ontkoppeld = "Rekeningen ontkoppeld";
    public const string AutomatischGekoppeld = "Automatisch gekoppeld";
    public const string EigenPostToegevoegd = "Eigen post toegevoegd";
    public const string EigenPostVerwijderd = "Eigen post verwijderd";
    public const string PostHernoemd = "Post hernoemd";
    public const string SjabloonregelVerwijderd = "Sjabloonregel verwijderd";
    public const string CorrectieToegevoegd = "Correctie toegevoegd";
    public const string CorrectieGewijzigd = "Correctie gewijzigd";
    public const string CorrectieVerwijderd = "Correctie verwijderd";
    public const string CorrectiesOvergenomen = "Terugkerende correcties overgenomen";
    public const string CorrectiesGeexporteerd = "Correcties geëxporteerd";
    public const string WerkprogrammaGestart = "Werkprogramma gestart";
    public const string PostMaterieel = "Post materieel";
    public const string PostNietMaterieel = "Post niet materieel";
    public const string StapBeantwoord = "Stap beantwoord";
    public const string StapAfgetekend = "Stap afgetekend";
    public const string StapToegevoegd = "Stap toegevoegd";
    public const string StapVerwijderd = "Stap verwijderd";
    public const string StapHersteld = "Stap hersteld";
    public const string NotitieGewijzigd = "Notitie gewijzigd";
    public const string SjabloonGewijzigd = "Werkprogrammasjabloon gewijzigd";
    public const string ReviewpuntGeplaatst = "Reviewpunt geplaatst";
    public const string ReviewpuntOpgelost = "Reviewpunt opgelost";
    public const string ReviewpuntHeropend = "Reviewpunt heropend";
    public const string OnderdeelGereviewd = "Onderdeel gereviewd";
    public const string ReviewTeruggezet = "Review teruggezet";
    public const string BijlageToegevoegd = "Bijlage toegevoegd";
    public const string BijlageVerwijderd = "Bijlage verwijderd";
    public const string KantoorGewijzigd = "Kantoorgegevens gewijzigd";
    public const string PresentatieGewijzigd = "Presentatie gewijzigd";
    public const string RapportGemaakt = "Rapport gemaakt";
    public const string ToelichtingGewijzigd = "Toelichting gewijzigd";
    public const string UitsplitsingGewijzigd = "Uitsplitsing gewijzigd";
    public const string GrondslagGewijzigd = "Grondslag aangepast";
    public const string GrondslagenbibliotheekGewijzigd = "Grondslagenbibliotheek gewijzigd";
    public const string VerslaggevingGekozen = "Grondslagen gekozen";
    public const string RekeningAangemaakt = "Grootboekrekening aangemaakt";
}
