using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een verkoopfactuur zoals de gebruiker hem invult (concept).</summary>
/// <param name="Relatie">De code van de klant.</param>
/// <param name="Referentie">Het kenmerk van de klant (inkoopordernummer, project).</param>
/// <param name="Tekst">Een eigen tekst onder de regels.</param>
/// <param name="Verdeling">De omzet over een periode verdelen (bijv. een jaarabonnement), via vooruitontvangen omzet.</param>
public sealed record Verkoopinvoer(
    string Relatie,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Verkoopregel> Regels,
    string? Referentie = null,
    string? Tekst = null,
    Periodeverdeling? Verdeling = null,
    Guid? LayoutId = null);

/// <summary>Een verkoopfactuur in de lijst.</summary>
/// <param name="Openstaand">Wat de klant nog moet betalen (alleen bij een definitieve factuur).</param>
public sealed record Verkooplijstregel(
    Guid Id,
    Verkoopstatus Status,
    string? Nummer,
    string Relatie,
    string Klant,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    decimal Exclusief,
    decimal Totaal,
    decimal? Openstaand,
    DateTimeOffset AangemaaktOp);

/// <summary>Een verkoopfactuur met alles om hem te tonen, op te maken of verder te bewerken.</summary>
/// <param name="Factuur">De factuur zoals hij op papier staat (met de klant als momentopname).</param>
/// <param name="Boeking">Dagboek en boekstuk van de boeking (definitief).</param>
/// <param name="Verdeelboekstukken">De maandboekingen van de periodeverdeling.</param>
public sealed record Verkoopweergave(
    Guid Id,
    Verkoopstatus Status,
    Verkoopfactuur Factuur,
    Verkoopinvoer Invoer,
    Partij Afzender,
    IReadOnlyList<Btwtotaal> Btw,
    decimal Totaal,
    IReadOnlyList<string> Vermeldingen,
    (string Dagboek, string Boekstuk)? Boeking,
    decimal? Openstaand,
    Guid? CreditVoorId,
    IReadOnlyList<string> Verdeelboekstukken,
    DateTimeOffset AangemaaktOp,
    string AangemaaktDoor,
    DateTimeOffset? DefinitiefOp,
    string? DefinitiefDoor)
{
    public decimal Exclusief => Factuur.TotaalExclusief;

    public decimal BtwTotaal => Btw.Sum(b => b.Btw);
}

/// <summary>Een klant in de keuzelijst bij het factureren, met wat er bij een nieuwe regel wordt voorgesteld.</summary>
public sealed record Klantkeuze(string Code, string Naam, Partij Gegevens, int? Betalingstermijn, string? Omzetrekening, string? Verkoopbtwcode);

/// <summary>Alles wat het factuurscherm nodig heeft: rekeningen, btw, klanten en de standaarden.</summary>
/// <param name="Omzetrekening">De standaard omzetrekening (de eerste W&amp;V-rekening met RGS-omzet).</param>
/// <param name="Verdeelrekening">De voorgestelde rekening voor een periodeverdeling (vooruitontvangen omzet).</param>
public sealed record Verkoopcontext(
    Rekeningschema Schema,
    Btwtabel Btwtabel,
    IReadOnlyList<Klantkeuze> Klanten,
    Partij Afzender,
    int Betalingstermijn,
    string? Omzetrekening,
    string? Verdeelrekening);

/// <summary>
/// Verkoopfacturen maken: een concept invullen en bewaren, dan definitief maken. Definitief krijgt de factuur het
/// volgende nummer in het jaar van de factuurdatum (20260001, zonder gaten), een betalingskenmerk, de boeking in het
/// verkoopboek (debiteuren debet, omzet en af te dragen btw credit) en een openstaande post bij de debiteuren, in één
/// transactie. Een definitieve factuur verandert niet meer: corrigeren gaat met een creditfactuur (in dezelfde reeks),
/// die met de openstaande factuur wordt verrekend.
/// </summary>
public sealed class VerkoopDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd, BankDienst bank)
{
    private static readonly JsonSerializerOptions Json = new();

    public const int MaximumRegels = 500;

    private DateOnly Vandaag => DateOnly.FromDateTime(tijd.GetLocalNow().DateTime);

    public async Task<Verkoopcontext> Context(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Context(db, administratie);
    }

    private static async Task<Verkoopcontext> Context(AdministratieDbContext db, Guid administratie)
    {
        var (schema, _) = await GrootboekDienst.Inrichting(db);
        var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie);
        var kantoor = await KantoorDienst.Lees(db);
        var relaties = await db.Relaties.AsNoTracking().Where(r => r.Klant).OrderBy(r => r.Naam).ToListAsync();
        var omzet = schema.Alle.FirstOrDefault(r => r.RgsCode?.StartsWith("WOmz", StringComparison.Ordinal) == true)?.Nummer;
        var verdeel = schema.Alle.FirstOrDefault(r => r.IsBalans && r.RgsCode?.StartsWith("BSchOpa", StringComparison.Ordinal) == true && r.Naam.Contains("ooruit", StringComparison.OrdinalIgnoreCase))?.Nummer
            ?? schema.Alle.FirstOrDefault(r => r.IsBalans && r.RgsCode?.StartsWith("BSchOpa", StringComparison.Ordinal) == true)?.Nummer;
        return new Verkoopcontext(schema, await BtwDienst.Lees(db),
            [.. relaties.Select(r => new Klantkeuze(r.Code, r.Naam, r.AlsKlant(), r.Betalingstermijn, r.Omzetrekening, r.Verkoopbtwcode))],
            a.Afzender(), a.Afwijkend().Op(kantoor).Betalingstermijn, omzet, verdeel);
    }

    /// <summary>De concepten en de definitieve facturen in de periode (op factuurdatum), nieuwste eerst.</summary>
    public async Task<IReadOnlyList<Verkooplijstregel>> Lijst(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var documenten = await db.Verkoopdocumenten.AsNoTracking().Include(v => v.Regels)
            .Where(v => v.Status == Verkoopstatus.Concept || (v.Datum >= van && v.Datum <= tot))
            .ToListAsync();
        var boekingen = documenten.Where(v => v.BoekingId is not null).Select(v => v.BoekingId!.Value).ToList();
        var open = await db.OpenstaandePosten.AsNoTracking().Where(p => boekingen.Contains(p.BoekingId)).ToDictionaryAsync(p => p.BoekingId, p => p.Openstaand);
        var tabel = await BtwDienst.Lees(db);
        return
        [
            .. documenten.Select(v =>
            {
                var factuur = Domein(v);
                var codes = tabel.Op(v.Datum);
                var totaal = Veilig(() => factuur.Totaal(codes), factuur.TotaalExclusief);
                return new Verkooplijstregel(v.Id, v.Status, v.Nummer, v.Relatie, factuur.Klant.Naam, v.Datum, v.Vervaldatum, v.Omschrijving,
                    factuur.TotaalExclusief, totaal, v.BoekingId is { } b && open.TryGetValue(b, out var o) ? o : null, v.AangemaaktOp);
            })
            .OrderBy(r => r.Status != Verkoopstatus.Concept).ThenByDescending(r => r.Datum).ThenByDescending(r => r.Nummer, StringComparer.Ordinal).ThenByDescending(r => r.AangemaaktOp),
        ];
    }

    public async Task<Verkoopweergave?> Open(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Weergave(db, administratie, id);
    }

    internal static async Task<Verkoopweergave?> Weergave(AdministratieDbContext db, Guid administratie, Guid id)
    {
        if (await db.Verkoopdocumenten.AsNoTracking().Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == id) is not { } v)
        {
            return null;
        }

        var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie);
        var codes = (await BtwDienst.Lees(db)).Op(v.Datum);
        var factuur = Domein(v);
        if (v.Status == Verkoopstatus.Concept && await db.Relaties.AsNoTracking().FirstOrDefaultAsync(r => r.Code == v.Relatie) is { } relatie)
        {
            // Een concept toont de klant zoals hij nu is; pas bij definitief wordt hij vastgelegd.
            factuur = factuur with { Klant = relatie.AlsKlant() };
        }

        if (v.CreditVoorId is { } creditVoor)
        {
            factuur = factuur with { CreditVoor = await db.Verkoopdocumenten.AsNoTracking().Where(x => x.Id == creditVoor).Select(x => x.Nummer).FirstOrDefaultAsync() };
        }

        var boeking = v.BoekingId is { } b ? await db.Boekingen.AsNoTracking().Where(x => x.Id == b).Select(x => new { x.Dagboek, x.Boekstuk }).FirstOrDefaultAsync() : null;
        var open = v.BoekingId is { } bid ? await db.OpenstaandePosten.AsNoTracking().Where(p => p.BoekingId == bid).Select(p => (decimal?)p.Openstaand).FirstOrDefaultAsync() : null;
        var verdeling = Verdeling(v);
        var btw = Veilig(() => factuur.Btw(codes), []);
        return new Verkoopweergave(v.Id, v.Status, factuur, Invoer(v), a.Afzender(), btw, factuur.TotaalExclusief + btw.Sum(x => x.Btw),
            Veilig(() => Verkoopcontrole.Vermeldingen(factuur, codes), []), boeking is null ? null : (boeking.Dagboek, boeking.Boekstuk), open, v.CreditVoorId,
            verdeling?.Boekstukken ?? [], v.AangemaaktOp, v.AangemaaktDoor, v.DefinitiefOp, v.DefinitiefDoor);
    }

    /// <summary>Bewaart een concept: nieuw (zonder id) of een bestaand concept. Een definitieve factuur wijzigt niet.</summary>
    public async Task<Uitkomst<Guid>> Bewaar(Uitvoerder wie, Guid administratie, Guid? id, Verkoopinvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Relaties.AsNoTracking().FirstOrDefaultAsync(r => r.Code == invoer.Relatie) is not { } relatie)
        {
            return Uitkomst<Guid>.Mislukt("Kies een klant.");
        }

        var fouten = new List<string>();
        if (invoer.Regels.Count > MaximumRegels)
        {
            fouten.Add($"Een factuur heeft hoogstens {MaximumRegels} regels.");
        }

        if (invoer.Regels.Any(r => Math.Abs(r.Aantal * r.Prijs) > 1_000_000_000m))
        {
            fouten.Add("Een regelbedrag is te groot.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        Verkoopdocument document;
        if (id is { } bestaand)
        {
            if (await db.Verkoopdocumenten.Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == bestaand) is not { } gevonden)
            {
                return Uitkomst<Guid>.Mislukt("Deze factuur bestaat niet (meer).");
            }

            if (gevonden.Status != Verkoopstatus.Concept)
            {
                return Uitkomst<Guid>.Mislukt($"Factuur {gevonden.Nummer} is definitief; corrigeer hem met een creditfactuur.");
            }

            document = gevonden;
            db.Verkoopdocumentregels.RemoveRange(document.Regels);
            document.Regels = [];
        }
        else
        {
            document = new Verkoopdocument
            {
                Id = Guid.NewGuid(),
                AdministratieId = administratie,
                Status = Verkoopstatus.Concept,
                AangemaaktOp = tijd.GetUtcNow(),
                AangemaaktDoor = wie.Naam,
            };
            db.Verkoopdocumenten.Add(document);
        }

        Neem(document, invoer, relatie);
        await db.SaveChangesAsync();
        await audit.Log(wie, id is null ? AuditActies.VerkoopconceptGemaakt : AuditActies.VerkoopconceptGewijzigd, $"{relatie.Code} {relatie.Naam}",
            $"{invoer.Datum:dd-MM-yyyy}; {invoer.Regels.Sum(r => r.Bedrag):0.00} excl. btw", administratie);
        return Uitkomst<Guid>.Met(document.Id);
    }

    private static void Neem(Verkoopdocument document, Verkoopinvoer invoer, Handelsrelatie relatie)
    {
        document.Relatie = relatie.Code;
        document.Klantgegevens = JsonSerializer.Serialize(relatie.AlsKlant(), Json);
        document.Datum = invoer.Datum;
        document.Vervaldatum = invoer.Vervaldatum;
        document.Omschrijving = Kort(invoer.Omschrijving?.Trim() ?? "", 500);
        document.Referentie = Leeg(invoer.Referentie, 100);
        document.Tekst = Leeg(invoer.Tekst, 4000);
        document.LayoutId = invoer.LayoutId;
        document.Verdeling = invoer.Verdeling is { } v ? JsonSerializer.Serialize(new Verdelingsgegevens(v.Van, v.Tot, v.Rekening, [], []), Json) : null;
        document.Regels.AddRange(invoer.Regels.Select((r, i) => new Verkoopdocumentregel
        {
            AdministratieId = document.AdministratieId,
            Volgorde = i + 1,
            Omschrijving = Kort(r.Omschrijving?.Trim() ?? "", 1000),
            Aantal = r.Aantal,
            Eenheid = Leeg(r.Eenheid, 20),
            Prijs = r.Prijs,
            Korting = r.Korting,
            Rekeningnummer = r.Rekeningnummer?.Trim() ?? "",
            BtwCode = r.BtwCode?.Trim().ToUpperInvariant() ?? "",
        }));
    }

    public async Task<Uitkomst> VerwijderConcept(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Verkoopdocumenten.Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == id) is not { } document)
        {
            return Uitkomst.Mislukt("Deze factuur bestaat niet (meer).");
        }

        if (document.Status != Verkoopstatus.Concept)
        {
            return Uitkomst.Mislukt($"Factuur {document.Nummer} is definitief en blijft bewaard; corrigeer hem met een creditfactuur.");
        }

        db.Verkoopdocumenten.Remove(document);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.VerkoopconceptVerwijderd, document.Relatie, $"{document.Datum:dd-MM-yyyy} {document.Omschrijving}", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Maakt een concept definitief: controleert de factuur (art. 35a, btw), geeft het volgende nummer, het
    /// betalingskenmerk, boekt hem en maakt de openstaande post; bij een creditfactuur ook de verrekening met de
    /// gecrediteerde factuur. Geeft het nummer.
    /// </summary>
    public async Task<Uitkomst<string>> MaakDefinitief(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Verkoopdocumenten.Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == id) is not { } document)
        {
            return Uitkomst<string>.Mislukt("Deze factuur bestaat niet (meer).");
        }

        if (document.Status != Verkoopstatus.Concept)
        {
            return Uitkomst<string>.Mislukt($"Factuur {document.Nummer} is al definitief.");
        }

        if (await db.Relaties.AsNoTracking().FirstOrDefaultAsync(r => r.Code == document.Relatie) is not { } relatie)
        {
            return Uitkomst<string>.Mislukt("De klant van deze factuur bestaat niet meer.");
        }

        var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie);
        var codes = (await BtwDienst.Lees(db)).Op(document.Datum);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        var factuur = Domein(document) with { Klant = relatie.AlsKlant() };
        var fouten = Verkoopcontrole.Controleer(factuur, a.Afzender(), codes).ToList();
        var dagboek = dagboeken.FirstOrDefault(d => d.Soort == Dagboeksoort.Verkoop);
        if (dagboek is null)
        {
            fouten.Add("Deze administratie heeft geen verkoopboek.");
        }

        fouten.AddRange(document.Regels.Where(r => schema.Zoek(r.Rekeningnummer) is null && !string.IsNullOrWhiteSpace(r.Rekeningnummer))
            .Select(r => $"Rekening {r.Rekeningnummer} bestaat niet."));
        if (factuur.Regels.Count > 0 && factuur.TotaalExclusief == 0 && factuur.Regels.All(r => r.Bedrag == 0))
        {
            fouten.Add("Het bedrag van de factuur is nul.");
        }

        var verdeling = Verdeling(document) is { } vg ? new Periodeverdeling(vg.Van, vg.Tot, vg.Rekening) : null;
        var memoriaal = dagboeken.Where(d => d.Soort == Dagboeksoort.Memoriaal).OrderBy(d => d.Code != "MEM").FirstOrDefault();
        if (verdeling is not null)
        {
            fouten.AddRange(verdeling.Controleer());
            if (schema.Zoek(verdeling.Rekening) is not { IsBalans: true })
            {
                fouten.Add($"Rekening {verdeling.Rekening} is geen balansrekening; kies bijv. vooruitontvangen omzet.");
            }

            if (memoriaal is null)
            {
                fouten.Add("Deze administratie heeft geen memoriaal voor de verdeling.");
            }
        }

        OpenstaandePost? gecrediteerd = null;
        if (document.CreditVoorId is { } creditVoor)
        {
            var origineel = await db.Verkoopdocumenten.AsNoTracking().FirstOrDefaultAsync(v => v.Id == creditVoor);
            if (origineel is not { Status: Verkoopstatus.Definitief } || origineel.Relatie != document.Relatie)
            {
                fouten.Add("Een creditfactuur hoort bij een definitieve factuur van dezelfde klant.");
            }
            else if (origineel.BoekingId is { } ob)
            {
                gecrediteerd = await db.OpenstaandePosten.FirstOrDefaultAsync(p => p.BoekingId == ob);
            }
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<string>.Mislukt([.. fouten.Distinct()]);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        var jaar = document.Datum.Year;
        var begin = jaar.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var uitgegeven = await db.Verkoopdocumenten.AsNoTracking().Where(v => v.Nummer != null && v.Nummer.StartsWith(begin)).Select(v => v.Nummer!).ToListAsync();
        var nummer = Factuurnummering.Volgende(jaar, uitgegeven);
        var kenmerk = Betalingskenmerk.Maak(nummer);
        factuur = factuur with
        {
            Nummer = nummer,
            Betalingskenmerk = kenmerk,
            Omschrijving = string.IsNullOrWhiteSpace(factuur.Omschrijving) ? $"Factuur {nummer} {relatie.Naam}" : factuur.Omschrijving,
        };
        Journaalpost post;
        OpenPost open;
        try
        {
            var teBoeken = factuur.NaarFactuur(codes);
            if (verdeling is not null)
            {
                teBoeken = teBoeken with { Regels = [.. teBoeken.Regels.Select(r => r with { Rekeningnummer = verdeling.Rekening })] };
            }

            (post, open) = Factuurboeker.Boek(teBoeken, dagboek!, nummer, schema, codes);
        }
        catch (Exception fout) when (fout is ArgumentException or InvalidOperationException)
        {
            return Uitkomst<string>.Mislukt(fout.Message.Split(" (Parameter", 2)[0]);
        }

        if (Journaalcontrole.Controleer(post, schema, dagboeken) is { Count: > 0 } controle)
        {
            return Uitkomst<string>.Mislukt([.. controle]);
        }

        var boeking = await grootboek.VoegToeAanKeten(db, administratie, post, wie);
        if (verdeling is not null)
        {
            var geboekt = factuur.NaarFactuur(codes);
            var verdeeld = await grootboek.BoekVerdeling(db, administratie, verdeling, memoriaal!, geboekt,
                [.. geboekt.Regels.Select(r => new Verdeelregel(r.Rekeningnummer, -r.Bedrag, r.Omschrijving))], schema, dagboeken, wie, []);
            if (!verdeeld.IsGelukt)
            {
                return Uitkomst<string>.Mislukt([.. verdeeld.Fouten]);
            }

            document.Verdeling = JsonSerializer.Serialize(new Verdelingsgegevens(verdeling.Van, verdeling.Tot, verdeling.Rekening,
                [.. factuur.Regels.Select(r => r.Rekeningnummer)], [.. verdeeld.Waarde!]), Json);
        }

        // Een creditfactuur verrekenen met wat er van de gecrediteerde factuur nog openstaat: beide posten gaan omlaag.
        string? verrekend = null;
        if (gecrediteerd is { Openstaand: > 0 } && open.Openstaand < 0)
        {
            var deel = Math.Min(gecrediteerd.Openstaand, -open.Openstaand);
            gecrediteerd.Openstaand -= deel;
            open = open with { Openstaand = open.Openstaand + deel };
            verrekend = $"{gecrediteerd.Factuurnummer} ({deel:0.00})";
        }

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
        });
        document.Status = Verkoopstatus.Definitief;
        document.Nummer = nummer;
        document.Betalingskenmerk = kenmerk;
        document.Omschrijving = factuur.Omschrijving;
        document.Klantgegevens = JsonSerializer.Serialize(factuur.Klant, Json);
        document.BoekingId = boeking.Id;
        document.DefinitiefOp = tijd.GetUtcNow();
        document.DefinitiefDoor = wie.Naam;
        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.VerkoopfactuurDefinitief, $"{dagboek!.Code} {nummer}", $"{relatie.Code}; {factuur.Totaal(codes):0.00}", administratie);
        if (verrekend is not null)
        {
            await audit.Log(wie, AuditActies.CreditnotaVerrekend, $"{relatie.Code} {nummer}", $"met {verrekend}", administratie);
        }

        // Een ontvangst die al op de bank stond (bijv. een vooruitbetaling met het nummer), lettert nu af.
        await bank.NaFactuur(wie, administratie, open.Id, null);
        return Uitkomst<string>.Met(nummer);
    }

    /// <summary>Een nieuw concept met dezelfde klant en regels, gedateerd vandaag.</summary>
    public async Task<Uitkomst<Guid>> Kopieer(Uitvoerder wie, Guid administratie, Guid id)
    {
        Verkoopinvoer invoer;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            if (await db.Verkoopdocumenten.AsNoTracking().Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == id) is not { } v)
            {
                return Uitkomst<Guid>.Mislukt("Deze factuur bestaat niet (meer).");
            }

            var termijn = v.Vervaldatum.DayNumber - v.Datum.DayNumber;
            invoer = Invoer(v) with { Datum = Vandaag, Vervaldatum = Vandaag.AddDays(termijn), Verdeling = null };
        }

        return await Bewaar(wie, administratie, null, invoer);
    }

    /// <summary>
    /// Een concept-creditfactuur bij een definitieve factuur: dezelfde regels met een negatief aantal. Na het definitief
    /// maken wordt hij verrekend met wat er van de factuur nog openstaat.
    /// </summary>
    public async Task<Uitkomst<Guid>> Crediteer(Uitvoerder wie, Guid administratie, Guid id)
    {
        Verkoopinvoer invoer;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            if (await db.Verkoopdocumenten.AsNoTracking().Include(v => v.Regels).FirstOrDefaultAsync(v => v.Id == id) is not { } v)
            {
                return Uitkomst<Guid>.Mislukt("Deze factuur bestaat niet (meer).");
            }

            if (v.Status != Verkoopstatus.Definitief)
            {
                return Uitkomst<Guid>.Mislukt("Alleen een definitieve factuur kun je crediteren; een concept pas je gewoon aan.");
            }

            if (v.CreditVoorId is not null)
            {
                return Uitkomst<Guid>.Mislukt("Dit is al een creditfactuur.");
            }

            var origineel = Invoer(v);
            invoer = origineel with
            {
                Datum = Vandaag,
                Vervaldatum = Vandaag,
                Omschrijving = Kort($"Creditfactuur bij factuur {v.Nummer}", 500),
                Regels = [.. origineel.Regels.Select(r => r with { Aantal = -r.Aantal })],
                Verdeling = null,
            };
        }

        var uitkomst = await Bewaar(wie, administratie, null, invoer);
        if (uitkomst.IsGelukt)
        {
            await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
            var credit = await db.Verkoopdocumenten.SingleAsync(x => x.Id == uitkomst.Waarde);
            credit.CreditVoorId = id;
            await db.SaveChangesAsync();
        }

        return uitkomst;
    }

    /// <summary>Het verkoopboek over een periode (op factuurdatum).</summary>
    public async Task<IReadOnlyList<Factuurboekregel>> Verkoopboek(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await Factuurboek.Lees(db, Factuursoort.Verkoop, van, tot);
    }

    /// <summary>Het jaar van de laatste verkoopfactuur (of het huidige jaar als er nog geen is).</summary>
    public async Task<int> LaatsteFactuurjaar(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.OpenstaandePosten.AsNoTracking().Where(p => p.Soort == Factuursoort.Verkoop)
            .OrderByDescending(p => p.Datum).Select(p => (int?)p.Datum.Year).FirstOrDefaultAsync() ?? Vandaag.Year;
    }

    /// <summary>De ontbrekende nummers in de reeks van een jaar; hoort altijd leeg te zijn.</summary>
    public async Task<IReadOnlyList<int>> Gaten(Uitvoerder wie, Guid administratie, int jaar)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var begin = jaar.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Factuurnummering.Gaten(jaar, await db.Verkoopdocumenten.AsNoTracking().Where(v => v.Nummer != null && v.Nummer.StartsWith(begin)).Select(v => v.Nummer!).ToListAsync());
    }

    internal static Verkoopfactuur Domein(Verkoopdocument v)
    {
        var klant = string.IsNullOrEmpty(v.Klantgegevens) ? new Partij(v.Relatie, new()) : JsonSerializer.Deserialize<Partij>(v.Klantgegevens, Json)!;
        return new Verkoopfactuur(v.Relatie, klant, v.Nummer, v.Datum, v.Vervaldatum, v.Omschrijving,
            [.. v.Regels.OrderBy(r => r.Volgorde).Select(r => r.NaarDomein())], v.Betalingskenmerk);
    }

    private static Verkoopinvoer Invoer(Verkoopdocument v) =>
        new(v.Relatie, v.Datum, v.Vervaldatum, v.Omschrijving, [.. v.Regels.OrderBy(r => r.Volgorde).Select(r => r.NaarDomein())], v.Referentie, v.Tekst,
            Verdeling(v) is { } g ? new Periodeverdeling(g.Van, g.Tot, g.Rekening) : null, v.LayoutId);

    private static Verdelingsgegevens? Verdeling(Verkoopdocument v) =>
        v.Verdeling is { } json ? JsonSerializer.Deserialize<Verdelingsgegevens>(json, Json) : null;

    /// <summary>Een berekening die kan mislukken op een concept met een onbekende btw-code: dan de terugval.</summary>
    private static T Veilig<T>(Func<T> berekening, T terugval)
    {
        try
        {
            return berekening();
        }
        catch (ArgumentException)
        {
            return terugval;
        }
    }

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;

    private static string? Leeg(string? tekst, int lengte) => string.IsNullOrWhiteSpace(tekst) ? null : Kort(tekst.Trim(), lengte);
}
