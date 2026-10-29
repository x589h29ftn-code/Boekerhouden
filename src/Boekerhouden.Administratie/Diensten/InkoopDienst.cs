using System.Security.Cryptography;
using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een ingelezen document in de lijst.</summary>
public sealed record DocumentRegel(
    Guid Id,
    string Bestandsnaam,
    Documentstatus Status,
    string? Leverancier,
    string? Factuurnummer,
    decimal? Totaal,
    string? Melding,
    DateTimeOffset IngelezenOp,
    string IngelezenDoor);

/// <summary>Wat er met een ingelezen bestand gebeurd is.</summary>
public sealed record Inleesuitkomst(Guid? DocumentId, Documentstatus? Status, string Melding)
{
    public bool IsGeboekt => Status == Documentstatus.Geboekt;
}

/// <summary>Een nieuwe leverancier, aangemaakt bij het verwerken van zijn eerste factuur.</summary>
public sealed record NieuweRelatie(string Naam, string? Iban, string? BtwNummer, string? KvkNummer);

/// <summary>De factuur zoals de gebruiker hem boekt: een bestaande relatie (code) of een nieuwe.</summary>
public sealed record Factuurinvoer(
    string? Relatie,
    NieuweRelatie? Nieuw,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Factuurregel> Regels,
    string? Betalingskenmerk = null);

/// <summary>Alles wat het scherm nodig heeft om een document te verwerken.</summary>
public sealed record Verwerking(
    DocumentRegel Document,
    HerkendeFactuur? Herkend,
    Inkoopvoorstel? Voorstel,
    IReadOnlyList<Relatie> Relaties,
    Rekeningschema Schema,
    IReadOnlyList<BtwCode> BtwCodes);

/// <summary>
/// Inkoopfacturen: inlezen (eerst de virusscan, dan pas openen), herkennen, het voorstel (regel &gt; geleerd &gt;
/// relatie), automatisch of door de gebruiker boeken, en leren van de keuze. Boeken zet de journaalpost in de keten en
/// maakt de openstaande post, in één transactie.
/// </summary>
public sealed class InkoopDienst(
    Toegang toegang,
    GrootboekDienst grootboek,
    AuditDienst audit,
    TimeProvider tijd,
    IEnumerable<IVirusscanner> virusscanners)
{
    private static readonly JsonSerializerOptions Json = new();

    private static IReadOnlyList<BtwCode> BtwCodes => Boekhouding.Btw.BtwCodes.Standaard;

    /// <summary>
    /// Leest een bestand in. Een besmet bestand gaat in quarantaine (zonder inhoud); is de scanner niet bereikbaar, dan
    /// wordt het niet bewaard. Daarna herkennen en, als dat veilig kan, meteen boeken.
    /// </summary>
    public async Task<Inleesuitkomst> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, string? inhoudstype, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(inhoud);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var naam = Path.GetFileName(bestandsnaam);
        byte[] bytes;
        try
        {
            bytes = await LeesBegrensd(inhoud, annulering);
        }
        catch (DocumentException fout)
        {
            return new(null, null, fout.Message);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (await db.Documenten.AsNoTracking().FirstOrDefaultAsync(d => d.Sha256 == hash && d.Status != Documentstatus.Quarantaine, annulering) is { } eerder)
        {
            return new(eerder.Id, eerder.Status, $"Dit bestand is al ingelezen op {Weergave.DatumTijd(eerder.IngelezenOp)}.");
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Bestandsnaam = naam.Length > 260 ? naam[..260] : naam,
            Inhoudstype = inhoudstype ?? "application/octet-stream",
            Grootte = bytes.Length,
            Sha256 = hash,
            IngelezenOp = tijd.GetUtcNow(),
            IngelezenDoor = wie.Naam,
        };

        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(bytes), annulering);
            if (scan.Uitslag == Scanuitslag.Besmet)
            {
                document.Status = Documentstatus.Quarantaine;
                document.Melding = $"Kwaadaardige inhoud gevonden: {scan.Toelichting}.";
                db.Documenten.Add(document);
                await db.SaveChangesAsync(annulering);
                await audit.Log(wie, AuditActies.DocumentQuarantaine, naam, scan.Toelichting, administratie);
                return new(document.Id, document.Status, $"{naam} bevat kwaadaardige inhoud en staat in quarantaine.");
            }

            if (scan.Uitslag == Scanuitslag.NietGescand)
            {
                return new(null, null, $"{scan.Toelichting} {naam} is niet ingelezen; probeer het later opnieuw.");
            }
        }

        var gegevens = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie, annulering);
        var eigen = new EigenGegevens(gegevens.Ibans, gegevens.BtwNummer is null ? [] : [gegevens.BtwNummer], [gegevens.Naam]);
        var herkenning = await Factuurherkenning.Standaard(eigen).HerkenAsync(new MemoryStream(bytes), naam, inhoudstype, annulering);
        document.Herkend = herkenning.Factuur is null ? null : JsonSerializer.Serialize(herkenning.Factuur, Json);
        document.Status = Documentstatus.TeVerwerken;
        document.Melding = herkenning.Melding;
        db.Documenten.Add(document);
        db.DocumentInhouden.Add(new DocumentInhoud { DocumentId = document.Id, AdministratieId = administratie, Inhoud = bytes });
        await db.SaveChangesAsync(annulering);
        await audit.Log(wie, AuditActies.DocumentIngelezen, naam, null, administratie);

        if (herkenning.Factuur is not { } herkend)
        {
            return new(document.Id, document.Status, $"{naam}: {herkenning.Melding}");
        }

        var context = await Context(db, administratie);
        var voorstel = Voorstel(herkend, context);
        if (voorstel.Automatisch)
        {
            var uitkomst = await BoekFactuur(db, administratie, document, voorstel.Factuur!, herkend, voorstel.Leverancier!.Relatie, nieuw: null, wie, automatisch: true);
            if (uitkomst.IsGelukt)
            {
                return new(document.Id, Documentstatus.Geboekt, $"{naam} is automatisch geboekt ({voorstel.Advies.Reden}).");
            }

            voorstel = voorstel with { Belemmeringen = [.. uitkomst.Fouten] };
        }

        document.Melding = Melding(voorstel);
        await db.SaveChangesAsync(annulering);
        return new(document.Id, document.Status, $"{naam}: {document.Melding}");
    }

    public async Task<IReadOnlyList<DocumentRegel>> Lijst(Uitvoerder wie, Guid administratie, Documentstatus? status = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Documenten.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        return [.. (await query.OrderByDescending(d => d.IngelezenOp).Take(500).ToListAsync()).Select(Regel)];
    }

    /// <summary>Het document met wat er herkend is en het (actuele) voorstel, om te verwerken.</summary>
    public async Task<Verwerking?> Verwerking(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Documenten.AsNoTracking().FirstOrDefaultAsync(d => d.Id == document) is not { } d)
        {
            return null;
        }

        var context = await Context(db, administratie);
        var herkend = Herkend(d);
        var voorstel = herkend is null ? null : Voorstel(herkend, context);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        return new Verwerking(Regel(d), herkend, voorstel, context.Relaties, schema, BtwCodes);
    }

    /// <summary>Boekt een document zoals de gebruiker het heeft ingevuld (of bevestigd), en onthoudt de keuze.</summary>
    public async Task<Uitkomst> Boek(Uitvoerder wie, Guid administratie, Guid document, Factuurinvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Documenten.FirstOrDefaultAsync(d => d.Id == document) is not { } d)
        {
            return Uitkomst.Mislukt("Dit document bestaat niet (meer).");
        }

        if (d.Status != Documentstatus.TeVerwerken)
        {
            return Uitkomst.Mislukt(d.Status == Documentstatus.Geboekt ? "Dit document is al geboekt." : "Dit document staat in quarantaine.");
        }

        var context = await Context(db, administratie);
        Relatie? relatie = null;
        if (invoer.Nieuw is null)
        {
            relatie = context.Relaties.FirstOrDefault(r => r.Code == invoer.Relatie);
            if (relatie is null)
            {
                return Uitkomst.Mislukt("Kies een leverancier of maak een nieuwe aan.");
            }
        }
        else if (string.IsNullOrWhiteSpace(invoer.Nieuw.Naam))
        {
            return Uitkomst.Mislukt("Vul de naam van de nieuwe leverancier in.");
        }

        var code = relatie?.Code ?? NieuweCode(invoer.Nieuw!.Naam, context.Relaties);
        var factuur = new Factuur(Factuursoort.Inkoop, code, invoer.Factuurnummer.Trim(), invoer.Datum, invoer.Vervaldatum,
            string.IsNullOrWhiteSpace(invoer.Omschrijving) ? $"{relatie?.Naam ?? invoer.Nieuw!.Naam} {invoer.Factuurnummer}".Trim() : invoer.Omschrijving.Trim(),
            invoer.Regels, string.IsNullOrWhiteSpace(invoer.Betalingskenmerk) ? null : invoer.Betalingskenmerk.Trim());
        return await BoekFactuur(db, administratie, d, factuur, Herkend(d), relatie, invoer.Nieuw, wie, automatisch: false);
    }

    /// <summary>De inhoud van een document, om te bekijken of te downloaden.</summary>
    public async Task<(byte[] Inhoud, string Inhoudstype, string Bestandsnaam)?> Inhoud(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var d = await db.Documenten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == document);
        var inhoud = await db.DocumentInhouden.AsNoTracking().FirstOrDefaultAsync(x => x.DocumentId == document);
        return d is null || inhoud is null ? null : (inhoud.Inhoud, d.Inhoudstype, d.Bestandsnaam);
    }

    private async Task<Uitkomst> BoekFactuur(
        AdministratieDbContext db, Guid administratie, Document document, Factuur factuur, HerkendeFactuur? herkend,
        Relatie? relatie, NieuweRelatie? nieuw, Uitvoerder wie, bool automatisch)
    {
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        var dagboek = dagboeken.FirstOrDefault(x => x.Soort == Dagboeksoort.Inkoop);
        if (dagboek is null)
        {
            return Uitkomst.Mislukt("Deze administratie heeft geen inkoopboek.");
        }

        var fouten = Factuurboeker.Controleer(factuur, dagboek, BtwCodes).ToList();
        var nummer = Factuurnummers.Normaliseer(factuur.Factuurnummer);
        var eerder = await db.OpenstaandePosten.AsNoTracking()
            .Where(p => p.Soort == Factuursoort.Inkoop && p.Relatie == factuur.Relatie).Select(p => p.Factuurnummer).ToListAsync();
        if (nummer.Length > 0 && eerder.Any(f => Factuurnummers.Normaliseer(f) == nummer))
        {
            fouten.Add($"Factuur {factuur.Factuurnummer} van deze leverancier is al geboekt.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Administraties\" WHERE \"Id\" = {administratie} FOR UPDATE");
        if (nieuw is not null)
        {
            var iban = string.IsNullOrWhiteSpace(nieuw.Iban) ? null : Iban.Normaliseer(nieuw.Iban);
            db.Relaties.Add(new Handelsrelatie
            {
                Id = Guid.NewGuid(),
                AdministratieId = administratie,
                Code = factuur.Relatie,
                Naam = nieuw.Naam.Trim(),
                Ibans = iban is not null && Iban.IsGeldig(iban) ? [iban] : [],
                BtwNummer = string.IsNullOrWhiteSpace(nieuw.BtwNummer) ? null : BtwNummer.Normaliseer(nieuw.BtwNummer),
                KvkNummer = string.IsNullOrWhiteSpace(nieuw.KvkNummer) ? null : nieuw.KvkNummer.Trim(),
                Tegenrekening = factuur.Regels[0].Rekeningnummer,
                AangemaaktOp = tijd.GetUtcNow(),
            });
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, factuur.Datum.Year);
        Journaalpost post;
        OpenPost open;
        try
        {
            (post, open) = Factuurboeker.Boek(factuur, dagboek, boekstuk, schema, BtwCodes);
        }
        catch (Exception fout) when (fout is ArgumentException or InvalidOperationException)
        {
            return Uitkomst.Mislukt(fout.Message.Split(" (Parameter", 2)[0]);
        }

        var controle = Journaalcontrole.Controleer(post, schema, dagboeken);
        if (controle.Count > 0)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        var boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        db.OpenstaandePosten.Add(new OpenstaandePost
        {
            Id = open.Id,
            AdministratieId = administratie,
            Soort = open.Soort,
            Relatie = open.Relatie,
            Factuurnummer = open.Factuurnummer,
            Datum = open.Datum,
            Vervaldatum = open.Vervaldatum,
            Bedrag = open.Bedrag,
            Openstaand = open.Openstaand,
            Betalingskenmerk = open.Betalingskenmerk,
            BoekingId = boeking.Id,
            DocumentId = document.Id,
        });
        document.Status = Documentstatus.Geboekt;
        document.BoekingId = boeking.Id;
        document.Melding = automatisch ? "Automatisch geboekt." : null;

        // Leren: de keuze van de gebruiker (of het automatisch gevolgde advies) onder de sleutel van de leverancier.
        var gegevens = herkend is null
            ? new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, factuur.Relatie, relatie?.Naam ?? nieuw?.Naam, factuur.Totaal(BtwCodes))
            : Inkoopverwerker.Gegevens(herkend, relatie ?? new Relatie(factuur.Relatie, nieuw!.Naam, []));
        if (Leergeheugen.Sleutel(gegevens with { Relatie = factuur.Relatie }) is { } sleutel)
        {
            db.Leerkeuzes.Add(new Leerkeuze
            {
                AdministratieId = administratie,
                Sleutel = sleutel,
                Rekeningnummer = factuur.Regels[0].Rekeningnummer,
                BtwCode = factuur.Regels[0].BtwCode,
                Tijdstip = tijd.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, automatisch ? AuditActies.AutomatischGeboekt : AuditActies.Geboekt,
            $"{dagboek.Code} {boekstuk}", $"{factuur.Relatie} {factuur.Factuurnummer}; {factuur.Totaal(BtwCodes):0.00}", administratie);
        return Uitkomst.Gelukt;
    }

    private sealed record Inkoopcontext(
        Data.Administratie Administratie,
        IReadOnlyList<Relatie> Relaties,
        IReadOnlyList<Verwerkingsregel> Regels,
        Leergeheugen Geheugen,
        IReadOnlyList<BestaandeFactuur> Bestaand);

    private static async Task<Inkoopcontext> Context(AdministratieDbContext db, Guid administratie)
    {
        var gegevens = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        var relaties = (await db.Relaties.AsNoTracking().OrderBy(r => r.Naam).ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var regels = (await db.Regels.AsNoTracking().OrderBy(r => r.Volgorde).ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var geheugen = new Leergeheugen();
        foreach (var keuze in await db.Leerkeuzes.AsNoTracking().OrderBy(k => k.Id).ToListAsync())
        {
            geheugen.Onthoud(keuze.Sleutel, new Keuze(keuze.Rekeningnummer, keuze.BtwCode));
        }

        var bestaand = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == Factuursoort.Inkoop)
            .Select(p => new BestaandeFactuur(p.Relatie, p.Factuurnummer, p.Datum, -p.Bedrag)).ToListAsync();
        return new Inkoopcontext(gegevens, relaties, regels, geheugen, bestaand);
    }

    private static Inkoopvoorstel Voorstel(HerkendeFactuur herkend, Inkoopcontext context) =>
        Inkoopverwerker.Verwerk(herkend, context.Relaties, context.Regels, context.Geheugen, BtwCodes, context.Bestaand,
            context.Administratie.Betalingstermijn, periodeOpen: null, context.Administratie.LimietAutomatisch);

    private static string Melding(Inkoopvoorstel voorstel) =>
        voorstel.Belemmeringen.Count > 0 ? string.Join(" ", voorstel.Belemmeringen) : voorstel.Advies.Reden;

    private static HerkendeFactuur? Herkend(Document document) =>
        document.Herkend is null ? null : JsonSerializer.Deserialize<HerkendeFactuur>(document.Herkend, Json);

    private static DocumentRegel Regel(Document d)
    {
        var herkend = Herkend(d);
        return new DocumentRegel(d.Id, d.Bestandsnaam, d.Status, herkend?.Naam, herkend?.Factuurnummer, herkend?.TotaalInclusief, d.Melding, d.IngelezenOp, d.IngelezenDoor);
    }

    /// <summary>Een code voor een nieuwe relatie: de eerste letters van de naam, uniek gemaakt met een volgnummer.</summary>
    internal static string NieuweCode(string naam, IReadOnlyList<Relatie> bestaand)
    {
        var woorden = Namen.Woorden(naam);
        var basis = woorden.Count == 0 ? string.Empty : string.Concat(woorden[0].Where(char.IsLetterOrDigit)).ToUpperInvariant();
        basis = basis.Length == 0 ? "REL" : basis[..Math.Min(10, basis.Length)];
        var code = basis;
        for (var i = 2; bestaand.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)); i++)
        {
            code = $"{basis}{i}";
        }

        return code;
    }

    private static async Task<byte[]> LeesBegrensd(Stream stream, CancellationToken annulering)
    {
        using var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = await stream.ReadAsync(buffer, annulering)) > 0)
        {
            if (kopie.Length + gelezen > PdfTekst.MaximumBytes)
            {
                throw new DocumentException($"Het bestand is groter dan {PdfTekst.MaximumBytes / 1024 / 1024} MB.");
            }

            kopie.Write(buffer, 0, gelezen);
        }

        return kopie.ToArray();
    }
}
