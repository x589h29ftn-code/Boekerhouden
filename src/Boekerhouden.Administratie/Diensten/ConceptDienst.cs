using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Waar een concept bij hoort.</summary>
public static class Conceptsoort
{
    public const string Inkoop = "inkoop";
    public const string Bank = "bank";
}

/// <summary>Een bewaard concept: de inhoud van het formulier, en wanneer en door wie.</summary>
public sealed record Conceptinhoud<T>(T Inhoud, DateTimeOffset BewaardOp, string BewaardDoor);

/// <summary>
/// Tussentijds bewaren: het formulier van een inkoopfactuur of bankmutatie die nog niet geboekt is, om later verder te
/// gaan (ook op een andere computer of door een collega). Bewaren mag wie mag invoeren; het boeken ruimt het concept op.
/// </summary>
public sealed class ConceptDienst(Toegang toegang, AuditDienst audit, TimeProvider tijd)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task Bewaar<T>(Uitvoerder wie, Guid administratie, string soort, Guid sleutel, T inhoud, string onderwerp)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var concept = await db.Concepten.FirstOrDefaultAsync(c => c.Soort == soort && c.Sleutel == sleutel);
        if (concept is null)
        {
            concept = new Concept { AdministratieId = administratie, Soort = soort, Sleutel = sleutel };
            db.Concepten.Add(concept);
        }

        (concept.Inhoud, concept.BewaardOp, concept.BewaardDoor) = (JsonSerializer.Serialize(inhoud, Json), tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ConceptBewaard, onderwerp, soort, administratie);
    }

    public async Task<Conceptinhoud<T>?> Lees<T>(Uitvoerder wie, Guid administratie, string soort, Guid sleutel)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var concept = await db.Concepten.AsNoTracking().FirstOrDefaultAsync(c => c.Soort == soort && c.Sleutel == sleutel);
        if (concept is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(concept.Inhoud, Json) is { } inhoud ? new(inhoud, concept.BewaardOp, concept.BewaardDoor) : null;
        }
        catch (JsonException)
        {
            // Een concept van een oudere versie van het formulier: dan begint het formulier gewoon opnieuw.
            return null;
        }
    }

    /// <summary>Welke van deze documenten of mutaties een bewaard concept hebben (voor een markering in de lijst).</summary>
    public async Task<IReadOnlySet<Guid>> Met(Uitvoerder wie, Guid administratie, string soort)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return (await db.Concepten.AsNoTracking().Where(c => c.Soort == soort).Select(c => c.Sleutel).ToListAsync()).ToHashSet();
    }

    /// <summary>Het concept weg (na boeken, of als de gebruiker opnieuw begint).</summary>
    public async Task Verwijder(Uitvoerder wie, Guid administratie, string soort, Guid sleutel)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        await db.Concepten.Where(c => c.Soort == soort && c.Sleutel == sleutel).ExecuteDeleteAsync();
    }
}
