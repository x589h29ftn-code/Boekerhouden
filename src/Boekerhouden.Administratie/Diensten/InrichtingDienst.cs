using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <param name="Klant">De gegevens als klant; leeg = ongewijzigd laten.</param>
public sealed record RelatieInvoer(string Naam, string? BtwNummer, string? KvkNummer, string? Tegenrekening, Klantgegevens? Klant = null);

/// <summary>
/// De gegevens van een relatie als klant: adres en contact voor op de factuur, betalingstermijn, en de omzetrekening
/// en btw-code die bij een nieuwe factuurregel worden voorgesteld.
/// </summary>
public sealed record Klantgegevens(
    bool IsKlant,
    string? Straat = null,
    string? Postcode = null,
    string? Plaats = null,
    string? Land = null,
    string? Email = null,
    string? Contactpersoon = null,
    int? Betalingstermijn = null,
    string? Omzetrekening = null,
    string? Verkoopbtwcode = null)
{
    internal static Klantgegevens Van(Handelsrelatie r) =>
        new(r.Klant, r.Straat, r.Postcode, r.Plaats, r.Land, r.Email, r.Contactpersoon, r.Betalingstermijn, r.Omzetrekening, r.Verkoopbtwcode);
}

/// <summary>Adres en contact van de eigen onderneming (afzender op de verkoopfacturen).</summary>
public sealed record Contactgegevens(
    string? Straat = null,
    string? Postcode = null,
    string? Plaats = null,
    string? Land = null,
    string? Email = null,
    string? Telefoon = null,
    string? Website = null);

/// <param name="ControleVanaf">Facturen vanaf dit bedrag altijd zelf controleren.</param>
/// <param name="ControleAfwijking">Facturen die meer dan dit percentage afwijken van de vorige altijd zelf controleren.</param>
public sealed record RelatieRegel(
    Guid Id,
    string Code,
    string Naam,
    IReadOnlyList<string> Ibans,
    string? BtwNummer,
    string? KvkNummer,
    string? Tegenrekening,
    decimal? ControleVanaf = null,
    decimal? ControleAfwijking = null,
    Klantgegevens? Klant = null);

public sealed record RegelRegel(Guid Id, int Volgorde, Verwerkingsregel Regel);

/// <summary>Een bevestigde verwerking met het moment waarop.</summary>
public sealed record Bevestigd(DateTimeOffset Tijdstip, Bevestiging Bevestiging);

/// <summary>
/// Wat het pakket van een leverancier geleerd heeft: de keuze (rekening, btw, omschrijving) met hoe vaak op rij
/// bevestigd en tot welk bedrag automatisch, de bevestigingen zelf, de plekken op de factuur en de regels die gelden.
/// </summary>
/// <param name="Drempel">Na hoeveel keer op rij het pakket zelf verwerkt (instelling van de administratie).</param>
public sealed record GeleerdeLeverancier(
    RelatieRegel Relatie,
    Geleerd? Inkoop,
    int Drempel,
    IReadOnlyList<Bevestigd> Bevestigingen,
    IReadOnlyList<Zoekgebied> Plekken,
    IReadOnlyList<RegelRegel> Regels);

/// <summary>De gegevens en instellingen van een administratie.</summary>
/// <param name="Afwijkend">Waarin de administratie afwijkt van het kantoor; leeg = de kantoorinstelling.</param>
/// <param name="Kantoor">De instellingen van het kantoor (alleen om te tonen; wijzigen gaat via <see cref="KantoorDienst"/>).</param>
public sealed record Instellingen(
    string Naam,
    string? KvkNummer,
    string? BtwNummer,
    IReadOnlyList<string> Ibans,
    AfwijkendeInstellingen Afwijkend,
    Verwerkingsinstellingen? Kantoor = null,
    Contactgegevens? Contact = null)
{
    /// <summary>Wat er voor de administratie geldt.</summary>
    public Verwerkingsinstellingen Geldend => Afwijkend.Op(Kantoor ?? Verwerkingsinstellingen.Standaard);
}

/// <summary>
/// De inrichting van een administratie: relaties (leveranciers en klanten), regels voor automatisch verwerken en de
/// instellingen. Een IBAN wijzigen is gevoelig (factuurfraude, beveiliging §4.4): alleen na bevestigen met het eigen
/// wachtwoord, met de oude en nieuwe waarde in het wijzigingslog.
/// </summary>
public sealed class InrichtingDienst(Toegang toegang, AuditDienst audit, TimeProvider tijd, IServiceScopeFactory scopeFactory, Werkomgeving omgeving)
{
    public async Task<IReadOnlyList<RelatieRegel>> Relaties(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await db.Relaties.AsNoTracking().OrderBy(r => r.Naam).ToListAsync())
            .Select(r => new RelatieRegel(r.Id, r.Code, r.Naam, r.Ibans, r.BtwNummer, r.KvkNummer, r.Tegenrekening, r.ControleVanaf, r.ControleAfwijking, Klantgegevens.Van(r)))];
    }

    /// <summary>Wat het pakket van deze leverancier geleerd heeft (inkoopfacturen), of leeg als de relatie niet bestaat.</summary>
    public async Task<GeleerdeLeverancier?> Geleerd(Uitvoerder wie, Guid administratie, string relatie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        if (await db.Relaties.AsNoTracking().FirstOrDefaultAsync(r => r.Code == relatie) is not { } r)
        {
            return null;
        }

        var adm = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        var geheugen = new Leergeheugen(adm.Afwijkend().Op(await KantoorDienst.Lees(db)));
        var sleutel = Leersleutel(relatie);
        var keuzes = await db.Leerkeuzes.AsNoTracking().Where(k => k.Sleutel == sleutel).OrderBy(k => k.Id).ToListAsync();
        foreach (var keuze in keuzes)
        {
            geheugen.Onthoud(keuze.Sleutel, keuze.NaarDomein());
        }

        var voorstel = geheugen.Voorstel(new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, relatie, r.Naam, 0m));
        var plekken = await db.Zoekplekken.AsNoTracking().Where(z => z.Relatie == relatie).OrderBy(z => z.Veld).ToListAsync();
        var regels = (await Regels(wie, administratie))
            .Where(x => x.Regel.Relatie == relatie || (x.Regel.NaamBevat is { Length: > 0 } deel && r.Naam.Contains(deel, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new GeleerdeLeverancier(
            new RelatieRegel(r.Id, r.Code, r.Naam, r.Ibans, r.BtwNummer, r.KvkNummer, r.Tegenrekening, r.ControleVanaf, r.ControleAfwijking, Klantgegevens.Van(r)),
            voorstel,
            geheugen.Drempel,
            [.. keuzes.Select(k => new Bevestigd(k.Tijdstip, k.NaarDomein()))],
            [.. plekken.Select(z => z.NaarDomein())],
            regels);
    }

    /// <summary>Vergeet de geleerde keuzes bij deze leverancier: het pakket begint weer bij nul (eerst zelf kiezen).</summary>
    public async Task<Uitkomst> VergeetGeleerd(Uitvoerder wie, Guid administratie, string relatie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var sleutel = Leersleutel(relatie);
        var keuzes = await db.Leerkeuzes.Where(k => k.Sleutel == sleutel).ToListAsync();
        if (keuzes.Count == 0)
        {
            return Uitkomst.Mislukt("Er is bij deze leverancier niets geleerd.");
        }

        db.Leerkeuzes.RemoveRange(keuzes);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.GeleerdVergeten, relatie, $"{keuzes.Count} bevestiging(en)", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Vergeet de plek van een veld op de factuur van deze leverancier.</summary>
    public async Task<Uitkomst> VergeetPlek(Uitvoerder wie, Guid administratie, string relatie, Factuurveld veld)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Zoekplekken.FirstOrDefaultAsync(z => z.Relatie == relatie && z.Veld == veld) is not { } plek)
        {
            return Uitkomst.Mislukt("Deze plek bestaat niet (meer).");
        }

        db.Zoekplekken.Remove(plek);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ZoekplekVergeten, relatie, veld.ToString(), administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Wanneer facturen van deze leverancier altijd ter controle komen, ook als ze herkend en geleerd zijn: vanaf een
    /// bedrag (inclusief btw) en/of bij een afwijking van meer dan een percentage ten opzichte van de vorige factuur.
    /// Leeg = geen eigen grens.
    /// </summary>
    public async Task<Uitkomst> ZetControle(Uitvoerder wie, Guid administratie, string relatie, decimal? vanaf, decimal? afwijking)
    {
        if (vanaf is < 0 || afwijking is < 0)
        {
            return Uitkomst.Mislukt("Een grens kan niet negatief zijn.");
        }

        if (afwijking is > 1000)
        {
            return Uitkomst.Mislukt("Een afwijking van meer dan 1000% heeft geen zin.");
        }

        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Relaties.FirstOrDefaultAsync(r => r.Code == relatie) is not { } r)
        {
            return Uitkomst.Mislukt("Deze relatie bestaat niet (meer).");
        }

        var oud = Controletekst(r.ControleVanaf, r.ControleAfwijking);
        (r.ControleVanaf, r.ControleAfwijking) = (vanaf is { } v ? decimal.Round(v, 2) : null, afwijking is { } a ? decimal.Round(a, 2) : null);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.ControleIngesteld, relatie, $"{oud} → {Controletekst(r.ControleVanaf, r.ControleAfwijking)}", administratie);
        return Uitkomst.Gelukt;
    }

    private static string Controletekst(decimal? vanaf, decimal? afwijking) =>
        vanaf is null && afwijking is null
            ? "geen eigen grens"
            : string.Join(", ", new[]
            {
                vanaf is { } v ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"vanaf {v:0.00}") : null,
                afwijking is { } a ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"afwijking {a:0.##}%") : null,
            }.Where(x => x is not null));

    /// <summary>De sleutel van het leergeheugen voor inkoopfacturen van een relatie.</summary>
    private static string Leersleutel(string relatie) =>
        Leergeheugen.Sleutel(new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, relatie, null, 0m))!;

    public async Task<Uitkomst<Guid>> VoegRelatieToe(Uitvoerder wie, Guid administratie, RelatieInvoer invoer, string? iban)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        var fouten = await Controleer(db, invoer);
        var schoon = string.IsNullOrWhiteSpace(iban) ? null : Iban.Normaliseer(iban);
        if (schoon is not null && !Iban.IsGeldig(schoon))
        {
            fouten.Add("Het IBAN is niet geldig.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Guid>.Mislukt([.. fouten]);
        }

        var bestaand = (await db.Relaties.AsNoTracking().ToListAsync()).Select(r => r.NaarDomein()).ToList();
        var relatie = new Handelsrelatie
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Code = InkoopDienst.NieuweCode(invoer.Naam, bestaand),
            AangemaaktOp = tijd.GetUtcNow(),
            Ibans = schoon is null ? [] : [schoon],
        };
        Neem(relatie, invoer);
        db.Relaties.Add(relatie);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RelatieToegevoegd, $"{relatie.Code} {relatie.Naam}", schoon, administratie);
        return Uitkomst<Guid>.Met(relatie.Id);
    }

    public async Task<Uitkomst> WijzigRelatie(Uitvoerder wie, Guid administratie, Guid id, RelatieInvoer invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        if (await db.Relaties.FirstOrDefaultAsync(r => r.Id == id) is not { } relatie)
        {
            return Uitkomst.Mislukt("Deze relatie bestaat niet (meer).");
        }

        var fouten = await Controleer(db, invoer);
        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        Neem(relatie, invoer);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RelatieGewijzigd, $"{relatie.Code} {relatie.Naam}", null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Zet de bankrekeningen van een relatie. Alleen na bevestigen met het eigen wachtwoord; het log krijgt de oude en
    /// de nieuwe IBAN's, zodat een gewijzigd rekeningnummer altijd terug te vinden is.
    /// </summary>
    public async Task<Uitkomst> ZetIbans(Uitvoerder wie, Guid administratie, Guid id, IReadOnlyList<string> ibans, string wachtwoord)
    {
        ArgumentNullException.ThrowIfNull(ibans);
        await using var db = await toegang.Open(wie, administratie, Recht.Invoeren);
        // In de Windows-app op één pc is er geen wachtwoord; het log houdt oud en nieuw wel bij.
        if (!omgeving.Lokaal && !await WachtwoordKlopt(wie, wachtwoord))
        {
            await audit.Log(wie, AuditActies.IbanWijzigingGeweigerd, id.ToString(), "wachtwoord onjuist", administratie);
            return Uitkomst.Mislukt("Bevestig met je wachtwoord; het ingevulde wachtwoord klopt niet.");
        }

        if (await db.Relaties.FirstOrDefaultAsync(r => r.Id == id) is not { } relatie)
        {
            return Uitkomst.Mislukt("Deze relatie bestaat niet (meer).");
        }

        var schoon = ibans.Where(i => !string.IsNullOrWhiteSpace(i)).Select(Iban.Normaliseer).Distinct().ToList();
        if (schoon.FirstOrDefault(i => !Iban.IsGeldig(i)) is { } ongeldig)
        {
            return Uitkomst.Mislukt($"{Iban.Toon(ongeldig)} is geen geldig IBAN.");
        }

        var oud = string.Join(", ", relatie.Ibans);
        relatie.Ibans = schoon;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.IbanGewijzigd, $"{relatie.Code} {relatie.Naam}", $"van [{oud}] naar [{string.Join(", ", schoon)}]", administratie);
        return Uitkomst.Gelukt;
    }

    public async Task<IReadOnlyList<RegelRegel>> Regels(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return [.. (await db.Regels.AsNoTracking().OrderBy(r => r.Volgorde).ToListAsync()).Select(r => new RegelRegel(r.Id, r.Volgorde, r.NaarDomein()))];
    }

    /// <summary>Voegt een regel achteraan toe. Regels bepalen wat automatisch geboekt wordt, dus alleen het kantoor.</summary>
    public async Task<Uitkomst> VoegRegelToe(Uitvoerder wie, Guid administratie, Verwerkingsregel regel)
    {
        ArgumentNullException.ThrowIfNull(regel);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(regel.Naam))
        {
            fouten.Add("Geef de regel een naam.");
        }

        if (!regel.HeeftVoorwaarden)
        {
            fouten.Add("Een regel heeft minstens één voorwaarde nodig.");
        }

        if (!await db.Rekeningen.AnyAsync(r => r.Nummer == regel.Rekeningnummer))
        {
            fouten.Add($"Rekening {regel.Rekeningnummer} bestaat niet.");
        }

        if (regel.BtwCode is not null && BtwCodes.Zoek((await BtwDienst.Lees(db)).Codes, regel.BtwCode) is null)
        {
            fouten.Add($"Btw-code {regel.BtwCode} bestaat niet.");
        }

        if (regel.BedragVan is { } van && regel.BedragTot is { } tot && van > tot)
        {
            fouten.Add("Het bedrag 'van' is hoger dan 'tot'.");
        }

        if (regel.TegenIban is { } iban && !Iban.IsGeldig(iban))
        {
            fouten.Add("Het IBAN van de tegenrekening is niet geldig.");
        }

        if (Omschrijvingssjabloon.Onbekend(regel.Omschrijving) is { Count: > 0 } onbekend)
        {
            fouten.Add($"Onbekende variabele in de omschrijving: {string.Join(", ", onbekend.Select(o => "#" + o))}.");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        var volgorde = (await db.Regels.MaxAsync(r => (int?)r.Volgorde) ?? 0) + 1;
        db.Regels.Add(new Automatiseringsregel
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Volgorde = volgorde,
            Naam = regel.Naam.Trim(),
            Rekeningnummer = regel.Rekeningnummer,
            BtwCode = regel.BtwCode,
            Automatisch = regel.Automatisch,
            Soort = regel.Soort,
            Relatie = Leeg(regel.Relatie),
            NaamBevat = Leeg(regel.NaamBevat),
            BedragVan = regel.BedragVan,
            BedragTot = regel.BedragTot,
            OmschrijvingBevat = Leeg(regel.OmschrijvingBevat),
            TegenIban = regel.TegenIban is null ? null : Iban.Normaliseer(regel.TegenIban),
            Richting = regel.Richting,
            Omschrijving = Leeg(regel.Omschrijving),
        });
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RegelToegevoegd, regel.Naam, $"→ {regel.Rekeningnummer} {regel.BtwCode}{(regel.Automatisch ? ", automatisch" : null)}", administratie);
        return Uitkomst.Gelukt;
    }

    public async Task<Uitkomst> VerwijderRegel(Uitvoerder wie, Guid administratie, Guid id)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Regels.FirstOrDefaultAsync(r => r.Id == id) is not { } regel)
        {
            return Uitkomst.Mislukt("Deze regel bestaat niet (meer).");
        }

        db.Regels.Remove(regel);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RegelVerwijderd, regel.Naam, null, administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>Schuift een regel één plek omhoog (eerder toegepast) of omlaag; de eerste regel die past, wint.</summary>
    public async Task<Uitkomst> VerplaatsRegel(Uitvoerder wie, Guid administratie, Guid id, bool omhoog)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var regels = await db.Regels.OrderBy(r => r.Volgorde).ToListAsync();
        var plek = regels.FindIndex(r => r.Id == id);
        var ander = omhoog ? plek - 1 : plek + 1;
        if (plek < 0 || ander < 0 || ander >= regels.Count)
        {
            return Uitkomst.Gelukt;
        }

        (regels[plek].Volgorde, regels[ander].Volgorde) = (regels[ander].Volgorde, regels[plek].Volgorde);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.RegelVerplaatst, regels[plek].Naam, omhoog ? "omhoog" : "omlaag", administratie);
        return Uitkomst.Gelukt;
    }

    public async Task<Instellingen> Instellingen(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie);
        return new Instellingen(a.Naam, a.KvkNummer, a.BtwNummer, a.Ibans, a.Afwijkend(), await KantoorDienst.Lees(db),
            new Contactgegevens(a.Straat, a.Postcode, a.Plaats, a.Land, a.Email, a.Telefoon, a.Website));
    }

    public async Task<Uitkomst> WijzigInstellingen(Uitvoerder wie, Guid administratie, Instellingen invoer)
    {
        ArgumentNullException.ThrowIfNull(invoer);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Vul de naam in.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.KvkNummer) && !(invoer.KvkNummer.Trim().Length == 8 && invoer.KvkNummer.Trim().All(char.IsAsciiDigit)))
        {
            fouten.Add("Een KvK-nummer heeft 8 cijfers.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.BtwNummer) && !BtwNummer.IsGeldig(invoer.BtwNummer))
        {
            fouten.Add("Het btw-nummer is niet geldig (bijv. NL123456789B01).");
        }

        if (invoer.Contact?.Land is { Length: > 0 } land && land.Trim().Length != 2)
        {
            fouten.Add("Het land is een landcode van twee letters (NL, BE, DE …).");
        }

        var ibans = invoer.Ibans.Where(i => !string.IsNullOrWhiteSpace(i)).Select(Iban.Normaliseer).Distinct().ToList();
        fouten.AddRange(ibans.Where(i => !Iban.IsGeldig(i)).Select(i => $"{i} is geen geldig IBAN."));
        var kantoor = await KantoorDienst.Lees(db);
        var afwijkend = invoer.Afwijkend with
        {
            Documentmap = Leeg(invoer.Afwijkend.Documentmap),
            Verschilrekening = Leeg(invoer.Afwijkend.Verschilrekening),
            Betaalverschil = invoer.Afwijkend.Betaalverschil is { } grens ? decimal.Round(grens, 2) : null,
        };
        fouten.AddRange(afwijkend.Op(kantoor).Controleer());
        if (afwijkend.Op(kantoor) is { Betaalverschil: > 0, Verschilrekening: { } verschilrekening })
        {
            var (schema, _) = await GrootboekDienst.Inrichting(db);
            if (schema.Zoek(verschilrekening) is not { } rekening)
            {
                fouten.Add($"Rekening {verschilrekening} voor betaalverschillen bestaat niet in deze administratie.");
            }
            else if (rekening.Functie is Boekhouding.Grootboek.Rekeningfunctie.Bank or Boekhouding.Grootboek.Rekeningfunctie.Debiteuren or Boekhouding.Grootboek.Rekeningfunctie.Crediteuren or Boekhouding.Grootboek.Rekeningfunctie.Voorbelasting or Boekhouding.Grootboek.Rekeningfunctie.AfTeDragenBtw)
            {
                fouten.Add($"Op {rekening.Nummer} {rekening.Naam} boek je geen betaalverschillen: die rekening hoort bij de bank, de openstaande posten of de btw.");
            }
        }
        if (fouten.Count > 0)
        {
            return Uitkomst.Mislukt([.. fouten]);
        }

        var a = await db.Administraties.SingleAsync(x => x.Id == administratie);
        var details = $"{KantoorDienst.Verschil(a.Afwijkend().Op(kantoor), afwijkend.Op(kantoor))}; IBAN's [{string.Join(", ", a.Ibans)}] → [{string.Join(", ", ibans)}]";
        a.Naam = invoer.Naam.Trim();
        a.KvkNummer = Leeg(invoer.KvkNummer);
        a.BtwNummer = string.IsNullOrWhiteSpace(invoer.BtwNummer) ? null : BtwNummer.Normaliseer(invoer.BtwNummer);
        a.Ibans = ibans;
        a.DrempelAutomatisch = afwijkend.Drempel;
        a.Bandbreedte = afwijkend.Bandbreedte;
        a.LimietAfwijkend = afwijkend.LimietAfwijkend;
        a.LimietAutomatisch = afwijkend.LimietAfwijkend ? afwijkend.Limiet : null;
        a.Betalingstermijn = afwijkend.Betalingstermijn;
        a.Adrescontrole = afwijkend.Adrescontrole;
        a.Documentmap = afwijkend.Documentmap;
        a.Betaalverschil = afwijkend.Betaalverschil;
        a.Verschilrekening = afwijkend.Verschilrekening;
        if (invoer.Contact is { } c)
        {
            (a.Straat, a.Postcode, a.Plaats, a.Land) = (Leeg(c.Straat), Leeg(c.Postcode), Leeg(c.Plaats), Leeg(c.Land)?.ToUpperInvariant());
            (a.Email, a.Telefoon, a.Website) = (Leeg(c.Email), Leeg(c.Telefoon), Leeg(c.Website));
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.InstellingenGewijzigd, a.Naam, details, administratie);
        return Uitkomst.Gelukt;
    }

    private static async Task<List<string>> Controleer(AdministratieDbContext db, RelatieInvoer invoer)
    {
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(invoer.Naam))
        {
            fouten.Add("Vul de naam in.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.BtwNummer) && !BtwNummer.IsGeldig(invoer.BtwNummer))
        {
            fouten.Add("Het btw-nummer is niet geldig.");
        }

        if (!string.IsNullOrWhiteSpace(invoer.Tegenrekening) && !await db.Rekeningen.AnyAsync(r => r.Nummer == invoer.Tegenrekening.Trim()))
        {
            fouten.Add($"Rekening {invoer.Tegenrekening} bestaat niet.");
        }

        if (invoer.Klant is { } k)
        {
            if (!string.IsNullOrWhiteSpace(k.Omzetrekening) && !await db.Rekeningen.AnyAsync(r => r.Nummer == k.Omzetrekening.Trim()))
            {
                fouten.Add($"Rekening {k.Omzetrekening} bestaat niet.");
            }

            if (k.Land is { Length: > 0 } land && land.Trim().Length != 2)
            {
                fouten.Add("Het land is een landcode van twee letters (NL, BE, DE …).");
            }

            if (k.Betalingstermijn is < 0 or > 365)
            {
                fouten.Add("De betalingstermijn ligt tussen 0 en 365 dagen.");
            }

            if (!string.IsNullOrWhiteSpace(k.Email) && !k.Email.Contains('@', StringComparison.Ordinal))
            {
                fouten.Add("Het e-mailadres is niet geldig.");
            }
        }

        return fouten;
    }

    private static void Neem(Handelsrelatie relatie, RelatieInvoer invoer)
    {
        relatie.Naam = invoer.Naam.Trim();
        relatie.BtwNummer = string.IsNullOrWhiteSpace(invoer.BtwNummer) ? null : BtwNummer.Normaliseer(invoer.BtwNummer);
        relatie.KvkNummer = Leeg(invoer.KvkNummer);
        relatie.Tegenrekening = Leeg(invoer.Tegenrekening);
        if (invoer.Klant is { } k)
        {
            relatie.Klant = k.IsKlant;
            (relatie.Straat, relatie.Postcode, relatie.Plaats, relatie.Land) = (Leeg(k.Straat), Leeg(k.Postcode), Leeg(k.Plaats), Leeg(k.Land)?.ToUpperInvariant());
            (relatie.Email, relatie.Contactpersoon, relatie.Betalingstermijn) = (Leeg(k.Email), Leeg(k.Contactpersoon), k.Betalingstermijn);
            (relatie.Omzetrekening, relatie.Verkoopbtwcode) = (Leeg(k.Omzetrekening), Leeg(k.Verkoopbtwcode)?.ToUpperInvariant());
        }
    }

    private async Task<bool> WachtwoordKlopt(Uitvoerder wie, string wachtwoord)
    {
        if (wie.Id is not { } id || string.IsNullOrEmpty(wachtwoord))
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Gebruiker>>();
        return await users.FindByIdAsync(id.ToString()) is { } gebruiker && await users.CheckPasswordAsync(gebruiker, wachtwoord);
    }

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
}
