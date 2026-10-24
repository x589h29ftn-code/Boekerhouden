using System.Security.Cryptography;
using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record BijlageBestand(string Bestandsnaam, string ContentType, byte[] Inhoud);

/// <summary>
/// Bijlagen bij een stap in het dossier (vraag E2: de app slaat ze zelf op, in de database, en ze gaan mee in de
/// back-up). Per bijlage wordt een SHA-256 bewaard.
/// </summary>
public sealed class BijlageDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public const long MaximaleGrootte = 25 * 1024 * 1024;

    public async Task<Uitkomst<Guid>> VoegToe(Guid stapId, string bestandsnaam, string? contentType, Stream inhoud, Uitvoerder wie, CancellationToken stoppen = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        ArgumentNullException.ThrowIfNull(wie);
        bestandsnaam = Bestandsnaam(bestandsnaam);
        if (bestandsnaam.Length is 0 or > 260)
        {
            return Uitkomst<Guid>.Mislukt("De bestandsnaam ontbreekt of is te lang.");
        }

        using var buffer = new MemoryStream();
        await inhoud.CopyToAsync(buffer, stoppen);
        if (buffer.Length > MaximaleGrootte)
        {
            return Uitkomst<Guid>.Mislukt($"Een bijlage is hoogstens {MaximaleGrootte / 1024 / 1024} MB.");
        }

        if (buffer.Length == 0)
        {
            return Uitkomst<Guid>.Mislukt("Het bestand is leeg.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(stoppen);
        if (await WerkprogrammaDienst.Stap(db, stapId) is not { } gevonden)
        {
            return Uitkomst<Guid>.Mislukt("Deze stap bestaat niet (meer).");
        }

        var (stap, onderdeel, dossier) = gevonden;
        if (onderdeel.GereviewdDoor is not null)
        {
            return Uitkomst<Guid>.Mislukt(WerkprogrammaDienst.Gereviewd);
        }

        var data = buffer.ToArray();
        var bijlage = new Bijlage
        {
            Id = Guid.NewGuid(),
            DossierId = dossier.Id,
            StapId = stap.Id,
            Bestandsnaam = bestandsnaam,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            Grootte = data.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(data)),
            Door = wie.Naam,
            Op = tijd.GetUtcNow(),
        };
        bijlage.Inhoud = new BijlageInhoud { BijlageId = bijlage.Id, Data = data };
        db.Bijlagen.Add(bijlage);
        await db.SaveChangesAsync(stoppen);
        await audit.Log(wie, AuditActies.BijlageToegevoegd, WerkprogrammaDienst.Onderwerp(dossier),
            $"{onderdeel.Titel} – {stap.Titel}: {bestandsnaam} ({data.Length} bytes, SHA-256 {bijlage.Sha256})");
        return Uitkomst<Guid>.Met(bijlage.Id);
    }

    public async Task<Uitkomst> Verwijder(Guid bijlageId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var bijlage = await db.Bijlagen.SingleOrDefaultAsync(b => b.Id == bijlageId);
        if (bijlage is null)
        {
            return Uitkomst.Mislukt("Deze bijlage bestaat niet (meer).");
        }

        var (stap, onderdeel, dossier) = (await WerkprogrammaDienst.Stap(db, bijlage.StapId))!.Value;
        if (onderdeel.GereviewdDoor is not null)
        {
            return Uitkomst.Mislukt(WerkprogrammaDienst.Gereviewd);
        }

        db.Bijlagen.Remove(bijlage);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BijlageVerwijderd, WerkprogrammaDienst.Onderwerp(dossier),
            $"{onderdeel.Titel} – {stap.Titel}: {bijlage.Bestandsnaam} (SHA-256 {bijlage.Sha256})");
        return Uitkomst.Gelukt;
    }

    /// <summary>Alleen de naam, zonder map (ook een Windows-pad) en zonder stuurtekens.</summary>
    internal static string Bestandsnaam(string? naam)
    {
        var tekst = naam?.Trim() ?? string.Empty;
        tekst = tekst[(tekst.LastIndexOfAny(['/', '\\']) + 1)..];
        return new string([.. tekst.Where(c => !char.IsControl(c))]).Trim();
    }

    public async Task<BijlageBestand?> Haal(Guid bijlageId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Bijlagen.AsNoTracking()
            .Where(b => b.Id == bijlageId)
            .Select(b => new BijlageBestand(b.Bestandsnaam, b.ContentType, b.Inhoud!.Data))
            .SingleOrDefaultAsync();
    }
}
