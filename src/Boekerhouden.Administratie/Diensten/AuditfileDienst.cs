using System.Reflection;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Auditfiles;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een gemaakte auditfile: de inhoud en een bestandsnaam.</summary>
public sealed record Auditfilebestand(byte[] Inhoud, string Bestandsnaam, int Boekingen);

/// <summary>Wat er van een ingelezen auditfile is overgenomen.</summary>
public sealed record Auditfileinlezing(Guid Administratie, string Naam, int Rekeningen, int Relaties, int Boekingen, int OpenstaandePosten);

/// <summary>
/// De XML Auditfile Financieel (XAF): een boekjaar van een administratie als XAF 3.2 (voor de Belastingdienst, de
/// accountant of een ander pakket), en een auditfile uit een ander pakket inlezen als nieuwe administratie.
/// </summary>
public sealed class AuditfileDienst(IDbContextFactory<AdministratieDbContext> dbFactory, Toegang toegang, AuditDienst audit, TimeProvider tijd)
{
    /// <summary>Groter dan dit leest het pakket niet in.</summary>
    public const long MaximumBytes = 200L * 1024 * 1024;

    /// <summary>
    /// Een boekjaar als XAF 3.2: rekeningschema met RGS-code, relaties, btw-codes, de beginbalans (de saldi van de
    /// balansrekeningen aan het begin van het jaar, met het resultaat van eerdere jaren op de rekening voor het
    /// onverdeelde resultaat) en alle geldende boekingen van het jaar per dagboek.
    /// </summary>
    public async Task<Auditfilebestand> Maak(Uitvoerder wie, Guid administratie, int boekjaar)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var adm = await db.Administraties.AsNoTracking().SingleAsync(a => a.Id == administratie);
        var (schema, dagboeken) = await GrootboekDienst.Inrichting(db);
        var begin = new DateOnly(boekjaar, 1, 1);
        var eind = new DateOnly(boekjaar, 12, 31);

        // Saldi voor het begin van het jaar (optellen in het geheugen: SQLite telt geen decimals op).
        var eerder = (await (from r in db.GeldendeRegels
                             join b in db.Boekingen on r.BoekingId equals b.Id
                             where b.Datum < begin
                             select new { r.Rekeningnummer, r.Bedrag }).ToListAsync())
            .GroupBy(r => r.Rekeningnummer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag), StringComparer.Ordinal);
        var beginbalans = schema.Alle.Where(r => r.IsBalans && eerder.GetValueOrDefault(r.Nummer) != 0)
            .Select(r => new AuditfileRegel(r.Nummer, eerder[r.Nummer])).ToList();
        var resultaatEerder = schema.Alle.Where(r => !r.IsBalans).Sum(r => eerder.GetValueOrDefault(r.Nummer));
        if (resultaatEerder != 0 && schema.Alle.FirstOrDefault(r => r.Functie == Rekeningfunctie.OnverdeeldResultaat) is { } onverdeeld)
        {
            var bestaand = beginbalans.FindIndex(r => r.Rekening == onverdeeld.Nummer);
            var regel = new AuditfileRegel(onverdeeld.Nummer, (bestaand >= 0 ? beginbalans[bestaand].Bedrag : 0m) + resultaatEerder);
            if (bestaand >= 0)
            {
                beginbalans[bestaand] = regel;
            }
            else
            {
                beginbalans.Add(regel);
            }
        }

        var boekingen = await db.Geldend.AsNoTracking().Include(b => b.Regels).Where(b => b.Datum >= begin && b.Datum <= eind)
            .OrderBy(b => b.Datum).ThenBy(b => b.Volgnummer).ToListAsync();
        var relaties = await db.Relaties.AsNoTracking().OrderBy(r => r.Code).ToListAsync();
        var btw = (await BtwDienst.Lees(db)).Op(eind);
        var afdragen = schema.Alle.FirstOrDefault(r => r.Functie == Rekeningfunctie.AfTeDragenBtw)?.Nummer;
        var vorderen = schema.Alle.FirstOrDefault(r => r.Functie == Rekeningfunctie.Voorbelasting)?.Nummer;

        var auditfile = new Auditfile(
            "3.2",
            new AuditfileKop(boekjaar, begin, eind, "EUR", DateOnly.FromDateTime(tijd.GetLocalNow().Date), "Sifer", Versie()),
            new AuditfileBedrijf(adm.Naam, adm.KvkNummer, adm.BtwNummer, adm.Straat, adm.Postcode, adm.Plaats, adm.Land),
            [.. relaties.Select(r => new AuditfileRelatie(r.Code, r.Naam, r.KvkNummer, r.BtwNummer, r.Klant, !r.Klant || r.Tegenrekening is not null, [.. r.Ibans]))],
            [.. schema.Alle.Select(r => new AuditfileRekening(r.Nummer, r.Naam, r.Soort, r.RgsCode))],
            [.. btw.Select(c => new AuditfileBtwcode(c.Code, c.Omschrijving, c.Richting == BtwRichting.Verkoop ? afdragen : null, c.Richting == BtwRichting.Inkoop ? vorderen : null))],
            beginbalans.Count > 0 ? begin : null,
            beginbalans,
            [.. dagboeken.Select(d => new AuditfileDagboek(d.Code, d.Naam, d.Soort, d.Rekeningnummer,
                [.. boekingen.Where(b => b.Dagboek == d.Code && b.Regels.Count > 0).Select(b => new AuditfileBoeking(b.Boekstuk, b.Omschrijving, b.Datum, b.Datum.Month,
                    [.. b.Regels.OrderBy(r => r.Regelnummer).Select(r => new AuditfileRegel(r.Rekeningnummer, r.Bedrag, r.Omschrijving, r.Relatie, r.Factuurnummer, r.BtwCode))]))]))
              .Where(d => d.Boekingen.Count > 0)]);
        var naam = $"Auditfile {Bestandsveilig(adm.Naam)} {boekjaar}.xaf";
        await audit.Log(wie, AuditActies.AuditfileGemaakt, naam, $"{auditfile.AantalBoekingen} boekingen", administratie);
        return new Auditfilebestand(AuditfileSchrijver.Schrijf(auditfile), naam, auditfile.AantalBoekingen);
    }

    /// <summary>
    /// Leest een auditfile (XAF 3.1/3.2, van 4.0 wat hetzelfde heet) als nieuwe administratie: de onderneming, het
    /// rekeningschema (met RGS-code en, waar die te herkennen is, de functie: bank, debiteuren, btw …), de relaties, de
    /// dagboeken, de beginbalans en alle boekingen, in de keten van het pakket. Openstaande posten volgen uit de regels op
    /// debiteuren en crediteuren met een relatie en een factuurnummer. Klopt er iets niet (totalen, evenwicht, een
    /// onbekende rekening), dan wordt niets ingelezen. De inlezer wordt lid met de rol Kantoor.
    /// </summary>
    public async Task<Uitkomst<Auditfileinlezing>> LeesIn(Uitvoerder wie, Stream inhoud, string bestandsnaam, Ondernemingsvorm vorm, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(wie);
        ArgumentNullException.ThrowIfNull(inhoud);
        if (wie.Id is not { } gebruiker)
        {
            return Uitkomst<Auditfileinlezing>.Mislukt("Log eerst in.");
        }

        Auditfile a;
        IReadOnlyList<string> fouten;
        try
        {
            (a, fouten) = AuditfileLezer.Lees(inhoud);
        }
        catch (AuditfileException fout)
        {
            return Uitkomst<Auditfileinlezing>.Mislukt($"{Path.GetFileName(bestandsnaam)}: {fout.Message}");
        }

        if (fouten.Count > 0)
        {
            return Uitkomst<Auditfileinlezing>.Mislukt([.. fouten.Take(10).Select(f => $"{Path.GetFileName(bestandsnaam)}: {f}")]);
        }

        var id = Guid.NewGuid();
        await using var db = await dbFactory.CreateDbContextAsync(annulering);
        db.Geopend = id;
        var btwcodes = (await BtwDienst.Lees(db)).Codes.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rekeningen = a.Rekeningen.Select(r => new Rekening(Kort(r.Nummer, 20), Kort(r.Naam, 200), r.Soort, r.RgsCode is { Length: <= 30 } rgs ? rgs : null, Functie(r, a))).ToList();
        ZetOnverdeeldResultaat(rekeningen, vorm);
        var schema = new Rekeningschema(rekeningen);

        // Dagboeken: de codes uit de auditfile (hoogstens 10 tekens, uniek), en een eigen memoriaal voor de beginbalans.
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        var dagboeken = new List<Dagboek>();
        foreach (var d in a.Dagboeken)
        {
            var code = Uniek(Kort(d.Code, 10), dagboeken.Select(x => x.Code));
            codes[d.Code] = code;
            var tegen = d.Tegenrekening ?? Tegenrekening(d, schema);
            dagboeken.Add(new Dagboek(code, Kort(d.Naam, 100), d.Soort, d.Soort is Dagboeksoort.Bank or Dagboeksoort.Kas ? tegen : null));
        }

        var beginCode = Uniek("BEG", dagboeken.Select(x => x.Code));
        if (a.Beginbalans.Count > 0)
        {
            dagboeken.Add(new Dagboek(beginCode, "Beginbalans", Dagboeksoort.Memoriaal));
        }

        // De boekingen als journaalposten; eerst alles controleren, dan pas opslaan.
        var posten = new List<Journaalpost>();
        if (a.Beginbalans.Count > 0)
        {
            posten.Add(new Journaalpost(beginCode, $"{a.Kop.Boekjaar}-0000", a.BeginbalansDatum ?? a.Kop.Begin, "Beginbalans",
                [.. a.Beginbalans.Where(r => r.Bedrag != 0).Select(r => new Journaalregel(Kort(r.Rekening, 20), r.Bedrag, "Beginbalans"))]));
        }

        foreach (var d in a.Dagboeken)
        {
            var gebruikt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in d.Boekingen)
            {
                var boekstuk = Uniek(Kort(b.Boekstuk.Length > 0 ? b.Boekstuk : $"{b.Datum:yyyy}-{gebruikt.Count + 1:0000}", 30), gebruikt);
                gebruikt.Add(boekstuk);
                var omschrijving = Kort(b.Omschrijving ?? b.Regels.Select(r => r.Omschrijving).FirstOrDefault(o => o is not null) ?? $"{d.Naam} {boekstuk}", 500);
                posten.Add(new Journaalpost(codes[d.Code], boekstuk, b.Datum, omschrijving,
                    [.. b.Regels.Where(r => r.Bedrag != 0).Select(r => new Journaalregel(Kort(r.Rekening, 20), r.Bedrag, KortOfLeeg(r.Omschrijving, 500),
                        r.BtwCode is { } c && btwcodes.Contains(c) ? c.ToUpperInvariant() : null, KortOfLeeg(r.Relatie, 50), KortOfLeeg(r.Factuurnummer, 50)))]));
            }
        }

        var controle = posten.SelectMany(p => Journaalcontrole.Controleer(p, schema, dagboeken).Select(f => $"{p.Dagboek} {p.Boekstuk}: {f}")).Take(10).ToList();
        if (controle.Count > 0)
        {
            return Uitkomst<Auditfileinlezing>.Mislukt([.. controle]);
        }

        var naam = Kort(a.Bedrijf.Naam, 300);
        var nu = tijd.GetUtcNow();
        await using var transactie = await db.Database.BeginTransactionAsync(annulering);
        db.Administraties.Add(new Data.Administratie
        {
            Id = id,
            Naam = naam,
            Ondernemingsvorm = vorm,
            KvkNummer = a.Bedrijf.KvkNummer,
            BtwNummer = a.Bedrijf.BtwNummer is { } btwnr && BtwNummer.IsGeldig(btwnr) ? BtwNummer.Normaliseer(btwnr) : null,
            Straat = KortOfLeeg(a.Bedrijf.Straat, 200),
            Postcode = KortOfLeeg(a.Bedrijf.Postcode, 20),
            Plaats = KortOfLeeg(a.Bedrijf.Plaats, 100),
            Land = a.Bedrijf.Land is { Length: 2 } land && land != "NL" ? land : null,
            AangemaaktOp = nu,
            AangemaaktDoor = wie.Naam,
        });
        db.Lidmaatschappen.Add(new Lidmaatschap { AdministratieId = id, GebruikerId = gebruiker, Rol = Lidmaatschapsrol.Kantoor });
        db.Rekeningen.AddRange(rekeningen.Select(r => new Grootboekrekening
        {
            Id = Guid.NewGuid(),
            AdministratieId = id,
            Nummer = r.Nummer,
            Naam = r.Naam,
            Soort = r.Soort,
            RgsCode = r.RgsCode,
            Functie = r.Functie,
        }));
        db.Dagboeken.AddRange(dagboeken.Select(d => new Dagboekdefinitie
        {
            Id = Guid.NewGuid(),
            AdministratieId = id,
            Code = d.Code,
            Naam = d.Naam,
            Soort = d.Soort,
            Rekeningnummer = d.Rekeningnummer,
        }));
        var relatiecodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in a.Relaties)
        {
            var code = Kort(r.Code, 50);
            if (!relatiecodes.Add(code))
            {
                continue;
            }

            db.Relaties.Add(new Handelsrelatie
            {
                Id = Guid.NewGuid(),
                AdministratieId = id,
                Code = code,
                Naam = Kort(r.Naam, 300),
                Ibans = [.. r.Ibans.Select(Iban.Normaliseer).Where(Iban.IsGeldig).Distinct()],
                BtwNummer = r.BtwNummer is { } b && BtwNummer.IsGeldig(b) ? BtwNummer.Normaliseer(b) : null,
                KvkNummer = r.KvkNummer,
                Klant = r.Klant,
                AangemaaktOp = nu,
            });
        }

        // De boekingen in de keten, van oud naar nieuw.
        var vorigeHash = Journaalketen.Begin;
        var volgnummer = 0L;
        var geboekt = new List<Boeking>();
        foreach (var post in posten.OrderBy(p => p.Datum).ThenBy(p => p.Dagboek == beginCode ? 0 : 1))
        {
            var boeking = new Boeking
            {
                Id = Guid.NewGuid(),
                AdministratieId = id,
                Volgnummer = ++volgnummer,
                Dagboek = post.Dagboek,
                Boekstuk = post.Boekstuk,
                Datum = post.Datum,
                Omschrijving = post.Omschrijving,
                GeboektOp = nu,
                GeboektDoorId = wie.Id,
                GeboektDoor = wie.Naam,
                VorigeHash = vorigeHash,
                Regels = [.. post.Regels.Select((r, i) => new Boekingsregel
                {
                    AdministratieId = id,
                    Regelnummer = i + 1,
                    Rekeningnummer = r.Rekeningnummer,
                    Bedrag = r.Bedrag,
                    Omschrijving = r.Omschrijving,
                    BtwCode = r.BtwCode,
                    Relatie = r.Relatie,
                    Factuurnummer = r.Factuurnummer,
                })],
            };
            boeking.Hash = Journaalketen.Hash(boeking.NaarDomein(), boeking.Volgnummer, vorigeHash);
            vorigeHash = boeking.Hash;
            db.Boekingen.Add(boeking);
            geboekt.Add(boeking);
        }

        var open = OpenstaandePosten(id, geboekt, schema);
        db.OpenstaandePosten.AddRange(open);
        await db.SaveChangesAsync(annulering);
        await transactie.CommitAsync(annulering);
        await audit.Log(wie, AuditActies.AuditfileIngelezen, naam, $"{Path.GetFileName(bestandsnaam)}; XAF {a.Versie}; {a.Kop.Boekjaar}; {geboekt.Count} boekingen", id);
        return Uitkomst<Auditfileinlezing>.Met(new Auditfileinlezing(id, naam, rekeningen.Count, relatiecodes.Count, geboekt.Count, open.Count));
    }

    /// <summary>
    /// De openstaande posten na de ingelezen boekingen: per rekening (debiteuren of crediteuren), relatie en factuurnummer
    /// de som van de regels; wat niet nul is, staat nog open. De eerste regel is de factuur.
    /// </summary>
    private static List<OpenstaandePost> OpenstaandePosten(Guid id, List<Boeking> boekingen, Rekeningschema schema)
    {
        var posten = new List<OpenstaandePost>();
        var regels = boekingen.SelectMany(b => b.Regels.Select(r => (Boeking: b, Regel: r)))
            .Where(x => x.Regel.Relatie is not null && x.Regel.Factuurnummer is not null
                && schema.Zoek(x.Regel.Rekeningnummer)?.Functie is Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren);
        foreach (var groep in regels.GroupBy(x => (x.Regel.Rekeningnummer, x.Regel.Relatie, x.Regel.Factuurnummer)))
        {
            var openstaand = groep.Sum(x => x.Regel.Bedrag);
            if (openstaand == 0)
            {
                continue;
            }

            var eerste = groep.OrderBy(x => x.Boeking.Datum).ThenBy(x => x.Boeking.Volgnummer).First();
            posten.Add(new OpenstaandePost
            {
                Id = Guid.NewGuid(),
                AdministratieId = id,
                Soort = schema.Zoek(groep.Key.Rekeningnummer)!.Functie == Rekeningfunctie.Debiteuren ? Factuursoort.Verkoop : Factuursoort.Inkoop,
                Relatie = groep.Key.Relatie!,
                Factuurnummer = groep.Key.Factuurnummer!,
                Datum = eerste.Boeking.Datum,
                Vervaldatum = eerste.Boeking.Datum.AddDays(30),
                Bedrag = eerste.Regel.Bedrag,
                Openstaand = openstaand,
                BoekingId = eerste.Boeking.Id,
            });
        }

        return posten;
    }

    /// <summary>De functie van een rekening: uit de RGS-code, of uit de btw-codes van de auditfile.</summary>
    private static Rekeningfunctie Functie(AuditfileRekening r, Auditfile a)
    {
        if (a.Btwcodes.Any(b => b.AfdragenRekening == r.Nummer))
        {
            return Rekeningfunctie.AfTeDragenBtw;
        }

        if (a.Btwcodes.Any(b => b.VorderenRekening == r.Nummer))
        {
            return Rekeningfunctie.Voorbelasting;
        }

        if (a.Dagboeken.Any(d => d.Soort == Dagboeksoort.Bank && d.Tegenrekening == r.Nummer))
        {
            return Rekeningfunctie.Bank;
        }

        if (a.Dagboeken.Any(d => d.Soort == Dagboeksoort.Kas && d.Tegenrekening == r.Nummer))
        {
            return Rekeningfunctie.Kas;
        }

        return r.RgsCode switch
        {
            { } c when c.StartsWith("BLimBan", StringComparison.Ordinal) => Rekeningfunctie.Bank,
            { } c when c.StartsWith("BLimKas", StringComparison.Ordinal) => Rekeningfunctie.Kas,
            { } c when c.StartsWith("BVorDeb", StringComparison.Ordinal) => Rekeningfunctie.Debiteuren,
            { } c when c.StartsWith("BSchCre", StringComparison.Ordinal) => Rekeningfunctie.Crediteuren,
            { } c when c.StartsWith("BVorVbkTvo", StringComparison.Ordinal) => Rekeningfunctie.Voorbelasting,
            { } c when c.StartsWith("BSchBepBtw", StringComparison.Ordinal) => Rekeningfunctie.AfTeDragenBtw,
            _ => Rekeningfunctie.Geen,
        };
    }

    /// <summary>De rekening voor het onverdeelde resultaat: bij een BV de overige reserves, bij een eenmanszaak het ondernemingsvermogen.</summary>
    private static void ZetOnverdeeldResultaat(List<Rekening> rekeningen, Ondernemingsvorm vorm)
    {
        if (rekeningen.Any(r => r.Functie == Rekeningfunctie.OnverdeeldResultaat))
        {
            return;
        }

        var voorvoegsel = vorm == Ondernemingsvorm.Eenmanszaak ? "BEivKap" : "BEivOvr";
        var i = rekeningen.FindIndex(r => r.Functie == Rekeningfunctie.Geen && r.RgsCode?.StartsWith(voorvoegsel, StringComparison.Ordinal) == true);
        if (i >= 0)
        {
            rekeningen[i] = rekeningen[i] with { Functie = Rekeningfunctie.OnverdeeldResultaat };
        }
    }

    /// <summary>De bank- of kasrekening van een dagboek zonder offsetAccID: de rekening die in elke boeking voorkomt.</summary>
    private static string? Tegenrekening(AuditfileDagboek d, Rekeningschema schema)
    {
        if (d.Soort is not (Dagboeksoort.Bank or Dagboeksoort.Kas) || d.Boekingen.Count == 0)
        {
            return null;
        }

        var functie = d.Soort == Dagboeksoort.Bank ? Rekeningfunctie.Bank : Rekeningfunctie.Kas;
        return d.Boekingen.Select(b => b.Regels.Select(r => r.Rekening).ToHashSet(StringComparer.Ordinal))
            .Aggregate((x, y) => { x.IntersectWith(y); return x; })
            .OrderByDescending(r => schema.Zoek(r)?.Functie == functie).FirstOrDefault();
    }

    private static string Uniek(string code, IEnumerable<string> bestaand)
    {
        var set = bestaand.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(code))
        {
            return code;
        }

        for (var n = 2; ; n++)
        {
            var kandidaat = $"{(code.Length > 7 ? code[..7] : code)}-{n}";
            if (!set.Contains(kandidaat))
            {
                return kandidaat;
            }
        }
    }

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;

    private static string? KortOfLeeg(string? tekst, int lengte) => string.IsNullOrWhiteSpace(tekst) ? null : Kort(tekst.Trim(), lengte);

    private static string Bestandsveilig(string naam) => string.Concat(naam.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

    private static string Versie() => typeof(AuditfileDienst).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0";
}
