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

/// <param name="Regels">De bewaarde uitsplitsing, of een voorstel (uit vorig jaar of één regel met het saldo).</param>
/// <param name="Bewaard">Of de regels al bewaard zijn (anders is het een voorstel).</param>
/// <param name="UitVorigJaar">Of het voorstel uit de uitsplitsing van vorig jaar komt.</param>
public sealed record UitsplitsingInfo(IReadOnlyList<SplitsRegel> Regels, bool Bewaard, bool UitVorigJaar);

/// <summary>
/// Het rapport van een dossier (plakje 6): de proefbalans na correcties, gepresenteerd als jaarrekening (BV) of als
/// bijlage bij de aangifte (eenmanszaak). Ook de presentatie per klant: omschrijvingen van rekeningen, rekeningen in
/// "Overige", per post wel of geen specificatie en uit- of ingeklapt; die rolt door naar volgende jaren. En per
/// dossier: toelichtingen bij posten en uitsplitsingen van rekeningen.
/// </summary>
public sealed class RapportDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, RubriceerDienst rubriceren, KantoorDienst kantoor, GrondslagDienst grondslagen, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleOmschrijving = 300;
    public const int MaximaleToelichting = 4000;
    public const int MaximaleSplitsregels = 50;

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
        var jaarrekening = Soort(dossier.Soort) == RapportSoort.Jaarrekening;
        var metGrondslagen = jaarrekening ? await grondslagen.VoorRapport(dossierId) : null;

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
            await ZonderSpecificatie(dossier.KlantId),
            await db.PostPresentaties.AsNoTracking().Where(p => p.KlantId == dossier.KlantId && p.Uitgeklapt != null)
                .ToDictionaryAsync(p => p.Sleutel, p => p.Uitgeklapt!.Value, StringComparer.Ordinal),
            await db.DossierToelichtingen.AsNoTracking().Where(t => t.DossierId == dossierId)
                .ToDictionaryAsync(t => t.Sleutel, t => t.Tekst, StringComparer.Ordinal),
            (await db.UitsplitsingRegels.AsNoTracking().Where(r => r.DossierId == dossierId).OrderBy(r => r.Volgorde).ToListAsync())
                .GroupBy(r => r.GrootboekrekeningId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<SplitsRegel>)[.. g.Select(r => new SplitsRegel(r.Omschrijving, r.Bedrag, r.BedragVorig))]),
            metGrondslagen?.Verslaggeving is null ? null : metGrondslagen.Grondslagen,
            metGrondslagen?.Keuzes));
        if (jaarrekening && metGrondslagen?.Verslaggeving is null)
        {
            rapport = rapport with { Waarschuwingen = [.. rapport.Waarschuwingen, "Grondslagen nog niet gekozen."] };
        }

        return Uitkomst<RapportInhoud>.Met(new RapportInhoud(dossier with { Grondslag = metGrondslagen?.Verslaggeving }, rapport, await kantoor.Haal(), tijd.GetUtcNow()));
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
    public Task<Uitkomst> ZetSpecificatie(Guid klantId, string sleutel, bool tonen, Uitvoerder wie) =>
        ZetPost(klantId, sleutel, p => p.ToonSpecificatie = tonen, $"specificatie {sleutel} {(tonen ? "wel" : "niet")} in het rapport", wie);

    /// <summary>Of een post in het rapport is uitgeklapt (subposten of rekeningen zichtbaar); null = de standaard.</summary>
    public Task<Uitkomst> ZetUitgeklapt(Guid klantId, string sleutel, bool? uitgeklapt, Uitvoerder wie) =>
        ZetPost(klantId, sleutel, p => p.Uitgeklapt = uitgeklapt, $"{sleutel} {(uitgeklapt switch { true => "uitgeklapt", false => "ingeklapt", null => "standaard" })} in het rapport", wie);

    private async Task<Uitkomst> ZetPost(Guid klantId, string sleutel, Action<PostPresentatie> wijzig, string wat, Uitvoerder wie)
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
        var nieuw = presentatie is null;
        presentatie ??= new PostPresentatie { KlantId = klantId, Sleutel = sleutel };
        var voor = (presentatie.ToonSpecificatie, presentatie.Uitgeklapt);
        wijzig(presentatie);
        if (voor == (presentatie.ToonSpecificatie, presentatie.Uitgeklapt))
        {
            return Uitkomst.Gelukt;
        }

        presentatie.GewijzigdOp = tijd.GetUtcNow();
        presentatie.GewijzigdDoor = wie.Naam;
        var standaard = presentatie is { ToonSpecificatie: true, Uitgeklapt: null };
        if (nieuw && !standaard)
        {
            db.PostPresentaties.Add(presentatie);
        }
        else if (!nieuw && standaard)
        {
            // Alles weer standaard: dan hoeft er niets te worden onthouden.
            db.PostPresentaties.Remove(presentatie);
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.PresentatieGewijzigd, KlantDienst.Onderwerp(klant), wat);
        return Uitkomst.Gelukt;
    }

    /// <summary>De toelichting bij een post in het dossier van vorig jaar (zelfde klant en soort), als voorstel.</summary>
    public async Task<string?> ToelichtingVorigJaar(Guid dossierId, string sleutel)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await (from d in db.Dossiers.AsNoTracking()
                      where d.Id == dossierId
                      join v in db.Dossiers.AsNoTracking() on new { d.KlantId, d.SoortOpdracht, Boekjaar = d.Boekjaar - 1 } equals new { v.KlantId, v.SoortOpdracht, v.Boekjaar }
                      join t in db.DossierToelichtingen.AsNoTracking() on v.Id equals t.DossierId
                      where t.Sleutel == sleutel
                      select t.Tekst).FirstOrDefaultAsync();
    }

    /// <summary>De toelichting bij een post in het rapport van dit dossier; leeg = geen toelichting.</summary>
    public async Task<Uitkomst> ZetToelichting(Guid dossierId, string sleutel, string? tekst, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        ArgumentNullException.ThrowIfNull(wie);
        tekst = string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
        if (sleutel.Length is 0 or > 60)
        {
            return Uitkomst.Mislukt("Onbekende post.");
        }

        if (tekst?.Length > MaximaleToelichting)
        {
            return Uitkomst.Mislukt($"Een toelichting is hoogstens {MaximaleToelichting} tekens.");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst.Mislukt("Dossier niet gevonden.");
        }

        var toelichting = await db.DossierToelichtingen.SingleOrDefaultAsync(t => t.DossierId == dossierId && t.Sleutel == sleutel);
        if (toelichting?.Tekst == tekst)
        {
            return Uitkomst.Gelukt;
        }

        if (tekst is null)
        {
            db.DossierToelichtingen.Remove(toelichting!);
        }
        else
        {
            if (toelichting is null)
            {
                toelichting = new DossierToelichting { DossierId = dossierId, Sleutel = sleutel };
                db.DossierToelichtingen.Add(toelichting);
            }

            toelichting.Tekst = tekst;
            toelichting.GewijzigdOp = tijd.GetUtcNow();
            toelichting.GewijzigdDoor = wie.Naam;
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ToelichtingGewijzigd, WerkprogrammaDienst.Onderwerp(dossier), tekst is null ? $"{sleutel}: toelichting verwijderd" : $"{sleutel}: {tekst}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// De uitsplitsing van een rekening in dit dossier. Is die er nog niet, dan een voorstel: de regels van vorig jaar
    /// (met hun bedragen als vorig jaar), of één regel met de omschrijving en het saldo zoals in het rapport.
    /// </summary>
    public async Task<UitsplitsingInfo> Uitsplitsing(Guid dossierId, Guid rekeningId, string omschrijving, decimal saldo, decimal saldoVorig)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var bewaard = await db.UitsplitsingRegels.AsNoTracking().Where(r => r.DossierId == dossierId && r.GrootboekrekeningId == rekeningId)
            .OrderBy(r => r.Volgorde).Select(r => new SplitsRegel(r.Omschrijving, r.Bedrag, r.BedragVorig)).ToListAsync();
        if (bewaard.Count > 0)
        {
            return new UitsplitsingInfo(bewaard, true, false);
        }

        var vorigJaar = await (from d in db.Dossiers.AsNoTracking()
                               where d.Id == dossierId
                               join v in db.Dossiers.AsNoTracking() on new { d.KlantId, d.SoortOpdracht, Boekjaar = d.Boekjaar - 1 } equals new { v.KlantId, v.SoortOpdracht, v.Boekjaar }
                               join r in db.UitsplitsingRegels.AsNoTracking() on v.Id equals r.DossierId
                               where r.GrootboekrekeningId == rekeningId
                               orderby r.Volgorde
                               select new SplitsRegel(r.Omschrijving, 0m, r.Bedrag)).ToListAsync();
        return vorigJaar.Count > 0
            ? new UitsplitsingInfo(vorigJaar, false, true)
            : new UitsplitsingInfo([new SplitsRegel(omschrijving, saldo, saldoVorig)], false, false);
    }

    /// <summary>
    /// Bewaart de uitsplitsing van een rekening (vervangt de vorige); zonder regels vervalt de uitsplitsing. Of de
    /// regels optellen tot het saldo, controleert het rapport: een verschil komt daar apart en als waarschuwing.
    /// </summary>
    public async Task<Uitkomst> BewaarUitsplitsing(Guid dossierId, Guid rekeningId, IReadOnlyList<SplitsRegel> regels, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(wie);
        var fouten = new List<string>();
        if (regels.Count > MaximaleSplitsregels)
        {
            fouten.Add($"Hoogstens {MaximaleSplitsregels} regels.");
        }

        if (regels.Any(r => string.IsNullOrWhiteSpace(r.Omschrijving) || r.Omschrijving.Trim().Length > 200))
        {
            fouten.Add("Geef elke regel een omschrijving (hoogstens 200 tekens).");
        }

        if (regels.Any(r => r.Bedrag != Math.Round(r.Bedrag, 2) || (r.BedragVorig is { } v && v != Math.Round(v, 2))))
        {
            fouten.Add("Bedragen hebben hoogstens twee decimalen.");
        }

        if (fouten.Count > 0)
        {
            return new Uitkomst(fouten);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        var rekening = dossier is null ? null
            : await db.Grootboekrekeningen.AsNoTracking().SingleOrDefaultAsync(g => g.Id == rekeningId && g.KlantId == dossier.KlantId);
        if (dossier is null || rekening is null)
        {
            return Uitkomst.Mislukt("Rekening niet gevonden in dit dossier.");
        }

        db.UitsplitsingRegels.RemoveRange(await db.UitsplitsingRegels.Where(r => r.DossierId == dossierId && r.GrootboekrekeningId == rekeningId).ToListAsync());
        var nu = tijd.GetUtcNow();
        db.UitsplitsingRegels.AddRange(regels.Select((r, i) => new UitsplitsingRegel
        {
            Id = Guid.NewGuid(),
            DossierId = dossierId,
            GrootboekrekeningId = rekeningId,
            Volgorde = i,
            Omschrijving = r.Omschrijving.Trim(),
            Bedrag = r.Bedrag,
            BedragVorig = r.BedragVorig,
            GewijzigdOp = nu,
            GewijzigdDoor = wie.Naam,
        }));
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.UitsplitsingGewijzigd, WerkprogrammaDienst.Onderwerp(dossier),
            regels.Count == 0 ? $"{rekening.Nummer} {rekening.Omschrijving}: uitsplitsing verwijderd"
                : $"{rekening.Nummer} {rekening.Omschrijving}: {string.Join("; ", regels.Select(r => $"{r.Omschrijving.Trim()} {Domain.Bedragen.Getal(r.Bedrag)}{(r.BedragVorig is { } v ? $" (vorig jaar {Domain.Bedragen.Getal(v)})" : null)}"))}");
        return Uitkomst.Gelukt;
    }
}
