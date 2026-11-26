using System.Security.Cryptography;
using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat er met een ingelezen bankbestand gebeurd is.</summary>
/// <param name="Dubbel">Mutaties die al eerder waren ingelezen (en dus overgeslagen).</param>
/// <param name="MogelijkDubbel">
/// Nieuwe mutaties met dezelfde datum en hetzelfde bedrag als een eerder ingelezen mutatie (een overlappend bestand
/// met een andere opmaak): ingelezen, maar niet automatisch verwerkt.
/// </param>
/// <param name="Meldingen">Bijv. dat een afschrift niet aansluit op het vorige.</param>
public sealed record Bankinlezing(int Afschriften, int Nieuw, int Dubbel, int Automatisch, IReadOnlyList<string> Meldingen, int MogelijkDubbel = 0);

/// <summary>Wat een nieuwe ronde automatisch verwerken opleverde: hoeveel er bekeken en hoeveel er geboekt zijn.</summary>
public sealed record Herverwerking(int Bekeken, int Geboekt);

/// <summary>
/// Een deel van een bankmutatie: op een grootboekrekening, of met <paramref name="Post"/> op een openstaande post (dan
/// telt de rekening niet). Het bedrag inclusief btw, zoals op het afschrift (zonder teken).
/// </summary>
/// <param name="Btw">Het btw-bedrag met de hand (zonder teken); leeg = volgens het percentage van de btw-code.</param>
public sealed record Bankregelinvoer(string Rekening, string? BtwCode, decimal Bedrag, string? Omschrijving = null, Guid? Post = null, decimal? Btw = null, string? Kostenplaats = null);

/// <summary>Het verschil bij afletteren op een rekening boeken in plaats van open te laten staan (bijv. betalingskorting).</summary>
public sealed record Verschilafboeking(string Rekening, string? BtwCode, string? Omschrijving = null);

/// <summary>De stand van één bankrekening: het laatste afschrift en wat er nog te verwerken is.</summary>
/// <param name="Iban">Leeg zolang er nog geen afschrift is en er bij het toevoegen geen IBAN is opgegeven.</param>
/// <param name="Rekening">De grootboekrekening van de bank.</param>
/// <param name="Van">De begindatum van het eerste afschrift (van het jaar van het laatste afschrift).</param>
/// <param name="Beginsaldo">Het beginsaldo op dat eerste afschrift.</param>
/// <param name="BeginGrootboek">Het saldo van de bank in het grootboek vóór die datum (de beginstand): hoort gelijk te zijn aan het beginsaldo.</param>
/// <param name="Grootboek">Het saldo van de bank in het grootboek tot en met de datum van het laatste afschrift.</param>
public sealed record Bankstand(string Dagboek, string Naam, string? Iban, DateOnly? Tot, decimal? Saldo, int TeVerwerken,
    string? Rekening = null, DateOnly? Van = null, decimal? Beginsaldo = null, decimal? BeginGrootboek = null, decimal? Grootboek = null)
{
    /// <summary>Beginstand in het grootboek min het beginsaldo van het eerste afschrift.</summary>
    public decimal? Beginverschil => Beginsaldo is { } b && BeginGrootboek is { } g ? g - b : null;

    /// <summary>Het grootboek min het eindsaldo van het laatste afschrift.</summary>
    public decimal? Verschil => Saldo is { } s && Grootboek is { } g ? g - s : null;
}

/// <summary>Een bankmutatie in de lijst.</summary>
public sealed record BankmutatieRegel(
    Guid Id,
    string Dagboek,
    DateOnly Datum,
    decimal Bedrag,
    string Omschrijving,
    string? TegenNaam,
    string? TegenIban,
    Banktransactiestatus Status,
    string? Melding,
    string? Betalingskenmerk = null,
    string? EndToEndId = null,
    bool MogelijkDubbel = false,
    Verwerkingswijze Verwerking = Verwerkingswijze.Onbekend);

/// <summary>Een openstaande post in de werklijst, met de naam van de relatie en het document.</summary>
/// <param name="Openstaand">Zoals in het grootboek: te ontvangen positief, te betalen negatief.</param>
public sealed record OpenpostRegel(
    Guid Id,
    Factuursoort Soort,
    string Relatie,
    string Naam,
    string Factuurnummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    decimal Bedrag,
    decimal Openstaand,
    Guid? DocumentId)
{
    /// <summary>Al deels betaald (of verrekend): het verschil staat nog open.</summary>
    public bool Deels => Openstaand != Bedrag;
}

/// <summary>Alles wat het scherm nodig heeft om een bankmutatie te verwerken.</summary>
/// <param name="OpenPosten">De openstaande posten met hetzelfde teken als de mutatie, om zelf te kiezen.</param>
public sealed record Bankverwerking(
    BankmutatieRegel Mutatie,
    Bankvoorstel Voorstel,
    IReadOnlyList<OpenPost> OpenPosten,
    IReadOnlyList<Relatie> Relaties,
    Rekeningschema Schema,
    IReadOnlyList<BtwCode> BtwCodes,
    Banksaldo? Saldo = null,
    Regeltreffer? Regel = null);

/// <summary>
/// De regel die bij een mutatie past (<see cref="Past"/>), of anders de eerste die bijna past (alles behalve het
/// bedrag): om die regel vanaf de mutatie aan te passen.
/// </summary>
public sealed record Regeltreffer(Guid Id, Verwerkingsregel Regel, bool Past);

/// <summary>
/// De stand van de bankrekening bij een mutatie: het saldo in het grootboek aan het eind van het jaar, en het saldo op
/// het afschrift voor en na deze mutatie.
/// </summary>
public sealed record Banksaldo(string Rekening, string Naam, int Jaar, decimal Eindsaldo, decimal VoorBoeking, decimal NaBoeking);

/// <summary>Een PDF-afschrift omgezet naar MT940: het bestand en wat erin staat.</summary>
public sealed record Pdfomzetting(byte[] Mt940, string Bestandsnaam, Bankafschrift Afschrift);

/// <summary>
/// De bank: afschriften inlezen (MT940 of CAMT.053) met saldocontrole, aansluiting en zonder dubbele mutaties; dan
/// per mutatie afletteren of op een rekening boeken, automatisch als het zeker is (<see cref="Bankverwerker"/>),
/// anders door de gebruiker. Elke boeking zet de journaalpost in de keten en werkt de openstaande posten bij, in één
/// transactie.
/// </summary>
/// <summary>Meer mutaties tegelijk geboekt: hoeveel, welke niet en waarom.</summary>
public sealed record Bulkboeking(int Geboekt, IReadOnlyList<Guid> Mislukt, IReadOnlyList<string> Redenen);

public sealed class BankDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd, IEnumerable<Documenten.IVirusscanner> virusscanners)
{
    /// <summary>Groter is geen bankafschrift.</summary>
    public const int MaximumBytes = 20 * 1024 * 1024;


    /// <summary>
    /// Leest een bankbestand in. Een afschrift waarvan de mutaties niet op het eindsaldo uitkomen, weigert het pakket
    /// (het hele bestand). Mutaties die al zijn ingelezen, worden overgeslagen; daarna verwerkt het pakket wat zeker is.
    /// </summary>
    public async Task<Uitkomst<Bankinlezing>> Lees(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(inhoud);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var naam = Path.GetFileName(bestandsnaam);
        IReadOnlyList<Bankafschrift> afschriften;
        try
        {
            using var kopie = await LeesBegrensd(inhoud, annulering);
            if (IsPdf(kopie))
            {
                // Een afschrift dat alleen als PDF bestaat: eerst omzetten (zoals de knop PDF → MT940), dan gewoon inlezen.
                var omgezet = await PdfNaarAfschrift(kopie.ToArray(), naam, annulering);
                if (!omgezet.IsGelukt)
                {
                    return Uitkomst<Bankinlezing>.Mislukt([.. omgezet.Fouten]);
                }

                afschriften = Bankbestand.Lees(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Mt940Schrijver.Schrijf(omgezet.Waarde!))));
            }
            else
            {
                afschriften = Bankbestand.Lees(kopie);
            }
        }
        catch (BankbestandException fout)
        {
            return Uitkomst<Bankinlezing>.Mislukt($"{naam}: {fout.Message}");
        }

        if (afschriften.Count == 0)
        {
            return Uitkomst<Bankinlezing>.Mislukt($"{naam} bevat geen bankafschrift.");
        }

        // Een afschrift per dag (CAMT) wordt één afschrift over de hele periode, ook de dagen zonder mutaties.
        var gelezen = afschriften.Count;
        afschriften = Bankafschrift.Samenvoegen(afschriften);

        if (afschriften.FirstOrDefault(a => !a.SluitAan) is { } fout2)
        {
            return Uitkomst<Bankinlezing>.Mislukt(
                $"{naam}: het afschrift van {Iban.Toon(fout2.Iban)} klopt niet: beginsaldo plus mutaties wijkt {Domain.Bedragen.Euro(fout2.Verschil)} af van het eindsaldo. Er is niets ingelezen.");
        }

        // Vanaf hier één transactie met de administratie vergrendeld: twee afschriften die tegelijk binnenkomen (bijv. een
        // map die in één keer wordt losgelaten) zien elkaars mutaties, zodat een overlap als dubbel telt in plaats van
        // dat de database dezelfde mutatie twee keer krijgt; en een afschrift komt er helemaal in, of niet.
        await using var inleestransactie = await db.Database.BeginTransactionAsync(annulering);
        await db.Vergrendel(administratie);

        // Het wijzigingslog schrijft via een eigen verbinding: pas na de commit, anders wacht het op deze transactie
        // (SQLite heeft één schrijver tegelijk) en staat er niets in het log van wat niet is ingelezen.
        var logregels = new List<(string Actie, string Onderwerp, string? Toelichting)>();

        // Eerst bij elke rekening het dagboek, zodat een onbekende rekening niets half inleest.
        // Een nieuw IBAN wordt pas aan zijn dagboek gekoppeld (en opgeslagen) als alle rekeningen in het bestand kloppen.
        var bankdagboeken = new Dictionary<string, Dagboekdefinitie>(StringComparer.Ordinal);
        var gekoppeld = new List<(Dagboekdefinitie Dagboek, string Iban)>();
        foreach (var iban in afschriften.Select(a => Iban.Normaliseer(a.Iban)).Distinct())
        {
            var (dagboek, fout, nieuwGekoppeld) = await Bankdagboek(db, administratie, iban);
            if (dagboek is null)
            {
                return Uitkomst<Bankinlezing>.Mislukt($"{naam}: {fout}");
            }

            bankdagboeken[iban] = dagboek;
            if (nieuwGekoppeld)
            {
                gekoppeld.Add((dagboek, iban));
            }
        }

        if (gekoppeld.Count > 0)
        {
            await db.SaveChangesAsync(annulering);
            foreach (var (dagboek, iban) in gekoppeld)
            {
                logregels.Add((AuditActies.BankdagboekGekoppeld, $"{dagboek.Code} {dagboek.Naam}", Iban.Toon(iban)));
            }
        }

        var meldingen = new List<string>();
        var nieuw = new List<Banktransactie>();
        var dubbel = 0;
        var mogelijkDubbel = 0;
        var gezien = new HashSet<string>(StringComparer.Ordinal);
        foreach (var afschrift in afschriften)
        {
            var iban = Iban.Normaliseer(afschrift.Iban);
            var dagboek = bankdagboeken[iban];

            var sleutels = Sleutels(afschrift, iban);
            var bekend = (await db.Banktransacties.AsNoTracking().Where(t => sleutels.Contains(t.Sleutel)).Select(t => t.Sleutel).ToListAsync(annulering))
                .ToHashSet(StringComparer.Ordinal);
            var regels = afschrift.Mutaties.Select((m, i) => (Mutatie: m, Sleutel: sleutels[i], Volgorde: i + 1))
                .Where(x => !bekend.Contains(x.Sleutel) && gezien.Add(x.Sleutel)).ToList();
            var alIngelezen = afschrift.Mutaties.Count - regels.Count;
            dubbel += alIngelezen;
            var verdacht = await Overlap(db, iban, afschrift, [.. regels.Select(r => r.Mutatie)], annulering);
            mogelijkDubbel += verdacht.Dubbel.Count;
            if (verdacht.Melding(naam, alIngelezen) is { } overlap)
            {
                meldingen.Add(overlap);
            }

            // Zonder nieuwe mutaties alleen het afschrift zelf onthouden (de stand van de rekening), als dat er nog niet is.
            // Geen nieuwe mutaties: alleen bewaren als het afschrift verder loopt dan wat er al is (de stand van de rekening).
            if (regels.Count == 0 && await db.Afschriften.AnyAsync(a => a.Iban == iban && a.Einddatum >= afschrift.Einddatum, annulering))
            {
                continue;
            }

            var opgeslagen = new Afschrift
            {
                Id = Guid.NewGuid(),
                AdministratieId = administratie,
                Dagboek = dagboek.Code,
                Iban = iban,
                Nummer = Kort(afschrift.Nummer, 50),
                Begindatum = afschrift.Begindatum,
                Beginsaldo = afschrift.Beginsaldo,
                Einddatum = afschrift.Einddatum,
                Eindsaldo = afschrift.Eindsaldo,
                Bestandsnaam = Kort(naam, 260)!,
                Melding = await Aansluiting(db, iban, afschrift),
                IngelezenOp = tijd.GetUtcNow(),
                IngelezenDoor = wie.Naam,
            };
            if (opgeslagen.Melding is { } melding)
            {
                meldingen.Add(melding);
            }

            db.Afschriften.Add(opgeslagen);
            foreach (var (mutatie, sleutel, volgorde) in regels)
            {
                var transactie = new Banktransactie
                {
                    Id = Guid.NewGuid(),
                    AdministratieId = administratie,
                    AfschriftId = opgeslagen.Id,
                    Volgorde = volgorde,
                    Dagboek = dagboek.Code,
                    Iban = iban,
                    Datum = mutatie.Datum,
                    Bedrag = mutatie.Bedrag,
                    Omschrijving = Kort(mutatie.Omschrijving, 1000) ?? string.Empty,
                    TegenIban = mutatie.TegenIban is { } tegen ? Kort(Iban.Normaliseer(tegen), 34) : null,
                    TegenNaam = Kort(mutatie.TegenNaam, 300),
                    Betalingskenmerk = Kort(mutatie.Betalingskenmerk, 50),
                    EndToEndId = Kort(mutatie.EndToEndId, 100),
                    Sleutel = sleutel,
                    Status = Banktransactiestatus.TeVerwerken,
                    MogelijkDubbel = verdacht.Dubbel.TryGetValue(mutatie, out var eerder),
                    Melding = eerder is null ? null : Kort(eerder, 1000),
                };
                db.Banktransacties.Add(transactie);
                nieuw.Add(transactie);
            }

            await db.SaveChangesAsync(annulering);
            logregels.Add((AuditActies.AfschriftIngelezen, $"{dagboek.Code} {Iban.Toon(iban)} {afschrift.Nummer}".Trim(),
                $"{naam}; {regels.Count} mutaties; eindsaldo {afschrift.Eindsaldo:0.00}"));
        }

        await inleestransactie.CommitAsync(annulering);
        foreach (var (actie, onderwerp, toelichting) in logregels)
        {
            await audit.Log(wie, actie, onderwerp, toelichting, administratie);
        }

        var automatisch = await VerwerkAutomatisch(db, administratie, nieuw, wie, annulering);
        return Uitkomst<Bankinlezing>.Met(new Bankinlezing(gelezen, nieuw.Count, dubbel, automatisch, meldingen, mogelijkDubbel));
    }

    /// <summary>
    /// Gaat opnieuw door alle mutaties in Te verwerken, bijv. nadat er facturen bij zijn geboekt of een regel is
    /// toegevoegd: wat nu zeker is (afletteren, of een regel of geleerde keuze), boekt het pakket; bij de rest komt de
    /// actuele reden te staan.
    /// </summary>
    public async Task<Herverwerking> VerwerkOpnieuw(Uitvoerder wie, Guid administratie, CancellationToken annulering = default)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var open = await db.Banktransacties.Where(t => t.Status == Banktransactiestatus.TeVerwerken).ToListAsync(annulering);
        var geboekt = await VerwerkAutomatisch(db, administratie, open, wie, annulering);
        await audit.Log(wie, AuditActies.OpnieuwVerwerkt, "Bank", $"{open.Count} bekeken, {geboekt} automatisch geboekt", administratie);
        return new Herverwerking(open.Count, geboekt);
    }

    /// <summary>Boekt van de mutaties wat zeker is, de oudste eerst; bij de rest de reden. Geeft het aantal geboekte.</summary>
    private async Task<int> VerwerkAutomatisch(AdministratieDbContext db, Guid administratie, IEnumerable<Banktransactie> transacties, Uitvoerder wie, CancellationToken annulering)
    {
        var automatisch = 0;
        var context = await Context(db, administratie);
        // Een mogelijk dubbele mutatie nooit vanzelf boeken: anders staat dezelfde betaling twee keer in de boeken.
        foreach (var transactie in transacties.Where(t => !t.MogelijkDubbel).OrderBy(t => t.Datum).ThenBy(t => t.Volgorde))
        {
            // De lijst is vooraf geladen: is de mutatie intussen (op een ander scherm) geboekt, dan overslaan.
            if (!await NogTeVerwerken(db, transactie.Id))
            {
                continue;
            }

            var voorstel = Voorstel(transactie, context);
            var uitkomst = KleinVerschil(voorstel, context) is { } klein
                ? await BoekAfletteren(db, administratie, transactie, [klein.Kandidaat.Posten[0].Id], wie, klein.Reden, klein.Afboeking)
                : voorstel.Automatisch switch
                {
                    Bankactie.Afletteren => await BoekAfletteren(db, administratie, transactie, [.. voorstel.Afletteren.Automatisch!.Posten.Select(p => p.Id)], wie, voorstel.Reden),
                    Bankactie.OpRekening => await BoekOpRekening(db, administratie, transactie, voorstel.Advies.Rekeningnummer!, voorstel.Advies.BtwCode,
                        voorstel.Advies.Bron is Adviesbron.Herkend or Adviesbron.Regel ? voorstel.Advies.Omschrijving : null, voorstel.Relatie, wie, voorstel.Reden),
                    _ => null,
                };
            if (uitkomst?.IsGelukt == true)
            {
                automatisch++;
                context = await Context(db, administratie);
            }
            else
            {
                // Alleen de melding, en alleen zolang de mutatie nog te verwerken is: de geladen mutatie kan verouderd
                // zijn (intussen geboekt), en die status mag niet terugkomen.
                var melding = Kort(uitkomst is null ? voorstel.Reden : string.Join(" ", uitkomst.Fouten), 1000);
                await db.Banktransacties.Where(t => t.Id == transactie.Id && t.Status == Banktransactiestatus.TeVerwerken)
                    .ExecuteUpdateAsync(x => x.SetProperty(t => t.Melding, melding), annulering);
            }
        }

        return automatisch;
    }

    /// <summary>
    /// Een klein betaalverschil op de enige passende factuur, als de administratie dat automatisch afboekt
    /// (instelling betaalverschil) en de rekening ervoor bestaat en geen bank, debiteuren, crediteuren of btw is.
    /// </summary>
    private static (Boekhouding.Afletteren.Afletterkandidaat Kandidaat, string Reden, Verschilafboeking Afboeking)? KleinVerschil(Bankvoorstel voorstel, Bankcontext context)
    {
        if (voorstel.Afletteren.KleinVerschil(context.Instellingen.Betaalverschil) is not { } kandidaat
            || context.Instellingen.Verschilrekening is not { } rekening
            || context.Schema.Zoek(rekening) is not { } r
            || r.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren or Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw)
        {
            return null;
        }

        // Te weinig betaald: er blijft iets open in de richting van de post (bij inkoop is die negatief).
        var tekst = Math.Sign(kandidaat.Verschil) == Math.Sign(kandidaat.Posten[0].Openstaand) ? "te weinig" : "te veel";
        var reden = $"{kandidaat.Reden}; {Domain.Bedragen.Euro(Math.Abs(kandidaat.Verschil))} {tekst} betaald, afgeboekt als betaalverschil";
        return (kandidaat, reden, new Verschilafboeking(r.Nummer, null, "Betaalverschil"));
    }

    public async Task<IReadOnlyList<BankmutatieRegel>> Lijst(Uitvoerder wie, Guid administratie, bool ookGeboekt = false)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Banktransacties.AsNoTracking();
        query = ookGeboekt ? query.Where(t => t.Status != Banktransactiestatus.Verwijderd) : query.Where(t => t.Status == Banktransactiestatus.TeVerwerken);

        var lijst = await query.OrderByDescending(t => t.Datum).ThenByDescending(t => t.Volgorde).Take(1000).ToListAsync();
        return [.. lijst.Select(Regel)];
    }

    /// <summary>De mutatie met het actuele voorstel en wat de gebruiker kan kiezen.</summary>
    public async Task<Bankverwerking?> Verwerking(Uitvoerder wie, Guid administratie, Guid mutatie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Banktransacties.AsNoTracking().FirstOrDefaultAsync(t => t.Id == mutatie) is not { } t)
        {
            return null;
        }

        var context = await Context(db, administratie);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        // Alle openstaande posten: eerst die in de richting van de mutatie, dan de rest (bijv. een creditnota).
        var posten = context.OpenPosten.Where(p => !p.IsBetaald)
            .OrderByDescending(p => Math.Sign(p.Openstaand) == Math.Sign(t.Bedrag)).ThenBy(p => p.Datum).ToList();
        return new Bankverwerking(Regel(t), Voorstel(t, context), posten, context.Relaties, schema, context.Btw.Op(t.Datum), await Saldo(db, t, schema),
            await Treffer(db, t, context.Relaties));
    }

    private static async Task<Regeltreffer?> Treffer(AdministratieDbContext db, Banktransactie t, IReadOnlyList<Relatie> relaties)
    {
        var mutatie = t.NaarDomein();
        var gegevens = Bankverwerker.Gegevens(mutatie, relaties.FirstOrDefault(r => r.HeeftIban(mutatie.TegenIban)));
        var regels = (await db.Regels.AsNoTracking().OrderBy(r => r.Volgorde).ToListAsync()).Select(r => (r.Id, Regel: r.NaarDomein())).ToList();
        return regels.FirstOrDefault(r => r.Regel.Past(gegevens)) is { Regel: not null } past ? new Regeltreffer(past.Id, past.Regel, true)
            : regels.FirstOrDefault(r => r.Regel.PastBijna(gegevens)) is { Regel: not null } bijna ? new Regeltreffer(bijna.Id, bijna.Regel, false)
            : null;
    }

    private static async Task<Banksaldo?> Saldo(AdministratieDbContext db, Banktransactie t, Rekeningschema schema)
    {
        var rekening = await db.Dagboeken.AsNoTracking().Where(d => d.Code == t.Dagboek).Select(d => d.Rekeningnummer).FirstOrDefaultAsync();
        if (rekening is null || await db.Afschriften.AsNoTracking().FirstOrDefaultAsync(a => a.Id == t.AfschriftId) is not { } afschrift)
        {
            return null;
        }

        // SQLite telt geen decimals op: eerst ophalen, dan optellen.
        var eindeJaar = new DateOnly(t.Datum.Year, 12, 31);
        var grootboek = (await (from r in db.GeldendeRegels
                                join b in db.Boekingen on r.BoekingId equals b.Id
                                where r.Rekeningnummer == rekening && b.Datum <= eindeJaar
                                select r.Bedrag).ToListAsync()).Sum();
        // Mutaties die bij het inlezen al bekend waren, staan niet bij dit afschrift. Ontbreken er vooraan (een bestand
        // dat overlapt met het vorige), dan terugrekenen vanaf het eindsaldo; anders vooruit vanaf het beginsaldo.
        var mutaties = await db.Banktransacties.AsNoTracking().Where(x => x.AfschriftId == t.AfschriftId).Select(x => new { x.Volgorde, x.Bedrag }).ToListAsync();
        var voor = mutaties.Min(x => x.Volgorde) > 1
            ? afschrift.Eindsaldo - mutaties.Where(x => x.Volgorde >= t.Volgorde).Sum(x => x.Bedrag)
            : afschrift.Beginsaldo + mutaties.Where(x => x.Volgorde < t.Volgorde).Sum(x => x.Bedrag);
        return new Banksaldo(rekening, schema.Zoek(rekening)?.Naam ?? "Bank", t.Datum.Year, grootboek, voor, voor + t.Bedrag);
    }

    /// <summary>
    /// Lettert een mutatie af op de gekozen openstaande posten (de oudste eerst; wat overblijft gaat naar de
    /// vraagposten). Hoort het IBAN van de tegenpartij nog bij geen enkele relatie, dan leert het pakket het bij de
    /// relatie van de posten: de volgende keer is het zeker.
    /// </summary>
    /// <param name="verschil">Het verschil tussen de posten en de mutatie niet open laten staan maar op deze rekening
    /// boeken (bijv. betalingskorting, met btw-code); de posten worden dan helemaal afgeletterd.</param>
    public async Task<Uitkomst> Afletteren(Uitvoerder wie, Guid administratie, Guid mutatie, IReadOnlyList<Guid> posten, Verschilafboeking? verschil = null)
    {
        ArgumentNullException.ThrowIfNull(posten);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await TeVerwerken(db, mutatie) is not { } t)
        {
            return Uitkomst.Mislukt("Deze mutatie bestaat niet (meer) of is al geboekt.");
        }

        if (posten.Count == 0)
        {
            return Uitkomst.Mislukt("Kies een of meer openstaande posten.");
        }

        var uitkomst = await BoekAfletteren(db, administratie, t, posten, wie, reden: null, verschil);
        if (uitkomst.IsGelukt)
        {
            await LeerIban(db, administratie, t, posten, wie);
        }

        return uitkomst;
    }

    /// <summary>
    /// Verdeelt een mutatie: de gekozen openstaande posten helemaal, de rest over regels op grootboekrekeningen (elk met
    /// btw-code en omschrijving). Posten en regels samen zijn precies het bedrag van de mutatie.
    /// </summary>
    public async Task<Uitkomst> Verdeel(Uitvoerder wie, Guid administratie, Guid mutatie, IReadOnlyList<Guid> posten, IReadOnlyList<Bankregelinvoer> regels)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(regels);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await TeVerwerken(db, mutatie) is not { } t)
        {
            return Uitkomst.Mislukt("Deze mutatie bestaat niet (meer) of is al geboekt.");
        }

        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (dagboeken.FirstOrDefault(d => d.Code == t.Dagboek) is not { } dagboek)
        {
            return Uitkomst.Mislukt($"Dagboek {t.Dagboek} bestaat niet meer.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, t.Datum) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        var codes = (await BtwDienst.Lees(db)).Op(t.Datum);
        var fouten = new List<string>();
        var verdeling = new List<(decimal, Afboeking)>();
        foreach (var r in regels.Where(r => r.Post is null && (r.Bedrag != 0 || !string.IsNullOrWhiteSpace(r.Rekening))))
        {
            if (schema.Zoek(r.Rekening?.Trim()) is not { } rekening)
            {
                fouten.Add(string.IsNullOrWhiteSpace(r.Rekening) ? "Kies bij elke regel een rekening." : $"Rekening {r.Rekening} bestaat niet.");
                continue;
            }

            if (rekening.Nummer == dagboek.Rekeningnummer)
            {
                fouten.Add($"{rekening.Nummer} is de rekening van deze bank zelf: kies de tegenrekening.");
                continue;
            }

            if (rekening.Functie is Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren)
            {
                fouten.Add($"Op {rekening.Nummer} boek je niet met een regel: kies daarvoor de openstaande post.");
                continue;
            }

            var btw = Boekhouding.Btw.BtwCodes.Zoek(codes, r.BtwCode);
            if (!string.IsNullOrWhiteSpace(r.BtwCode) && btw is null)
            {
                fouten.Add($"Btw-code {r.BtwCode} bestaat niet.");
                continue;
            }

            if (r.Btw is { } hand && (btw is not { BtwOpFactuur: true } || Math.Abs(hand) > Math.Abs(r.Bedrag)))
            {
                fouten.Add(btw is not { BtwOpFactuur: true }
                    ? $"Regel {rekening.Nummer}: kies een btw-code met btw, of haal het btw-bedrag weg."
                    : $"Het btw-bedrag {Domain.Bedragen.Getal(Math.Abs(hand))} is hoger dan de regel ({Domain.Bedragen.Getal(Math.Abs(r.Bedrag))}).");
                continue;
            }

            verdeling.Add((Math.Sign(t.Bedrag) * r.Bedrag, new Afboeking(rekening.Nummer, btw, string.IsNullOrWhiteSpace(r.Omschrijving) ? null : Kort(r.Omschrijving.Trim(), 200), r.Btw,
                string.IsNullOrWhiteSpace(r.Kostenplaats) ? null : r.Kostenplaats.Trim())));
        }

        // Posten: de hele post, of per regel een bedrag op een post (een deel, of meer dan openstaat).
        var delen = regels.Where(r => r.Post is not null).ToList();
        var ids = posten.Concat(delen.Select(r => r.Post!.Value)).ToList();
        var open = await db.OpenstaandePosten.Where(p => ids.Contains(p.Id)).ToListAsync();
        if (ids.Distinct().Count() != ids.Count)
        {
            fouten.Add("Een openstaande post staat er twee keer in.");
        }
        else if (open.Count != ids.Count || open.Any(p => p.Openstaand == 0))
        {
            fouten.Add("Een van de openstaande posten is intussen betaald.");
        }

        if (delen.Any(r => r.Bedrag == 0))
        {
            fouten.Add("Vul bij elke openstaande post een bedrag in.");
        }

        var postbedragen = open.Select(p => (Post: p, Bedrag: delen.FirstOrDefault(r => r.Post == p.Id) is { } deel ? Math.Sign(t.Bedrag) * deel.Bedrag : p.Openstaand)).ToList();
        if (verdeling.Count == 0 && open.Count == 0)
        {
            fouten.Add("Kies een openstaande post of vul een regel in.");
        }

        if (fouten.Count == 0 && postbedragen.Sum(p => p.Bedrag) + verdeling.Sum(v => v.Item1) is var totaal && totaal != t.Bedrag)
        {
            fouten.Add($"De posten en regels ({Domain.Bedragen.Getal(Math.Abs(totaal))}) sluiten niet aan op de mutatie ({Domain.Bedragen.Getal(Math.Abs(t.Bedrag))}).");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten.Distinct()]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (!await NogTeVerwerken(db, t.Id))
        {
            return Uitkomst.Mislukt("Deze mutatie is intussen al geboekt.");
        }

        // De posten opnieuw lezen: een andere boeking kan ze intussen (deels) betaald hebben.
        foreach (var o in open)
        {
            var was = o.Openstaand;
            await db.Entry(o).ReloadAsync();
            if (o.Openstaand != was)
            {
                return Uitkomst.Mislukt($"Factuur {o.Factuurnummer} is intussen (deels) betaald; open de mutatie opnieuw.");
            }
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, t.Datum.Year);
        var (post, bijgewerkt) = Bankboeker.Verdeeld(t.NaarDomein(), [.. postbedragen.Select(p => (p.Post.NaarDomein(), p.Bedrag))], verdeling, dagboek, boekstuk, schema);
        if (Journaalcontrole.Controleer(post, schema, dagboeken).Concat(await KostenplaatsDienst.Fouten(db, post)).ToList() is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        Boeking boeking;
        try
        {
            boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        }
        catch (PeriodeAfgeslotenException fout)
        {
            // Een collega sloot de periode op hetzelfde moment af.
            return Uitkomst.Mislukt(fout.Message);
        }
        foreach (var p in bijgewerkt)
        {
            open.Single(o => o.Id == p.Id).Openstaand = p.Openstaand;
        }

        t.Status = Banktransactiestatus.Geboekt;
        ConceptWeg(db, t.Id);
        t.BoekingId = boeking.Id;
        t.Melding = null;
        t.Verwerking = Verwerkingswijze.Handmatig;
        db.Banktransacties.Update(t);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.Geboekt, $"{dagboek.Code} {boekstuk}",
            $"{t.Bedrag:0.00} verdeeld: {string.Join(", ", open.Select(p => $"{p.Relatie} {p.Factuurnummer}").Concat(verdeling.Select(v => $"{v.Item2.Rekeningnummer} {v.Item1:0.00}")))}", administratie);
        if (open.Count > 0)
        {
            await LeerIban(db, administratie, t, [.. ids.Distinct()], wie);
        }

        return Uitkomst.Gelukt;
    }

    /// <summary>Boekt een mutatie op een grootboekrekening (met btw-code), en onthoudt de keuze.</summary>
    public async Task<Uitkomst> OpRekening(Uitvoerder wie, Guid administratie, Guid mutatie, string rekening, string? btwCode, string? omschrijving)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await TeVerwerken(db, mutatie) is not { } t)
        {
            return Uitkomst.Mislukt("Deze mutatie bestaat niet (meer) of is al geboekt.");
        }

        if (string.IsNullOrWhiteSpace(rekening))
        {
            return Uitkomst.Mislukt("Kies een rekening.");
        }

        var relaties = await Relaties(db);
        var relatie = relaties.FirstOrDefault(r => r.HeeftIban(t.TegenIban));
        return await BoekOpRekening(db, administratie, t, rekening.Trim(), string.IsNullOrWhiteSpace(btwCode) ? null : btwCode.Trim(),
            string.IsNullOrWhiteSpace(omschrijving) ? null : omschrijving.Trim(), relatie, wie, reden: null);
    }

    /// <summary>
    /// Boekt meer mutaties in één keer op dezelfde rekening met dezelfde btw-code (bijv. alle pinbetalingen bij de
    /// supermarkt op kantinekosten); elke mutatie wordt een eigen boeking met zijn eigen omschrijving. Geeft het aantal
    /// geboekte mutaties met de mutaties die niet lukten en waarom; lukt er geen, dan de fouten.
    /// </summary>
    public async Task<Uitkomst<Bulkboeking>> OpRekening(Uitvoerder wie, Guid administratie, IReadOnlyList<Guid> mutaties, string rekening, string? btwCode)
    {
        ArgumentNullException.ThrowIfNull(mutaties);
        if (mutaties.Count == 0)
        {
            return Uitkomst<Bulkboeking>.Mislukt("Kies een of meer mutaties.");
        }

        var geboekt = 0;
        var fouten = new List<string>();
        var mislukt = new List<Guid>();
        foreach (var mutatie in mutaties.Distinct())
        {
            var uitkomst = await OpRekening(wie, administratie, mutatie, rekening, btwCode, null);
            if (uitkomst.IsGelukt)
            {
                geboekt++;
            }
            else
            {
                fouten.AddRange(uitkomst.Fouten);
                mislukt.Add(mutatie);
            }
        }

        // Ook als een deel lukt: welke niet, en waarom (bijv. een afgesloten periode).
        return geboekt == 0 ? Uitkomst<Bulkboeking>.Mislukt([.. fouten.Distinct()]) : Uitkomst<Bulkboeking>.Met(new Bulkboeking(geboekt, mislukt, [.. fouten.Distinct()]));
    }

    /// <summary>
    /// Na het boeken van een nieuwe factuur: hoort die bij een bankmutatie die nog te verwerken is? Uploadde de gebruiker
    /// de factuur bij een mutatie en is het bedrag gelijk, dan wordt die mutatie erop afgeletterd (en het IBAN geleerd).
    /// Anders lettert het pakket een mutatie af waarvoor deze factuur nu de enige zekere kandidaat is.
    /// </summary>
    /// <returns>Of er een mutatie is afgeletterd.</returns>
    public async Task<bool> NaFactuur(Uitvoerder wie, Guid administratie, Guid post, Guid? transactie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.OpenstaandePosten.AsNoTracking().FirstOrDefaultAsync(p => p.Id == post) is not { Openstaand: not 0 } open)
        {
            return false;
        }

        if (transactie is { } id && await TeVerwerken(db, id) is { } bij && bij.Bedrag == open.Openstaand)
        {
            var uitkomst = await BoekAfletteren(db, administratie, bij, [post], wie, $"Gekoppeld: factuur {open.Factuurnummer} geüpload bij deze betaling");
            if (uitkomst.IsGelukt)
            {
                await LeerIban(db, administratie, bij, [post], wie);
            }

            return uitkomst.IsGelukt;
        }

        var context = await Context(db, administratie);
        var kandidaten = await db.Banktransacties.Where(t => t.Status == Banktransactiestatus.TeVerwerken && !t.MogelijkDubbel && (open.Openstaand > 0 ? t.Bedrag > 0 : t.Bedrag < 0))
            .OrderBy(t => t.Datum).ToListAsync();
        foreach (var t in kandidaten)
        {
            if (Voorstel(t, context).Afletteren.Automatisch is { } zeker && zeker.Posten.Any(p => p.Id == post))
            {
                return (await BoekAfletteren(db, administratie, t, [.. zeker.Posten.Select(p => p.Id)], wie, $"Afgeletterd na het boeken van factuur {open.Factuurnummer}")).IsGelukt;
            }
        }

        return false;
    }

    /// <summary>De openstaande posten (debiteuren en crediteuren), de oudste vervaldatum eerst.</summary>
    public async Task<IReadOnlyList<OpenpostRegel>> OpenPosten(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var namen = await db.Relaties.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Naam);
        var posten = await db.OpenstaandePosten.AsNoTracking().Where(p => p.Openstaand != 0).ToListAsync();
        return
        [
            .. posten.OrderBy(p => p.Vervaldatum).ThenBy(p => p.Factuurnummer, StringComparer.Ordinal)
                .Select(p => new OpenpostRegel(p.Id, p.Soort, p.Relatie, namen.GetValueOrDefault(p.Relatie, p.Relatie), p.Factuurnummer, p.Datum, p.Vervaldatum, p.Bedrag, p.Openstaand, p.DocumentId)),
        ];
    }

    /// <summary>Het saldo van de vraagposten (wat bij afletteren niet op een factuur paste), met het rekeningnummer.</summary>
    /// <summary>
    /// Per bankdagboek: de datum en het eindsaldo van het laatste ingelezen afschrift, hoeveel mutaties er nog te
    /// verwerken zijn, en de aansluiting met het grootboek: de beginstand (het grootboek vóór het eerste afschrift van
    /// het jaar tegen het beginsaldo daarvan) en het grootboek tot en met het laatste afschrift tegen het eindsaldo.
    /// </summary>
    public async Task<IReadOnlyList<Bankstand>> Stand(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dagboeken = await db.Dagboeken.AsNoTracking().Where(d => d.Soort == Dagboeksoort.Bank).OrderBy(d => d.Code).ToListAsync();
        var afschriften = await db.Afschriften.AsNoTracking().Where(a => a.Einddatum != null).ToListAsync();
        var open = (await db.Banktransacties.AsNoTracking().Where(t => t.Status == Banktransactiestatus.TeVerwerken).Select(t => t.Dagboek).ToListAsync())
            .CountBy(d => d).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var rekeningen = dagboeken.Select(d => d.Rekeningnummer).OfType<string>().ToList();
        // Optellen in het geheugen: SQLite telt geen decimals op.
        var regels = await (from r in db.GeldendeRegels
                            join b in db.Geldend on r.BoekingId equals b.Id
                            where rekeningen.Contains(r.Rekeningnummer)
                            select new { r.Rekeningnummer, b.Datum, b.Dagboek, r.Bedrag }).ToListAsync();
        var lijst = new List<Bankstand>();
        foreach (var d in dagboeken)
        {
            var eigen = afschriften.Where(a => a.Dagboek == d.Code).OrderBy(a => a.Einddatum).ThenBy(a => a.IngelezenOp).ToList();
            var laatste = eigen.LastOrDefault();
            // Het eerste afschrift van het jaar van het laatste (op de einddatum: bij MT940 is de begindatum vaak de
            // laatste dag van het jaar ervoor).
            var eerste = laatste is null ? null : eigen.Where(a => a.Einddatum!.Value.Year == laatste.Einddatum!.Value.Year).OrderBy(a => a.Begindatum ?? a.Einddatum).FirstOrDefault();
            decimal? Saldo(Func<DateOnly, string, bool> wanneer) =>
                d.Rekeningnummer is { } rekening ? regels.Where(r => r.Rekeningnummer == rekening && wanneer(r.Datum, r.Dagboek)).Sum(r => r.Bedrag) : null;

            // De beginstand: het grootboek vóór de eerste mutatie van dat afschrift. De begindatum betekent niet bij elke
            // bank hetzelfde (MT940 :60F: en CAMT PRCD = het slot van de dag ervoor, CAMT OPBD = het begin van de dag),
            // en de beginbalans staat op 1 januari: tot de eerste mutatie (een bankboeking ervóór hoort niet bij dit
            // afschrift); zonder mutaties tot na de einddatum (het saldo is dan niet veranderd).
            DateOnly? grens = null;
            if (eerste?.Begindatum is not null)
            {
                var eersteMutatie = await db.Banktransacties.AsNoTracking().Where(t => t.AfschriftId == eerste.Id).Select(t => (DateOnly?)t.Datum).OrderBy(x => x).FirstOrDefaultAsync();
                grens = eersteMutatie ?? eerste.Einddatum!.Value.AddDays(1);
            }

            lijst.Add(new Bankstand(d.Code, d.Naam, d.Iban, laatste?.Einddatum, laatste?.Eindsaldo, open.GetValueOrDefault(d.Code), d.Rekeningnummer,
                eerste?.Begindatum, eerste?.Beginsaldo,
                // Op de dag van de eerste mutatie telt wat niet uit de bank komt wel mee (de beginbalans op 1 januari).
                grens is { } g ? Saldo((x, dagboek) => x < g || (x == g && dagboek != d.Code)) : null,
                laatste?.Einddatum is { } tot ? Saldo((x, _) => x <= tot) : null));
        }

        return lijst;
    }

    /// <summary>
    /// Een bankdagboek aanpassen: de naam altijd; het IBAN zolang er nog geen mutaties van zijn ingelezen, en de
    /// grootboekrekening zolang er nog niets in het dagboek is geboekt (anders klopt de bank in het grootboek niet meer).
    /// Alleen het kantoor; gelogd met wat er veranderde.
    /// </summary>
    public async Task<Uitkomst> WijzigBank(Uitvoerder wie, Guid administratie, string code, string naam, string? iban, string rekening)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(naam);
        ArgumentNullException.ThrowIfNull(rekening);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var dagboeken = await db.Dagboeken.ToListAsync();
        if (dagboeken.FirstOrDefault(d => d.Code == code && d.Soort == Dagboeksoort.Bank) is not { } d)
        {
            return Uitkomst.Mislukt($"Bankdagboek {code} bestaat niet.");
        }

        naam = naam.Trim();
        // Geen rekening opgegeven: de rekening blijft zoals hij is.
        rekening = rekening.Trim() is { Length: > 0 } opgegeven ? opgegeven : d.Rekeningnummer ?? "";
        var nieuwIban = string.IsNullOrWhiteSpace(iban) ? null : Iban.Normaliseer(iban);
        var fouten = new List<string>();
        if (naam.Length == 0)
        {
            fouten.Add("Geef de bankrekening een naam.");
        }

        if (nieuwIban != d.Iban)
        {
            if (nieuwIban is not null && !Iban.IsGeldig(nieuwIban))
            {
                fouten.Add($"{iban} is geen geldig IBAN.");
            }
            else if (nieuwIban is not null && dagboeken.FirstOrDefault(x => x.Iban == nieuwIban && x.Id != d.Id) is { } ander)
            {
                fouten.Add($"{Iban.Toon(nieuwIban)} hoort al bij dagboek {ander.Code} {ander.Naam}.");
            }
            else if (await db.Banktransacties.AnyAsync(t => t.Dagboek == code && t.Status != Banktransactiestatus.Verwijderd))
            {
                fouten.Add("Het IBAN is niet meer te wijzigen: er zijn al mutaties van deze rekening ingelezen.");
            }
        }

        if (rekening != (d.Rekeningnummer ?? ""))
        {
            var (schema, _) = await GrootboekDienst.Inrichting(db);
            if (schema.Zoek(rekening) is not { } r)
            {
                fouten.Add($"Rekening {rekening} staat niet in het rekeningschema.");
            }
            else if (r.Soort != Rekeningsoort.Balans)
            {
                fouten.Add($"Rekening {rekening} is geen balansrekening.");
            }
            else if (r.Functie is not (Rekeningfunctie.Geen or Rekeningfunctie.Bank))
            {
                fouten.Add($"Rekening {rekening} is al de rekening {Weergave.Tekst(r.Functie)}; kies een eigen rekening voor de bank.");
            }
            else if (dagboeken.FirstOrDefault(x => x.Rekeningnummer == rekening && x.Id != d.Id) is { } ander)
            {
                fouten.Add($"Rekening {rekening} hoort al bij dagboek {ander.Code} {ander.Naam}.");
            }
            else if (await db.Boekingen.AnyAsync(b => b.Dagboek == code))
            {
                fouten.Add("De grootboekrekening is niet meer te wijzigen: er is al in dit dagboek geboekt.");
            }
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        var wijzigingen = new[]
        {
            d.Naam != naam ? $"naam {d.Naam} → {naam}" : null,
            d.Iban != nieuwIban ? $"IBAN {(d.Iban is null ? "geen" : Iban.Toon(d.Iban))} → {(nieuwIban is null ? "geen" : Iban.Toon(nieuwIban))}" : null,
            (d.Rekeningnummer ?? "") != rekening ? $"rekening {d.Rekeningnummer} → {rekening}" : null,
        }.OfType<string>().ToList();
        if (wijzigingen.Count == 0)
        {
            return Uitkomst.Gelukt;
        }

        if ((d.Rekeningnummer ?? "") != rekening)
        {
            await MaakBankrekening(db, rekening);
        }

        (d.Naam, d.Iban, d.Rekeningnummer) = (naam, nieuwIban, rekening.Length > 0 ? rekening : d.Rekeningnummer);
        var adm = await db.Administraties.SingleAsync(a => a.Id == administratie);
        if (nieuwIban is not null && adm.Ibans.Count > 0 && !adm.Ibans.Select(Iban.Normaliseer).Contains(nieuwIban))
        {
            adm.Ibans = [.. adm.Ibans, nieuwIban];
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DagboekGewijzigd, $"{d.Code} {d.Naam}", string.Join("; ", wijzigingen), administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// De grootboekrekening van een bankdagboek krijgt de functie Bank: dan behandelt het pakket hem overal als bank
    /// (de bankregel van een boeking blijft vast, een btw-betaling vanaf deze rekening telt als betaling).
    /// </summary>
    private static async Task MaakBankrekening(AdministratieDbContext db, string rekening)
    {
        if (await db.Rekeningen.FirstOrDefaultAsync(r => r.Nummer == rekening) is { Functie: Rekeningfunctie.Geen } r)
        {
            r.Functie = Rekeningfunctie.Bank;
        }
    }

    /// <summary>
    /// Een bankrekening erbij: een bankdagboek met de grootboekrekening en (als die al bekend is) het IBAN, dat dan bij
    /// de eigen rekeningen van de administratie komt. Afschriften van dat IBAN komen voortaan in dit dagboek; zonder
    /// IBAN krijgt het dagboek het IBAN van het eerste afschrift dat bij geen ander dagboek hoort. Alleen het kantoor.
    /// </summary>
    public async Task<Uitkomst<string>> VoegBankToe(Uitvoerder wie, Guid administratie, string code, string naam, string? iban, string rekening)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(naam);
        ArgumentNullException.ThrowIfNull(rekening);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        code = code.Trim().ToUpperInvariant();
        naam = naam.Trim();
        rekening = rekening.Trim();
        var genormaliseerd = string.IsNullOrWhiteSpace(iban) ? null : Iban.Normaliseer(iban);
        var fouten = new List<string>();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9]{2,10}$"))
        {
            fouten.Add("Een dagboekcode bestaat uit 2 tot 10 letters of cijfers, bijv. BNK2.");
        }

        if (naam.Length == 0)
        {
            fouten.Add("Geef de bankrekening een naam, bijv. Spaarrekening.");
        }

        if (genormaliseerd is not null && !Iban.IsGeldig(genormaliseerd))
        {
            fouten.Add($"{iban} is geen geldig IBAN.");
        }

        var (schema, _) = await GrootboekDienst.Inrichting(db);
        if (schema.Zoek(rekening) is not { } r)
        {
            fouten.Add($"Rekening {rekening} staat niet in het rekeningschema.");
        }
        else if (r.Soort != Rekeningsoort.Balans)
        {
            fouten.Add($"Rekening {rekening} is geen balansrekening.");
        }
        else if (r.Functie is not (Rekeningfunctie.Geen or Rekeningfunctie.Bank))
        {
            fouten.Add($"Rekening {rekening} is al de rekening {Weergave.Tekst(r.Functie)}; kies een eigen rekening voor de bank.");
        }

        var dagboeken = await db.Dagboeken.ToListAsync();
        if (dagboeken.Any(d => d.Code == code))
        {
            fouten.Add($"Dagboek {code} bestaat al.");
        }

        if (dagboeken.FirstOrDefault(d => d.Rekeningnummer == rekening) is { } ander)
        {
            fouten.Add($"Rekening {rekening} hoort al bij dagboek {ander.Code} {ander.Naam}.");
        }

        if (genormaliseerd is not null && dagboeken.FirstOrDefault(d => d.Iban == genormaliseerd) is { } metIban)
        {
            fouten.Add($"{Iban.Toon(genormaliseerd)} hoort al bij dagboek {metIban.Code} {metIban.Naam}.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<string>.Mislukt([.. fouten]);
        }

        await MaakBankrekening(db, rekening);
        db.Dagboeken.Add(new Dagboekdefinitie
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Code = code,
            Naam = naam,
            Soort = Dagboeksoort.Bank,
            Rekeningnummer = rekening,
            Iban = genormaliseerd,
        });
        // Bij de eigen rekeningen alleen als die lijst er al is: een lege lijst laat het eerste afschrift van de andere
        // bankdagboeken (nog zonder IBAN) toe, een lijst met alleen deze rekening zou dat weigeren.
        if (genormaliseerd is not null)
        {
            var adm = await db.Administraties.SingleAsync(a => a.Id == administratie);
            if (adm.Ibans.Count > 0 && !adm.Ibans.Select(Iban.Normaliseer).Contains(genormaliseerd))
            {
                adm.Ibans = [.. adm.Ibans, genormaliseerd];
            }
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.DagboekToegevoegd, $"{code} {naam}", $"bank, rekening {rekening}{(genormaliseerd is null ? null : $", {Iban.Toon(genormaliseerd)}")}", administratie);
        return Uitkomst<string>.Met(code);
    }

    public async Task<(string? Rekening, decimal Saldo)> Vraagposten(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        if (schema.MetFunctie(Rekeningfunctie.Vraagposten).FirstOrDefault() is not { } rekening)
        {
            return (null, 0m);
        }

        var bedragen = await db.GeldendeRegels.AsNoTracking().Where(r => r.Rekeningnummer == rekening.Nummer).Select(r => r.Bedrag).ToListAsync();
        return (rekening.Nummer, bedragen.Sum());
    }

    private async Task<Uitkomst> BoekAfletteren(
        AdministratieDbContext db, Guid administratie, Banktransactie t, IReadOnlyList<Guid> postIds, Uitvoerder wie, string? reden, Verschilafboeking? verschil = null)
    {
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (dagboeken.FirstOrDefault(d => d.Code == t.Dagboek) is not { } dagboek)
        {
            return Uitkomst.Mislukt($"Dagboek {t.Dagboek} bestaat niet meer.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, t.Datum) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        Afboeking? afboeking = null;
        if (verschil is not null)
        {
            if (schema.Zoek(verschil.Rekening.Trim()) is not { } rekening || rekening.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren)
            {
                return Uitkomst.Mislukt("Kies voor het verschil een gewone grootboekrekening (niet bank, debiteuren of crediteuren).");
            }

            var btw = Boekhouding.Btw.BtwCodes.Zoek((await BtwDienst.Lees(db)).Op(t.Datum), verschil.BtwCode);
            if (!string.IsNullOrWhiteSpace(verschil.BtwCode) && btw is null)
            {
                return Uitkomst.Mislukt($"Btw-code {verschil.BtwCode} bestaat niet.");
            }

            afboeking = new Afboeking(rekening.Nummer, btw, string.IsNullOrWhiteSpace(verschil.Omschrijving) ? null : Kort(verschil.Omschrijving.Trim(), 200));
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (!await NogTeVerwerken(db, t.Id))
        {
            return Uitkomst.Mislukt("Deze mutatie is intussen al geboekt.");
        }

        var ids = postIds.Distinct().ToList();
        var posten = await db.OpenstaandePosten.Where(p => ids.Contains(p.Id)).ToListAsync();
        // Bij automatisch verwerken leeft deze context lang: een post die al eerder geladen was, komt dan met de oude
        // stand terug (een collega kan hem intussen hebben afgeletterd). Onder het slot de actuele stand lezen.
        foreach (var p in posten)
        {
            await db.Entry(p).ReloadAsync();
        }
        // Een post die al betaald is, mag alleen als enige en in dezelfde richting als de factuur: een dubbele betaling
        // (te vorderen op die factuur).
        // Posten met een ander teken (een creditnota) alleen als ze samen precies het bedrag zijn, of met een afboeking.
        bool Open(OpenstaandePost p) => p.Openstaand != 0 && Math.Sign(p.Openstaand) == Math.Sign(t.Bedrag);
        var samen = posten.Count > 1 && posten.All(p => p.Openstaand != 0) && (posten.Sum(p => p.Openstaand) == t.Bedrag || afboeking is not null);
        // Een storno of terugbetaling van een betaling: de factuur staat daarna (weer) open, maar nooit meer dan het factuurbedrag.
        bool Terug(OpenstaandePost p) => Math.Sign(p.Bedrag) == -Math.Sign(t.Bedrag) && p.Openstaand - t.Bedrag is var na
            && Math.Abs(na) <= Math.Abs(p.Bedrag) && Math.Sign(na) != -Math.Sign(p.Bedrag);
        if (posten.Count != ids.Count || !(posten.All(Open) || samen || (posten is [var enige] && (Math.Sign(enige.Bedrag) == Math.Sign(t.Bedrag) || Terug(enige)))))
        {
            return Uitkomst.Mislukt("Een van de openstaande posten is intussen betaald of past niet bij deze mutatie.");
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, t.Datum.Year);
        Journaalpost post;
        IReadOnlyList<OpenPost> bijgewerkt;
        try
        {
            (post, bijgewerkt) = Bankboeker.Afletteren(t.NaarDomein(), [.. posten.Select(p => p.NaarDomein())], dagboek, boekstuk, schema, afboeking);
        }
        catch (Exception fout) when (fout is ArgumentException or InvalidOperationException)
        {
            return Uitkomst.Mislukt(fout.Message.Split(" (Parameter", 2)[0]);
        }

        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        Boeking boeking;
        try
        {
            boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        }
        catch (PeriodeAfgeslotenException fout)
        {
            // Een collega sloot de periode op hetzelfde moment af.
            return Uitkomst.Mislukt(fout.Message);
        }
        foreach (var open in bijgewerkt)
        {
            posten.Single(p => p.Id == open.Id).Openstaand = open.Openstaand;
        }

        t.Status = Banktransactiestatus.Geboekt;
        ConceptWeg(db, t.Id);
        t.BoekingId = boeking.Id;
        t.Melding = reden;
        t.Verwerking = reden is null ? Verwerkingswijze.Handmatig : Verwerkingswijze.Afgeletterd;
        db.Banktransacties.Update(t);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, reden is null ? AuditActies.Afgeletterd : AuditActies.AutomatischGeboekt, $"{dagboek.Code} {boekstuk}",
            $"{t.Bedrag:0.00} op {string.Join(", ", bijgewerkt.Select(p => $"{p.Relatie} {p.Factuurnummer}"))}{(afboeking is null ? null : $"; verschil op {afboeking.Rekeningnummer}")}", administratie);
        return Uitkomst.Gelukt;
    }

    private async Task<Uitkomst> BoekOpRekening(
        AdministratieDbContext db, Guid administratie, Banktransactie t, string rekening, string? btwCode, string? omschrijving, Relatie? relatie, Uitvoerder wie, string? reden)
    {
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        if (dagboeken.FirstOrDefault(d => d.Code == t.Dagboek) is not { } dagboek)
        {
            return Uitkomst.Mislukt($"Dagboek {t.Dagboek} bestaat niet meer.");
        }

        if (await GrootboekDienst.Afgesloten(db, administratie, t.Datum) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        var btw = Boekhouding.Btw.BtwCodes.Zoek((await BtwDienst.Lees(db)).Op(t.Datum), btwCode);
        if (btwCode is not null && btw is null)
        {
            return Uitkomst.Mislukt($"Btw-code {btwCode} bestaat niet.");
        }

        if (schema.Zoek(rekening) is not { } gekozen)
        {
            return Uitkomst.Mislukt($"Rekening {rekening} bestaat niet.");
        }

        if (gekozen.Functie is Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren)
        {
            return Uitkomst.Mislukt("Een betaling van een factuur letter je af op de openstaande post, niet op debiteuren of crediteuren.");
        }

        if (gekozen.Nummer == dagboek.Rekeningnummer)
        {
            return Uitkomst.Mislukt($"{gekozen.Nummer} is de rekening van deze bank zelf: kies de tegenrekening.");
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (!await NogTeVerwerken(db, t.Id))
        {
            return Uitkomst.Mislukt("Deze mutatie is intussen al geboekt.");
        }

        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, t.Datum.Year);
        var mutatie = t.NaarDomein();
        Journaalpost post;
        try
        {
            post = Bankboeker.OpRekening(mutatie, gekozen.Nummer, btw, dagboek, boekstuk, schema, omschrijving is null ? null : Kort(omschrijving, 200));
        }
        catch (Exception fout) when (fout is ArgumentException or InvalidOperationException)
        {
            return Uitkomst.Mislukt(fout.Message.Split(" (Parameter", 2)[0]);
        }

        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        Boeking boeking;
        try
        {
            boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        }
        catch (PeriodeAfgeslotenException fout)
        {
            // Een collega sloot de periode op hetzelfde moment af.
            return Uitkomst.Mislukt(fout.Message);
        }
        t.Status = Banktransactiestatus.Geboekt;
        ConceptWeg(db, t.Id);
        t.BoekingId = boeking.Id;
        t.Melding = reden;
        t.Verwerking = reden is null ? Verwerkingswijze.Handmatig : Verwerkingswijze.Autocodering;
        db.Banktransacties.Update(t);

        // Leren, zoals bij een inkoopfactuur: de keuze met het bedrag en het IBAN van deze mutatie.
        var gegevens = Bankverwerker.Gegevens(mutatie, relatie);
        if (Leergeheugen.Sleutel(gegevens) is { } sleutel)
        {
            var bevestiging = Leergeheugen.Bij(gegevens, new Keuze(gekozen.Nummer, btw?.Code));
            db.Leerkeuzes.Add(new Leerkeuze
            {
                AdministratieId = administratie,
                Sleutel = sleutel,
                Rekeningnummer = bevestiging.Keuze.Rekeningnummer,
                BtwCode = bevestiging.Keuze.BtwCode,
                Bedrag = bevestiging.Bedrag,
                Iban = bevestiging.Iban,
                Tijdstip = tijd.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, reden is null ? AuditActies.Geboekt : AuditActies.AutomatischGeboekt, $"{dagboek.Code} {boekstuk}",
            $"{t.Bedrag:0.00} op {gekozen.Nummer}{(btw is null ? null : $" ({btw.Code})")}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Het IBAN van de tegenpartij bij de relatie van de afgeletterde posten, als het nog bij geen relatie hoort. Het
    /// komt van het eigen bankafschrift (er is echt van of naar die rekening betaald), dus zonder wachtwoord; wel in het log.
    /// </summary>
    private async Task LeerIban(AdministratieDbContext db, Guid administratie, Banktransactie t, IReadOnlyList<Guid> postIds, Uitvoerder wie)
    {
        if (t.TegenIban is not { } iban || !Iban.IsGeldig(iban))
        {
            return;
        }

        var codes = await db.OpenstaandePosten.AsNoTracking().Where(p => postIds.Contains(p.Id)).Select(p => p.Relatie).Distinct().ToListAsync();
        var relaties = await db.Relaties.ToListAsync();
        if (codes is not [var code] || relaties.Any(r => r.NaarDomein().HeeftIban(iban)) || relaties.FirstOrDefault(r => r.Code == code) is not { } relatie)
        {
            return;
        }

        relatie.Ibans = [.. relatie.Ibans, iban];
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.IbanGeleerd, relatie.Code, $"{Iban.Toon(iban)} uit bankmutatie van {Weergave.Datum(t.Datum)}", administratie);
    }

    /// <summary>
    /// Het bankdagboek voor een IBAN. Heeft nog geen dagboek dit IBAN, dan krijgt het eerste bankdagboek zonder IBAN
    /// het, mits het IBAN bij de eigen rekeningen van de administratie staat (of die nog leeg zijn): zo komt een
    /// afschrift van een andere klant nooit in deze administratie.
    /// </summary>
    /// <remarks>
    /// Slaat niets op (de aanroeper doet dat als alle rekeningen in het bestand kloppen). Zijn er meer bankdagboeken
    /// zonder IBAN, dan kiest het pakket niet zelf: het IBAN hoort dan bij het dagboek gezet te worden (tandwiel).
    /// </remarks>
    private static async Task<(Dagboekdefinitie? Dagboek, string? Fout, bool Gekoppeld)> Bankdagboek(AdministratieDbContext db, Guid administratie, string iban)
    {
        var bank = await db.Dagboeken.Where(d => d.Soort == Dagboeksoort.Bank).OrderBy(d => d.Code).ToListAsync();
        if (bank.FirstOrDefault(d => d.Iban == iban) is { } bekend)
        {
            return (bekend, null, false);
        }

        var adm = await db.Administraties.SingleAsync(a => a.Id == administratie);
        var eigen = adm.Ibans.Select(Iban.Normaliseer).ToList();
        if (eigen.Count > 0 && !eigen.Contains(iban))
        {
            return (null, $"rekening {Iban.Toon(iban)} hoort niet bij deze administratie (eigen rekeningen: {string.Join(", ", eigen.Select(Iban.Toon))}). Voeg hem toe bij Instellingen als hij wel van de klant is.", false);
        }

        var vrije = bank.Where(d => d.Iban is null && d.Rekeningnummer is not null).ToList();
        if (vrije.Count == 0)
        {
            return (null, $"er is geen bankdagboek voor rekening {Iban.Toon(iban)}. Voeg bij de bank een bankrekening toe met dit IBAN.", false);
        }

        if (vrije.Count > 1)
        {
            return (null, $"rekening {Iban.Toon(iban)} hoort nog bij geen bankdagboek, en er zijn er meer zonder IBAN ({string.Join(", ", vrije.Select(d => d.Code))}). Zet het IBAN bij het juiste dagboek (bij de bank, het tandwiel).", false);
        }

        var vrij = vrije[0];

        vrij.Iban = iban;
        // De eigen rekeningen: dit IBAN en die van de andere bankdagboeken (bijv. een spaarrekening die al is toegevoegd).
        var erbij = bank.Select(d => d.Iban).OfType<string>().Append(iban).Where(i => !eigen.Contains(i)).Distinct().ToList();
        if (erbij.Count > 0)
        {
            adm.Ibans = [.. adm.Ibans, .. erbij];
        }

        return (vrij, null, true);
    }

    /// <summary>Sluit het beginsaldo aan op het eindsaldo van het vorige afschrift van deze rekening? Anders een melding.</summary>
    /// <summary>
    /// Verwijdert mutaties die nog te verwerken zijn (bijv. dubbel ingelezen uit een overlappend bestand). Ze verdwijnen
    /// uit de lijsten, maar blijven bewaard voor het saldo van het afschrift en om ze bij opnieuw inlezen te herkennen.
    /// Een geboekte mutatie kan niet weg. Geeft het aantal verwijderde.
    /// </summary>
    public async Task<Uitkomst<int>> Verwijder(Uitvoerder wie, Guid administratie, IReadOnlyList<Guid> mutaties)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(mutaties);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        // Eerst het slot, dan lezen: een mutatie die net (op een ander scherm) geboekt wordt, gaat niet op Verwijderd.
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var gekozen = await db.Banktransacties.Where(t => mutaties.Contains(t.Id)).ToListAsync();
        if (gekozen.Count == 0)
        {
            return Uitkomst<int>.Mislukt("Kies de mutaties die weg moeten.");
        }

        if (gekozen.Where(t => t.Status == Banktransactiestatus.Geboekt).ToList() is { Count: > 0 } geboekt)
        {
            return Uitkomst<int>.Mislukt(geboekt.Count == 1
                ? $"De mutatie van {Weergave.Datum(geboekt[0].Datum)} ({Domain.Bedragen.Euro(geboekt[0].Bedrag)}) is al geboekt en kan niet weg."
                : $"{geboekt.Count} mutaties zijn al geboekt en kunnen niet weg. Er is niets verwijderd.");
        }

        var weg = gekozen.Where(t => t.Status == Banktransactiestatus.TeVerwerken).ToList();
        foreach (var t in weg)
        {
            t.Status = Banktransactiestatus.Verwijderd;
            ConceptWeg(db, t.Id);
            t.Melding = Kort($"Verwijderd door {wie.Naam} op {Weergave.Datum(DateOnly.FromDateTime(tijd.GetLocalNow().Date))}.", 1000);
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        if (weg.Count > 0)
        {
            await audit.Log(wie, AuditActies.BankmutatiesVerwijderd, $"{weg.Count} {(weg.Count == 1 ? "mutatie" : "mutaties")}",
                string.Join("; ", weg.OrderBy(t => t.Datum).Select(t => $"{t.Dagboek} {t.Datum:dd-MM-yyyy} {t.Bedrag:0.00} {t.TegenNaam}".TrimEnd())), administratie);
        }

        return Uitkomst<int>.Met(weg.Count);
    }

    /// <summary>
    /// Zet een geboekte mutatie terug naar Te verwerken als zijn boeking niet meer op de bank staat (vervallen of
    /// verwijderd, zie de aansluiting): dan kan hij opnieuw geboekt worden. Staat de boeking nog op de bank, dan niet.
    /// </summary>
    public async Task<Uitkomst> TerugNaarTeVerwerken(Uitvoerder wie, Guid administratie, Guid mutatie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Banktransacties.FirstOrDefaultAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.Geboekt) is not { } t)
        {
            return Uitkomst.Mislukt("Deze mutatie is niet geboekt.");
        }

        var rekening = await db.Dagboeken.AsNoTracking().Where(d => d.Code == t.Dagboek).Select(d => d.Rekeningnummer).FirstOrDefaultAsync();
        var opBank = t.BoekingId is { } id
            ? (await db.GeldendeRegels.AsNoTracking().Where(r => r.BoekingId == id && r.Rekeningnummer == rekening).Select(r => r.Bedrag).ToListAsync()).Sum()
            : 0m;
        if (opBank != 0)
        {
            return Uitkomst.Mislukt("Deze mutatie is al geboekt; pas die boeking aan.");
        }

        var stuk = t.BoekingId is { } vorige ? await db.Boekingen.AsNoTracking().Where(b => b.Id == vorige).Select(b => b.Dagboek + " " + b.Boekstuk).FirstOrDefaultAsync() : null;
        (t.Status, t.BoekingId, t.Verwerking, t.Melding) = (Banktransactiestatus.TeVerwerken, null, Verwerkingswijze.Onbekend, Kort($"De boeking{(stuk is null ? null : $" {stuk}")} stond niet meer op de bank; opnieuw te verwerken.", 1000));
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BankmutatieTerug, $"{t.Dagboek} {t.Datum:dd-MM-yyyy} {t.Bedrag:0.00}", stuk, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Draait een geboekte mutatie terug (bijv. verkeerd afgeletterd, ook als dat vanzelf ging): een tegenboeking in het
    /// bankdagboek (de boeking zelf blijft staan, de keten ook), de facturen staan weer open zoals ervoor, en de mutatie
    /// staat weer in Te verwerken. Niet in een afgesloten periode.
    /// </summary>
    public async Task<Uitkomst> Terugdraaien(Uitvoerder wie, Guid administratie, Guid mutatie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await db.Banktransacties.FirstOrDefaultAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.Geboekt) is not { } t)
        {
            return Uitkomst.Mislukt("Deze mutatie is niet geboekt.");
        }

        var boeking = t.BoekingId is { } boekingId ? await db.Geldend.AsNoTracking().Include(b => b.Regels).FirstOrDefaultAsync(b => b.Id == boekingId) : null;
        // Staat de boeking niet meer (vervallen of al teruggedraaid), dan alleen de mutatie terug.
        var teruggedraaid = boeking is not null && await db.Boekingen.AnyAsync(b => b.Herstelt == boeking.Id);
        string? tegenstuk = null;
        if (boeking is { Regels.Count: > 0 } && !teruggedraaid)
        {
            if (await GrootboekDienst.Afgesloten(db, administratie, boeking.Datum) is { } afgesloten)
            {
                return Uitkomst.Mislukt(afgesloten);
            }

            var (schema, _) = await GrootboekDienst.Inrichting(db);
            tegenstuk = await GrootboekDienst.VolgendBoekstuk(db, boeking.Dagboek, boeking.Datum.Year);
            var omschrijving = $"Terugdraaien {boeking.Dagboek} {boeking.Boekstuk}: {boeking.Omschrijving}";
            var tegen = boeking.NaarDomein().Tegenboeking(tegenstuk, boeking.Datum, omschrijving.Length > 200 ? omschrijving[..200] : omschrijving);
            var nieuw = await grootboek.VoegToeAanKeten(db, administratie, tegen, wie);
            nieuw.Herstelt = boeking.Id;
            // De afgeletterde facturen weer open: de tegenboeking werkt de openstaande posten bij zoals de boeking dat deed.
            await GrootboekDienst.WerkOpenstaandePostenBij(db, tegen, schema, nieuw, memoriaal: false);
        }

        var stuk = boeking is null ? null : $"{boeking.Dagboek} {boeking.Boekstuk}";
        (t.Status, t.BoekingId, t.Verwerking) = (Banktransactiestatus.TeVerwerken, null, Verwerkingswijze.Onbekend);
        t.Melding = Kort(stuk is null ? "Teruggedraaid; opnieuw te verwerken." : $"Boeking {stuk} is teruggedraaid; opnieuw te verwerken.", 1000);
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.BoekingTeruggedraaid, stuk ?? $"{t.Dagboek} {t.Datum:dd-MM-yyyy} {t.Bedrag:0.00}",
            tegenstuk is null ? "mutatie terug naar te verwerken" : $"tegenboeking {t.Dagboek} {tegenstuk}; mutatie {t.Datum:dd-MM-yyyy} {t.Bedrag:0.00}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>De gebruiker heeft gekeken: deze mutatie is niet dubbel. Daarna verwerkt het pakket hem gewoon.</summary>
    public async Task<Uitkomst> GeenDubbel(Uitvoerder wie, Guid administratie, Guid mutatie)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Banktransacties.FirstOrDefaultAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.TeVerwerken) is not { } t)
        {
            return Uitkomst.Mislukt("De mutatie is niet (meer) te verwerken.");
        }

        if (!t.MogelijkDubbel)
        {
            return Uitkomst.Gelukt;
        }

        t.MogelijkDubbel = false;
        t.Melding = null;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.GeenDubbeleBankmutatie, $"{t.Dagboek} {t.Datum:dd-MM-yyyy} {t.Bedrag:0.00}", t.TegenNaam, administratie);
        await VerwerkAutomatisch(db, administratie, [t], wie, CancellationToken.None);
        return Uitkomst.Gelukt;
    }

    /// <summary>Wat een nieuw afschrift gemeen heeft met wat er van die rekening al is ingelezen.</summary>
    private sealed record Overlapping(IReadOnlyList<Afschrift> Afschriften, Dictionary<Bankmutatie, string> Dubbel)
    {
        /// <summary>Een korte melding als het afschrift overlapt met eerder ingelezen afschriften of mutaties; anders leeg.</summary>
        public string? Melding(string bestand, int alIngelezen)
        {
            if (Afschriften.Count == 0 && alIngelezen == 0 && Dubbel.Count == 0)
            {
                return null;
            }

            var met = Afschriften.Count == 0 ? "eerder ingelezen mutaties"
                : string.Join(", ", Afschriften.Select(a => a.Begindatum is { } ab && a.Einddatum is { } ae
                    ? $"{a.Bestandsnaam} ({Weergave.Datum(ab)} t/m {Weergave.Datum(ae)})" : a.Bestandsnaam));
            var delen = new List<string>();
            if (alIngelezen > 0)
            {
                delen.Add($"{alIngelezen} al ingelezen");
            }

            if (Dubbel.Count > 0)
            {
                delen.Add($"{Dubbel.Count} mogelijk dubbel");
            }

            return $"{bestand} overlapt met {met}" + (delen.Count > 0 ? $": {string.Join(", ", delen)}." : ".");
        }
    }

    /// <summary>
    /// Zoekt bij de nieuwe mutaties van een afschrift naar eerder ingelezen mutaties van dezelfde rekening met dezelfde
    /// datum en hetzelfde bedrag (en dezelfde tegenrekening als beide die hebben): bij een overlappend bestand uit een
    /// ander formaat (bijv. eerst PDF, dan MT940) verschilt de omschrijving vaak net, zodat de sleutel ze niet herkent.
    /// Elke eerdere mutatie telt maar voor één nieuwe.
    /// </summary>
    private static async Task<Overlapping> Overlap(AdministratieDbContext db, string iban, Bankafschrift afschrift, IReadOnlyList<Bankmutatie> nieuw, CancellationToken annulering)
    {
        var data = afschrift.Mutaties.Select(m => m.Datum).ToList();
        var van = afschrift.Begindatum is { } b && (data.Count == 0 || b < data.Min()) ? b : data.DefaultIfEmpty(DateOnly.MaxValue).Min();
        var tot = afschrift.Einddatum is { } e && (data.Count == 0 || e > data.Max()) ? e : data.DefaultIfEmpty(DateOnly.MinValue).Max();
        // Een afschrift overlapt als zijn periode echt in die van een ander valt; aansluitend (eind = begin) is normaal.
        var afschriften = (await db.Afschriften.AsNoTracking().Where(a => a.Iban == iban && a.Begindatum != null && a.Einddatum != null).ToListAsync(annulering))
            .Where(a => afschrift.Begindatum is { } ab && afschrift.Einddatum is { } ae && a.Begindatum < ae && a.Einddatum > ab).OrderBy(a => a.Begindatum).ToList();

        var dubbel = new Dictionary<Bankmutatie, string>(ReferenceEqualityComparer.Instance);
        if (nieuw.Count == 0)
        {
            return new(afschriften, dubbel);
        }

        var eerder = await db.Banktransacties.AsNoTracking()
            .Where(t => t.Iban == iban && t.Status != Banktransactiestatus.Verwijderd && t.Datum >= van && t.Datum <= tot)
            .Join(db.Afschriften, t => t.AfschriftId, a => a.Id, (t, a) => new { t.Id, t.Datum, t.Bedrag, t.TegenIban, t.TegenNaam, a.Bestandsnaam })
            .ToListAsync(annulering);
        var gebruikt = new HashSet<Guid>();
        foreach (var m in nieuw)
        {
            var tegen = m.TegenIban is { } ti ? Iban.Normaliseer(ti) : null;
            var gelijk = eerder.FirstOrDefault(x => !gebruikt.Contains(x.Id) && x.Datum == m.Datum && x.Bedrag == m.Bedrag
                && (x.TegenIban is null || tegen is null || x.TegenIban == tegen));
            if (gelijk is not null)
            {
                gebruikt.Add(gelijk.Id);
                dubbel[m] = $"Mogelijk dubbel: op {Weergave.Datum(gelijk.Datum)} staat al {Domain.Bedragen.Euro(gelijk.Bedrag)}{(gelijk.TegenNaam is { Length: > 0 } n ? $" van {n}" : null)}, ingelezen uit {gelijk.Bestandsnaam}.";
            }
        }

        return new(afschriften, dubbel);
    }

    private static async Task<string?> Aansluiting(AdministratieDbContext db, string iban, Bankafschrift afschrift)
    {
        if (afschrift.Begindatum is not { } begin)
        {
            return null;
        }

        // Valt het begin midden in een afschrift dat er al is (een bestand dat overlapt), dan is er niets te vergelijken:
        // het vorige afschrift eindigt dan vóór dat andere, niet vlak voor dit begin.
        if (await db.Afschriften.AsNoTracking().AnyAsync(a => a.Iban == iban && a.Einddatum > begin && (a.Begindatum ?? a.Einddatum) < begin))
        {
            return null;
        }

        var vorige = await db.Afschriften.AsNoTracking().Where(a => a.Iban == iban && a.Einddatum != null && a.Einddatum <= begin)
            .OrderByDescending(a => a.Einddatum).ThenByDescending(a => a.IngelezenOp).FirstOrDefaultAsync();
        if (vorige is null || vorige.Eindsaldo == afschrift.Beginsaldo)
        {
            return null;
        }

        // Begint dit afschrift op de laatste dag van het vorige (een dag overlap), dan is het beginsaldo dat van vóór die
        // dag: het eindsaldo min wat er die dag al was ingelezen.
        if (vorige.Einddatum == begin)
        {
            var dieDag = (await db.Banktransacties.AsNoTracking().Where(t => t.Iban == iban && t.Datum == begin).Select(t => t.Bedrag).ToListAsync()).Sum();
            if (vorige.Eindsaldo - dieDag == afschrift.Beginsaldo)
            {
                return null;
            }
        }

        return $"Het beginsaldo van {Iban.Toon(iban)} ({Domain.Bedragen.Euro(afschrift.Beginsaldo)} op {Weergave.Datum(begin)}) sluit niet aan op het eindsaldo van het vorige afschrift ({Domain.Bedragen.Euro(vorige.Eindsaldo)} op {Weergave.Datum(vorige.Einddatum!.Value)}): mist er een afschrift?";
    }

    /// <summary>
    /// Een sleutel per mutatie (SHA-256 van <see cref="Bankmutatie.Sleutel"/>). Twee gelijke mutaties op één afschrift
    /// krijgen een volgnummer, zodat ze allebei worden ingelezen, maar bij opnieuw inlezen allebei worden herkend.
    /// </summary>
    internal static List<string> Sleutels(Bankafschrift afschrift, string iban)
    {
        var aantal = new Dictionary<string, int>(StringComparer.Ordinal);
        var sleutels = new List<string>();
        foreach (var mutatie in afschrift.Mutaties)
        {
            var basis = mutatie.Sleutel(iban);
            aantal[basis] = aantal.GetValueOrDefault(basis) + 1;
            sleutels.Add(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mutatie.Sleutel(iban, aantal[basis] - 1)))));
        }

        return sleutels;
    }

    private sealed record Bankcontext(IReadOnlyList<OpenPost> OpenPosten, IReadOnlyList<Relatie> Relaties, IReadOnlyList<Verwerkingsregel> Regels, Leergeheugen Geheugen, Btwtabel Btw, Verwerkingsinstellingen Instellingen, Rekeningschema Schema, bool BtwPlichtig = true, Ondernemingsvorm Vorm = Ondernemingsvorm.BV, Belastinginstellingen? Belasting = null);

    private static async Task<Bankcontext> Context(AdministratieDbContext db, Guid administratie)
    {
        var adm = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        var instellingen = adm.Afwijkend().Op(await KantoorDienst.Lees(db));
        var geheugen = new Leergeheugen(instellingen);
        foreach (var keuze in await db.Leerkeuzes.AsNoTracking().OrderBy(k => k.Id).ToListAsync())
        {
            geheugen.Onthoud(keuze.Sleutel, keuze.NaarDomein());
        }

        // Ook de betaalde posten van het jaar voor de oudste mutatie die nog te verwerken is: een betaling met hun
        // nummer is mogelijk een dubbele betaling.
        var sinds = (await db.Banktransacties.Where(t => t.Status == Banktransactiestatus.TeVerwerken).MinAsync(t => (DateOnly?)t.Datum))?.AddYears(-1) ?? DateOnly.MaxValue;
        var posten = (await db.OpenstaandePosten.AsNoTracking().Where(p => p.Openstaand != 0 || p.Datum >= sinds).ToListAsync()).Select(p => p.NaarDomein()).ToList();
        var regels = (await db.Regels.AsNoTracking().OrderBy(r => r.Volgorde).ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        return new Bankcontext(posten, await Relaties(db), regels, geheugen, await BtwDienst.Lees(db), instellingen, schema, adm.BtwPlichtig, adm.Ondernemingsvorm, adm.Belasting());
    }

    /// <summary>Het voorstel bij een mutatie; in een administratie die niet btw-plichtig is zonder btw-code.</summary>
    private static Bankvoorstel Voorstel(Banktransactie t, Bankcontext context)
    {
        var voorstel = Bankverwerker.Verwerk(t.NaarDomein(), context.OpenPosten, context.Relaties, context.Regels, context.Geheugen, context.Btw.Op(t.Datum));

        // Geen regel en niets geleerd: een betaling aan de Belastingdienst herkennen (btw, loonheffingen, vpb, ib), met
        // de vaste rekening per belasting als die is ingesteld. Zelf boeken alleen als dat aan staat én het
        // betalingskenmerk een geldig controlecijfer heeft (dan staat de soort vast); anders een voorstel.
        // Ook als er iets geleerd is: de Belastingdienst heeft één IBAN voor alle belastingen, dus wat bij de btw geleerd
        // is, zou anders ook de loonheffing en de vennootschapsbelasting op de btw-rekening boeken. Een eigen regel gaat
        // wel voor, en bij motorrijtuigenbelasting het geleerde (per voertuig).
        var herkend = Belastingherkenning.Herken(voorstel.Mutatie, context.Schema, context.Vorm, context.Belasting?.Vast());
        if (herkend is { Rekening: { } rekening } belasting
            && (voorstel.Advies.Rekeningnummer is null
                || (voorstel.Advies.Bron is Adviesbron.Geleerd or Adviesbron.Relatie && belasting.Soort != Belastingsoort.Motorrijtuigenbelasting)))
        {
            // Motorrijtuigenbelasting nooit meteen zelf: de ene auto is zakelijk, de andere privé. Het leergeheugen
            // onthoudt de keuze per voertuig en boekt zelf na het ingestelde aantal bevestigingen.
            var automatisch = context.Belasting?.Automatisch == true && belasting.Aanslag is not null && belasting.Soort != Belastingsoort.Motorrijtuigenbelasting;
            voorstel = voorstel with { Advies = new Verwerkingsadvies(rekening.Nummer, null, Adviesbron.Herkend, automatisch, belasting.Reden, Omschrijving: belasting.Omschrijving) };
        }
        else if (voorstel.Advies is { Bron: Adviesbron.Geleerd or Adviesbron.Relatie, Automatisch: true }
                 && herkend?.Soort != Belastingsoort.Motorrijtuigenbelasting
                 && Belastingherkenning.VanBelastingdienst(voorstel.Mutatie))
        {
            // Van de Belastingdienst, maar welke belasting (of welke rekening) is niet te zeggen: wat geleerd is, kan bij
            // een andere belasting horen. Alleen als voorstel, niet zelf boeken.
            voorstel = voorstel with { Advies = voorstel.Advies with { Automatisch = false } };
        }

        return context.BtwPlichtig ? voorstel : voorstel with { Advies = voorstel.Advies with { BtwCode = null } };
    }

    private static async Task<IReadOnlyList<Relatie>> Relaties(AdministratieDbContext db) =>
        [.. (await db.Relaties.AsNoTracking().OrderBy(r => r.Naam).ToListAsync()).Select(r => r.NaarDomein())];

    /// <summary>
    /// Na het vergrendelen van de administratie opnieuw in de database kijken of de mutatie nog te verwerken is: twee
    /// keer snel op Boeken (of tegelijk vanaf twee schermen) leest eerst allebei "te verwerken" en wacht dan op het slot;
    /// de tweede mag daarna niet nog een keer boeken.
    /// </summary>
    private static Task<bool> NogTeVerwerken(AdministratieDbContext db, Guid mutatie) =>
        db.Banktransacties.AsNoTracking().AnyAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.TeVerwerken);

    private static async Task<Banktransactie?> TeVerwerken(AdministratieDbContext db, Guid mutatie) =>
        await db.Banktransacties.FirstOrDefaultAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.TeVerwerken);

    private static BankmutatieRegel Regel(Banktransactie t) =>
        new(t.Id, t.Dagboek, t.Datum, t.Bedrag, t.Omschrijving, t.TegenNaam, t.TegenIban, t.Status, t.Melding, t.Betalingskenmerk, t.EndToEndId, t.MogelijkDubbel, t.Verwerking);

    /// <summary>Een tussentijds bewaard concept van een mutatie die nu geboekt of verwijderd is: weg (anders komt het terug).</summary>
    private static void ConceptWeg(AdministratieDbContext db, Guid mutatie) =>
        db.Concepten.RemoveRange(db.Concepten.Where(c => c.Soort == Conceptsoort.Bank && c.Sleutel == mutatie));

    private static string? Kort(string? tekst, int lengte) =>
        tekst is null ? null : tekst.Length > lengte ? tekst[..lengte] : tekst;

    /// <summary>
    /// Zet een rekeningafschrift dat alleen als PDF bestaat om naar MT940 (om te bewaren of in een ander pakket in te
    /// lezen). Eerst de virusscan; het afschrift moet sluiten (vorig saldo plus mutaties is het nieuwe saldo). Er wordt
    /// niets ingelezen of bewaard.
    /// </summary>
    public async Task<Uitkomst<Pdfomzetting>> PdfNaarMt940(Uitvoerder wie, Guid administratie, Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        await using (await toegang.Open(wie, administratie, Recht.Invoeren))
        {
        }

        var naam = Path.GetFileName(bestandsnaam);
        try
        {
            using var kopie = await LeesBegrensd(inhoud, annulering);
            if (!IsPdf(kopie))
            {
                return Uitkomst<Pdfomzetting>.Mislukt($"{naam} is geen PDF.");
            }

            var omgezet = await PdfNaarAfschrift(kopie.ToArray(), naam, annulering);
            if (!omgezet.IsGelukt)
            {
                return Uitkomst<Pdfomzetting>.Mislukt([.. omgezet.Fouten]);
            }

            var afschrift = omgezet.Waarde!;
            var mt940 = System.Text.Encoding.UTF8.GetBytes(Mt940Schrijver.Schrijf(afschrift));
            return Uitkomst<Pdfomzetting>.Met(new Pdfomzetting(mt940, $"{Path.GetFileNameWithoutExtension(naam)}.mt940", afschrift));
        }
        catch (BankbestandException fout)
        {
            return Uitkomst<Pdfomzetting>.Mislukt($"{naam}: {fout.Message}");
        }
    }

    private async Task<Uitkomst<Bankafschrift>> PdfNaarAfschrift(byte[] pdf, string naam, CancellationToken annulering)
    {
        if (virusscanners.FirstOrDefault() is { } scanner)
        {
            var scan = await scanner.ScanAsync(new MemoryStream(pdf), annulering);
            if (scan.Uitslag != Documenten.Scanuitslag.Schoon)
            {
                return Uitkomst<Bankafschrift>.Mislukt(scan.Uitslag == Documenten.Scanuitslag.Besmet
                    ? $"{naam} bevat kwaadaardige inhoud ({scan.Toelichting}) en is niet gelezen."
                    : $"{scan.Toelichting} {naam} is niet gelezen; probeer het later opnieuw.");
            }
        }

        try
        {
            return Uitkomst<Bankafschrift>.Met(Documenten.PdfBankafschrift.Lees(pdf));
        }
        catch (Documenten.DocumentException fout)
        {
            return Uitkomst<Bankafschrift>.Mislukt($"{naam}: {fout.Message}");
        }
    }

    private static bool IsPdf(MemoryStream inhoud) =>
        inhoud.Length > 5 && inhoud.GetBuffer().AsSpan(0, (int)Math.Min(inhoud.Length, 1024)).IndexOf("%PDF-"u8) >= 0;

    private static async Task<MemoryStream> LeesBegrensd(Stream stream, CancellationToken annulering)
    {
        var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = await stream.ReadAsync(buffer, annulering)) > 0)
        {
            if (kopie.Length + gelezen > MaximumBytes)
            {
                await kopie.DisposeAsync();
                throw new BankbestandException($"Het bestand is groter dan {MaximumBytes / 1024 / 1024} MB.");
            }

            kopie.Write(buffer, 0, gelezen);
        }

        kopie.Position = 0;
        return kopie;
    }
}
