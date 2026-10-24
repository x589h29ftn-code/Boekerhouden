using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rubricering;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>Een gemaakt rapport met wat de opmaak nodig heeft: het dossier, de kantoorgegevens en het moment.</summary>
public sealed record RapportInhoud(DossierDetail Dossier, Rapport Rapport, Kantoorinstellingen Kantoor, DateTimeOffset Op);

/// <param name="RapportOmschrijving">De omschrijving in het rapport; null = die uit de administratie.</param>
public sealed record RekeningPresentatieInfo(Guid Id, string Nummer, string Omschrijving, string? RapportOmschrijving, bool Verbergen, decimal? Huidig, decimal? Vorig);

/// <param name="Rekeningen">De rekeningen in de specificatie van de post (niet die onder een eigen post: die staat als één regel).</param>
public sealed record PostPresentatieInfo(string Code, string Naam, bool ToonSpecificatie, IReadOnlyList<RekeningPresentatieInfo> Rekeningen);

public sealed record PresentatieOverzicht(Guid KlantId, IReadOnlyList<PostPresentatieInfo> Posten);

/// <summary>
/// Het rapport van een dossier (plakje 6): de proefbalans na correcties, gepresenteerd als jaarrekening (BV) of als
/// bijlage bij de aangifte (eenmanszaak). Ook de presentatie per klant: omschrijvingen van rekeningen, rekeningen in
/// "Overige" en per post wel of geen specificatie. Die rolt door naar volgende jaren.
/// </summary>
public sealed class RapportDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RubriceerDienst rubriceren, KantoorDienst kantoor, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleOmschrijving = 300;

    /// <summary>Maakt het rapport; null als het dossier niet bestaat.</summary>
    public async Task<Uitkomst<RapportInhoud>?> Maak(Guid dossierId)
    {
        var overzicht = await rubriceren.Overzicht(dossierId, metVoorstellen: false);
        if (overzicht is null)
        {
            return null;
        }

        if (overzicht.Schema is null || overzicht.Balans is null)
        {
            return Uitkomst<RapportInhoud>.Mislukt("Er is nog geen RGS-versie ingelezen; zonder rubricering is er geen rapport.");
        }

        var dossier = overzicht.Dossier;
        await using var db = await dbFactory.CreateDbContextAsync();
        var plaats = await db.Klanten.AsNoTracking().Where(k => k.Id == dossier.KlantId).Select(k => k.Plaats).SingleAsync();

        var rapport = RapportOpbouw.Maak(new RapportGegevens(
            Soort(dossier.Soort),
            dossier.KlantNaam,
            plaats,
            dossier.Boekjaar,
            Concept: true,
            overzicht.Schema,
            overzicht.Balans,
            [.. overzicht.Rekeningen.Select(r => new RekeningSaldo(r.Id, r.Nummer, r.Omschrijving, r.Saldo, r.SaldoVorig, r.Correcties))],
            overzicht.EigenPosten,
            code => Naam(overzicht, code),
            await Weergave(dossier.KlantId),
            await ZonderSpecificatie(dossier.KlantId)));
        return Uitkomst<RapportInhoud>.Met(new RapportInhoud(dossier, rapport, await kantoor.Haal(), tijd.GetUtcNow()));
    }

    /// <summary>De presentatie per hoofdpost met saldo, met de rekeningen die in de specificatie staan; null als het dossier niet bestaat.</summary>
    public async Task<PresentatieOverzicht?> Presentatie(Guid dossierId)
    {
        var overzicht = await rubriceren.Overzicht(dossierId, metVoorstellen: false);
        if (overzicht is null)
        {
            return null;
        }

        var klantId = overzicht.Dossier.KlantId;
        if (overzicht is not { Schema: { } schema, Balans: { } balans })
        {
            return new PresentatieOverzicht(klantId, []);
        }

        var weergave = await Weergave(klantId);
        var zonder = await ZonderSpecificatie(klantId);
        var eigen = overzicht.EigenPosten.Select(e => PostBalans.EigenSleutel(e.Id)).ToHashSet(StringComparer.Ordinal);
        string? Hoofdpost(string code) =>
            schema.Zoek(code) is { Niveau: 2 } ? code : schema.Voorouders(code).FirstOrDefault(c => c.Niveau == 2)?.Code;

        var perPost = new Dictionary<string, List<RekeningPresentatieInfo>>(StringComparer.Ordinal);
        foreach (var rekening in overzicht.Rekeningen.Where(r => r.HeeftSaldo))
        {
            var posten = new[] { balans.Plaatsing.GetValueOrDefault(rekening.Id), balans.PlaatsingVorig.GetValueOrDefault(rekening.Id) }
                .OfType<string>().Where(s => !eigen.Contains(s)).Select(Hoofdpost).OfType<string>().Distinct(StringComparer.Ordinal);
            foreach (var post in posten)
            {
                if (!perPost.TryGetValue(post, out var lijst))
                {
                    lijst = [];
                    perPost[post] = lijst;
                }

                var w = weergave.GetValueOrDefault(rekening.Id);
                lijst.Add(new RekeningPresentatieInfo(rekening.Id, rekening.Nummer, rekening.Omschrijving, w?.Omschrijving, w?.Verbergen ?? false, rekening.Saldo, rekening.SaldoVorig));
            }
        }

        return new PresentatieOverzicht(klantId, [.. schema.Hoofdposten
            .Where(p => balans.Totaal(p.Code) is { Aantal: > 0 } t && (t.Huidig != 0 || t.Vorig != 0))
            .Select(p => new PostPresentatieInfo(p.Code, Naam(overzicht, p.Code), !zonder.Contains(p.Code), perPost.GetValueOrDefault(p.Code) ?? []))]);
    }

    /// <summary>
    /// Namen in het rapport voor posten waarvan de RGS-omschrijving voor elke rechtsvorm tegelijk geldt; een klant- of
    /// kantoornaam gaat altijd voor.
    /// </summary>
    private static readonly Dictionary<string, string> Rapportnamen = new(StringComparer.Ordinal)
    {
        ["BEiv"] = "Eigen vermogen",
    };

    private static string Naam(RubriceerOverzicht overzicht, string code) =>
        !overzicht.IsHernoemd(code) && Rapportnamen.TryGetValue(code, out var naam) ? naam : overzicht.Naam(code);

    public static RapportSoort Soort(SoortOpdracht soort) =>
        soort == SoortOpdracht.SamenstellenJaarrekening ? RapportSoort.Jaarrekening : RapportSoort.AangifteBijlage;

    /// <summary>De presentatie van de rekeningen van een klant, per rekening.</summary>
    public async Task<IReadOnlyDictionary<Guid, RekeningWeergave>> Weergave(Guid klantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RekeningPresentaties.AsNoTracking().Where(p => p.KlantId == klantId)
            .ToDictionaryAsync(p => p.GrootboekrekeningId, p => new RekeningWeergave(p.Omschrijving, p.Verbergen));
    }

    /// <summary>De posten (sleutels) van een klant waarvan de specificatie niet in het rapport komt.</summary>
    public async Task<IReadOnlySet<string>> ZonderSpecificatie(Guid klantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.PostPresentaties.AsNoTracking().Where(p => p.KlantId == klantId && !p.ToonSpecificatie)
            .Select(p => p.Sleutel).ToListAsync()).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Hoe een rekening in de specificatie staat: een andere omschrijving (leeg = die uit de administratie), of
    /// verborgen, dan telt hij mee in "Overige" van zijn post.
    /// </summary>
    public async Task<Uitkomst> ZetWeergave(Guid klantId, Guid rekeningId, string? omschrijving, bool verbergen, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        omschrijving = string.IsNullOrWhiteSpace(omschrijving) ? null : omschrijving.Trim();
        if (omschrijving?.Length > MaximaleOmschrijving)
        {
            return Uitkomst.Mislukt($"Een omschrijving is hoogstens {MaximaleOmschrijving} tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var rekening = await db.Grootboekrekeningen.AsNoTracking().SingleOrDefaultAsync(g => g.Id == rekeningId && g.KlantId == klantId);
        if (rekening is null)
        {
            return Uitkomst.Mislukt("Rekening niet gevonden.");
        }

        if (omschrijving == rekening.Omschrijving)
        {
            omschrijving = null;
        }

        var presentatie = await db.RekeningPresentaties.SingleOrDefaultAsync(p => p.GrootboekrekeningId == rekeningId);
        if (presentatie?.Omschrijving == omschrijving && (presentatie?.Verbergen ?? false) == verbergen)
        {
            return Uitkomst.Gelukt;
        }

        if (omschrijving is null && !verbergen)
        {
            db.RekeningPresentaties.Remove(presentatie!);
        }
        else
        {
            if (presentatie is null)
            {
                presentatie = new RekeningPresentatie { GrootboekrekeningId = rekeningId, KlantId = klantId };
                db.RekeningPresentaties.Add(presentatie);
            }

            presentatie.Omschrijving = omschrijving;
            presentatie.Verbergen = verbergen;
            presentatie.GewijzigdOp = tijd.GetUtcNow();
            presentatie.GewijzigdDoor = wie.Naam;
        }

        await db.SaveChangesAsync();
        var klant = await db.Klanten.AsNoTracking().SingleAsync(k => k.Id == klantId);
        var wat = verbergen ? "in Overige" : omschrijving is null ? "omschrijving uit de administratie" : $"in het rapport als '{omschrijving}'";
        await audit.Log(wie, AuditActies.PresentatieGewijzigd, KlantDienst.Onderwerp(klant), $"{rekening.Nummer} {rekening.Omschrijving}: {wat}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Of de specificatie van een post (hoofdpost) in het rapport komt.</summary>
    public async Task<Uitkomst> ZetSpecificatie(Guid klantId, string sleutel, bool tonen, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        ArgumentNullException.ThrowIfNull(wie);
        if (sleutel.Length is 0 or > 60)
        {
            return Uitkomst.Mislukt("Onbekende post.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var klant = await db.Klanten.AsNoTracking().SingleOrDefaultAsync(k => k.Id == klantId);
        if (klant is null)
        {
            return Uitkomst.Mislukt("Klant niet gevonden.");
        }

        var presentatie = await db.PostPresentaties.SingleOrDefaultAsync(p => p.KlantId == klantId && p.Sleutel == sleutel);
        if ((presentatie?.ToonSpecificatie ?? true) == tonen)
        {
            return Uitkomst.Gelukt;
        }

        if (tonen)
        {
            db.PostPresentaties.Remove(presentatie!);
        }
        else
        {
            db.PostPresentaties.Add(new PostPresentatie { KlantId = klantId, Sleutel = sleutel, ToonSpecificatie = false, GewijzigdOp = tijd.GetUtcNow(), GewijzigdDoor = wie.Naam });
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.PresentatieGewijzigd, KlantDienst.Onderwerp(klant), $"specificatie {sleutel} {(tonen ? "wel" : "niet")} in het rapport");
        return Uitkomst.Gelukt;
    }
}
