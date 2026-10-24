using System.Text.RegularExpressions;
using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Grondslagen;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

public sealed record GrondslagInvoer(string Titel, string? TekstCommercieel, string? TekstFiscaal, GrondslagOpname Opname, string Codes, bool Jaargebonden);

/// <param name="Verslaggeving">Commercieel of fiscaal; null = nog niet gekozen (dan geen grondslagen).</param>
public sealed record GrondslagenVoorRapport(Verslaggeving? Verslaggeving, IReadOnlyList<Grondslag> Grondslagen, IReadOnlyDictionary<string, GrondslagKeuze> Keuzes);

/// <summary>
/// De grondslagen in de jaarrekening van een BV: de bibliotheek van het kantoor (Beheer › Grondslagen), de keuze
/// commercieel of fiscaal per dossier, en per dossier welke grondslagen open of dicht staan en eigen teksten. Een
/// keuze rolt door naar volgende jaren, behalve bij jaargebonden grondslagen zoals foutenherstel.
/// </summary>
public sealed partial class GrondslagDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    public const int MaximaleTekst = 4000;

    /// <summary>
    /// Brengt de bibliotheek bij met de meegeleverde grondslagen: nieuwe komen erbij, en wat het kantoor niet heeft
    /// aangepast krijgt de nieuwe standaardtekst (vervallen standaardgrondslagen gaan eruit). Aanpassingen van het
    /// kantoor blijven; de indeling (groep en volgorde) volgt altijd de standaard.
    /// </summary>
    public static async Task VulAan(BoekerhoudenDbContext db, DateTimeOffset nu)
    {
        ArgumentNullException.ThrowIfNull(db);
        var standaard = StandaardGrondslagen.Maak(nu);
        var bestaand = await db.GrondslagSjablonen.ToDictionaryAsync(g => g.Sleutel, StringComparer.Ordinal);
        foreach (var nieuw in standaard)
        {
            if (!bestaand.Remove(nieuw.Sleutel, out var oud))
            {
                db.GrondslagSjablonen.Add(nieuw);
                continue;
            }

            oud.Groep = nieuw.Groep;
            oud.Volgorde = nieuw.Volgorde;
            if (oud.GewijzigdDoor == StandaardGrondslagen.Door
                && (oud.Titel, oud.TekstCommercieel, oud.TekstFiscaal, oud.Opname, oud.Codes, oud.Jaargebonden)
                != (nieuw.Titel, nieuw.TekstCommercieel, nieuw.TekstFiscaal, nieuw.Opname, nieuw.Codes, nieuw.Jaargebonden))
            {
                (oud.Titel, oud.TekstCommercieel, oud.TekstFiscaal, oud.Opname, oud.Codes, oud.Jaargebonden, oud.GewijzigdOp) =
                    (nieuw.Titel, nieuw.TekstCommercieel, nieuw.TekstFiscaal, nieuw.Opname, nieuw.Codes, nieuw.Jaargebonden, nu);
            }
        }

        db.GrondslagSjablonen.RemoveRange(bestaand.Values.Where(g => g.GewijzigdDoor == StandaardGrondslagen.Door));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<GrondslagSjabloon>> Bibliotheek()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GrondslagSjablonen.AsNoTracking().OrderBy(g => g.Volgorde).ToListAsync();
    }

    public Task<Uitkomst> Bewaar(string sleutel, GrondslagInvoer invoer, Uitvoerder wie) => Bewaar(sleutel, invoer, wie, wie?.Naam ?? string.Empty);

    /// <param name="door">Wie de grondslag heeft gewijzigd; <see cref="StandaardGrondslagen.Door"/> als het de standaard is.</param>
    private async Task<Uitkomst> Bewaar(string sleutel, GrondslagInvoer invoer, Uitvoerder wie, string door)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        ArgumentNullException.ThrowIfNull(wie);
        var titel = invoer.Titel?.Trim() ?? string.Empty;
        var commercieel = Leeg(invoer.TekstCommercieel);
        var fiscaal = Leeg(invoer.TekstFiscaal);
        var codes = string.Join(",", (invoer.Codes ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var fouten = new List<string>();
        if (titel.Length is 0 or > 200)
        {
            fouten.Add("Geef de grondslag een titel (hoogstens 200 tekens).");
        }

        if (commercieel is null && fiscaal is null)
        {
            fouten.Add("Vul ten minste één tekst in: commercieel of fiscaal.");
        }

        if (commercieel?.Length > MaximaleTekst || fiscaal?.Length > MaximaleTekst)
        {
            fouten.Add($"Een tekst is hoogstens {MaximaleTekst} tekens.");
        }

        if (codes.Length > 400 || !codes.Split(',', StringSplitOptions.RemoveEmptyEntries).All(c => CodePatroon().IsMatch(c)))
        {
            fouten.Add("Geef RGS-codes zoals BMva of WBed, gescheiden door komma's.");
        }

        if (invoer.Opname == GrondslagOpname.BijPost && codes.Length == 0)
        {
            fouten.Add("Een grondslag 'bij post' heeft ten minste één RGS-code nodig.");
        }

        if (fouten.Count > 0)
        {
            return new Uitkomst(fouten);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var grondslag = await db.GrondslagSjablonen.SingleOrDefaultAsync(g => g.Sleutel == sleutel);
        if (grondslag is null)
        {
            return Uitkomst.Mislukt("Deze grondslag bestaat niet (meer).");
        }

        grondslag.Titel = titel;
        grondslag.TekstCommercieel = commercieel;
        grondslag.TekstFiscaal = fiscaal;
        grondslag.Opname = invoer.Opname;
        grondslag.Codes = codes;
        grondslag.Jaargebonden = invoer.Jaargebonden;
        grondslag.GewijzigdOp = tijd.GetUtcNow();
        grondslag.GewijzigdDoor = door;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.GrondslagenbibliotheekGewijzigd, titel, $"{sleutel}: {invoer.Opname}{(codes.Length > 0 ? $" ({codes})" : null)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Zet een grondslag terug naar de meegeleverde tekst en instellingen; daarna gaat hij weer mee met nieuwe standaardteksten.</summary>
    public async Task<Uitkomst> HerstelStandaard(string sleutel, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var standaard = StandaardGrondslagen.Maak(tijd.GetUtcNow()).SingleOrDefault(g => g.Sleutel == sleutel);
        if (standaard is null)
        {
            return Uitkomst.Mislukt("Voor deze grondslag is geen standaardtekst meegeleverd.");
        }

        return await Bewaar(sleutel, new GrondslagInvoer(standaard.Titel, standaard.TekstCommercieel, standaard.TekstFiscaal, standaard.Opname, standaard.Codes, standaard.Jaargebonden),
            wie, StandaardGrondslagen.Door);
    }

    /// <summary>De keuze van het laatste eerdere jaarrekeningdossier van de klant, als voorstel voor een nieuw dossier.</summary>
    public async Task<Verslaggeving?> VorigeVerslaggeving(Guid klantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Dossiers.AsNoTracking()
            .Where(d => d.KlantId == klantId && d.SoortOpdracht == SoortOpdracht.SamenstellenJaarrekening && d.Grondslag != null)
            .OrderByDescending(d => d.Boekjaar).Select(d => d.Grondslag).FirstOrDefaultAsync();
    }

    public async Task<Uitkomst> KiesVerslaggeving(Guid dossierId, Verslaggeving keuze, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier is null)
        {
            return Uitkomst.Mislukt("Dossier niet gevonden.");
        }

        if (dossier.SoortOpdracht != SoortOpdracht.SamenstellenJaarrekening)
        {
            return Uitkomst.Mislukt("Grondslagen kies je alleen bij het samenstellen van een jaarrekening.");
        }

        if (dossier.Grondslag == keuze)
        {
            return Uitkomst.Gelukt;
        }

        var oud = dossier.Grondslag;
        dossier.Grondslag = keuze;
        dossier.GewijzigdOp = tijd.GetUtcNow();
        dossier.GewijzigdDoor = wie.Naam;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.VerslaggevingGekozen, WerkprogrammaDienst.Onderwerp(dossier),
            oud is null ? Weergave.Tekst(keuze) : $"{Weergave.Tekst(oud.Value)} → {Weergave.Tekst(keuze)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// De grondslagen voor het rapport van een dossier: de bibliotheek met de tekst voor de gekozen variant, en per
    /// grondslag de keuze uit dit dossier of, als die er niet is, uit het laatste eerdere jaar (niet bij jaargebonden).
    /// Naam, plaats, KvK-nummer en boekjaar zijn ingevuld.
    /// </summary>
    public async Task<GrondslagenVoorRapport?> VoorRapport(Guid dossierId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        if (dossier?.Klant is not { } klant)
        {
            return null;
        }

        string Vul(string tekst) => StandaardGrondslagen.Vul(tekst, klant.Naam, klant.Plaats, klant.KvkNummer, dossier.Boekjaar);

        var bibliotheek = await db.GrondslagSjablonen.AsNoTracking().OrderBy(g => g.Volgorde).ToListAsync();
        var grondslagen = dossier.Grondslag is not { } variant ? [] : bibliotheek
            .Select(g => (g, Tekst: variant == Verslaggeving.Commercieel ? g.TekstCommercieel : g.TekstFiscaal))
            .Where(x => !string.IsNullOrWhiteSpace(x.Tekst))
            .Select(x => new Grondslag(x.g.Sleutel, x.g.Groep, x.g.Titel, Vul(x.Tekst!), (GrondslagStandaard)x.g.Opname,
                x.g.Codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToList();

        var keuzes = (await GeldendeKeuzes(db, dossier)).ToDictionary(
            k => k.Key, k => k.Value with { Tekst = k.Value.Tekst is null ? null : Vul(k.Value.Tekst) }, StringComparer.Ordinal);
        return new GrondslagenVoorRapport(dossier.Grondslag, grondslagen, keuzes);
    }

    /// <summary>Per grondslag de keuze uit dit dossier of, als die er niet is, uit het laatste eerdere jaar (niet bij jaargebonden).</summary>
    private static async Task<Dictionary<string, GrondslagKeuze>> GeldendeKeuzes(BoekerhoudenDbContext db, Dossier dossier)
    {
        var jaargebonden = (await db.GrondslagSjablonen.AsNoTracking().Where(g => g.Jaargebonden).Select(g => g.Sleutel).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var eerdere = await (from k in db.DossierGrondslagen.AsNoTracking()
                             join d in db.Dossiers.AsNoTracking() on k.DossierId equals d.Id
                             where d.KlantId == dossier.KlantId && d.SoortOpdracht == dossier.SoortOpdracht && d.Boekjaar <= dossier.Boekjaar
                             select new { k.Sleutel, k.Opgenomen, k.Tekst, d.Boekjaar })
            .ToListAsync();
        return eerdere
            .Where(k => k.Boekjaar == dossier.Boekjaar || !jaargebonden.Contains(k.Sleutel))
            .GroupBy(k => k.Sleutel)
            .ToDictionary(g => g.Key, g => g.MaxBy(k => k.Boekjaar)!, StringComparer.Ordinal)
            .ToDictionary(k => k.Key, k => new GrondslagKeuze(k.Value.Opgenomen, k.Value.Tekst), StringComparer.Ordinal);
    }

    /// <summary>Een grondslag in dit dossier open (in het rapport) of dicht zetten.</summary>
    public Task<Uitkomst> ZetOpgenomen(Guid dossierId, string sleutel, bool opgenomen, Uitvoerder wie) =>
        Wijzig(dossierId, sleutel, wie, k => k.Opgenomen = opgenomen, opgenomen ? "in het rapport" : "niet in het rapport");

    /// <summary>Een eigen tekst voor een grondslag in dit dossier; leeg = de tekst uit de bibliotheek.</summary>
    public Task<Uitkomst> ZetTekst(Guid dossierId, string sleutel, string? tekst, Uitvoerder wie)
    {
        tekst = Leeg(tekst);
        return tekst?.Length > MaximaleTekst
            ? Task.FromResult(Uitkomst.Mislukt($"Een tekst is hoogstens {MaximaleTekst} tekens."))
            : Wijzig(dossierId, sleutel, wie, k => k.Tekst = tekst, tekst is null ? "standaardtekst" : $"eigen tekst: {tekst}");
    }

    /// <summary>
    /// Legt een keuze vast in dit dossier. De keuze die tot nu toe gold (uit een eerder jaar) is het vertrekpunt,
    /// zodat een eigen tekst blijft staan als je alleen open of dicht zet.
    /// </summary>
    private async Task<Uitkomst> Wijzig(Guid dossierId, string sleutel, Uitvoerder wie, Action<DossierGrondslag> wijzig, string wat)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        var dossier = await db.Dossiers.AsNoTracking().Include(d => d.Klant).SingleOrDefaultAsync(d => d.Id == dossierId);
        var grondslag = await db.GrondslagSjablonen.AsNoTracking().SingleOrDefaultAsync(g => g.Sleutel == sleutel);
        if (dossier is null || grondslag is null)
        {
            return Uitkomst.Mislukt("Grondslag of dossier niet gevonden.");
        }

        var keuze = await db.DossierGrondslagen.SingleOrDefaultAsync(k => k.DossierId == dossierId && k.Sleutel == sleutel);
        if (keuze is null)
        {
            var geldend = (await GeldendeKeuzes(db, dossier)).GetValueOrDefault(sleutel);
            keuze = new DossierGrondslag { DossierId = dossierId, Sleutel = sleutel, Opgenomen = geldend?.Opgenomen, Tekst = geldend?.Tekst };
            db.DossierGrondslagen.Add(keuze);
        }

        wijzig(keuze);
        keuze.GewijzigdOp = tijd.GetUtcNow();
        keuze.GewijzigdDoor = wie.Naam;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.GrondslagGewijzigd, WerkprogrammaDienst.Onderwerp(dossier), $"{grondslag.Titel}: {wat}");
        return Uitkomst.Gelukt;
    }

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    [GeneratedRegex("^[BW][A-Za-z]{0,20}$")]
    private static partial Regex CodePatroon();
}
