using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Regels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// De instellingen van het kantoor: de standaard voor elke administratie, die daarin per instelling kan afwijken.
/// Alleen de beheerder wijzigt ze; dat controleert de dienst zelf, niet alleen de pagina.
/// </summary>
public sealed class KantoorDienst(IDbContextFactory<AdministratieDbContext> dbFactory, IServiceScopeFactory scopeFactory, AuditDienst audit)
{
    public async Task<Verwerkingsinstellingen> Instellingen()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Lees(db);
    }

    public async Task<Uitkomst> Wijzig(Uitvoerder wie, Verwerkingsinstellingen nieuw)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(nieuw);
        if (!await IsBeheerder(wie))
        {
            throw new GeenToegangException();
        }

        if (nieuw.Controleer() is { Count: > 0 } fouten)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var rij = await db.Kantoorinstellingen.FirstOrDefaultAsync(k => k.Id == Kantoorinstellingen.EnigeId);
        if (rij is null)
        {
            rij = new Kantoorinstellingen();
            db.Kantoorinstellingen.Add(rij);
        }

        var oud = rij.NaarDomein();
        rij.DrempelAutomatisch = nieuw.Drempel;
        rij.Bandbreedte = nieuw.Bandbreedte;
        rij.LimietAutomatisch = nieuw.Limiet;
        rij.Betalingstermijn = nieuw.Betalingstermijn;
        rij.Adrescontrole = nieuw.Adrescontrole;
        rij.Documentmap = string.IsNullOrWhiteSpace(nieuw.Documentmap) ? null : nieuw.Documentmap.Trim();
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KantoorinstellingenGewijzigd, "Kantoor", Verschil(oud, rij.NaarDomein()));
        return Uitkomst.Gelukt;
    }

    /// <summary>De instellingen van het kantoor, of de standaard als ze nog nooit zijn opgeslagen.</summary>
    internal static async Task<Verwerkingsinstellingen> Lees(AdministratieDbContext db) =>
        (await db.Kantoorinstellingen.AsNoTracking().FirstOrDefaultAsync(k => k.Id == Kantoorinstellingen.EnigeId))?.NaarDomein()
        ?? Verwerkingsinstellingen.Standaard;

    /// <summary>Wat er veranderd is, voor het wijzigingslog: "drempel 2 → 3; adrescontrole aan → uit".</summary>
    internal static string Verschil(Verwerkingsinstellingen oud, Verwerkingsinstellingen nieuw)
    {
        static string Tekst(object? waarde) => waarde switch
        {
            null => "geen",
            bool b => b ? "aan" : "uit",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Convert.ToString(waarde, CultureInfo.InvariantCulture) ?? "geen",
        };

        var delen = new (string Naam, object? Oud, object? Nieuw)[]
        {
            ("drempel", oud.Drempel, nieuw.Drempel),
            ("bandbreedte", oud.Bandbreedte, nieuw.Bandbreedte),
            ("limiet", oud.Limiet, nieuw.Limiet),
            ("betalingstermijn", oud.Betalingstermijn, nieuw.Betalingstermijn),
            ("adrescontrole", oud.Adrescontrole, nieuw.Adrescontrole),
            ("documentmap", oud.Documentmap, nieuw.Documentmap),
        };
        var anders = delen.Where(d => !Equals(d.Oud, d.Nieuw)).Select(d => $"{d.Naam} {Tekst(d.Oud)} → {Tekst(d.Nieuw)}").ToList();
        return anders.Count == 0 ? "geen wijziging" : string.Join("; ", anders);
    }

    private async Task<bool> IsBeheerder(Uitvoerder wie)
    {
        if (wie.Id is not { } id)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        return await users.FindByIdAsync(id.ToString()) is { } gebruiker && await users.IsInRoleAsync(gebruiker, Rollen.Beheerder);
    }
}
