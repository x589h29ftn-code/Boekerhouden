using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record SjabloonSamenvatting(Guid Id, SoortOpdracht Soort, string Naam, string Versie, int Onderdelen, int Stappen, DateTimeOffset GewijzigdOp, string GewijzigdDoor);

public sealed record SjabloonStapInvoer(string Titel, string? Instructie, bool Verplicht);

/// <summary>
/// Beheer van de werkprogrammasjablonen van het kantoor (Beheer › Werkprogramma's). Wijzigingen gelden voor dossiers
/// die daarna starten; lopende dossiers hebben hun eigen kopie.
/// </summary>
public sealed class WerkprogrammaSjabloonDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public async Task<IReadOnlyList<SjabloonSamenvatting>> Lijst()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WerkprogrammaSjablonen.AsNoTracking()
            .OrderBy(w => w.SoortOpdracht)
            .Select(w => new SjabloonSamenvatting(w.Id, w.SoortOpdracht, w.Naam, w.Versie, w.Onderdelen.Count, w.Onderdelen.SelectMany(o => o.Stappen).Count(), w.GewijzigdOp, w.GewijzigdDoor))
            .ToListAsync();
    }

    public async Task<WerkprogrammaSjabloon?> Haal(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sjabloon = await db.WerkprogrammaSjablonen.AsNoTracking().Include(w => w.Onderdelen).ThenInclude(o => o.Stappen)
            .AsSplitQuery()
            .SingleOrDefaultAsync(w => w.Id == id);
        if (sjabloon is not null)
        {
            sjabloon.Onderdelen = [.. sjabloon.Onderdelen.OrderBy(o => o.Fase).ThenBy(o => o.Volgorde)];
            foreach (var onderdeel in sjabloon.Onderdelen)
            {
                onderdeel.Stappen = [.. onderdeel.Stappen.OrderBy(s => s.Volgorde)];
            }
        }

        return sjabloon;
    }

    public async Task<Uitkomst> ZetVersie(Guid sjabloonId, string versie, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        versie = versie?.Trim() ?? string.Empty;
        if (versie.Length is 0 or > 40)
        {
            return Uitkomst.Mislukt("Geef een versie van hoogstens 40 tekens, bijvoorbeeld 2027.1.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var sjabloon = await db.WerkprogrammaSjablonen.SingleOrDefaultAsync(w => w.Id == sjabloonId);
        if (sjabloon is null)
        {
            return Uitkomst.Mislukt("Dit sjabloon bestaat niet (meer).");
        }

        var oud = sjabloon.Versie;
        sjabloon.Versie = versie;
        Gewijzigd(sjabloon, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonGewijzigd, sjabloon.Naam, $"versie {oud} → {versie}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Een stap toevoegen (<paramref name="stapId"/> null) of wijzigen.</summary>
    public async Task<Uitkomst<Guid>> BewaarStap(Guid onderdeelId, Guid? stapId, SjabloonStapInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var titel = invoer.Titel?.Trim() ?? string.Empty;
        if (titel.Length is 0 or > 300 || invoer.Instructie?.Length > 4000)
        {
            return Uitkomst<Guid>.Mislukt("Geef de stap een titel van hoogstens 300 tekens (instructie hoogstens 4000).");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var onderdeel = await db.SjabloonOnderdelen.Include(o => o.Stappen).SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        SjabloonStap? stap;
        if (stapId is { } id)
        {
            stap = onderdeel.Stappen.SingleOrDefault(s => s.Id == id);
            if (stap is null)
            {
                return Uitkomst<Guid>.Mislukt("Deze stap bestaat niet (meer).");
            }
        }
        else
        {
            stap = new SjabloonStap { Id = Guid.NewGuid(), OnderdeelId = onderdeelId, Volgorde = onderdeel.Stappen.Count == 0 ? 0 : onderdeel.Stappen.Max(s => s.Volgorde) + 1 };
            db.SjabloonStappen.Add(stap);
        }

        stap.Titel = titel;
        stap.Instructie = string.IsNullOrWhiteSpace(invoer.Instructie) ? null : invoer.Instructie.Trim();
        stap.Verplicht = invoer.Verplicht;
        var sjabloon = await db.WerkprogrammaSjablonen.SingleAsync(w => w.Id == onderdeel.SjabloonId);
        Gewijzigd(sjabloon, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonGewijzigd, sjabloon.Naam, $"{onderdeel.Titel}: stap '{titel}' {(stapId is null ? "toegevoegd" : "gewijzigd")}");
        return Uitkomst<Guid>.Met(stap.Id);
    }

    public async Task<Uitkomst> VerwijderStap(Guid stapId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var stap = await db.SjabloonStappen.SingleOrDefaultAsync(s => s.Id == stapId);
        if (stap is null)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var onderdeel = await db.SjabloonOnderdelen.SingleAsync(o => o.Id == stap.OnderdeelId);
        var sjabloon = await db.WerkprogrammaSjablonen.SingleAsync(w => w.Id == onderdeel.SjabloonId);
        db.SjabloonStappen.Remove(stap);
        Gewijzigd(sjabloon, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonGewijzigd, sjabloon.Naam, $"{onderdeel.Titel}: stap '{stap.Titel}' verwijderd");
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> VerplaatsStap(Guid stapId, int richting, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var stap = await db.SjabloonStappen.SingleOrDefaultAsync(s => s.Id == stapId);
        if (stap is null)
        {
            return Uitkomst.Mislukt("Deze stap bestaat niet (meer).");
        }

        var stappen = await db.SjabloonStappen.Where(s => s.OnderdeelId == stap.OnderdeelId).OrderBy(s => s.Volgorde).ToListAsync();
        var i = stappen.FindIndex(s => s.Id == stapId);
        var j = i + Math.Sign(richting);
        if (j < 0 || j >= stappen.Count)
        {
            return Uitkomst.Gelukt;
        }

        (stappen[i], stappen[j]) = (stappen[j], stappen[i]);
        for (var k = 0; k < stappen.Count; k++)
        {
            stappen[k].Volgorde = k;
        }

        await db.SaveChangesAsync();
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Een onderdeel toevoegen: een algemeen onderdeel in voorbereiding of afronding, of een post in de uitvoering
    /// (met een RGS-hoofdpost, niveau 2).
    /// </summary>
    public async Task<Uitkomst<Guid>> VoegOnderdeelToe(Guid sjabloonId, Fase fase, string titel, string? hoofdpostCode, bool verplicht, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        titel = titel?.Trim() ?? string.Empty;
        hoofdpostCode = string.IsNullOrWhiteSpace(hoofdpostCode) ? null : hoofdpostCode.Trim();
        if (titel.Length is 0 or > 200)
        {
            return Uitkomst<Guid>.Mislukt("Geef het onderdeel een titel van hoogstens 200 tekens.");
        }

        if ((fase == Fase.Uitvoering) != (hoofdpostCode is not null))
        {
            return Uitkomst<Guid>.Mislukt("Een onderdeel in de uitvoering hoort bij een hoofdpost (RGS-code van niveau 2, bijv. BMva); andere onderdelen niet.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var sjabloon = await db.WerkprogrammaSjablonen.Include(w => w.Onderdelen).SingleOrDefaultAsync(w => w.Id == sjabloonId);
        if (sjabloon is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit sjabloon bestaat niet (meer).");
        }

        if (hoofdpostCode is not null && sjabloon.Onderdelen.Any(o => o.HoofdpostCode == hoofdpostCode))
        {
            return Uitkomst<Guid>.Mislukt($"Er is al een onderdeel voor {hoofdpostCode}.");
        }

        var onderdeel = new SjabloonOnderdeel
        {
            Id = Guid.NewGuid(),
            SjabloonId = sjabloonId,
            Fase = fase,
            Soort = hoofdpostCode is null ? OnderdeelSoort.Algemeen : OnderdeelSoort.Post,
            Titel = titel,
            HoofdpostCode = hoofdpostCode,
            Verplicht = verplicht && hoofdpostCode is null,
            Volgorde = sjabloon.Onderdelen.Count == 0 ? 0 : sjabloon.Onderdelen.Max(o => o.Volgorde) + 1,
        };
        db.SjabloonOnderdelen.Add(onderdeel);
        Gewijzigd(sjabloon, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonGewijzigd, sjabloon.Naam, $"onderdeel '{titel}' toegevoegd ({Weergave.Tekst(fase)})");
        return Uitkomst<Guid>.Met(onderdeel.Id);
    }

    /// <summary>Een onderdeel met zijn stappen verwijderen; de planning (keuze materiële posten) kan niet weg.</summary>
    public async Task<Uitkomst> VerwijderOnderdeel(Guid onderdeelId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var onderdeel = await db.SjabloonOnderdelen.SingleOrDefaultAsync(o => o.Id == onderdeelId);
        if (onderdeel is null)
        {
            return Uitkomst.Mislukt("Dit onderdeel bestaat niet (meer).");
        }

        if (onderdeel.Soort == OnderdeelSoort.MaterielePosten)
        {
            return Uitkomst.Mislukt("De planning met de keuze van materiële posten hoort altijd in het werkprogramma.");
        }

        var sjabloon = await db.WerkprogrammaSjablonen.SingleAsync(w => w.Id == onderdeel.SjabloonId);
        db.SjabloonOnderdelen.Remove(onderdeel);
        Gewijzigd(sjabloon, wie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.SjabloonGewijzigd, sjabloon.Naam, $"onderdeel '{onderdeel.Titel}' verwijderd");
        return Uitkomst.Gelukt;
    }

    private void Gewijzigd(WerkprogrammaSjabloon sjabloon, Uitvoerder wie)
    {
        sjabloon.GewijzigdOp = tijd.GetUtcNow();
        sjabloon.GewijzigdDoor = wie.Naam;
    }
}
