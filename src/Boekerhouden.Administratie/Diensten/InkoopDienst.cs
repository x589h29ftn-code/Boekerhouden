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
    Documentsoort Soort,
    string? Leverancier,
    string? Factuurnummer,
    DateOnly? Factuurdatum,
    decimal? Totaal,
    string? Melding,
    DateTimeOffset IngelezenOp,
    string IngelezenDoor);

/// <summary>Een eerder geboekte factuur van een leverancier: hoe die toen geboekt is.</summary>
/// <param name="Totaal">Het totaal inclusief btw.</param>
/// <param name="Rekening">De kostenrekening (bij meerdere regels die met het grootste bedrag).</param>
/// <param name="Post">De openstaande post (om een creditnota mee te verrekenen).</param>
/// <param name="Openstaand">Wat er nog openstaat, zoals in het grootboek (te betalen negatief).</param>
public sealed record EerdereFactuur(DateOnly Datum, string Factuurnummer, decimal Totaal, string Rekening, string? BtwCode, bool Betaald, Guid Post, decimal Openstaand);

/// <summary>
/// Een geboekte factuur zoals hij in het formulier stond: uit de boeking en de openstaande post. De bedragen staan
/// zonder minteken (ook bij een creditnota), zoals de gebruiker ze invult.
/// </summary>
/// <param name="Totaal">Het totaal inclusief btw.</param>
public sealed record GeboekteFactuur(
    string Relatie,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    string? Betalingskenmerk,
    bool Creditnota,
    decimal Totaal,
    IReadOnlyList<Factuurregel> Regels,
    string Dagboek,
    string Boekstuk,
    Periodeverdeling? Verdeling = null,
    IReadOnlyList<string>? Verdeelboekstukken = null);

/// <summary>
/// Een factuur in het inkoopboek: wanneer en onder welk boekstuk geboekt, van wie, en de bedragen zoals op de factuur
/// (een creditnota negatief). <paramref name="Openstaand"/> is wat er nog betaald moet worden.
/// </summary>
public sealed record Factuurboekregel(
    DateOnly Datum,
    string Dagboek,
    string Boekstuk,
    string Relatie,
    string Leverancier,
    string Factuurnummer,
    string Omschrijving,
    decimal Exclusief,
    decimal Btw,
    decimal Totaal,
    decimal Openstaand,
    Guid? Document);

/// <summary>De periodeverdeling van een geboekt document, zoals bewaard bij het document (JSON).</summary>
internal sealed record Verdelingsgegevens(DateOnly Van, DateOnly Tot, string Rekening, List<string> Kostenrekeningen, List<string> Boekstukken);

/// <summary>Wat er met een ingelezen bestand gebeurd is.</summary>
public sealed record Inleesuitkomst(Guid? DocumentId, Documentstatus? Status, string Melding)
{
    public bool IsGeboekt => Status == Documentstatus.Geboekt;
}

/// <summary>Een nieuwe leverancier, aangemaakt bij het verwerken van zijn eerste factuur.</summary>
public sealed record NieuweRelatie(string Naam, string? Iban, string? BtwNummer, string? KvkNummer);

/// <summary>
/// De factuur zoals de gebruiker hem boekt: een bestaande relatie (code) of een nieuwe. Bij een creditnota staan de
/// bedragen positief (zoals op het papier) en boekt het pakket ze omgekeerd; <c>VerrekenMet</c> is de openstaande
/// factuur van dezelfde leverancier waarmee de creditnota wordt verrekend.
/// </summary>
public sealed record Factuurinvoer(
    string? Relatie,
    NieuweRelatie? Nieuw,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Factuurregel> Regels,
    string? Betalingskenmerk = null,
    IReadOnlyList<Zoekgebied>? Aanwijzingen = null,
    bool Creditnota = false,
    Guid? VerrekenMet = null,
    Periodeverdeling? Verdeling = null);

/// <summary>Alles wat het scherm nodig heeft om een document te verwerken.</summary>
public sealed record Verwerking(
    DocumentRegel Document,
    HerkendeFactuur? Herkend,
    Inkoopvoorstel? Voorstel,
    IReadOnlyList<Relatie> Relaties,
    Rekeningschema Schema,
    IReadOnlyList<BtwCode> BtwCodes,
    Btwtabel Btwtabel,
    IReadOnlyList<Factuurveld>? UitZoektemplate = null);

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
    IEnumerable<IVirusscanner> virusscanners,
    BankDienst bank,
    IEnumerable<IOcrMotor> ocrMotoren)
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Hoeveel verwerkte documenten de inkooplijst toont; alles staat in het inkoopboek.</summary>
    public const int RecentVerwerkt = 100;

    /// <summary>Het jaar van de laatste geboekte inkoopfactuur (voor het inkoopboek), of leeg als er nog geen is.</summary>
    public async Task<int?> LaatsteFactuurjaar(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == Factuursoort.Inkoop)
            .OrderByDescending(p => p.Datum).Select(p => (int?)p.Datum.Year).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Het inkoopboek over een periode (op factuurdatum): elke geboekte inkoopfactuur en creditnota met de bedragen en
    /// wat er nog openstaat, op datum en boekstuk. Een teruggedraaide factuur staat er niet meer in.
    /// </summary>
    public async Task<IReadOnlyList<Factuurboekregel>> Inkoopboek(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Factuurboek.Lees(db, Factuursoort.Inkoop, van, tot);
    }

    /// <summary>
    /// Leest een bestand in. Een besmet bestand gaat in quarantaine (zonder inhoud); is de scanner niet bereikbaar, dan
    /// wordt het niet bewaard. Daarna herkennen en, als dat veilig kan, meteen boeken.
    /// </summary>
    /// <param name="banktransactie">De bankmutatie waarbij de gebruiker de factuur uploadt: na het boeken worden ze gekoppeld.</param>
    public async Task<Inleesuitkomst> Lees(
        Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, string? inhoudstype, Guid? banktransactie = null, CancellationToken annulering = default)
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
            BanktransactieId = banktransactie,
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
        var ocr = ocrMotoren.FirstOrDefault() is { } motor ? new OcrLezer(motor) : null;
        var herkenning = await Factuurherkenning.Standaard(eigen, ocr: ocr).HerkenAsync(new MemoryStream(bytes), naam, inhoudstype, annulering);

        // Een foto wordt bewaard als PDF (de foto als pagina), zodat de viewer en het aanwijzen werken.
        string? tekstlaag = null;
        if (herkenning.Ocr is { } laag)
        {
            tekstlaag = JsonSerializer.Serialize(laag.Woorden, Json);
            if (laag.Pdf is { } pdf)
            {
                (bytes, document.Inhoudstype, document.Bestandsnaam) = (pdf, "application/pdf", Path.ChangeExtension(document.Bestandsnaam, ".pdf"));
            }
        }

        var context = await Context(db, administratie);
        if (herkenning.Factuur is { } eerst)
        {
            herkenning = herkenning with { Factuur = (await PasZoektemplateToe(db, eerst, Woorden(bytes, tekstlaag), context.Relaties)).Factuur };
        }

        document.Herkend = herkenning.Factuur is null ? null : JsonSerializer.Serialize(herkenning.Factuur, Json);
        document.Status = Documentstatus.TeVerwerken;
        document.Melding = herkenning.Melding;
        db.Documenten.Add(document);
        db.DocumentInhouden.Add(new DocumentInhoud { DocumentId = document.Id, AdministratieId = administratie, Inhoud = bytes, Tekstlaag = tekstlaag });
        await db.SaveChangesAsync(annulering);
        await audit.Log(wie, AuditActies.DocumentIngelezen, naam, null, administratie);

        if (herkenning.Factuur is not { } herkend)
        {
            return new(document.Id, document.Status, $"{naam}: {herkenning.Melding}");
        }

        var voorstel = Voorstel(herkend, context);
        if (voorstel.Automatisch)
        {
            var uitkomst = await BoekFactuur(db, administratie, document, voorstel.Factuur!, herkend, voorstel.Leverancier!.Relatie, nieuw: null, wie,
                automatisch: true, voorstel.Advies.Omschrijving);
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

    /// <summary>
    /// Gaat opnieuw door alle facturen in Te verwerken, met wat het pakket nu weet: nieuwe relaties, regels, wat er
    /// intussen geleerd is en het zoektemplate. Zo gaan na een paar keer zelf boeken de andere facturen van die
    /// leverancier vanzelf. Wat nu automatisch kan, wordt geboekt (de oudste eerst); bij de rest komt de actuele reden.
    /// </summary>
    /// <param name="voortgang">Na elk document: hoeveel er gedaan zijn van hoeveel (voor een stand op het scherm).</param>
    public async Task<Herverwerking> VerwerkOpnieuw(Uitvoerder wie, Guid administratie, IProgress<(int Gedaan, int Totaal)>? voortgang = null, CancellationToken annulering = default)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var documenten = await db.Documenten.Where(d => d.Status == Documentstatus.TeVerwerken && d.Soort == Documentsoort.Factuur)
            .OrderBy(d => d.IngelezenOp).ToListAsync(annulering);
        var context = await Context(db, administratie);
        var (bekeken, geboekt) = (0, 0);
        for (var i = 0; i < documenten.Count; i++)
        {
            var document = documenten[i];
            voortgang?.Report((i, documenten.Count));
            if (Herkend(document) is not { } herkend)
            {
                continue;
            }

            bekeken++;
            (herkend, _) = await PasZoektemplateToe(db, herkend, await Woorden(db, document.Id), context.Relaties);
            document.Herkend = JsonSerializer.Serialize(herkend, Json);
            var voorstel = Voorstel(herkend, context);
            if (voorstel.Automatisch)
            {
                var uitkomst = await BoekFactuur(db, administratie, document, voorstel.Factuur!, herkend, voorstel.Leverancier!.Relatie, nieuw: null, wie,
                    automatisch: true, voorstel.Advies.Omschrijving);
                if (uitkomst.IsGelukt)
                {
                    geboekt++;
                    context = await Context(db, administratie);
                    continue;
                }

                voorstel = voorstel with { Belemmeringen = [.. uitkomst.Fouten] };
            }

            document.Melding = Melding(voorstel);
            await db.SaveChangesAsync(annulering);
        }

        await audit.Log(wie, AuditActies.OpnieuwVerwerkt, "Inkoop", $"{bekeken} bekeken, {geboekt} automatisch geboekt", administratie);
        return new Herverwerking(bekeken, geboekt);
    }

    public async Task<IReadOnlyList<DocumentRegel>> Lijst(Uitvoerder wie, Guid administratie, Documentstatus? status = null)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Documenten.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        // Te verwerken altijd helemaal (niets mag uit beeld raken); van de rest de laatste honderd (de rest staat in het inkoopboek).
        var teVerwerken = status is null or Documentstatus.TeVerwerken
            ? await query.Where(d => d.Status == Documentstatus.TeVerwerken).OrderByDescending(d => d.IngelezenOp).ToListAsync()
            : [];
        var overig = status is Documentstatus.TeVerwerken
            ? []
            : await query.Where(d => d.Status != Documentstatus.TeVerwerken).OrderByDescending(d => d.IngelezenOp).Take(RecentVerwerkt).ToListAsync();
        var documenten = teVerwerken.Concat(overig).Select(Regel).ToList();

        // Bij een geboekt document geldt wat er geboekt is (leverancier, nummer, datum en totaal), niet wat er herkend was.
        var geboekt = documenten.Where(d => d.Status == Documentstatus.Geboekt).Select(d => (Guid?)d.Id).ToList();
        if (geboekt.Count > 0)
        {
            var posten = await db.OpenstaandePosten.AsNoTracking().Where(p => geboekt.Contains(p.DocumentId)).ToListAsync();
            var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);
            for (var i = 0; i < documenten.Count; i++)
            {
                if (posten.FirstOrDefault(p => p.DocumentId == documenten[i].Id) is { } post)
                {
                    documenten[i] = documenten[i] with
                    {
                        Leverancier = namen.GetValueOrDefault(post.Relatie, post.Relatie),
                        Factuurnummer = post.Factuurnummer,
                        Factuurdatum = post.Datum,
                        Totaal = -post.Bedrag,
                    };
                }
            }
        }

        return documenten;
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
        IReadOnlyList<Factuurveld> uitTemplate = [];
        if (herkend is not null && d.Status == Documentstatus.TeVerwerken)
        {
            // Een plek die na het inlezen is aangewezen, telt ook voor facturen die al klaarstaan.
            (herkend, uitTemplate) = await PasZoektemplateToe(db, herkend, await Woorden(db, d.Id), context.Relaties);
        }

        var voorstel = herkend is null ? null : Voorstel(herkend, context);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        return new Verwerking(Regel(d), herkend, voorstel, context.Relaties, schema, context.Btw.Op(herkend?.Factuurdatum ?? DateOnly.MaxValue), context.Btw, uitTemplate);
    }

    /// <summary>Bewaart een document zonder boeking: privé, of geen boeking nodig. Het blijft in te zien en is terug te zetten.</summary>
    public async Task<Uitkomst> Archiveer(Uitvoerder wie, Guid administratie, Guid document, Documentsoort soort, string? notitie)
    {
        if (soort is not (Documentsoort.Prive or Documentsoort.Overig))
        {
            return Uitkomst.Mislukt("Alleen een privé-document of een document zonder boeking wordt zonder boeking bewaard.");
        }

        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Documenten.FirstOrDefaultAsync(d => d.Id == document) is not { Status: Documentstatus.TeVerwerken } d)
        {
            return Uitkomst.Mislukt("Dit document staat niet (meer) in Te verwerken.");
        }

        d.Status = Documentstatus.Gearchiveerd;
        d.Soort = soort;
        d.Melding = string.IsNullOrWhiteSpace(notitie) ? null : Kort(notitie.Trim());
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DocumentGearchiveerd, d.Bestandsnaam, $"{(soort == Documentsoort.Prive ? "privé" : "geen boeking")}{(d.Melding is null ? null : $": {d.Melding}")}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Zet een bewaard document (privé of geen boeking) terug in Te verwerken.</summary>
    public async Task<Uitkomst> Terugzetten(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Documenten.FirstOrDefaultAsync(d => d.Id == document) is not { Status: Documentstatus.Gearchiveerd } d)
        {
            return Uitkomst.Mislukt("Dit document is niet bewaard zonder boeking.");
        }

        d.Status = Documentstatus.TeVerwerken;
        d.Soort = Documentsoort.Factuur;
        d.Melding = null;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DocumentTeruggezet, d.Bestandsnaam, null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Hoe een geboekt document geboekt is (om te tonen of te heropenen), of leeg als het niet geboekt is.</summary>
    public async Task<GeboekteFactuur?> Geboekt(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Geboekt(db, document);
    }

    private static async Task<GeboekteFactuur?> Geboekt(AdministratieDbContext db, Guid document)
    {
        if (await db.Documenten.AsNoTracking().FirstOrDefaultAsync(d => d.Id == document) is not { Status: Documentstatus.Geboekt, BoekingId: { } boekingId } doc
            || await db.OpenstaandePosten.AsNoTracking().FirstOrDefaultAsync(p => p.BoekingId == boekingId) is not { } post)
        {
            return null;
        }

        var boeking = await db.Boekingen.AsNoTracking().Include(b => b.Regels).SingleAsync(b => b.Id == boekingId);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var functies = boeking.Regels.ToDictionary(r => r, r => schema.Zoek(r.Rekeningnummer)?.Functie ?? Rekeningfunctie.Geen);
        var kosten = boeking.Regels.Where(r => functies[r] is not (Rekeningfunctie.Crediteuren or Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw))
            .OrderBy(r => r.Regelnummer).ToList();
        var btw = boeking.Regels.Where(r => functies[r] == Rekeningfunctie.Voorbelasting).ToList();
        // Zoals op het papier: een creditnota positief, een korting op een factuur negatief.
        var teken = post.Bedrag > 0 ? -1 : 1;
        var regels = kosten.Select(r =>
        {
            // De btw per regel staat per btw-code in de boeking; bij één regel met die code is dat de btw van de regel.
            var eigen = kosten.Count(k => k.BtwCode == r.BtwCode) == 1 ? btw.FirstOrDefault(b => b.BtwCode == r.BtwCode)?.Bedrag : null;
            return new Factuurregel(r.Omschrijving == boeking.Omschrijving ? string.Empty : r.Omschrijving ?? string.Empty, r.Rekeningnummer, r.BtwCode ?? "I0",
                teken * r.Bedrag, eigen is { } b ? teken * b : null);
        }).ToList();

        // Met een periodeverdeling staan de kosten op de verdeelrekening; de kostenrekeningen per regel zijn bewaard.
        var verdeling = doc.Verdeling is { } json ? JsonSerializer.Deserialize<Verdelingsgegevens>(json, Json) : null;
        if (verdeling is not null && verdeling.Kostenrekeningen.Count == regels.Count)
        {
            regels = [.. regels.Select((r, i) => r with { Rekeningnummer = verdeling.Kostenrekeningen[i] })];
        }

        return new GeboekteFactuur(post.Relatie, post.Factuurnummer, post.Datum, post.Vervaldatum, boeking.Omschrijving, post.Betalingskenmerk,
            Creditnota: post.Bedrag > 0, Math.Abs(post.Bedrag), regels, boeking.Dagboek, boeking.Boekstuk,
            verdeling is null ? null : new Periodeverdeling(verdeling.Van, verdeling.Tot, verdeling.Rekening), verdeling?.Boekstukken);
    }

    /// <summary>
    /// Draait de boeking van een document terug: een tegenboeking in hetzelfde dagboek (de boeking zelf blijft staan, de
    /// keten ook), de openstaande post vervalt, en het document staat weer in Te verwerken. Niet als de factuur al
    /// (deels) betaald of verrekend is: dan eerst die betaling of verrekening terugdraaien.
    /// </summary>
    public async Task<Uitkomst> Terugdraaien(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Documenten.FirstOrDefaultAsync(d => d.Id == document) is not { Status: Documentstatus.Geboekt, BoekingId: { } boekingId } d)
        {
            return Uitkomst.Mislukt("Dit document is niet geboekt.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var post = await db.OpenstaandePosten.FirstOrDefaultAsync(p => p.BoekingId == boekingId);
        if (post is not null && post.Openstaand != post.Bedrag)
        {
            return Uitkomst.Mislukt($"Factuur {post.Factuurnummer} is al (deels) betaald of verrekend; draai eerst die betaling of verrekening terug.");
        }

        var boeking = await db.Boekingen.AsNoTracking().Include(b => b.Regels).SingleAsync(b => b.Id == boekingId);
        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, boeking.Dagboek, boeking.Datum.Year);
        var tegen = boeking.NaarDomein().Tegenboeking(boekstuk, boeking.Datum, Kort($"Terugdraaien {boeking.Dagboek} {boeking.Boekstuk}: {boeking.Omschrijving}"));
        (await grootboek.VoegToeAanKeten(db, administratie, tegen, wie)).Herstelt = boeking.Id;

        // Een periodeverdeling gaat mee terug: elke maandboeking krijgt een tegenboeking op dezelfde datum.
        if (d.Verdeling is { } json && JsonSerializer.Deserialize<Verdelingsgegevens>(json, Json) is { } verdeling)
        {
            foreach (var stuk in verdeling.Boekstukken)
            {
                await db.SaveChangesAsync();
                if (await GrootboekDienst.GeldendeBoeking(db, stuk) is not { Regels.Count: > 0 } maand)
                {
                    continue;
                }

                var terug = maand.NaarDomein().Tegenboeking(await GrootboekDienst.VolgendBoekstuk(db, maand.Dagboek, maand.Datum.Year), maand.Datum,
                    Kort($"Terugdraaien {stuk}: {maand.Omschrijving}"));
                (await grootboek.VoegToeAanKeten(db, administratie, terug, wie)).Herstelt = maand.Id;
            }

            d.Verdeling = null;
        }
        if (post is not null)
        {
            db.OpenstaandePosten.Remove(post);
        }

        d.Status = Documentstatus.TeVerwerken;
        d.Soort = Documentsoort.Factuur;
        d.BoekingId = null;
        d.Melding = $"Boeking {boeking.Dagboek} {boeking.Boekstuk} is teruggedraaid.";
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.BoekingTeruggedraaid, $"{boeking.Dagboek} {boeking.Boekstuk}", $"tegenboeking {boeking.Dagboek} {boekstuk}; {d.Bestandsnaam}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Verwijdert een document. Is het geboekt, dan wordt eerst de boeking teruggedraaid (<see cref="Terugdraaien"/>):
    /// het journaal wijzigt nooit, dus de boeking en de tegenboeking blijven er samen in staan en heffen elkaar op.
    /// Daarna verdwijnen het document en zijn inhoud uit de administratie.
    /// </summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid administratie, Guid document)
    {
        string? teruggedraaid = null;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            if (await db.Documenten.AsNoTracking().FirstOrDefaultAsync(d => d.Id == document) is not { } d)
            {
                return Uitkomst.Mislukt("Dit document bestaat niet (meer).");
            }

            if (d.Status == Documentstatus.Geboekt)
            {
                var terug = await Terugdraaien(wie, administratie, document);
                if (!terug.IsGelukt)
                {
                    return terug;
                }

                teruggedraaid = "boeking teruggedraaid";
            }
        }

        await using var context = await toegang.Open(wie, administratie, Recht.Invoeren);
        var weg = await context.Documenten.SingleAsync(d => d.Id == document);
        context.DocumentInhouden.RemoveRange(await context.DocumentInhouden.Where(i => i.DocumentId == document).ToListAsync());
        context.Documenten.Remove(weg);
        await context.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DocumentVerwijderd, weg.Bestandsnaam, teruggedraaid, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>De laatste facturen van een leverancier, de nieuwste eerst, met de rekening en btw-code van toen.</summary>
    public async Task<IReadOnlyList<EerdereFactuur>> Historie(Uitvoerder wie, Guid administratie, string relatie, int aantal = 5)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var posten = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == Factuursoort.Inkoop && p.Relatie == relatie)
            .OrderByDescending(p => p.Datum).ThenByDescending(p => p.Factuurnummer).Take(aantal).ToListAsync();
        var boekingen = posten.Select(p => p.BoekingId).ToList();
        var regels = await db.Boekingsregels.AsNoTracking().Where(r => boekingen.Contains(r.BoekingId)).ToListAsync();
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        bool IsKosten(Boekingsregel r) => schema.Zoek(r.Rekeningnummer)?.Functie is not (Rekeningfunctie.Crediteuren or Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw);

        // Met een periodeverdeling staat de factuur op de verdeelrekening; de kostenrekening staat bij het document.
        var documenten = posten.Where(p => p.DocumentId is not null).Select(p => p.DocumentId!.Value).ToList();
        var verdeeld = (await db.Documenten.AsNoTracking().Where(d => documenten.Contains(d.Id) && d.Verdeling != null).Select(d => new { d.Id, d.Verdeling }).ToListAsync())
            .ToDictionary(d => d.Id, d => JsonSerializer.Deserialize<Verdelingsgegevens>(d.Verdeling!, Json)?.Kostenrekeningen.FirstOrDefault());
        string Rekening(OpenstaandePost p, Boekingsregel r) => p.DocumentId is { } doc && verdeeld.GetValueOrDefault(doc) is { } kosten ? kosten : r.Rekeningnummer;
        return
        [
            .. posten.Select(p => (Post: p, Regel: regels.Where(r => r.BoekingId == p.BoekingId && IsKosten(r)).MaxBy(r => Math.Abs(r.Bedrag))))
                .Where(x => x.Regel is not null)
                .Select(x => new EerdereFactuur(x.Post.Datum, x.Post.Factuurnummer, -x.Post.Bedrag, Rekening(x.Post, x.Regel!), x.Regel!.BtwCode, x.Post.Openstaand == 0, x.Post.Id, x.Post.Openstaand)),
        ];
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

        if (d.Status is not (Documentstatus.TeVerwerken or Documentstatus.Geboekt))
        {
            return Uitkomst.Mislukt(d.Status == Documentstatus.Quarantaine ? "Dit document staat in quarantaine." : "Dit document is bewaard zonder boeking.");
        }

        // Een geboekte factuur aanpassen: een nieuwe versie van dezelfde boeking, en de openstaande post gaat mee.
        Boeking? vorige = null;
        OpenstaandePost? vorigePost = null;
        if (d.Status == Documentstatus.Geboekt)
        {
            vorige = d.BoekingId is { } boekingId ? await db.Boekingen.AsNoTracking().Include(b => b.Regels).SingleOrDefaultAsync(b => b.Id == boekingId) : null;
            vorigePost = vorige is null ? null : await db.OpenstaandePosten.FirstOrDefaultAsync(p => p.BoekingId == vorige.Id);
            if (vorige is null || vorigePost is null)
            {
                return Uitkomst.Mislukt("De boeking van dit document is niet aan te passen.");
            }

            if (vorigePost.Openstaand != vorigePost.Bedrag)
            {
                return Uitkomst.Mislukt($"Factuur {vorigePost.Factuurnummer} is al (deels) betaald of verrekend; draai eerst die betaling of verrekening terug.");
            }
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
        var herkend = Herkend(d);
        var naam = relatie?.Naam ?? invoer.Nieuw!.Naam.Trim();
        var kenmerk = string.IsNullOrWhiteSpace(invoer.Betalingskenmerk) ? null : invoer.Betalingskenmerk.Trim();
        var variabelen = new Factuurvariabelen(invoer.Factuurnummer.Trim(), herkend?.Omschrijving, naam, invoer.Datum, invoer.Vervaldatum, kenmerk);
        var sjabloon = Omschrijvingssjabloon.IsSjabloon(invoer.Omschrijving) ? invoer.Omschrijving.Trim() : null;
        var omschrijving = string.IsNullOrWhiteSpace(invoer.Omschrijving) ? $"{naam} {invoer.Factuurnummer}".Trim() : Omschrijvingssjabloon.Vul(invoer.Omschrijving, variabelen);
        // Bedragen zoals op het papier: een negatieve regel is een korting (op een creditnota: een toeslag). Een creditnota
        // waarvan de regels al negatief zijn ingevoerd, staat al goed.
        var teken = invoer.Creditnota && invoer.Regels.Sum(r => r.Bedrag) > 0 ? -1 : 1;
        var factuur = new Factuur(Factuursoort.Inkoop, code, invoer.Factuurnummer.Trim(), invoer.Datum, invoer.Vervaldatum, Kort(omschrijving),
            [.. invoer.Regels.Select(r => r with
            {
                Omschrijving = Kort(string.IsNullOrWhiteSpace(r.Omschrijving) ? omschrijving : Omschrijvingssjabloon.Vul(r.Omschrijving, variabelen)),
                Bedrag = teken * r.Bedrag,
                Btw = r.Btw is { } btw ? teken * btw : null,
            })],
            kenmerk);
        d.Soort = invoer.Creditnota ? Documentsoort.Creditnota : Documentsoort.Factuur;
        var uitkomst = await BoekFactuur(db, administratie, d, factuur, herkend, relatie, invoer.Nieuw, wie, automatisch: false, sjabloon, invoer.VerrekenMet, invoer.Verdeling, vorige, vorigePost);
        if (uitkomst.IsGelukt && invoer.Aanwijzingen is { Count: > 0 } aanwijzingen)
        {
            await BewaarZoektemplate(db, administratie, code, aanwijzingen, herkend, wie);
        }

        if (uitkomst.IsGelukt && herkend is { Bron: not Herkenningsbron.EFactuur })
        {
            await LeerPlekken(db, administratie, code, d.Id, invoer, herkend, wie);
        }

        return uitkomst;
    }

    /// <summary>
    /// Leert na het boeken zelf waar de waarden staan, zonder dat de gebruiker ze aanwijst: elke geboekte waarde die op
    /// precies één plek op de PDF staat (factuurnummer, datums, kenmerk, totaal; bij één regel ook exclusief en btw),
    /// wordt een plek in het zoektemplate van de leverancier. Een bestaande plek blijft, tenzij de herkenning (met het
    /// template) iets anders gaf dan er geboekt is: dan verhuist hij naar de plek van de geboekte waarde en gaat hij
    /// voortaan voor. Wat de gebruiker deze keer aanwees, gaat altijd voor.
    /// </summary>
    private async Task LeerPlekken(AdministratieDbContext db, Guid administratie, string relatie, Guid document, Factuurinvoer invoer, HerkendeFactuur herkend, Uitvoerder wie)
    {
        if (await Woorden(db, document) is not { Count: > 0 } woorden)
        {
            return;
        }

        var aangewezen = (invoer.Aanwijzingen ?? []).Select(a => a.Veld).ToHashSet();
        var totaal = Math.Abs(invoer.Regels.Sum(r => r.Bedrag + (r.Btw ?? 0)));
        var enige = invoer.Regels.Count == 1 ? invoer.Regels[0] : null;
        decimal? exclusief = enige is null ? null : Math.Abs(enige.Bedrag);
        decimal? btw = enige?.Btw is { } b ? Math.Abs(b) : null;
        var velden = new List<(Factuurveld Veld, IReadOnlyList<string> Vormen, bool Gelijk, bool Bedrag)>
        {
            (Factuurveld.Factuurnummer, Tekstvormen.Tekst(invoer.Factuurnummer), herkend.Factuurnummer is { } n && Factuurnummers.Normaliseer(n) == Factuurnummers.Normaliseer(invoer.Factuurnummer), false),
            (Factuurveld.Factuurdatum, Tekstvormen.Datum(invoer.Datum), herkend.Factuurdatum == invoer.Datum, false),
            (Factuurveld.Vervaldatum, herkend.Vervaldatum is null ? [] : Tekstvormen.Datum(invoer.Vervaldatum), herkend.Vervaldatum == invoer.Vervaldatum, false),
            (Factuurveld.Betalingskenmerk, Tekstvormen.Tekst(invoer.Betalingskenmerk), herkend.Betalingskenmerk == invoer.Betalingskenmerk?.Replace(" ", "", StringComparison.Ordinal), false),
            (Factuurveld.Totaal, Tekstvormen.Bedrag(totaal), herkend.TotaalInclusief is { } i && Math.Abs(i) == totaal, true),
            (Factuurveld.Exclusief, Tekstvormen.Bedrag(exclusief), herkend.TotaalExclusief is { } e && Math.Abs(e) == exclusief, true),
            (Factuurveld.Btw, btw is > 0 ? Tekstvormen.Bedrag(btw) : [], herkend.Btwregels.Count > 0 && Math.Abs(herkend.TotaalBtw) == btw, true),
        };
        velden.RemoveAll(v => aangewezen.Contains(v.Veld) || v.Vormen.Count == 0);
        if (velden.Count == 0)
        {
            return;
        }

        var plekken = PdfTekst.Zoek(woorden, velden.ToDictionary(v => v.Veld.ToString(), v => v.Vormen));

        var bestaand = await db.Zoekplekken.Where(z => z.Relatie == relatie).ToListAsync();
        var geleerd = new List<Factuurveld>();
        foreach (var (veld, _, gelijk, bedrag) in velden)
        {
            var plek = bestaand.FirstOrDefault(z => z.Veld == veld);
            if (plekken[veld.ToString()] is not [var gevonden] || (plek is not null && gelijk))
            {
                continue;
            }

            if (plek is null)
            {
                plek = new Zoekplek { AdministratieId = administratie, Relatie = relatie, Veld = veld };
                db.Zoekplekken.Add(plek);
            }

            // Wat ruimte rondom: een volgend nummer of bedrag kan langer zijn. Bedragen staan meestal rechts uitgelijnd
            // (ze groeien naar links), nummers en datums links uitgelijnd (ze groeien naar rechts).
            var (x0, x1) = (Math.Min(gevonden.X0, gevonden.X1), Math.Max(gevonden.X0, gevonden.X1));
            var (y0, y1) = (Math.Min(gevonden.Y0, gevonden.Y1), Math.Max(gevonden.Y0, gevonden.Y1));
            (plek.Pagina, plek.X0, plek.Y0, plek.X1, plek.Y1) = bedrag
                ? (gevonden.Pagina, x0 - 25, y0 - 1.5, x1 + 4, y1 + 1.5)
                : (gevonden.Pagina, x0 - 2, y0 - 1.5, x1 + 15, y1 + 1.5);
            (plek.Voorrang, plek.Tijdstip) = (!gelijk && Heeft(herkend, veld), tijd.GetUtcNow());
            geleerd.Add(veld);
        }

        if (geleerd.Count > 0)
        {
            await db.SaveChangesAsync();
            await audit.Log(wie, AuditActies.ZoektemplateBijgewerkt, relatie, $"zelf geleerd: {string.Join(", ", geleerd)}", administratie);
        }
    }

    /// <summary>Vond de herkenning dit veld (dan was hij fout, als hij afwijkt van wat er geboekt is)?</summary>
    private static bool Heeft(HerkendeFactuur h, Factuurveld veld) => veld switch
    {
        Factuurveld.Factuurnummer => h.Factuurnummer is not null,
        Factuurveld.Factuurdatum => h.Factuurdatum is not null,
        Factuurveld.Vervaldatum => h.Vervaldatum is not null,
        Factuurveld.Betalingskenmerk => h.Betalingskenmerk is not null,
        Factuurveld.Totaal => h.TotaalInclusief is not null,
        Factuurveld.Exclusief => h.TotaalExclusief is not null,
        Factuurveld.Btw => h.Btwregels.Count > 0,
        _ => h.Omschrijving is not null,
    };

    /// <summary>
    /// De tekst op de plekken die de gebruiker eerder bij deze leverancier aanwees, vult aan wat de herkenning mist (of
    /// gaat voor, als de gebruiker de herkenning toen corrigeerde). Alleen bij een PDF van een herkende leverancier.
    /// </summary>
    private static async Task<(HerkendeFactuur Factuur, IReadOnlyList<Factuurveld> Ingevuld)> PasZoektemplateToe(
        AdministratieDbContext db, HerkendeFactuur herkend, IReadOnlyList<Tekstwoord>? woorden, IReadOnlyList<Relatie> relaties)
    {
        if (woorden is not { Count: > 0 } || Leverancierherkenning.Zoek(herkend, relaties) is not { } leverancier)
        {
            return (herkend, []);
        }

        var plekken = await db.Zoekplekken.AsNoTracking().Where(z => z.Relatie == leverancier.Relatie.Code).ToListAsync();
        if (plekken.Count == 0)
        {
            return (herkend, []);
        }

        // Een pagina die deze factuur niet heeft, levert geen tekst op (en dus niets in te vullen).
        return Zoektemplate.Pas(herkend, plekken.Select(p => (p.NaarDomein(), string.Join(" ", PdfTekst.InGebied(woorden, p.Pagina, p.X0, p.Y0, p.X1, p.Y1)))));
    }

    /// <summary>
    /// Bewaart per veld de laatst aangewezen plek bij deze leverancier. Vond de herkenning dat veld wel (maar wees de
    /// gebruiker het toch aan), dan gaat de plek voortaan voor.
    /// </summary>
    private async Task BewaarZoektemplate(AdministratieDbContext db, Guid administratie, string relatie, IReadOnlyList<Zoekgebied> aanwijzingen, HerkendeFactuur? herkend, Uitvoerder wie)
    {
        var bestaand = await db.Zoekplekken.Where(z => z.Relatie == relatie).ToListAsync();
        foreach (var aanwijzing in aanwijzingen.GroupBy(a => a.Veld).Select(g => g.Last()))
        {
            var voorrang = aanwijzing.Veld switch
            {
                Factuurveld.Factuurnummer => herkend?.Factuurnummer is not null,
                Factuurveld.Factuurdatum => herkend?.Factuurdatum is not null,
                Factuurveld.Vervaldatum => herkend?.Vervaldatum is not null,
                Factuurveld.Betalingskenmerk => herkend?.Betalingskenmerk is not null,
                Factuurveld.Totaal => herkend?.TotaalInclusief is not null,
                Factuurveld.Exclusief => herkend?.TotaalExclusief is not null,
                Factuurveld.Btw => herkend?.Btwregels.Count > 0,
                _ => herkend?.Omschrijving is not null,
            };
            var plek = bestaand.FirstOrDefault(z => z.Veld == aanwijzing.Veld);
            if (plek is null)
            {
                plek = new Zoekplek { AdministratieId = administratie, Relatie = relatie, Veld = aanwijzing.Veld };
                db.Zoekplekken.Add(plek);
            }

            (plek.Pagina, plek.X0, plek.Y0, plek.X1, plek.Y1, plek.Voorrang, plek.Tijdstip) =
                (aanwijzing.Pagina, aanwijzing.X0, aanwijzing.Y0, aanwijzing.X1, aanwijzing.Y1, voorrang, tijd.GetUtcNow());
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ZoektemplateBijgewerkt, relatie, string.Join(", ", aanwijzingen.Select(a => a.Veld).Distinct()), administratie);
    }

    /// <summary>De inhoud van een document, om te bekijken of te downloaden.</summary>
    public async Task<(byte[] Inhoud, string Inhoudstype, string Bestandsnaam)?> Inhoud(Uitvoerder wie, Guid administratie, Guid document)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var d = await db.Documenten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == document);
        var inhoud = await db.DocumentInhouden.AsNoTracking().FirstOrDefaultAsync(x => x.DocumentId == document);
        return d is null || inhoud is null ? null : (inhoud.Inhoud, d.Inhoudstype, d.Bestandsnaam);
    }

    /// <summary>
    /// De tekst in een aangewezen gebied van een PDF (in PDF-punten), om een omschrijving, bedrag of nummer over te
    /// nemen. Leeg als het document geen PDF is of er in dat gebied niets staat.
    /// </summary>
    public async Task<IReadOnlyList<string>> TekstInGebied(Uitvoerder wie, Guid administratie, Guid document, int pagina, double x0, double y0, double x1, double y1)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Woorden(db, document) is { } woorden ? PdfTekst.InGebied(woorden, pagina, x0, y0, x1, y1) : [];
    }

    /// <summary>
    /// De woorden van een document met hun plek: van de OCR bij een scan of foto, anders uit de PDF. Leeg als het
    /// document geen PDF is of niet te lezen.
    /// </summary>
    private static async Task<IReadOnlyList<Tekstwoord>?> Woorden(AdministratieDbContext db, Guid document)
    {
        var inhoud = await db.DocumentInhouden.AsNoTracking().Where(x => x.DocumentId == document).Select(x => new { x.Inhoud, x.Tekstlaag }).FirstOrDefaultAsync();
        return inhoud is null ? null : Woorden(inhoud.Inhoud, inhoud.Tekstlaag);
    }

    private static IReadOnlyList<Tekstwoord>? Woorden(byte[] inhoud, string? tekstlaag)
    {
        if (tekstlaag is not null)
        {
            return JsonSerializer.Deserialize<List<Tekstwoord>>(tekstlaag, Json);
        }

        try
        {
            return Bestandssoort.Extensie(inhoud) == ".pdf" ? PdfTekst.Woorden(inhoud) : null;
        }
        catch (DocumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waar de waarden van de velden op de factuur staan (per veld de vormen waarin de waarde kan staan), om ze op te
    /// laten lichten in de viewer. Leeg bij een document dat geen PDF is.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PdfPlek>>> Vindplaatsen(
        Uitvoerder wie, Guid administratie, Guid document, IReadOnlyDictionary<string, IReadOnlyList<string>> velden)
    {
        ArgumentNullException.ThrowIfNull(velden);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Woorden(db, document) is { } woorden
            ? PdfTekst.Zoek(woorden, velden)
            : velden.ToDictionary(v => v.Key, _ => (IReadOnlyList<PdfPlek>)[]);
    }

    /// <summary>Het memoriaal van het standaardschema; daar komt een periodeverdeling in (ook als er meer memorialen zijn).</summary>
    private static readonly string StandaardMemoriaal = StandaardRekeningschema.Dagboeken.First(d => d.Soort == Dagboeksoort.Memoriaal).Code;

    private async Task<Uitkomst> BoekFactuur(
        AdministratieDbContext db, Guid administratie, Document document, Factuur factuur, HerkendeFactuur? herkend,
        Relatie? relatie, NieuweRelatie? nieuw, Uitvoerder wie, bool automatisch, string? sjabloon, Guid? verrekenMet = null, Periodeverdeling? verdeling = null,
        Boeking? vorige = null, OpenstaandePost? vorigePost = null)
    {
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        var dagboek = dagboeken.FirstOrDefault(x => x.Soort == Dagboeksoort.Inkoop);
        if (dagboek is null)
        {
            return Uitkomst.Mislukt("Deze administratie heeft geen inkoopboek.");
        }

        // Transitorisch: de kosten eerst op de verdeelrekening (balans), daarna per maand in het memoriaal naar de kosten.
        var memoriaal = dagboeken.Where(x => x.Soort == Dagboeksoort.Memoriaal).OrderBy(x => x.Code != StandaardMemoriaal).FirstOrDefault();
        if (verdeling is not null)
        {
            var verdeelfouten = verdeling.Controleer().ToList();
            if (schema.Zoek(verdeling.Rekening) is not { IsBalans: true })
            {
                verdeelfouten.Add($"Rekening {verdeling.Rekening} is geen balansrekening; kies bijv. vooruitbetaalde kosten.");
            }

            if (memoriaal is null)
            {
                verdeelfouten.Add("Deze administratie heeft geen memoriaal voor de verdeling.");
            }

            if (verdeelfouten.Count > 0)
            {
                return Uitkomst.Mislukt([.. verdeelfouten]);
            }
        }

        // De btw-codes met de tarieven op de factuurdatum (een nieuw tarief geldt vanaf zijn ingangsdatum).
        var codes = (await BtwDienst.Lees(db)).Op(factuur.Datum);
        var fouten = Factuurboeker.Controleer(factuur, dagboek, codes).ToList();
        var nummer = Factuurnummers.Normaliseer(factuur.Factuurnummer);
        var zelf = vorigePost?.Id;
        var (vorigeRelatie, vorigBedrag) = (vorigePost?.Relatie, vorigePost?.Bedrag);
        var eerder = await db.OpenstaandePosten.AsNoTracking()
            .Where(p => p.Soort == Factuursoort.Inkoop && p.Relatie == factuur.Relatie && p.Id != zelf).Select(p => p.Factuurnummer).ToListAsync();
        if (nummer.Length > 0 && eerder.Any(f => Factuurnummers.Normaliseer(f) == nummer))
        {
            fouten.Add($"Factuur {factuur.Factuurnummer} van deze leverancier is al geboekt.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
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

        var boekstuk = vorige?.Boekstuk ?? await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, factuur.Datum.Year);
        var oudeVerdeling = document.Verdeling is { } oudeJson ? JsonSerializer.Deserialize<Verdelingsgegevens>(oudeJson, Json) : null;
        Journaalpost post;
        OpenPost open;
        try
        {
            var teBoeken = verdeling is null ? factuur : factuur with { Regels = [.. factuur.Regels.Select(r => r with { Rekeningnummer = verdeling.Rekening })] };
            (post, open) = Factuurboeker.Boek(teBoeken, dagboek, boekstuk, schema, codes);
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

        var boeking = vorige is null
            ? await grootboek.VoegToeAanKeten(db, administratie, post, wie)
            : await grootboek.NieuweVersie(db, administratie, vorige, post, wie);
        var oudeStukken = oudeVerdeling?.Boekstukken ?? [];
        if (verdeling is not null)
        {
            var verdeeld = await grootboek.BoekVerdeling(db, administratie, verdeling, memoriaal!, factuur, [.. factuur.Regels.Select(r => new Verdeelregel(r.Rekeningnummer, r.Bedrag, r.Omschrijving))], schema, dagboeken, wie, oudeStukken);
            if (!verdeeld.IsGelukt)
            {
                return Uitkomst.Mislukt([.. verdeeld.Fouten]);
            }

            document.Verdeling = JsonSerializer.Serialize(new Verdelingsgegevens(verdeling.Van, verdeling.Tot, verdeling.Rekening,
                [.. factuur.Regels.Select(r => r.Rekeningnummer)], [.. verdeeld.Waarde!]), Json);
        }
        else if (oudeStukken.Count > 0)
        {
            // Niet meer verdeeld: de maandboekingen van toen vervallen (een lege nieuwe versie).
            await grootboek.LaatVervallen(db, administratie, oudeStukken, wie);
            document.Verdeling = null;
        }

        var credit = open.Openstaand > 0;
        string? verrekend = null;
        if (verrekenMet is { } metId)
        {
            // Een creditnota verrekenen met een openstaande factuur van dezelfde leverancier: beide posten gaan omlaag.
            var met = await db.OpenstaandePosten.FirstOrDefaultAsync(p => p.Id == metId);
            if (!credit || met is null || met.Soort != Factuursoort.Inkoop || met.Relatie != factuur.Relatie || met.Openstaand >= 0)
            {
                return Uitkomst.Mislukt("Een creditnota is alleen te verrekenen met een openstaande factuur van dezelfde leverancier.");
            }

            var deel = Math.Min(open.Openstaand, -met.Openstaand);
            met.Openstaand += deel;
            open = open with { Openstaand = open.Openstaand - deel };
            verrekend = $"{met.Factuurnummer} ({deel:0.00})";
        }

        if (vorigePost is null)
        {
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
        }
        else
        {
            // Dezelfde openstaande post (met zijn koppelingen), met de gegevens van de aangepaste factuur.
            (vorigePost.Relatie, vorigePost.Factuurnummer, vorigePost.Datum, vorigePost.Vervaldatum) = (open.Relatie, open.Factuurnummer, open.Datum, open.Vervaldatum);
            (vorigePost.Bedrag, vorigePost.Openstaand, vorigePost.Betalingskenmerk, vorigePost.BoekingId) = (open.Bedrag, open.Openstaand, open.Betalingskenmerk, boeking.Id);
        }

        document.Status = Documentstatus.Geboekt;
        document.BoekingId = boeking.Id;
        document.Melding = automatisch ? "Automatisch geboekt." : null;

        // Leren: de keuze van de gebruiker (of het automatisch gevolgde advies) onder de sleutel van de leverancier, met
        // het bedrag, het IBAN en de postcodes van deze factuur; daaraan herkent het pakket later een afwijking. Een
        // creditnota leert niets: die zegt niets over de volgende factuur.
        var gegevens = (herkend is null
            ? new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, factuur.Relatie, relatie?.Naam ?? nieuw?.Naam, 0m)
            : Inkoopverwerker.Gegevens(herkend, relatie ?? new Relatie(factuur.Relatie, nieuw!.Naam, []))) with
        { Relatie = factuur.Relatie, Bedrag = factuur.Totaal(codes) };
        if (vorigBedrag < 0 && Leergeheugen.Sleutel(new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, vorigeRelatie, null, 0m)) is { } vorigeSleutel)
        {
            // Aangepast: de bevestiging van toen wordt vervangen door deze, zodat de factuur maar één keer meetelt.
            var oudBedrag = -vorigBedrag.Value;
            if (await db.Leerkeuzes.Where(k => k.Sleutel == vorigeSleutel && k.Bedrag == oudBedrag).OrderByDescending(k => k.Id).FirstOrDefaultAsync() is { } oudeKeuze)
            {
                db.Leerkeuzes.Remove(oudeKeuze);
            }
        }

        if (!credit && Leergeheugen.Sleutel(gegevens) is { } sleutel)
        {
            var bevestiging = Leergeheugen.Bij(gegevens, new Keuze(factuur.Regels[0].Rekeningnummer, factuur.Regels[0].BtwCode), sjabloon);
            db.Leerkeuzes.Add(new Leerkeuze
            {
                AdministratieId = administratie,
                Sleutel = sleutel,
                Rekeningnummer = bevestiging.Keuze.Rekeningnummer,
                BtwCode = bevestiging.Keuze.BtwCode,
                Bedrag = bevestiging.Bedrag,
                Iban = bevestiging.Iban,
                Postcodes = bevestiging.Postcodes is { Count: > 0 } p ? [.. p] : null,
                Omschrijving = bevestiging.Omschrijving,
                Tijdstip = tijd.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        if (vorige is not null)
        {
            await audit.Log(wie, AuditActies.BoekingAangepast, $"{dagboek.Code} {boekstuk}",
                $"versie {boeking.Versie}; {(credit ? "creditnota " : null)}{factuur.Relatie} {factuur.Factuurnummer}; {factuur.Totaal(codes):0.00}", administratie);
            return Uitkomst.Gelukt;
        }

        await audit.Log(wie, automatisch ? AuditActies.AutomatischGeboekt : AuditActies.Geboekt,
            $"{dagboek.Code} {boekstuk}", $"{(credit ? "creditnota " : null)}{factuur.Relatie} {factuur.Factuurnummer}; {factuur.Totaal(codes):0.00}", administratie);
        if (verrekend is not null)
        {
            await audit.Log(wie, AuditActies.CreditnotaVerrekend, $"{factuur.Relatie} {factuur.Factuurnummer}", $"met {verrekend}", administratie);
        }
        await ZetInDocumentmap(db, administratie, document, factuur, relatie?.Naam ?? nieuw?.Naam, wie);
        await bank.NaFactuur(wie, administratie, open.Id, document.BanktransactieId);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Een kopie van het geboekte document in de documentmap van de administratie (bijv. OneDrive per klant en
    /// boekjaar). Het origineel blijft in de database; lukt de kopie niet, dan blijft de boeking staan en komt de reden
    /// in het log en bij het document.
    /// </summary>
    private async Task ZetInDocumentmap(AdministratieDbContext db, Guid administratie, Document document, Factuur factuur, string? relatie, Uitvoerder wie)
    {
        var adm = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        if (adm.Afwijkend().Op(await KantoorDienst.Lees(db)).Documentmap is not { Length: > 0 } sjabloon)
        {
            return;
        }

        var gegevens = new Documentgegevens(adm.Naam, "Inkoop", factuur.Datum, relatie, factuur.Factuurnummer, Path.GetExtension(document.Bestandsnaam));
        string? pad = null;
        try
        {
            var inhoud = await db.DocumentInhouden.AsNoTracking().Where(i => i.DocumentId == document.Id).Select(i => i.Inhoud).SingleAsync();
            var map = Documentpad.Map(sjabloon, gegevens);
            Directory.CreateDirectory(map);
            pad = Path.Combine(map, Documentpad.Bestandsnaam(gegevens));
            pad = await SchrijfNieuw(pad, inhoud);
            await audit.Log(wie, AuditActies.DocumentOpgeslagen, document.Bestandsnaam, pad, administratie);
        }
        catch (Exception fout) when (fout is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            document.Melding = $"Geboekt, maar niet in de documentmap gezet: {fout.Message}";
            db.Documenten.Update(document);
            await db.SaveChangesAsync();
            await audit.Log(wie, AuditActies.DocumentNietOpgeslagen, document.Bestandsnaam, $"{pad ?? sjabloon}: {fout.Message}", administratie);
        }
    }

    /// <summary>Schrijft het bestand zonder iets te overschrijven: staat er al een ander bestand, dan "naam (2).pdf".</summary>
    private static async Task<string> SchrijfNieuw(string pad, byte[] inhoud)
    {
        var map = Path.GetDirectoryName(pad)!;
        var naam = Path.GetFileNameWithoutExtension(pad);
        var extensie = Path.GetExtension(pad);
        for (var n = 1; ; n++)
        {
            var poging = n == 1 ? pad : Path.Combine(map, $"{naam} ({n}){extensie}");
            if (File.Exists(poging))
            {
                if ((await File.ReadAllBytesAsync(poging)).AsSpan().SequenceEqual(inhoud))
                {
                    return poging;
                }

                continue;
            }

            await using var bestand = new FileStream(poging, FileMode.CreateNew, FileAccess.Write);
            await bestand.WriteAsync(inhoud);
            return poging;
        }
    }

    private sealed record Inkoopcontext(
        Data.Administratie Administratie,
        Verwerkingsinstellingen Instellingen,
        IReadOnlyList<Relatie> Relaties,
        IReadOnlyList<Verwerkingsregel> Regels,
        Leergeheugen Geheugen,
        IReadOnlyList<BestaandeFactuur> Bestaand,
        Btwtabel Btw);

    private static async Task<Inkoopcontext> Context(AdministratieDbContext db, Guid administratie)
    {
        var gegevens = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        var relaties = (await db.Relaties.AsNoTracking().OrderBy(r => r.Naam).ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var regels = (await db.Regels.AsNoTracking().OrderBy(r => r.Volgorde).ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var instellingen = gegevens.Afwijkend().Op(await KantoorDienst.Lees(db));
        var geheugen = new Leergeheugen(instellingen);
        foreach (var keuze in await db.Leerkeuzes.AsNoTracking().OrderBy(k => k.Id).ToListAsync())
        {
            geheugen.Onthoud(keuze.Sleutel, keuze.NaarDomein());
        }

        var bestaand = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == Factuursoort.Inkoop)
            .Select(p => new BestaandeFactuur(p.Relatie, p.Factuurnummer, p.Datum, -p.Bedrag, p.Openstaand == 0)).ToListAsync();
        return new Inkoopcontext(gegevens, instellingen, relaties, regels, geheugen, bestaand, await BtwDienst.Lees(db));
    }

    private static Inkoopvoorstel Voorstel(HerkendeFactuur herkend, Inkoopcontext context) =>
        Inkoopverwerker.Verwerk(herkend, context.Relaties, context.Regels, context.Geheugen, context.Btw.Op(herkend.Factuurdatum ?? DateOnly.MaxValue), context.Bestaand,
            context.Instellingen.Betalingstermijn, periodeOpen: null, context.Instellingen.Limiet);

    private static string Kort(string tekst) => tekst.Length > 200 ? tekst[..200] : tekst;

    private static string Melding(Inkoopvoorstel voorstel) =>
        voorstel.Belemmeringen.Count > 0 ? string.Join(" ", voorstel.Belemmeringen) : voorstel.Advies.Reden;

    private static HerkendeFactuur? Herkend(Document document) =>
        document.Herkend is null ? null : JsonSerializer.Deserialize<HerkendeFactuur>(document.Herkend, Json);

    private static DocumentRegel Regel(Document d)
    {
        var herkend = Herkend(d);
        return new DocumentRegel(d.Id, d.Bestandsnaam, d.Status, d.Soort, herkend?.Naam, herkend?.Factuurnummer, herkend?.Factuurdatum, herkend?.TotaalInclusief, d.Melding, d.IngelezenOp, d.IngelezenDoor);
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
