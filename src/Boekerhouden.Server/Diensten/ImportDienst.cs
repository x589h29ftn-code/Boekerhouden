using System.Security.Cryptography;
using Boekerhouden.Data;
using Boekerhouden.Domain;
using Boekerhouden.Import;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>Uitkomst van de controle vóór het importeren: alle meldingen (I1–I11) en of importeren mogelijk is.</summary>
public sealed record ImportControle(string Bestandsnaam, int Boekjaar, string Sha256, Saldibalans? Saldibalans, IReadOnlyList<ImportMelding> Meldingen)
{
    public bool KanImporteren => Saldibalans is not null;

    public bool VervangtBestaande => Meldingen.Any(m => m.Code == ImportCodes.BestaandeImport);

    public IEnumerable<ImportMelding> Fouten => Meldingen.Where(m => m.Ernst == MeldingErnst.Fout);

    public IEnumerable<ImportMelding> Waarschuwingen => Meldingen.Where(m => m.Ernst == MeldingErnst.Waarschuwing);

    public IEnumerable<ImportMelding> Informatie => Meldingen.Where(m => m.Ernst == MeldingErnst.Informatie);
}

public sealed record ImportOverzichtRegel(
    Guid Id,
    int Boekjaar,
    bool Actief,
    string Bestandsnaam,
    int AantalRegels,
    decimal TotaalDebet,
    decimal TotaalCredit,
    DateTimeOffset GeimporteerdOp,
    string GeimporteerdDoor);

public sealed record ProefbalansRegel(string Nummer, string Omschrijving, decimal? Bedrag, decimal? BedragVorigJaar);

/// <summary>De saldibalans van een boekjaar uit de actieve import, met het vorige jaar ernaast (als dat is ingelezen).</summary>
public sealed record Proefbalans(int Boekjaar, ImportOverzichtRegel? Import, ImportOverzichtRegel? ImportVorigJaar, IReadOnlyList<ProefbalansRegel> Regels)
{
    public decimal Totaal => Regels.Sum(r => r.Bedrag ?? 0m);

    public decimal TotaalVorigJaar => Regels.Sum(r => r.BedragVorigJaar ?? 0m);
}

/// <summary>
/// Importeert saldibalansen per klant en boekjaar. Controleren en importeren zijn twee stappen: de gebruiker ziet eerst
/// alle meldingen. Importeren leest het bestand opnieuw in, zodat er nooit iets anders wordt opgeslagen dan is gecontroleerd.
/// </summary>
public sealed class ImportDienst(IDbContextFactory<BoekerhoudenDbContext> dbFactory, AuditDienst audit, RubriceerDienst rubriceer, TimeProvider tijd)
{
    /// <summary>Grotere bestanden weigeren we; een saldibalans is hooguit enkele duizenden regels.</summary>
    public const long MaximaleBestandsgrootte = 20 * 1024 * 1024;

    private const int MaxVoorbeelden = 10;

    public async Task<ImportControle> Controleer(Guid klantId, int boekjaar, byte[] inhoud, string bestandsnaam)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        ArgumentNullException.ThrowIfNull(bestandsnaam);
        var sha = Convert.ToHexStringLower(SHA256.HashData(inhoud));

        if (inhoud.LongLength > MaximaleBestandsgrootte)
        {
            return new ImportControle(bestandsnaam, boekjaar, sha, null,
                [new ImportMelding(ImportCodes.Bestand, MeldingErnst.Fout, "Het bestand is groter dan 20 MB. Is dit wel een saldibalans?")]);
        }

        using var stream = new MemoryStream(inhoud, writable: false);
        var resultaat = ImportSjabloonLezer.Lees(stream, bestandsnaam, new ImportOpties { Boekjaar = boekjaar });
        var meldingen = resultaat.Meldingen.ToList();

        await using var db = await dbFactory.CreateDbContextAsync();
        var klant = await db.Klanten.AsNoTracking().SingleOrDefaultAsync(k => k.Id == klantId)
                    ?? throw new InvalidOperationException("De klant bestaat niet (meer).");

        if (resultaat.Saldibalans is { } balans)
        {
            if (KlantWijktAf(balans.Klant, klant))
            {
                meldingen.Add(new ImportMelding(ImportCodes.KlantAfwijkend, MeldingErnst.Waarschuwing,
                    $"In het bestand staat klant '{balans.Klant}', maar je importeert bij {klant.Klantnummer} {klant.Naam}. Klopt dat?"));
            }

            var bestaande = await ActieveBatch(db, klantId, boekjaar);
            if (bestaande is not null)
            {
                var zelfde = bestaande.Sha256 == sha ? " Het is hetzelfde bestand." : string.Empty;
                meldingen.Add(new ImportMelding(ImportCodes.BestaandeImport, MeldingErnst.Waarschuwing,
                    $"Er is al een saldibalans {boekjaar} ingelezen ({bestaande.Bestandsnaam}, {Datum(bestaande.GeimporteerdOp)} door {bestaande.GeimporteerdDoor}).{zelfde} " +
                    "Importeren vervangt die; de vorige import blijft bewaard in de historie."));
            }

            meldingen.AddRange(await Verschillen(db, klantId, boekjaar, bestaande, balans));
        }

        return new ImportControle(bestandsnaam, boekjaar, sha, resultaat.Saldibalans, meldingen);
    }

    public async Task<Uitkomst<Guid>> Importeer(Guid klantId, int boekjaar, byte[] inhoud, string bestandsnaam, Uitvoerder wie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        var controle = await Controleer(klantId, boekjaar, inhoud, bestandsnaam);
        if (controle.Saldibalans is not { } balans)
        {
            return Uitkomst<Guid>.Mislukt([.. controle.Fouten.Select(f => f.Tekst)]);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transactie = await db.Database.BeginTransactionAsync();
        var klant = await db.Klanten.SingleAsync(k => k.Id == klantId);

        // De omschrijving bij een nummer volgt het meest recente boekjaar; een historisch jaar overschrijft die niet.
        var laatsteJaar = await db.ImportBatches.Where(b => b.KlantId == klantId && b.Actief).MaxAsync(b => (int?)b.Boekjaar);
        var omschrijvingBijwerken = laatsteJaar is null || boekjaar >= laatsteJaar;

        var rekeningen = await db.Grootboekrekeningen.Where(g => g.KlantId == klantId).ToDictionaryAsync(g => g.Nummer, StringComparer.Ordinal);
        foreach (var regel in balans.Regels)
        {
            if (!rekeningen.TryGetValue(regel.Grootboeknummer, out var rekening))
            {
                rekening = new Grootboekrekening { Id = Guid.NewGuid(), KlantId = klantId, Nummer = regel.Grootboeknummer };
                db.Grootboekrekeningen.Add(rekening);
                rekeningen.Add(rekening.Nummer, rekening);
                rekening.Omschrijving = regel.Omschrijving;
                rekening.RgsCode = regel.RgsCode;
            }
            else if (omschrijvingBijwerken)
            {
                rekening.Omschrijving = regel.Omschrijving;
                rekening.RgsCode = regel.RgsCode ?? rekening.RgsCode;
            }
        }

        await db.ImportBatches.Where(b => b.KlantId == klantId && b.Boekjaar == boekjaar && b.Actief)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Actief, false));

        var nu = tijd.GetUtcNow();
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            KlantId = klantId,
            Boekjaar = boekjaar,
            Actief = true,
            Bestandsnaam = Path.GetFileName(bestandsnaam),
            Sha256 = controle.Sha256,
            AantalRegels = balans.Regels.Count,
            TotaalDebet = balans.TotaalDebet,
            TotaalCredit = balans.TotaalCredit,
            GeimporteerdOp = nu,
            GeimporteerdDoor = wie.Naam,
            Saldi = [.. balans.Regels.Select(r => new Saldo
            {
                Id = Guid.NewGuid(),
                GrootboekrekeningId = rekeningen[r.Grootboeknummer].Id,
                Bedrag = r.Bedrag,
            })],
        };
        db.ImportBatches.Add(batch);

        await db.Dossiers.Where(d => d.KlantId == klantId && d.Boekjaar == boekjaar && d.Status == DossierStatus.Nieuw)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DossierStatus.InUitvoering)
                .SetProperty(d => d.GewijzigdOp, nu)
                .SetProperty(d => d.GewijzigdDoor, wie.Naam));

        await db.SaveChangesAsync();
        await transactie.CommitAsync();

        await audit.Log(wie, AuditActies.SaldibalansGeimporteerd, $"{KlantDienst.Onderwerp(klant)} – boekjaar {boekjaar}",
            $"{batch.Bestandsnaam}; {batch.AantalRegels} regels; debet {Bedragen.Euro(batch.TotaalDebet)}; SHA-256 {batch.Sha256}" +
            (controle.VervangtBestaande ? "; vervangt de vorige import" : string.Empty));

        // Nieuwe rekeningen meteen koppelen waar dat zeker kan (kantoorsjabloon of RGS-code uit het bestand).
        await rubriceer.AutomatischKoppelen(klantId, wie);
        return Uitkomst<Guid>.Met(batch.Id);
    }

    public async Task<IReadOnlyList<ImportOverzichtRegel>> Imports(Guid klantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ImportBatches.AsNoTracking()
            .Where(b => b.KlantId == klantId)
            .OrderByDescending(b => b.Boekjaar).ThenByDescending(b => b.GeimporteerdOp)
            .Select(b => new ImportOverzichtRegel(b.Id, b.Boekjaar, b.Actief, b.Bestandsnaam, b.AantalRegels,
                b.TotaalDebet, b.TotaalCredit, b.GeimporteerdOp, b.GeimporteerdDoor))
            .ToListAsync();
    }

    public async Task<Proefbalans> Proefbalans(Guid klantId, int boekjaar)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var huidig = await ActieveBatch(db, klantId, boekjaar);
        var vorig = await ActieveBatch(db, klantId, boekjaar - 1);

        var bedragen = await SaldiPerNummer(db, huidig?.Id);
        var vorigeBedragen = await SaldiPerNummer(db, vorig?.Id);
        var omschrijvingen = await db.Grootboekrekeningen.AsNoTracking().Where(g => g.KlantId == klantId)
            .ToDictionaryAsync(g => g.Nummer, g => g.Omschrijving, StringComparer.Ordinal);

        var regels = bedragen.Keys.Union(vorigeBedragen.Keys)
            .Order(StringComparer.Ordinal)
            .Select(n => new ProefbalansRegel(n, omschrijvingen.GetValueOrDefault(n, string.Empty),
                bedragen.TryGetValue(n, out var b) ? b : null,
                vorigeBedragen.TryGetValue(n, out var v) ? v : null))
            .ToList();

        return new Proefbalans(boekjaar, Overzicht(huidig), Overzicht(vorig), regels);
    }

    internal static bool KlantWijktAf(string? inBestand, Klant klant)
    {
        if (string.IsNullOrWhiteSpace(inBestand))
        {
            return false;
        }

        var bestand = Normaliseer(inBestand);
        var naam = Normaliseer(klant.Naam);
        var nummer = Normaliseer(klant.Klantnummer);
        return !(bestand.Contains(naam, StringComparison.Ordinal) || naam.Contains(bestand, StringComparison.Ordinal)
                 || (nummer.Length > 0 && bestand.Contains(nummer, StringComparison.Ordinal)));
    }

    private static string Normaliseer(string tekst) =>
        new([.. tekst.ToUpperInvariant().Where(char.IsLetterOrDigit)]);

    private static async Task<List<ImportMelding>> Verschillen(BoekerhoudenDbContext db, Guid klantId, int boekjaar, ImportBatch? bestaande, Saldibalans balans)
    {
        // Vergelijk met de vorige import van hetzelfde jaar; is die er niet, dan met het vorige boekjaar.
        var basis = bestaande ?? await ActieveBatch(db, klantId, boekjaar - 1);
        if (basis is null)
        {
            return [new ImportMelding(ImportCodes.Verschillen, MeldingErnst.Informatie,
                $"Eerste saldibalans {boekjaar}: er is geen eerdere import van {boekjaar} of {boekjaar - 1} om mee te vergelijken.")];
        }

        var waarmee = bestaande is not null ? $"de vorige import van {boekjaar}" : $"boekjaar {basis.Boekjaar}";
        var eerder = await db.Saldi.AsNoTracking()
            .Where(s => s.ImportBatchId == basis.Id)
            .Select(s => s.Grootboekrekening!.Nummer)
            .ToListAsync();
        var bekend = await db.Grootboekrekeningen.AsNoTracking().Where(g => g.KlantId == klantId)
            .ToDictionaryAsync(g => g.Nummer, g => g.Omschrijving, StringComparer.Ordinal);
        var nu = balans.Regels.ToDictionary(r => r.Grootboeknummer, r => r.Omschrijving, StringComparer.Ordinal);

        var meldingen = new List<ImportMelding>();
        var nieuw = nu.Keys.Except(eerder, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var weg = eerder.Except(nu.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var anders = nu.Where(r => bekend.TryGetValue(r.Key, out var o) && !string.Equals(o, r.Value, StringComparison.Ordinal))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => $"{r.Key}: '{bekend[r.Key]}' → '{r.Value}'")
            .ToList();

        if (nieuw.Count > 0)
        {
            meldingen.Add(Info($"{nieuw.Count} nieuwe rekening(en) ten opzichte van {waarmee}: {Opsomming(nieuw)}"));
        }

        if (weg.Count > 0)
        {
            meldingen.Add(Info($"{weg.Count} rekening(en) uit {waarmee} komen niet meer voor: {Opsomming(weg)}"));
        }

        if (anders.Count > 0)
        {
            meldingen.Add(Info($"{anders.Count} rekening(en) met een andere omschrijving dan tot nu toe: {Opsomming(anders)}"));
        }

        if (meldingen.Count == 0)
        {
            meldingen.Add(Info($"Dezelfde rekeningen en omschrijvingen als in {waarmee}."));
        }

        return meldingen;

        static ImportMelding Info(string tekst) => new(ImportCodes.Verschillen, MeldingErnst.Informatie, tekst);
    }

    private static string Opsomming(List<string> items) =>
        items.Count <= MaxVoorbeelden
            ? string.Join(", ", items)
            : $"{string.Join(", ", items.Take(MaxVoorbeelden))} en nog {items.Count - MaxVoorbeelden}";

    private static Task<ImportBatch?> ActieveBatch(BoekerhoudenDbContext db, Guid klantId, int boekjaar) =>
        db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.KlantId == klantId && b.Boekjaar == boekjaar && b.Actief);

    private static async Task<Dictionary<string, decimal>> SaldiPerNummer(BoekerhoudenDbContext db, Guid? batchId) =>
        batchId is null
            ? new Dictionary<string, decimal>(StringComparer.Ordinal)
            : await db.Saldi.AsNoTracking().Where(s => s.ImportBatchId == batchId)
                .Select(s => new { s.Grootboekrekening!.Nummer, s.Bedrag })
                .ToDictionaryAsync(s => s.Nummer, s => s.Bedrag, StringComparer.Ordinal);

    private static ImportOverzichtRegel? Overzicht(ImportBatch? b) =>
        b is null ? null : new(b.Id, b.Boekjaar, b.Actief, b.Bestandsnaam, b.AantalRegels, b.TotaalDebet, b.TotaalCredit, b.GeimporteerdOp, b.GeimporteerdDoor);

    private static string Datum(DateTimeOffset tijdstip) => Weergave.DatumTijd(tijdstip);
}
