using System.Security.Cryptography;
using Boekerhouden.Data;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Import;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record RgsVersieRegel(Guid Id, string Versie, string Bestandsnaam, int AantalCodes, bool Actief, DateTimeOffset IngelezenOp, string IngelezenDoor);

/// <summary>Verslag na het inlezen van een RGS-werkboek.</summary>
public sealed record RgsInleesVerslag(
    Guid Id,
    string Versie,
    bool Actief,
    IReadOnlyDictionary<int, int> PerNiveau,
    int Inactief,
    int MetOmslag,
    int BvGrootboekcodes,
    int EenmanszaakGrootboekcodes,
    IReadOnlyList<ImportMelding> Meldingen);

/// <summary>
/// Beheert de RGS-versies: het officiële werkboek inlezen, een versie actief maken en de actieve versie als boom
/// leveren (in het geheugen bewaard, want die verandert zelden).
/// </summary>
public sealed class RgsDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd) : IDisposable
{
    public const long MaximaleBestandsgrootte = 50 * 1024 * 1024;

    private readonly SemaphoreSlim slot = new(1, 1);
    private RgsSchema? actief;
    private bool geladen;

    public async Task<Uitkomst<RgsInleesVerslag>> Importeer(byte[] inhoud, string bestandsnaam, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        ArgumentNullException.ThrowIfNull(wie);
        if (inhoud.LongLength > MaximaleBestandsgrootte)
        {
            return Uitkomst<RgsInleesVerslag>.Mislukt("Het bestand is groter dan 50 MB. Is dit het RGS-werkboek?");
        }

        var werkboek = RgsWerkboekLezer.Lees(new MemoryStream(inhoud, writable: false), bestandsnaam);
        if (!werkboek.IsGeslaagd)
        {
            return Uitkomst<RgsInleesVerslag>.Mislukt([.. werkboek.Meldingen.Where(m => m.Ernst == MeldingErnst.Fout).Select(m => m.Tekst)]);
        }

        if (werkboek.Versie is null)
        {
            return Uitkomst<RgsInleesVerslag>.Mislukt("De RGS-versie staat niet in het werkboek of de bestandsnaam (bijv. 'RGS 3.8-def.xlsx').");
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(inhoud));
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.RgsVersies.AnyAsync(v => v.Sha256 == sha))
        {
            return Uitkomst<RgsInleesVerslag>.Mislukt("Dit werkboek is al ingelezen.");
        }

        // Een nieuwere (of de eerste) versie wordt meteen actief; een oudere niet.
        var huidige = await db.RgsVersies.Where(v => v.Actief).Select(v => v.Versie).SingleOrDefaultAsync();
        var wordtActief = huidige is null || VergelijkVersie(werkboek.Versie, huidige) >= 0;

        var versie = new RgsVersie
        {
            Id = Guid.NewGuid(),
            Versie = werkboek.Versie,
            Bestandsnaam = Path.GetFileName(bestandsnaam),
            Sha256 = sha,
            AantalCodes = werkboek.Codes.Count,
            IngelezenOp = tijd.GetUtcNow(),
            IngelezenDoor = wie.Naam,
            Codes = [.. werkboek.Codes.Select(c => new RgsCodeRegel
            {
                Code = c.Code,
                Niveau = c.Niveau,
                Ouder = c.Ouder,
                Omschrijving = c.Omschrijving,
                OmschrijvingLang = c.OmschrijvingLang,
                Kant = c.Kant switch { Saldokant.Debet => "D", Saldokant.Credit => "C", _ => null },
                Omslagcode = c.Omslagcode,
                Volgorde = c.Volgorde,
                Referentienummer = c.Referentienummer,
                Inactief = c.Inactief,
                Kiesfilters = [.. c.Kiesfilters],
                Wegfilters = [.. c.Wegfilters],
            })],
        };

        await using (var transactie = await db.Database.BeginTransactionAsync())
        {
            if (wordtActief)
            {
                await db.RgsVersies.Where(v => v.Actief).ExecuteUpdateAsync(s => s.SetProperty(v => v.Actief, false));
                versie.Actief = true;
            }

            db.RgsVersies.Add(versie);
            await db.SaveChangesAsync();
            await transactie.CommitAsync();
        }

        Vergeet();
        await audit.Log(wie, AuditActies.RgsIngelezen, $"RGS {versie.Versie}",
            $"{versie.Bestandsnaam}; {versie.AantalCodes} codes; {(wordtActief ? "actief" : "niet actief")}; SHA-256 {sha}");

        var schema = new RgsSchema(werkboek.Versie, werkboek.Codes);
        int Grootboekcodes(RgsSelectie s) => schema.Selecteer(s).Count(c => schema.Zoek(c)!.Niveau == 4);
        return Uitkomst<RgsInleesVerslag>.Met(new RgsInleesVerslag(
            versie.Id,
            versie.Versie,
            wordtActief,
            werkboek.Codes.GroupBy(c => c.Niveau).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            werkboek.Codes.Count(c => c.Inactief),
            werkboek.Codes.Count(c => c.Omslagcode is not null),
            Grootboekcodes(RgsSelectie.BV),
            Grootboekcodes(RgsSelectie.Eenmanszaak),
            werkboek.Meldingen));
    }

    public async Task<IReadOnlyList<RgsVersieRegel>> Versies()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RgsVersies.AsNoTracking()
            .OrderByDescending(v => v.IngelezenOp)
            .Select(v => new RgsVersieRegel(v.Id, v.Versie, v.Bestandsnaam, v.AantalCodes, v.Actief, v.IngelezenOp, v.IngelezenDoor))
            .ToListAsync();
    }

    public async Task<Uitkomst> Activeer(Guid id, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var versie = await db.RgsVersies.SingleOrDefaultAsync(v => v.Id == id);
        if (versie is null)
        {
            return Uitkomst.Mislukt("Deze RGS-versie bestaat niet (meer).");
        }

        await using (var transactie = await db.Database.BeginTransactionAsync())
        {
            await db.RgsVersies.Where(v => v.Actief).ExecuteUpdateAsync(s => s.SetProperty(v => v.Actief, false));
            versie.Actief = true;
            await db.SaveChangesAsync();
            await transactie.CommitAsync();
        }

        Vergeet();
        await audit.Log(wie, AuditActies.RgsGeactiveerd, $"RGS {versie.Versie}", versie.Bestandsnaam);
        return Uitkomst.Gelukt;
    }

    /// <summary>De actieve RGS-versie als boom, of null als er nog geen werkboek is ingelezen.</summary>
    public async Task<RgsSchema?> ActiefSchema()
    {
        if (geladen)
        {
            return actief;
        }

        await slot.WaitAsync();
        try
        {
            if (!geladen)
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var versie = await db.RgsVersies.AsNoTracking().SingleOrDefaultAsync(v => v.Actief);
                actief = versie is null ? null : new RgsSchema(versie.Versie, (await db.RgsCodes.AsNoTracking()
                        .Where(c => c.RgsVersieId == versie.Id)
                        .ToListAsync())
                    .Select(c => new RgsCode(c.Code, c.Niveau, c.Ouder, c.Omschrijving, c.OmschrijvingLang,
                        c.Kant switch { "D" => Saldokant.Debet, "C" => Saldokant.Credit, _ => null },
                        c.Omslagcode, c.Volgorde, c.Referentienummer, c.Inactief,
                        c.Kiesfilters.ToHashSet(StringComparer.Ordinal), c.Wegfilters.ToHashSet(StringComparer.Ordinal))));
                geladen = true;
            }

            return actief;
        }
        finally
        {
            slot.Release();
        }
    }

    public static RgsSelectie SelectieVoor(Rechtsvorm rechtsvorm) =>
        rechtsvorm == Rechtsvorm.Eenmanszaak ? RgsSelectie.Eenmanszaak : RgsSelectie.BV;

    /// <summary>Vergelijkt versienummers als getallen ("3.10" is nieuwer dan "3.9").</summary>
    public static int VergelijkVersie(string a, string b) =>
        Version.TryParse(a, out var x) && Version.TryParse(b, out var y) ? x.CompareTo(y) : string.CompareOrdinal(a, b);

    public void Dispose() => slot.Dispose();

    private void Vergeet()
    {
        geladen = false;
        actief = null;
    }
}
