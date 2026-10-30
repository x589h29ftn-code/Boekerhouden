using Boekerhouden.Administratie.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Schrijft het wijzigingslog. Regels worden alleen toegevoegd; de database weigert wijzigen en verwijderen.</summary>
public sealed class AuditDienst(IDbContextFactory<AdministratieDbContext> dbFactory, TimeProvider tijd)
{
    public async Task Log(Uitvoerder wie, string actie, string? onderwerp = null, string? details = null, Guid? administratie = null)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.AuditRegels.Add(new AuditRegel
        {
            Tijdstip = tijd.GetUtcNow(),
            GebruikerId = wie.Id,
            Gebruiker = wie.Naam,
            AdministratieId = administratie,
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
            // PostgreSQL: ILIKE; in SQLite is LIKE al ongevoelig voor hoofdletters.
            query = db.Database.IsNpgsql()
                ? query.Where(a => EF.Functions.ILike(a.Gebruiker, patroon)
                                   || EF.Functions.ILike(a.Actie, patroon)
                                   || (a.Onderwerp != null && EF.Functions.ILike(a.Onderwerp, patroon)))
                : query.Where(a => EF.Functions.Like(a.Gebruiker, patroon)
                                   || EF.Functions.Like(a.Actie, patroon)
                                   || (a.Onderwerp != null && EF.Functions.Like(a.Onderwerp, patroon)));
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
    public const string AdministratieAangemaakt = "Administratie aangemaakt";
    public const string LidToegevoegd = "Lid toegevoegd";
    public const string RekeningToegevoegd = "Grootboekrekening toegevoegd";
    public const string Geboekt = "Geboekt";
    public const string AutomatischGeboekt = "Automatisch geboekt";
    public const string DocumentIngelezen = "Document ingelezen";
    public const string RelatieToegevoegd = "Relatie toegevoegd";
    public const string RelatieGewijzigd = "Relatie gewijzigd";
    public const string IbanGewijzigd = "IBAN gewijzigd";
    public const string IbanWijzigingGeweigerd = "IBAN wijzigen geweigerd";
    public const string RegelToegevoegd = "Regel toegevoegd";
    public const string RegelVerwijderd = "Regel verwijderd";
    public const string RegelVerplaatst = "Regel verplaatst";
    public const string InstellingenGewijzigd = "Instellingen gewijzigd";

    public const string KantoorinstellingenGewijzigd = "Kantoorinstellingen gewijzigd";

    public const string ZoektemplateBijgewerkt = "Zoektemplate van leverancier bijgewerkt";
    public const string GeleerdVergeten = "Geleerde keuzes vergeten";
    public const string ZoekplekVergeten = "Plek op de factuur vergeten";
    public const string ControleIngesteld = "Grens voor zelf controleren ingesteld";

    public const string DocumentOpgeslagen = "Document in de documentmap gezet";

    public const string DocumentNietOpgeslagen = "Document niet in de documentmap gezet";
    public const string DocumentQuarantaine = "Document in quarantaine";
    public const string TweestapAan = "Tweestapsverificatie aangezet";
    public const string TweestapUit = "Tweestapsverificatie uitgezet";
    public const string HerstelcodesVernieuwd = "Herstelcodes vernieuwd";
    public const string HerstelcodeGebruikt = "Ingelogd met herstelcode";
    public const string TweedeStapMislukt = "Tweede stap mislukt";
    public const string PasskeyToegevoegd = "Passkey toegevoegd";
    public const string PasskeyVerwijderd = "Passkey verwijderd";
    public const string IngelogdMetPasskey = "Ingelogd met passkey";
    public const string AfschriftIngelezen = "Bankafschrift ingelezen";
    public const string BankdagboekGekoppeld = "Bankdagboek gekoppeld aan rekening";
    public const string Afgeletterd = "Afgeletterd";
    public const string IbanGeleerd = "IBAN geleerd bij relatie";
    public const string DocumentGearchiveerd = "Document bewaard zonder boeking";
    public const string DocumentTeruggezet = "Document terug naar Te verwerken";
    public const string DocumentVerwijderd = "Document verwijderd";
    public const string BoekingTeruggedraaid = "Boeking teruggedraaid";
    public const string BoekingAangepast = "Boeking aangepast";
    public const string CreditnotaVerrekend = "Creditnota verrekend";
    public const string OpnieuwVerwerkt = "Opnieuw automatisch verwerkt";
    public const string BtwtariefToegevoegd = "Btw-tarief toegevoegd";
    public const string BtwtariefVerwijderd = "Btw-tarief verwijderd";
    public const string BtwcodeToegevoegd = "Btw-code toegevoegd";
    public const string BtwcodeGewijzigd = "Btw-code gewijzigd";
    public const string KetenGecontroleerd = "Journaal gecontroleerd";
}
