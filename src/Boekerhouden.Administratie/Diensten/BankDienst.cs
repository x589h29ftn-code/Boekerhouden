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
/// <param name="Meldingen">Bijv. dat een afschrift niet aansluit op het vorige.</param>
public sealed record Bankinlezing(int Afschriften, int Nieuw, int Dubbel, int Automatisch, IReadOnlyList<string> Meldingen);

/// <summary>Wat een nieuwe ronde automatisch verwerken opleverde: hoeveel er bekeken en hoeveel er geboekt zijn.</summary>
public sealed record Herverwerking(int Bekeken, int Geboekt);

/// <summary>
/// Een deel van een bankmutatie: op een grootboekrekening, of met <paramref name="Post"/> op een openstaande post (dan
/// telt de rekening niet). Het bedrag inclusief btw, zoals op het afschrift (zonder teken).
/// </summary>
public sealed record Bankregelinvoer(string Rekening, string? BtwCode, decimal Bedrag, string? Omschrijving = null, Guid? Post = null);

/// <summary>Het verschil bij afletteren op een rekening boeken in plaats van open te laten staan (bijv. betalingskorting).</summary>
public sealed record Verschilafboeking(string Rekening, string? BtwCode, string? Omschrijving = null);

/// <summary>De stand van één bankrekening: het laatste afschrift en wat er nog te verwerken is.</summary>
public sealed record Bankstand(string Dagboek, string Naam, string Iban, DateOnly? Tot, decimal? Saldo, int TeVerwerken);

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
    string? Melding);

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
    IReadOnlyList<BtwCode> BtwCodes);

/// <summary>
/// De bank: afschriften inlezen (MT940 of CAMT.053) met saldocontrole, aansluiting en zonder dubbele mutaties; dan
/// per mutatie afletteren of op een rekening boeken, automatisch als het zeker is (<see cref="Bankverwerker"/>),
/// anders door de gebruiker. Elke boeking zet de journaalpost in de keten en werkt de openstaande posten bij, in één
/// transactie.
/// </summary>
public sealed class BankDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd)
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
            afschriften = Bankbestand.Lees(kopie);
        }
        catch (BankbestandException fout)
        {
            return Uitkomst<Bankinlezing>.Mislukt($"{naam}: {fout.Message}");
        }

        if (afschriften.Count == 0)
        {
            return Uitkomst<Bankinlezing>.Mislukt($"{naam} bevat geen bankafschrift.");
        }

        if (afschriften.FirstOrDefault(a => !a.SluitAan) is { } fout2)
        {
            return Uitkomst<Bankinlezing>.Mislukt(
                $"{naam}: het afschrift van {Iban.Toon(fout2.Iban)} klopt niet: beginsaldo plus mutaties wijkt {Domain.Bedragen.Euro(fout2.Verschil)} af van het eindsaldo. Er is niets ingelezen.");
        }

        // Eerst bij elke rekening het dagboek, zodat een onbekende rekening niets half inleest.
        var bankdagboeken = new Dictionary<string, Dagboekdefinitie>(StringComparer.Ordinal);
        foreach (var iban in afschriften.Select(a => Iban.Normaliseer(a.Iban)).Distinct())
        {
            var (dagboek, fout) = await Bankdagboek(db, administratie, iban, wie);
            if (dagboek is null)
            {
                return Uitkomst<Bankinlezing>.Mislukt($"{naam}: {fout}");
            }

            bankdagboeken[iban] = dagboek;
        }

        var meldingen = new List<string>();
        var nieuw = new List<Banktransactie>();
        var dubbel = 0;
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
            dubbel += afschrift.Mutaties.Count - regels.Count;
            if (regels.Count == 0)
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
                };
                db.Banktransacties.Add(transactie);
                nieuw.Add(transactie);
            }

            await db.SaveChangesAsync(annulering);
            await audit.Log(wie, AuditActies.AfschriftIngelezen, $"{dagboek.Code} {Iban.Toon(iban)} {afschrift.Nummer}".Trim(),
                $"{naam}; {regels.Count} mutaties; eindsaldo {afschrift.Eindsaldo:0.00}", administratie);
        }

        var automatisch = await VerwerkAutomatisch(db, administratie, nieuw, wie, annulering);
        return Uitkomst<Bankinlezing>.Met(new Bankinlezing(afschriften.Count, nieuw.Count, dubbel, automatisch, meldingen));
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
        foreach (var transactie in transacties.OrderBy(t => t.Datum).ThenBy(t => t.Volgorde))
        {
            var voorstel = Voorstel(transactie, context);
            var uitkomst = KleinVerschil(voorstel, context) is { } klein
                ? await BoekAfletteren(db, administratie, transactie, [klein.Kandidaat.Posten[0].Id], wie, klein.Reden, klein.Afboeking)
                : voorstel.Automatisch switch
                {
                    Bankactie.Afletteren => await BoekAfletteren(db, administratie, transactie, [.. voorstel.Afletteren.Automatisch!.Posten.Select(p => p.Id)], wie, voorstel.Reden),
                    Bankactie.OpRekening => await BoekOpRekening(db, administratie, transactie, voorstel.Advies.Rekeningnummer!, voorstel.Advies.BtwCode, null, voorstel.Relatie, wie, voorstel.Reden),
                    _ => null,
                };
            if (uitkomst?.IsGelukt == true)
            {
                automatisch++;
                context = await Context(db, administratie);
            }
            else
            {
                transactie.Melding = Kort(uitkomst is null ? voorstel.Reden : string.Join(" ", uitkomst.Fouten), 1000);
                db.Banktransacties.Update(transactie);
                await db.SaveChangesAsync(annulering);
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

        var tekst = kandidaat.Verschil > 0 ? "te weinig" : "te veel";
        var reden = $"{kandidaat.Reden}; {Domain.Bedragen.Euro(Math.Abs(kandidaat.Verschil))} {tekst} betaald, afgeboekt als betaalverschil";
        return (kandidaat, reden, new Verschilafboeking(r.Nummer, null, "Betaalverschil"));
    }

    public async Task<IReadOnlyList<BankmutatieRegel>> Lijst(Uitvoerder wie, Guid administratie, bool ookGeboekt = false)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var query = db.Banktransacties.AsNoTracking();
        if (!ookGeboekt)
        {
            query = query.Where(t => t.Status == Banktransactiestatus.TeVerwerken);
        }

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
        return new Bankverwerking(Regel(t), Voorstel(t, context), posten, context.Relaties, schema, context.Btw.Op(t.Datum));
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

            if (rekening.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren)
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

            verdeling.Add((Math.Sign(t.Bedrag) * r.Bedrag, new Afboeking(rekening.Nummer, btw, string.IsNullOrWhiteSpace(r.Omschrijving) ? null : Kort(r.Omschrijving.Trim(), 200))));
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
            fouten.Add($"De posten en regels ({Domain.Bedragen.Getal(Math.Abs(totaal))}) sluiten niet op de mutatie ({Domain.Bedragen.Getal(Math.Abs(t.Bedrag))}).");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten.Distinct()]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, dagboek.Code, t.Datum.Year);
        var (post, bijgewerkt) = Bankboeker.Verdeeld(t.NaarDomein(), [.. postbedragen.Select(p => (p.Post.NaarDomein(), p.Bedrag))], verdeling, dagboek, boekstuk, schema);
        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst.Mislukt([.. controle]);
        }

        var boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        foreach (var p in bijgewerkt)
        {
            open.Single(o => o.Id == p.Id).Openstaand = p.Openstaand;
        }

        t.Status = Banktransactiestatus.Geboekt;
        t.BoekingId = boeking.Id;
        t.Melding = null;
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
        var kandidaten = await db.Banktransacties.Where(t => t.Status == Banktransactiestatus.TeVerwerken && (open.Openstaand > 0 ? t.Bedrag > 0 : t.Bedrag < 0))
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
    /// Per bankdagboek met een IBAN: de datum en het eindsaldo van het laatste ingelezen afschrift en hoeveel mutaties
    /// er nog te verwerken zijn (voor de kop van het bankscherm).
    /// </summary>
    public async Task<IReadOnlyList<Bankstand>> Stand(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var dagboeken = await db.Dagboeken.AsNoTracking().Where(d => d.Soort == Dagboeksoort.Bank && d.Iban != null).OrderBy(d => d.Code).ToListAsync();
        var afschriften = await db.Afschriften.AsNoTracking().Where(a => a.Einddatum != null).ToListAsync();
        var open = (await db.Banktransacties.AsNoTracking().Where(t => t.Status == Banktransactiestatus.TeVerwerken).Select(t => t.Dagboek).ToListAsync())
            .CountBy(d => d).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return
        [
            .. dagboeken.Select(d =>
            {
                var laatste = afschriften.Where(a => a.Dagboek == d.Code).OrderBy(a => a.Einddatum).ThenBy(a => a.IngelezenOp).LastOrDefault();
                return new Bankstand(d.Code, d.Naam, d.Iban!, laatste?.Einddatum, laatste?.Eindsaldo, open.GetValueOrDefault(d.Code));
            }),
        ];
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
        var ids = postIds.Distinct().ToList();
        var posten = await db.OpenstaandePosten.Where(p => ids.Contains(p.Id)).ToListAsync();
        // Een post die al betaald is, mag alleen als enige en in dezelfde richting als de factuur: een dubbele betaling
        // (te vorderen op die factuur).
        // Posten met een ander teken (een creditnota) alleen als ze samen precies het bedrag zijn, of met een afboeking.
        bool Open(OpenstaandePost p) => p.Openstaand != 0 && Math.Sign(p.Openstaand) == Math.Sign(t.Bedrag);
        var samen = posten.Count > 1 && posten.All(p => p.Openstaand != 0) && (posten.Sum(p => p.Openstaand) == t.Bedrag || afboeking is not null);
        if (posten.Count != ids.Count || !(posten.All(Open) || samen || (posten is [var enige] && Math.Sign(enige.Bedrag) == Math.Sign(t.Bedrag))))
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

        var boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        foreach (var open in bijgewerkt)
        {
            posten.Single(p => p.Id == open.Id).Openstaand = open.Openstaand;
        }

        t.Status = Banktransactiestatus.Geboekt;
        t.BoekingId = boeking.Id;
        t.Melding = reden;
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

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
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

        var boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        t.Status = Banktransactiestatus.Geboekt;
        t.BoekingId = boeking.Id;
        t.Melding = reden;
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
    private async Task<(Dagboekdefinitie? Dagboek, string? Fout)> Bankdagboek(AdministratieDbContext db, Guid administratie, string iban, Uitvoerder wie)
    {
        var bank = await db.Dagboeken.Where(d => d.Soort == Dagboeksoort.Bank).OrderBy(d => d.Code).ToListAsync();
        if (bank.FirstOrDefault(d => d.Iban == iban) is { } bekend)
        {
            return (bekend, null);
        }

        var adm = await db.Administraties.SingleAsync(a => a.Id == administratie);
        var eigen = adm.Ibans.Select(Iban.Normaliseer).ToList();
        if (eigen.Count > 0 && !eigen.Contains(iban))
        {
            return (null, $"rekening {Iban.Toon(iban)} hoort niet bij deze administratie (eigen rekeningen: {string.Join(", ", eigen.Select(Iban.Toon))}). Voeg hem toe bij Instellingen als hij wel van de klant is.");
        }

        if (bank.FirstOrDefault(d => d.Iban is null && d.Rekeningnummer is not null) is not { } vrij)
        {
            return (null, $"er is geen bankdagboek voor rekening {Iban.Toon(iban)}.");
        }

        vrij.Iban = iban;
        if (!eigen.Contains(iban))
        {
            adm.Ibans = [.. adm.Ibans, iban];
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BankdagboekGekoppeld, $"{vrij.Code} {vrij.Naam}", Iban.Toon(iban), administratie);
        return (vrij, null);
    }

    /// <summary>Sluit het beginsaldo aan op het eindsaldo van het vorige afschrift van deze rekening? Anders een melding.</summary>
    private static async Task<string?> Aansluiting(AdministratieDbContext db, string iban, Bankafschrift afschrift)
    {
        if (afschrift.Begindatum is not { } begin)
        {
            return null;
        }

        var vorige = await db.Afschriften.AsNoTracking().Where(a => a.Iban == iban && a.Einddatum != null && a.Einddatum <= begin)
            .OrderByDescending(a => a.Einddatum).ThenByDescending(a => a.IngelezenOp).FirstOrDefaultAsync();
        return vorige is null || vorige.Eindsaldo == afschrift.Beginsaldo
            ? null
            : $"Het beginsaldo van {Iban.Toon(iban)} ({Domain.Bedragen.Euro(afschrift.Beginsaldo)} op {Weergave.Datum(begin)}) sluit niet aan op het eindsaldo van het vorige afschrift ({Domain.Bedragen.Euro(vorige.Eindsaldo)} op {Weergave.Datum(vorige.Einddatum!.Value)}): mist er een afschrift?";
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

    private sealed record Bankcontext(IReadOnlyList<OpenPost> OpenPosten, IReadOnlyList<Relatie> Relaties, IReadOnlyList<Verwerkingsregel> Regels, Leergeheugen Geheugen, Btwtabel Btw, Verwerkingsinstellingen Instellingen, Rekeningschema Schema);

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
        return new Bankcontext(posten, await Relaties(db), regels, geheugen, await BtwDienst.Lees(db), instellingen, schema);
    }

    private static Bankvoorstel Voorstel(Banktransactie t, Bankcontext context) =>
        Bankverwerker.Verwerk(t.NaarDomein(), context.OpenPosten, context.Relaties, context.Regels, context.Geheugen, context.Btw.Op(t.Datum));

    private static async Task<IReadOnlyList<Relatie>> Relaties(AdministratieDbContext db) =>
        [.. (await db.Relaties.AsNoTracking().OrderBy(r => r.Naam).ToListAsync()).Select(r => r.NaarDomein())];

    private static async Task<Banktransactie?> TeVerwerken(AdministratieDbContext db, Guid mutatie) =>
        await db.Banktransacties.FirstOrDefaultAsync(t => t.Id == mutatie && t.Status == Banktransactiestatus.TeVerwerken);

    private static BankmutatieRegel Regel(Banktransactie t) =>
        new(t.Id, t.Dagboek, t.Datum, t.Bedrag, t.Omschrijving, t.TegenNaam, t.TegenIban, t.Status, t.Melding);

    private static string? Kort(string? tekst, int lengte) =>
        tekst is null ? null : tekst.Length > lengte ? tekst[..lengte] : tekst;

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
