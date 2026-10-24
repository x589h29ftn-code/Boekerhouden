using System.Text.RegularExpressions;
using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record KantoorInvoer(string Naam, string? Adres, string? Postcode, string? Plaats, string Kleur);

/// <summary>De gegevens en de huisstijl van het kantoor (Beheer › Kantoor), voor het voorblad en de opmaak van rapporten.</summary>
public sealed partial class KantoorDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleLogoGrootte = 1024 * 1024;

    private static readonly HashSet<string> LogoTypes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

    /// <summary>De instellingen; zijn ze er nog niet, dan lege standaardwaarden.</summary>
    public async Task<Kantoorinstellingen> Haal()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Kantoorinstellingen.AsNoTracking().SingleOrDefaultAsync() ?? new Kantoorinstellingen();
    }

    public async Task<Uitkomst> Bewaar(KantoorInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var naam = invoer.Naam?.Trim() ?? string.Empty;
        var kleur = invoer.Kleur?.Trim() ?? string.Empty;
        var fouten = new List<string>();
        if (naam.Length is 0 or > 200)
        {
            fouten.Add("Vul de naam van het kantoor in (hoogstens 200 tekens).");
        }

        if (!KleurPatroon().IsMatch(kleur))
        {
            fouten.Add("Geef de huisstijlkleur als #rrggbb, bijvoorbeeld #1f4e79.");
        }

        if (invoer.Adres?.Length > 200 || invoer.Postcode?.Length > 20 || invoer.Plaats?.Length > 100)
        {
            fouten.Add("Adres, postcode of plaats is te lang.");
        }

        if (fouten.Count > 0)
        {
            return new Uitkomst(fouten);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var kantoor = await Instellingen(db);
        kantoor.Naam = naam;
        kantoor.Adres = Leeg(invoer.Adres);
        kantoor.Postcode = Leeg(invoer.Postcode);
        kantoor.Plaats = Leeg(invoer.Plaats);
        kantoor.Kleur = kleur.ToLowerInvariant();
        Gewijzigd(kantoor, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KantoorGewijzigd, naam, $"{kantoor.Adres}, {kantoor.Postcode} {kantoor.Plaats}; kleur {kantoor.Kleur}");
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> ZetLogo(byte[] logo, string? type, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(logo);
        ArgumentNullException.ThrowIfNull(wie);
        if (type is null || !LogoTypes.Contains(type))
        {
            return Uitkomst.Mislukt("Een logo is een PNG- of JPEG-afbeelding.");
        }

        if (logo.Length is 0 or > MaximaleLogoGrootte)
        {
            return Uitkomst.Mislukt("Een logo is hoogstens 1 MB.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var kantoor = await Instellingen(db);
        kantoor.Logo = logo;
        kantoor.LogoType = type.ToLowerInvariant();
        Gewijzigd(kantoor, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KantoorGewijzigd, kantoor.Naam, $"logo ingesteld ({logo.Length} bytes)");
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> VerwijderLogo(Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var kantoor = await Instellingen(db);
        kantoor.Logo = null;
        kantoor.LogoType = null;
        Gewijzigd(kantoor, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.KantoorGewijzigd, kantoor.Naam, "logo verwijderd");
        return Uitkomst.Gelukt;
    }

    private static async Task<Kantoorinstellingen> Instellingen(BoekerhoudenDbContext db)
    {
        var kantoor = await db.Kantoorinstellingen.SingleOrDefaultAsync();
        if (kantoor is null)
        {
            kantoor = new Kantoorinstellingen();
            db.Kantoorinstellingen.Add(kantoor);
        }

        return kantoor;
    }

    private void Gewijzigd(Kantoorinstellingen kantoor, Uitvoerder wie)
    {
        kantoor.GewijzigdOp = tijd.GetUtcNow();
        kantoor.GewijzigdDoor = wie.Naam;
    }

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex KleurPatroon();
}
