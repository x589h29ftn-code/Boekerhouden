using System.Text.Json;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De btw van een jaar: de perioden met hun aangifte, wat is vastgelegd, de aansluiting en de rondrekening.</summary>
/// <param name="Aanpassingen">De met de hand aangepaste bedragen van de jaaropstelling, per sleutel.</param>
/// <param name="Signalen">Opmerkingen bij de btw privégebruik van auto's (vaste activa tegen rubriek 1d).</param>
public sealed record Btwstandjaar(Btwjaar Jaar, IReadOnlyList<Btwaansluiting> Aansluitingen, Btwrondrekening Rondrekening, IReadOnlyDictionary<string, decimal> Aanpassingen,
    IReadOnlyList<Boekhouding.Activa.Privegebruiksignaal> Signalen)
{
    /// <summary>De jaaropstelling omzetbelasting met de aangepaste bedragen.</summary>
    public IReadOnlyList<Jaaropstellingregel> Jaaropstelling => Rondrekening.Jaaropstelling(Aanpassingen);
}

/// <summary>
/// De btw-aangifte: per periode (maand, kwartaal of jaar, in te stellen per administratie) wat de boekingen met hun
/// btw-code geven, de aansluiting met de btw-rekeningen in het grootboek tot en met die periode, de rondrekening over
/// het jaar, en een ingediende aangifte vastleggen (en weer intrekken). Inzien met lezen; vastleggen en de frequentie
/// wijzigen alleen het kantoor.
/// </summary>
public sealed class BtwaangifteDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Aangiftefrequentie> Frequentie(Uitvoerder wie, Guid administratie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        return await db.Administraties.Where(a => a.Id == administratie).Select(a => a.Btwfrequentie).SingleAsync();
    }

    public async Task ZetFrequentie(Uitvoerder wie, Guid administratie, Aangiftefrequentie frequentie)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var a = await db.Administraties.SingleAsync(x => x.Id == administratie);
        if (a.Btwfrequentie == frequentie)
        {
            return;
        }

        var oud = a.Btwfrequentie;
        a.Btwfrequentie = frequentie;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwfrequentieGewijzigd, a.Naam, $"{Naam(oud)} → {Naam(frequentie)}", administratie);
    }

    /// <summary>Het btw-overzicht van een jaar, met per periode de aansluiting van 1 januari tot en met het eind van die periode.</summary>
    public async Task<Btwstandjaar> Jaar(Uitvoerder wie, Guid administratie, int jaar)
    {
        var (begin, eind) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var frequentie = await db.Administraties.Where(a => a.Id == administratie).Select(a => a.Btwfrequentie).SingleAsync();
        var codes = (await BtwDienst.Lees(db)).Op(eind);
        var vastgelegd = (await db.IngediendeAangiften.AsNoTracking().Where(v => v.Van >= begin && v.Van <= eind).ToListAsync()).Select(NaarDomein).ToList();
        var (posten, schema) = await grootboek.JournaalMetSchema(wie, administratie, begin, eind);
        var btwjaar = Btwjaar.Maak(jaar, frequentie, posten, schema, codes, vastgelegd);
        var aansluitingen = btwjaar.Perioden.Select(p => Btwaansluiting.Bereken(posten, schema, codes, begin, p.Periode.Tot)).ToList();
        var aanpassingen = await db.Btwaanpassingen.AsNoTracking().Where(a => a.Jaar == jaar && !a.Sleutel.StartsWith(VpbDienst.Voorvoegsel)).ToDictionaryAsync(a => a.Sleutel, a => a.Bedrag, StringComparer.Ordinal);
        var signalen = Boekhouding.Activa.Privegebruikcontrole.Signalen(jaar, await ActivaDienst.Vervoermiddelen(db, schema), btwjaar.Jaaraangifte.Rubriek("1d").Btw);
        return new Btwstandjaar(btwjaar, aansluitingen, Btwrondrekening.Maak(btwjaar, posten, schema, codes), aanpassingen, signalen);
    }

    /// <summary>
    /// Een bedrag in de jaaropstelling met de hand aanpassen, of met <paramref name="bedrag"/> leeg terug naar het
    /// berekende bedrag. Alleen het kantoor; alleen bedragen die geen totaal zijn.
    /// </summary>
    public async Task<Uitkomst> PasAan(Uitvoerder wie, Guid administratie, int jaar, string sleutel, decimal? bedrag)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var stand = await Jaar(wie, administratie, jaar);
        if (!stand.Rondrekening.Aanpasbaar.Contains(sleutel, StringComparer.Ordinal))
        {
            return Uitkomst.Mislukt("Dit bedrag is niet aan te passen.");
        }

        var oud = await db.Btwaanpassingen.FirstOrDefaultAsync(a => a.Jaar == jaar && a.Sleutel == sleutel);
        var vorig = oud?.Bedrag;
        if (bedrag is null)
        {
            if (oud is null)
            {
                return Uitkomst.Gelukt;
            }

            db.Btwaanpassingen.Remove(oud);
        }
        else if (oud is null)
        {
            db.Btwaanpassingen.Add(new Btwaanpassing
            {
                AdministratieId = administratie,
                Jaar = jaar,
                Sleutel = sleutel,
                Bedrag = bedrag.Value,
                GewijzigdOp = tijd.GetUtcNow(),
                GewijzigdDoor = wie.Naam,
            });
        }
        else
        {
            (oud.Bedrag, oud.GewijzigdOp, oud.GewijzigdDoor) = (bedrag.Value, tijd.GetUtcNow(), wie.Naam);
        }

        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.BtwjaaropstellingAangepast, $"{jaar} {sleutel}",
            bedrag is { } b ? $"{vorig?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "berekend"} → {b.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" : "terug naar berekend", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Een aangifte vastleggen zoals hij is ingediend: de berekening van nu, per rubriek, in hele euro's. Wat het afronden
    /// scheelt (in het voordeel van de ondernemer) wordt in het memoriaal van de btw-rekening naar de rekening voor
    /// afrondingsverschillen geboekt, zodat er na de betaling niets op de btw-rekeningen blijft staan. Is de periode al
    /// afgesloten, dan blijft het verschil staan.
    /// </summary>
    public async Task<Uitkomst> Vastleggen(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        await using var transactie = await db.Database.BeginTransactionAsync();
        await db.Vergrendel(administratie);
        if (await db.IngediendeAangiften.AnyAsync(v => v.Van == van && v.Tot == tot))
        {
            return Uitkomst.Mislukt("Deze aangifte is al vastgelegd.");
        }

        var codes = (await BtwDienst.Lees(db)).Op(tot);
        var (posten, schema) = await grootboek.JournaalMetSchema(wie, administratie, van, tot);
        var berekend = Btwaangifte.Bereken(posten, schema, codes, van, tot);
        // Zoals hij wordt ingediend: in hele euro's.
        var aangifte = berekend.Afgerond();
        var vastlegging = new IngediendeAangifte
        {
            AdministratieId = administratie,
            Van = van,
            Tot = tot,
            Rubrieken = JsonSerializer.Serialize(aangifte.Rubrieken.Where(r => r.Grondslag != 0 || r.Btw != 0), Json),
            Voorbelasting = aangifte.Voorbelasting,
            VastgelegdOp = tijd.GetUtcNow(),
            VastgelegdDoor = wie.Naam,
        };
        db.IngediendeAangiften.Add(vastlegging);
        (string? Stuk, Rekening? Nieuw) afronding = (null, null);
        if (berekend.Afronding != 0 && await GrootboekDienst.Afgesloten(db, administratie, tot) is null)
        {
            afronding = await BoekAfronding(db, administratie, schema, berekend.Afronding, Periodenaam(van, tot), tot, wie);
            vastlegging.Afrondingsboeking = afronding.Stuk;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException fout) when (Databasefout.IsDubbel(fout))
        {
            // Een collega legde dezelfde aangifte op hetzelfde moment vast.
            return Uitkomst.Mislukt("Deze aangifte is al vastgelegd.");
        }

        await transactie.CommitAsync();
        if (afronding.Nieuw is { } nieuw)
        {
            await audit.Log(wie, AuditActies.RekeningToegevoegd, $"{nieuw.Nummer} {nieuw.Naam}", $"{nieuw.RgsCode}; voor het afronden van de btw-aangifte", administratie);
        }

        await audit.Log(wie, AuditActies.BtwaangifteVastgelegd, Periodenaam(van, tot),
            $"te betalen {aangifte.TeBetalen:0.00}" + (afronding.Stuk is { } stuk ? $"; afronding {berekend.Afronding:0.00} geboekt ({stuk})" : ""), administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Boekt het afrondingsverschil van een aangifte: debet op de rekening voor af te dragen btw, credit op de rekening
    /// voor afrondingsverschillen (die wordt aangemaakt als de administratie hem nog niet heeft). Geeft "dagboek
    /// boekstuk" (leeg als er geen memoriaal of btw-rekening is) en de rekening als die nieuw is.
    /// </summary>
    private async Task<(string? Stuk, Rekening? Nieuw)> BoekAfronding(AdministratieDbContext db, Guid administratie, Rekeningschema schema, decimal bedrag, string periode, DateOnly datum, Uitvoerder wie)
    {
        var (_, dagboeken) = await GrootboekDienst.Inrichting(db);
        var memoriaal = dagboeken.Where(d => d.Soort == Dagboeksoort.Memoriaal).OrderBy(d => d.Code != "MEM").FirstOrDefault();
        var btw = schema.MetFunctie(Rekeningfunctie.AfTeDragenBtw).FirstOrDefault() ?? schema.MetFunctie(Rekeningfunctie.Voorbelasting).FirstOrDefault();
        if (memoriaal is null || btw is null)
        {
            return (null, null);
        }

        var bestaand = schema.MetFunctie(Rekeningfunctie.Afrondingsverschillen).FirstOrDefault();
        var naar = bestaand ?? MaakAfrondingsrekening(db, administratie, schema);
        var boekstuk = await GrootboekDienst.VolgendBoekstuk(db, memoriaal.Code, datum.Year);
        var omschrijving = $"Afronding btw-aangifte {periode}";
        await grootboek.VoegToeAanKeten(db, administratie, new Journaalpost(memoriaal.Code, boekstuk, datum, omschrijving,
        [
            new Journaalregel(btw.Nummer, bedrag, omschrijving),
            new Journaalregel(naar.Nummer, -bedrag, omschrijving),
        ]), wie);
        return ($"{memoriaal.Code} {boekstuk}", bestaand is null ? naar : null);
    }

    private static Rekening MaakAfrondingsrekening(AdministratieDbContext db, Guid administratie, Rekeningschema schema)
    {
        var standaard = StandaardRekeningschema.Afrondingsrekening;
        var nummer = Enumerable.Range(4910, 90).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(n => schema.Zoek(n) is null)
            ?? Enumerable.Range(1, 9999).Select(n => $"9{n:0000}").First(n => schema.Zoek(n) is null);
        var rekening = standaard with { Nummer = nummer };
        db.Rekeningen.Add(new Grootboekrekening
        {
            Id = Guid.NewGuid(),
            AdministratieId = administratie,
            Nummer = rekening.Nummer,
            Naam = rekening.Naam,
            Soort = rekening.Soort,
            RgsCode = rekening.RgsCode,
            Functie = rekening.Functie,
        });
        return rekening;
    }

    /// <summary>Een vastgelegde aangifte weer intrekken (bijv. vastgelegd voordat alles geboekt was).</summary>
    public async Task<Uitkomst> Intrekken(Uitvoerder wie, Guid administratie, DateOnly van, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.IngediendeAangiften.FirstOrDefaultAsync(v => v.Van == van && v.Tot == tot) is not { } aangifte)
        {
            return Uitkomst.Mislukt("Deze aangifte is niet vastgelegd.");
        }

        // De aangifte hoort bij de periode: is die afgesloten, dan eerst heropenen.
        if (await GrootboekDienst.Afgesloten(db, administratie, tot) is { } afgesloten)
        {
            return Uitkomst.Mislukt(afgesloten);
        }

        await using var transactie = await db.Database.BeginTransactionAsync();
        db.IngediendeAangiften.Remove(aangifte);
        if (aangifte.Afrondingsboeking is { } afronding)
        {
            // De afronding hoort bij de aangifte: die vervalt mee.
            await grootboek.LaatVervallen(db, administratie, [afronding], wie);
        }

        await db.SaveChangesAsync();
        await transactie.CommitAsync();
        await audit.Log(wie, AuditActies.BtwaangifteIngetrokken, Periodenaam(van, tot), $"te betalen was {NaarDomein(aangifte).TeBetalen:0.00}", administratie);
        return Uitkomst.Gelukt;
    }

    private static VastgelegdeAangifte NaarDomein(IngediendeAangifte a) =>
        new(a.Van, a.Tot, JsonSerializer.Deserialize<List<Aangifterubriek>>(a.Rubrieken, Json) ?? [], a.Voorbelasting, a.VastgelegdOp, a.VastgelegdDoor);

    private static string Periodenaam(DateOnly van, DateOnly tot) =>
        (tot.Month - van.Month + 1) switch
        {
            1 => Aangifteperiode.Waarin(van, Aangiftefrequentie.Maand).Naam,
            3 => Aangifteperiode.Waarin(van, Aangiftefrequentie.Kwartaal).Naam,
            12 => Aangifteperiode.Waarin(van, Aangiftefrequentie.Jaar).Naam,
            _ => $"{van:dd-MM-yyyy} t/m {tot:dd-MM-yyyy}",
        };

    public static string Naam(Aangiftefrequentie frequentie) => frequentie switch
    {
        Aangiftefrequentie.Maand => "per maand",
        Aangiftefrequentie.Kwartaal => "per kwartaal",
        _ => "per jaar",
    };
}
