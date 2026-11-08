using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Wat een update van de database deed: van welke naar welke versie, met welke reservekopie ervoor.</summary>
public sealed record Updateverslag(string? Van, string Naar, int Migraties, Reservekopie? Reservekopie, long Boekingen, long Administraties);

/// <summary>De database is bijgewerkt door een nieuwere versie van het pakket; deze versie kent die niet en opent hem niet.</summary>
public sealed class NieuwereDatabaseException(string melding) : InvalidOperationException(melding);

/// <summary>De update van de database is mislukt; de gegevens zijn teruggezet zoals ze waren.</summary>
public sealed class DatabaseupdateMisluktException(string melding, Exception? oorzaak = null) : InvalidOperationException(melding, oorzaak);

/// <summary>
/// Een nieuwe versie van de Windows-app op bestaande gegevens: eerst controleren dat de database niet van een nieuwere
/// versie is (dan niet openen), dan een gecontroleerde reservekopie, dan de migraties, en daarna controleren dat alles
/// er nog is (integriteit, even veel administraties en boekingen, elke hash-keten klopt). Mislukt iets, dan gaat de
/// reservekopie terug en opent de app niet: de gegevens blijven precies zoals ze waren.
/// </summary>
public static class Databaseupdate
{
    public static async Task<Updateverslag?> Voer(IServiceProvider diensten, Reservekopieen kopieen)
    {
        ArgumentNullException.ThrowIfNull(diensten);
        ArgumentNullException.ThrowIfNull(kopieen);
        IReadOnlyList<string> bekend, toegepast, open;
        await using (var scope = diensten.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
            bekend = [.. db.Database.GetMigrations()];
            toegepast = File.Exists(kopieen.Database) ? [.. await db.Database.GetAppliedMigrationsAsync()] : [];
            open = [.. await db.Database.GetPendingMigrationsAsync()];
        }

        if (toegepast.Except(bekend).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } onbekend)
        {
            throw new NieuwereDatabaseException(
                $"Deze gegevens zijn bijgewerkt door een nieuwere versie van het pakket (databaseversie {onbekend[^1]}). Deze versie opent ze niet, om niets te beschadigen: installeer de nieuwste versie.");
        }

        if (open.Count == 0)
        {
            return null;
        }

        var naar = bekend[^1];
        if (toegepast.Count == 0)
        {
            // Een nieuwe database: niets te bewaren.
            await Migreer(diensten);
            return new Updateverslag(null, naar, open.Count, null, 0, 0);
        }

        var voor = Reservekopieen.Onderzoek(kopieen.Database);
        if (!voor.Integer)
        {
            throw new DatabaseupdateMisluktException("De database is beschadigd (integriteitscontrole); de update is niet uitgevoerd. Zet een reservekopie terug.");
        }

        var kopie = kopieen.Maak(Reservekopiesoort.VoorUpdate, $"vóór de update van {toegepast[^1]} naar {naar}");
        string? fout = null;
        Exception? oorzaak = null;
        try
        {
            await Migreer(diensten);
            var na = Reservekopieen.Onderzoek(kopieen.Database);
            if (!na.Integer)
            {
                fout = "na de update is de database niet in orde (integriteitscontrole)";
            }
            else if (na.Boekingen != voor.Boekingen || na.Administraties != voor.Administraties)
            {
                fout = $"na de update kloppen de aantallen niet ({voor.Administraties} administraties en {voor.Boekingen} boekingen ervoor, {na.Administraties} en {na.Boekingen} erna)";
            }
            else if (await ControleerKetens(diensten) is { } keten)
            {
                fout = keten;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            (fout, oorzaak) = (e.Message, e);
        }

        if (fout is not null)
        {
            kopieen.ZetBestandTerug(Path.Combine(kopieen.Map, kopie.Bestand));
            throw new DatabaseupdateMisluktException(
                $"De update van de database is mislukt ({fout}). De gegevens zijn teruggezet zoals ze waren (reservekopie {kopie.Bestand}); er is niets verloren. Gebruik de vorige versie van het pakket en meld het probleem.",
                oorzaak);
        }

        return new Updateverslag(toegepast[^1], naar, open.Count, kopie, voor.Boekingen, voor.Administraties);
    }

    private static async Task Migreer(IServiceProvider diensten)
    {
        await using var scope = diensten.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdministratieDbContext>().Database.MigrateAsync();
    }

    /// <summary>De hash-keten van elke administratie; geeft de eerste afwijking, of leeg als alles klopt.</summary>
    internal static async Task<string?> ControleerKetens(IServiceProvider diensten)
    {
        await using var scope = diensten.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministratieDbContext>();
        var administraties = await db.Administraties.AsNoTracking().Select(a => new { a.Id, a.Naam }).ToListAsync();
        foreach (var a in administraties)
        {
            var boekingen = await db.Boekingen.IgnoreQueryFilters().AsNoTracking().Include(b => b.Regels)
                .Where(b => b.AdministratieId == a.Id).OrderBy(b => b.Volgnummer).ToListAsync();
            var fout = Journaalketen.Controleer(boekingen.Select(b => new Ketenschakel(b.Volgnummer, b.NaarDomein(), b.Hash, b.VorigeHash)));
            if (fout.Count > 0)
            {
                return $"de hash-keten van {a.Naam} klopt niet bij boeking {string.Join(", ", fout.Take(5))}";
            }
        }

        return null;
    }
}
