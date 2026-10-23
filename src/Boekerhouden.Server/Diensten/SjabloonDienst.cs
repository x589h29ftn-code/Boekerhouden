using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record SjabloonRegelInfo(Guid Id, string Rekeningnummer, string Omschrijving, string Code, string? CodeOmschrijving, DateTimeOffset GewijzigdOp, string GewijzigdDoor);

/// <summary>Het kantoorsjabloon: rekeningnummers die bij het kantoor altijd aan dezelfde RGS-code horen.</summary>
public sealed class SjabloonDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RgsDienst rgs, AuditDienst audit)
{
    public async Task<IReadOnlyList<SjabloonRegelInfo>> Lijst(string? zoek = null)
    {
        var schema = await rgs.ActiefSchema();
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.SjabloonRegels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zoek))
        {
            var patroon = $"%{zoek.Trim()}%";
            query = query.Where(r => EF.Functions.ILike(r.Rekeningnummer, patroon) || EF.Functions.ILike(r.Omschrijving, patroon) || EF.Functions.ILike(r.Code, patroon));
        }

        var regels = await query.OrderBy(r => r.Rekeningnummer).ToListAsync();
        return [.. regels.Select(r => new SjabloonRegelInfo(r.Id, r.Rekeningnummer, r.Omschrijving, r.Code,
            schema?.Zoek(r.Code)?.Omschrijving, r.GewijzigdOp, r.GewijzigdDoor))];
    }

    public async Task<Uitkomst> Verwijder(Guid id, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var regel = await db.SjabloonRegels.SingleOrDefaultAsync(r => r.Id == id);
        if (regel is null)
        {
            return Uitkomst.Mislukt("Deze regel bestaat niet (meer).");
        }

        db.SjabloonRegels.Remove(regel);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonregelVerwijderd, $"{regel.Rekeningnummer} {regel.Omschrijving}", $"was gekoppeld aan {regel.Code}");
        return Uitkomst.Gelukt;
    }
}
