using Boekerhouden.Data;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Correcties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <param name="Toelichting">Optionele omschrijving bij de regel.</param>
public sealed record CorrectieRegelInfo(Guid RekeningId, string Nummer, string RekeningOmschrijving, decimal Bedrag, string? Toelichting);

public sealed record CorrectieInfo(
    Guid Id,
    int Nummer,
    string Omschrijving,
    CorrectieSoort Soort,
    bool Terugkerend,
    bool VerwerktBijKlant,
    bool Overgenomen,
    IReadOnlyList<CorrectieRegelInfo> Regels,
    CorrectieEffect Effect,
    string GewijzigdDoor,
    DateTimeOffset GewijzigdOp)
{
    /// <summary>Het totaal van de boeking (debet = credit).</summary>
    public decimal Totaal => Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);
}

/// <summary>Een grootboekrekening om op te boeken, met de RGS-code waaraan hij gekoppeld is (of null).</summary>
public sealed record RekeningKeuze(Guid Id, string Nummer, string Omschrijving, string? Code, bool AangemaaktBijCorrectie)
{
    public string Weergave => $"{Nummer} {Omschrijving}";
}

/// <param name="VorigJaarOnverwerkt">Correcties uit het dossier van vorig jaar die de klant niet in de administratie
/// heeft verwerkt en die de balans raken: de beginbalans van dit jaar sluit dan waarschijnlijk niet aan.</param>
/// <param name="TerugkerendOverTeNemen">Terugkerende boekingen uit vorig jaar die nog niet in dit dossier staan.</param>
public sealed record CorrectieOverzicht(
    DossierDetail Dossier,
    IReadOnlyList<CorrectieInfo> Boekingen,
    IReadOnlyList<RekeningKeuze> Rekeningen,
    CorrectieEffect Totaal,
    IReadOnlyList<CorrectieInfo> VorigJaarOnverwerkt,
    int TerugkerendOverTeNemen);

public sealed record CorrectieRegelInvoer(Guid RekeningId, decimal Bedrag, string? Toelichting = null);

public sealed record CorrectieInvoer(string Omschrijving, CorrectieSoort Soort, bool Terugkerend, bool VerwerktBijKlant, IReadOnlyList<CorrectieRegelInvoer> Regels);

/// <summary>
/// Correctieboekingen (memoriaal) in een dossier. Ze veranderen de saldi in het dossier, niet de administratie van de
/// klant; met "verwerkt bij klant" houd je bij of de klant de boeking zelf ook heeft gemaakt.
/// </summary>
public sealed class CorrectieDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleLengte = 300;

    public async Task<CorrectieOverzicht?> Overzicht(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await Dossier(db, dossierId);
        if (dossier is null)
        {
            return null;
        }

        var codes = await db.Koppelingen.AsNoTracking().Where(k => k.KlantId == dossier.KlantId)
            .ToDictionaryAsync(k => k.GrootboekrekeningId, k => k.Code);
        var rekeningen = await db.Grootboekrekeningen.AsNoTracking().Where(g => g.KlantId == dossier.KlantId)
            .OrderBy(g => g.Nummer)
            .Select(g => new { g.Id, g.Nummer, g.Omschrijving, g.AangemaaktBijCorrectie })
            .ToListAsync();

        var boekingen = await Boekingen(db, dossierId, codes);
        var vorigDossier = await VorigDossier(db, dossier);
        var vorige = vorigDossier is null ? [] : await Boekingen(db, vorigDossier.Value, codes);
        var overgenomen = boekingen.Count == 0 ? [] : await db.Correctieboekingen.AsNoTracking()
            .Where(c => c.DossierId == dossierId && c.OvergenomenVanId != null)
            .Select(c => c.OvergenomenVanId!.Value)
            .ToListAsync();

        return new CorrectieOverzicht(
            dossier,
            boekingen,
            [.. rekeningen.Select(r => new RekeningKeuze(r.Id, r.Nummer, r.Omschrijving, codes.GetValueOrDefault(r.Id), r.AangemaaktBijCorrectie))],
            boekingen.Aggregate(default(CorrectieEffect), (som, b) => som.Plus(b.Effect)),
            [.. vorige.Where(b => b.Soort == CorrectieSoort.Correctie && !b.VerwerktBijKlant && !b.Terugkerend
                                  && b.Regels.Any(r => codes.GetValueOrDefault(r.RekeningId) is not { } code || !code.StartsWith('W')))],
            vorige.Count(b => b.Terugkerend && !overgenomen.Contains(b.Id)));
    }

    /// <summary>Een nieuwe boeking (<paramref name="boekingId"/> null) of een bestaande wijzigen.</summary>
    public async Task<Uitkomst<Guid>> Bewaar(Guid dossierId, Guid? boekingId, CorrectieInvoer invoer, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit dossier bestaat niet (meer).");
        }

        var omschrijving = invoer.Omschrijving?.Trim() ?? string.Empty;
        var fouten = Boekingscontrole.Controleer(omschrijving, [.. invoer.Regels.Select(r => new Boekingsregel(r.RekeningId, r.Bedrag))]).ToList();
        if (omschrijving.Length > MaximaleLengte || invoer.Regels.Any(r => r.Toelichting?.Trim().Length > MaximaleLengte))
        {
            fouten.Add($"Een omschrijving is hoogstens {MaximaleLengte} tekens.");
        }

        var ids = invoer.Regels.Select(r => r.RekeningId).Distinct().ToList();
        var rekeningen = await db.Grootboekrekeningen.AsNoTracking().Where(g => g.KlantId == dossier.KlantId && ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id);
        if (rekeningen.Count != ids.Count)
        {
            fouten.Add("Kies op elke regel een grootboekrekening van deze klant.");
        }

        if (fouten.Count > 0)
        {
            return new Uitkomst<Guid>(default, fouten);
        }

        Correctieboeking boeking;
        var nu = tijd.GetUtcNow();
        if (boekingId is { } id)
        {
            var bestaand = await db.Correctieboekingen.Include(c => c.Regels).SingleOrDefaultAsync(c => c.Id == id && c.DossierId == dossierId);
            if (bestaand is null)
            {
                return Uitkomst<Guid>.Mislukt("Deze boeking bestaat niet (meer).");
            }

            boeking = bestaand;
            db.Correctieregels.RemoveRange(boeking.Regels);
            boeking.Regels.Clear();
        }
        else
        {
            boeking = new Correctieboeking
            {
                Id = Guid.NewGuid(),
                DossierId = dossierId,
                Nummer = (await db.Correctieboekingen.Where(c => c.DossierId == dossierId).MaxAsync(c => (int?)c.Nummer) ?? 0) + 1,
                AangemaaktOp = nu,
                AangemaaktDoor = wie.Naam,
            };
            db.Correctieboekingen.Add(boeking);
        }

        boeking.Omschrijving = omschrijving;
        boeking.Soort = invoer.Soort;
        boeking.Terugkerend = invoer.Terugkerend;
        boeking.VerwerktBijKlant = invoer.VerwerktBijKlant;
        boeking.GewijzigdOp = nu;
        boeking.GewijzigdDoor = wie.Naam;
        // Expliciet toevoegen: een nieuwe regel met een eigen Id zou EF anders als bestaande regel bijwerken.
        db.Correctieregels.AddRange(invoer.Regels.Select((regel, volgorde) => new Correctieregel
        {
            Id = Guid.NewGuid(),
            CorrectieboekingId = boeking.Id,
            Volgorde = volgorde,
            GrootboekrekeningId = regel.RekeningId,
            Bedrag = regel.Bedrag,
            Omschrijving = string.IsNullOrWhiteSpace(regel.Toelichting) ? null : regel.Toelichting.Trim(),
        }));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Uitkomst<Guid>.Mislukt("De boeking is tegelijk door iemand anders gewijzigd of toegevoegd. Laad de pagina opnieuw en probeer het nog eens.");
        }

        await audit.Log(wie, boekingId is null ? AuditActies.CorrectieToegevoegd : AuditActies.CorrectieGewijzigd,
            Onderwerp(dossier, boeking.Nummer), Beschrijving(boeking, rekeningen));
        return Uitkomst<Guid>.Met(boeking.Id);
    }

    public async Task<Uitkomst> Verwijder(Guid dossierId, Guid boekingId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var boeking = await db.Correctieboekingen.Include(c => c.Regels).ThenInclude(r => r.Grootboekrekening)
            .SingleOrDefaultAsync(c => c.Id == boekingId && c.DossierId == dossierId);
        if (boeking is null)
        {
            return Uitkomst.Mislukt("Deze boeking bestaat niet (meer).");
        }

        var dossier = await db.Dossiers.Include(d => d.Klant).SingleAsync(d => d.Id == dossierId);
        var beschrijving = Beschrijving(boeking, boeking.Regels.DistinctBy(r => r.GrootboekrekeningId).ToDictionary(r => r.GrootboekrekeningId, r => r.Grootboekrekening!));
        db.Correctieboekingen.Remove(boeking);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.CorrectieVerwijderd, Onderwerp(dossier, boeking.Nummer), beschrijving);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Een grootboekrekening aanmaken om op te corrigeren, bijv. een nog te betalen post die de klant niet heeft. Leest
    /// de klant later een saldibalans in met hetzelfde nummer, dan is het dezelfde rekening.
    /// </summary>
    public async Task<Uitkomst<Guid>> MaakRekening(Guid dossierId, string nummer, string omschrijving, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        nummer = nummer?.Trim() ?? string.Empty;
        omschrijving = omschrijving?.Trim() ?? string.Empty;
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst<Guid>.Mislukt("Dit dossier bestaat niet (meer).");
        }

        if (nummer.Length == 0 || omschrijving.Length == 0)
        {
            return Uitkomst<Guid>.Mislukt("Vul een rekeningnummer en een omschrijving in.");
        }

        if (nummer.Length > 50 || omschrijving.Length > MaximaleLengte || nummer.Any(char.IsWhiteSpace))
        {
            return Uitkomst<Guid>.Mislukt("Een rekeningnummer is hoogstens 50 tekens zonder spaties; een omschrijving hoogstens 300 tekens.");
        }

        if (await db.Grootboekrekeningen.AsNoTracking().SingleOrDefaultAsync(g => g.KlantId == dossier.KlantId && g.Nummer == nummer) is { } bestaand)
        {
            return Uitkomst<Guid>.Mislukt($"Rekening {nummer} bestaat al: {bestaand.Omschrijving}.");
        }

        var rekening = new Grootboekrekening
        {
            Id = Guid.NewGuid(),
            KlantId = dossier.KlantId,
            Nummer = nummer,
            Omschrijving = omschrijving,
            AangemaaktBijCorrectie = true,
        };
        db.Grootboekrekeningen.Add(rekening);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Uitkomst<Guid>.Mislukt($"Rekening {nummer} bestaat al.");
        }

        await audit.Log(wie, AuditActies.RekeningAangemaakt, $"{KlantDienst.Onderwerp(dossier.Klant!)} – {dossier.Naam}", $"{nummer} {omschrijving}");
        return Uitkomst<Guid>.Met(rekening.Id);
    }

    /// <summary>Neemt de terugkerende boekingen uit het dossier van vorig jaar over die hier nog niet staan.</summary>
    public async Task<Uitkomst<int>> NeemTerugkerendeOver(Guid dossierId, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst<int>.Mislukt("Dit dossier bestaat niet (meer).");
        }

        var vorigId = await db.Dossiers.Where(d => d.KlantId == dossier.KlantId && d.Boekjaar == dossier.Boekjaar - 1 && d.SoortOpdracht == dossier.SoortOpdracht)
            .Select(d => (Guid?)d.Id).SingleOrDefaultAsync();
        var al = await db.Correctieboekingen.Where(c => c.DossierId == dossierId && c.OvergenomenVanId != null).Select(c => c.OvergenomenVanId).ToListAsync();
        var over = vorigId is null ? [] : await db.Correctieboekingen.AsNoTracking().Include(c => c.Regels)
            .Where(c => c.DossierId == vorigId && c.Terugkerend && !al.Contains(c.Id))
            .OrderBy(c => c.Nummer)
            .ToListAsync();
        if (over.Count == 0)
        {
            return Uitkomst<int>.Mislukt("Er zijn geen terugkerende boekingen uit vorig jaar om over te nemen.");
        }

        var nummer = await db.Correctieboekingen.Where(c => c.DossierId == dossierId).MaxAsync(c => (int?)c.Nummer) ?? 0;
        var nu = tijd.GetUtcNow();
        foreach (var bron in over)
        {
            db.Correctieboekingen.Add(new Correctieboeking
            {
                Id = Guid.NewGuid(),
                DossierId = dossierId,
                Nummer = ++nummer,
                Omschrijving = bron.Omschrijving,
                Soort = bron.Soort,
                Terugkerend = true,
                OvergenomenVanId = bron.Id,
                AangemaaktOp = nu,
                AangemaaktDoor = wie.Naam,
                GewijzigdOp = nu,
                GewijzigdDoor = wie.Naam,
                Regels = [.. bron.Regels.OrderBy(r => r.Volgorde).Select(r => new Correctieregel
                {
                    Id = Guid.NewGuid(),
                    Volgorde = r.Volgorde,
                    GrootboekrekeningId = r.GrootboekrekeningId,
                    Bedrag = r.Bedrag,
                    Omschrijving = r.Omschrijving,
                })],
            });
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.CorrectiesOvergenomen, $"{KlantDienst.Onderwerp(dossier.Klant!)} – {dossier.Naam}",
            string.Join("; ", over.Select(b => $"{b.Nummer} {b.Omschrijving}")));
        return Uitkomst<int>.Met(over.Count);
    }

    private static Task<DossierDetail?> Dossier(BoekerhoudenDbContext db, Guid dossierId) =>
        db.Dossiers.AsNoTracking()
            .Where(d => d.Id == dossierId)
            .Select(d => new DossierDetail(d.Id, d.Naam, d.Boekjaar, d.SoortOpdracht, d.Status, d.KlantId, d.Klant!.Klantnummer, d.Klant.Naam, d.Klant.Rechtsvorm))
            .SingleOrDefaultAsync();

    private static Task<Guid?> VorigDossier(BoekerhoudenDbContext db, DossierDetail dossier) =>
        db.Dossiers.AsNoTracking()
            .Where(d => d.KlantId == dossier.KlantId && d.Boekjaar == dossier.Boekjaar - 1 && d.SoortOpdracht == dossier.Soort)
            .Select(d => (Guid?)d.Id)
            .SingleOrDefaultAsync();

    private static async Task<List<CorrectieInfo>> Boekingen(BoekerhoudenDbContext db, Guid dossierId, Dictionary<Guid, string> codes)
    {
        var boekingen = await db.Correctieboekingen.AsNoTracking()
            .Where(c => c.DossierId == dossierId)
            .OrderBy(c => c.Nummer)
            .Select(c => new
            {
                c.Id,
                c.Nummer,
                c.Omschrijving,
                c.Soort,
                c.Terugkerend,
                c.VerwerktBijKlant,
                Overgenomen = c.OvergenomenVanId != null,
                c.GewijzigdDoor,
                c.GewijzigdOp,
                Regels = c.Regels.OrderBy(r => r.Volgorde)
                    .Select(r => new CorrectieRegelInfo(r.GrootboekrekeningId, r.Grootboekrekening!.Nummer, r.Grootboekrekening.Omschrijving, r.Bedrag, r.Omschrijving))
                    .ToList(),
            })
            .ToListAsync();
        return [.. boekingen.Select(b => new CorrectieInfo(
            b.Id,
            b.Nummer,
            b.Omschrijving,
            b.Soort,
            b.Terugkerend,
            b.VerwerktBijKlant,
            b.Overgenomen,
            b.Regels,
            CorrectieEffect.Bereken(b.Regels.Select(r => new Boekingsregel(r.RekeningId, r.Bedrag)), id => codes.GetValueOrDefault(id)),
            b.GewijzigdDoor,
            b.GewijzigdOp))];
    }

    private static string Onderwerp(Dossier dossier, int nummer) => $"{KlantDienst.Onderwerp(dossier.Klant!)} – {dossier.Naam} – correctie {nummer}";

    /// <summary>Voor het wijzigingslog: de exacte bedragen, niet afgerond.</summary>
    private static string Beschrijving(Correctieboeking boeking, IReadOnlyDictionary<Guid, Grootboekrekening> rekeningen) =>
        $"{boeking.Omschrijving} ({boeking.Soort}): " + string.Join("; ", boeking.Regels.OrderBy(r => r.Volgorde)
            .Select(r => $"{rekeningen.GetValueOrDefault(r.GrootboekrekeningId)?.Nummer} {Bedragen.DebetCredit(r.Bedrag)}"));
}
