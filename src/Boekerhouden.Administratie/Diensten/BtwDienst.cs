using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Btw;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een btw-tarief in de lijst, met zijn id (om te verwijderen).</summary>
public sealed record Btwtariefregeling(int Id, Tarief Tarief, DateOnly Vanaf, decimal Percentage);

/// <summary>
/// De btw-tabel van het kantoor: de btw-codes (soort en tarief, en zo de rubrieken van de aangifte) en de tarieven
/// door de tijd. Iedereen leest; alleen de beheerder wijzigt (dat controleert de dienst zelf). Een nieuw tarief geldt
/// voor boekingen vanaf de ingangsdatum; wat al geboekt is, verandert niet.
/// </summary>
public sealed class BtwDienst(IDbContextFactory<AdministratieDbContext> dbFactory, IServiceScopeFactory scopeFactory, AuditDienst audit)
{
    public async Task<Btwtabel> Tabel()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Lees(db);
    }

    public async Task<IReadOnlyList<Btwtariefregeling>> Tarieven()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Btwtarieven.AsNoTracking().OrderBy(t => t.Tarief).ThenBy(t => t.Vanaf)
            .Select(t => new Btwtariefregeling(t.Id, t.Tarief, t.Vanaf, t.Percentage)).ToListAsync();
    }

    /// <summary>De btw-tabel uit de database (codes en tarieven); zonder rijen de standaard.</summary>
    internal static async Task<Btwtabel> Lees(AdministratieDbContext db)
    {
        var codes = await db.Btwcodes.AsNoTracking().OrderBy(c => c.Volgorde).ThenBy(c => c.Code).ToListAsync();
        var tarieven = await db.Btwtarieven.AsNoTracking().ToListAsync();
        return new Btwtabel(
            codes.Count == 0 ? BtwCodes.Definities : [.. codes.Select(c => c.NaarDomein())],
            tarieven.Count == 0 ? Btwtarieven.Standaard : new Btwtarieven(tarieven.Select(t => t.NaarDomein())));
    }

    /// <summary>Een nieuw percentage voor een tarief vanaf een datum, bijv. hoog 21,15% vanaf 1-1-2027.</summary>
    public async Task<Uitkomst> VoegTariefToe(Uitvoerder wie, Tarief tarief, DateOnly vanaf, decimal percentage)
    {
        await AlleenBeheerder(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var bestaand = await db.Btwtarieven.AsNoTracking().ToListAsync();
        var nieuw = new Btwtariefregel(tarief, vanaf, percentage);
        if (Btwtarieven.Controleer([.. bestaand.Select(t => t.NaarDomein()), nieuw]) is { Count: > 0 } fouten)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        db.Btwtarieven.Add(new Btwtariefrecord { Tarief = tarief, Vanaf = vanaf, Percentage = percentage });
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwtariefToegevoegd, $"Tarief {Naam(tarief)}", $"{Btwpercentage.Tekst(percentage)} vanaf {vanaf.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Verwijdert een tarief; het laatste percentage van een tarief blijft altijd staan.</summary>
    public async Task<Uitkomst> VerwijderTarief(Uitvoerder wie, int id)
    {
        await AlleenBeheerder(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Btwtarieven.FirstOrDefaultAsync(t => t.Id == id) is not { } tarief)
        {
            return Uitkomst.Mislukt("Dit tarief bestaat niet (meer).");
        }

        if (await db.Btwtarieven.CountAsync(t => t.Tarief == tarief.Tarief) == 1)
        {
            return Uitkomst.Mislukt($"Het tarief {Naam(tarief.Tarief)} houdt minstens één percentage.");
        }

        db.Btwtarieven.Remove(tarief);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwtariefVerwijderd, $"Tarief {Naam(tarief.Tarief)}",
            $"{Btwpercentage.Tekst(tarief.Percentage)} vanaf {tarief.Vanaf.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Een nieuwe btw-code; de soort bepaalt de rubrieken in de aangifte.</summary>
    public async Task<Uitkomst> VoegCodeToe(Uitvoerder wie, string code, string omschrijving, Btwsoort soort, Tarief tarief)
    {
        await AlleenBeheerder(wie);
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        omschrijving = (omschrijving ?? string.Empty).Trim();
        if (code.Length is 0 or > 10 || !code.All(char.IsAsciiLetterOrDigit))
        {
            return Uitkomst.Mislukt("Een btw-code is 1 tot 10 letters of cijfers.");
        }

        if (omschrijving.Length == 0)
        {
            return Uitkomst.Mislukt("Vul een omschrijving in.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Btwcodes.AnyAsync(c => c.Code == code))
        {
            return Uitkomst.Mislukt($"Btw-code {code} bestaat al.");
        }

        var volgorde = (await db.Btwcodes.MaxAsync(c => (int?)c.Volgorde) ?? 0) + 10;
        db.Btwcodes.Add(new Btwcodedefinitie { Code = code, Omschrijving = omschrijving, Soort = soort, Tarief = tarief, Actief = true, Volgorde = volgorde });
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwcodeToegevoegd, code, $"{omschrijving}; {Btwsoorten.Van(soort).Naam}, tarief {Naam(tarief)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Omschrijving wijzigen of de code (in)actief maken. Soort en tarief niet: daar hangen geboekte regels aan.</summary>
    public async Task<Uitkomst> WijzigCode(Uitvoerder wie, string code, string omschrijving, bool actief)
    {
        await AlleenBeheerder(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Btwcodes.FirstOrDefaultAsync(c => c.Code == code) is not { } rij)
        {
            return Uitkomst.Mislukt($"Btw-code {code} bestaat niet.");
        }

        if (string.IsNullOrWhiteSpace(omschrijving))
        {
            return Uitkomst.Mislukt("Vul een omschrijving in.");
        }

        var oud = $"{rij.Omschrijving}{(rij.Actief ? null : " (inactief)")}";
        (rij.Omschrijving, rij.Actief) = (omschrijving.Trim(), actief);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwcodeGewijzigd, code, $"{oud} → {rij.Omschrijving}{(actief ? null : " (inactief)")}");
        return Uitkomst.Gelukt;
    }

    public static string Naam(Tarief tarief) => tarief switch
    {
        Tarief.Hoog => "hoog",
        Tarief.Laag => "laag",
        Tarief.Nul => "nul",
        _ => "geen btw",
    };

    private async Task AlleenBeheerder(Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        if (wie.Id is not { } id || await users.FindByIdAsync(id.ToString()) is not { } gebruiker || !await users.IsInRoleAsync(gebruiker, Rollen.Beheerder))
        {
            throw new GeenToegangException();
        }
    }
}
