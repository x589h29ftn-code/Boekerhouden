using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Vpb;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>De fiscale positie van een jaar met de aanpassingen die erin zijn gedaan (per sleutel), en de rekeningen van de specificaties.</summary>
public sealed record Vpbstand(Fiscalepositie Positie, IReadOnlyDictionary<string, decimal> Aanpassingen, DateOnly Tot);

/// <summary>
/// De vennootschapsbelasting van een BV: de fiscale positie per jaar uit het grootboek (resultaat, niet en beperkt
/// aftrekbare kosten volgens de fiscale indeling van de rekeningen, investeringen uit de vaste activa, betaalde voorlopige
/// aanslagen op de balansrekening), met de hand aan te passen bedragen en tarieven, en de fiscale indeling van de
/// rekeningen zelf. Inzien met lezen; aanpassen alleen het kantoor. Alleen voor een BV.
/// </summary>
public sealed class VpbDienst(Toegang toegang, GrootboekDienst grootboek, AuditDienst audit, TimeProvider tijd)
{
    /// <summary>De aanpassingen van de fiscale positie staan met dit voorvoegsel bij die van de btw (zelfde tabel).</summary>
    public const string Voorvoegsel = "vpb.";

    /// <summary>
    /// De fiscale positie van een jaar, met het grootboek tot en met <paramref name="tot"/> (standaard het hele jaar; het
    /// dashboard rekent met het jaar tot vandaag). Leeg als de administratie geen BV is.
    /// </summary>
    public async Task<Vpbstand?> Positie(Uitvoerder wie, Guid administratie, int jaar, DateOnly? tot = null)
    {
        var (begin, eind) = (new DateOnly(jaar, 1, 1), tot ?? new DateOnly(jaar, 12, 31));
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var adm = await db.Administraties.AsNoTracking().Where(a => a.Id == administratie)
            .Select(a => new { a.Ondernemingsvorm, a.RekeningVennootschapsbelasting }).SingleAsync();
        if (adm.Ondernemingsvorm != Ondernemingsvorm.BV)
        {
            return null;
        }

        var (posten, schema) = await grootboek.JournaalMetSchema(wie, administratie, begin, eind);
        var regels = posten.Where(p => p.Datum >= begin).SelectMany(p => p.Regels).ToList();
        var saldi = regels.GroupBy(r => r.Rekeningnummer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag), StringComparer.Ordinal);

        // Betaald: wat er in het jaar is afgeboekt (debet) op de balansrekening(en) van de vennootschapsbelasting.
        var balans = Fiscaleindeling.Rekeningen(schema, Fiscaalsoort.Balans).Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
        if (adm.RekeningVennootschapsbelasting is { Length: > 0 } ingesteld)
        {
            balans.Add(ingesteld);
        }

        var betaald = regels.Where(r => balans.Contains(r.Rekeningnummer) && r.Bedrag > 0).Sum(r => r.Bedrag);
        var investeringen = await db.Activa.AsNoTracking().Where(a => a.Aanschafdatum >= begin && a.Aanschafdatum <= new DateOnly(jaar, 12, 31))
            .Select(a => a.Aanschafwaarde).ToListAsync();
        var aanpassingen = await Aanpassingen(db, jaar);
        var positie = Fiscalepositie.Bereken(new Fiscalegegevens(jaar, schema, saldi, betaald, investeringen.Sum()), aanpassingen);
        return new Vpbstand(positie, aanpassingen, eind);
    }

    /// <summary>
    /// Een bedrag of tarief in de fiscale positie met de hand aanpassen, of met <paramref name="bedrag"/> leeg terug naar
    /// het berekende. Alleen het kantoor; alleen wat geen totaal is.
    /// </summary>
    public async Task<Uitkomst> PasAan(Uitvoerder wie, Guid administratie, int jaar, string sleutel, decimal? bedrag)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await Positie(wie, administratie, jaar) is not { } stand)
        {
            return Uitkomst.Mislukt("De fiscale positie is er alleen voor een BV.");
        }

        if (!stand.Positie.Aanpasbaar.Contains(sleutel, StringComparer.Ordinal))
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
        await audit.Log(wie, AuditActies.FiscalepositieAangepast, $"{jaar} {sleutel}",
            bedrag is { } b ? $"{vorig?.ToString("0.00", CultureInfo.InvariantCulture) ?? "berekend"} → {b.ToString("0.00", CultureInfo.InvariantCulture)}" : "terug naar berekend", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// De fiscale indeling van een rekening instellen (niet of beperkt aftrekbaar, de last of de balanspost van de
    /// vennootschapsbelasting, of gewoon), of met <paramref name="soort"/> leeg terug naar automatisch. Alleen het kantoor.
    /// </summary>
    public async Task<Uitkomst> ZetFiscaal(Uitvoerder wie, Guid administratie, string rekeningnummer, Fiscaalsoort? soort)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        if (await db.Rekeningen.FirstOrDefaultAsync(r => r.Nummer == rekeningnummer) is not { } rekening)
        {
            return Uitkomst.Mislukt($"Rekening {rekeningnummer} bestaat niet.");
        }

        if (rekening.Fiscaal == soort)
        {
            return Uitkomst.Gelukt;
        }

        var oud = Fiscaleindeling.Van(rekening.NaarDomein());
        rekening.Fiscaal = soort;
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.FiscaleindelingGewijzigd, $"{rekening.Nummer} {rekening.Naam}",
            $"{Naam(oud)} → {(soort is { } s ? Naam(s) : $"automatisch ({Naam(Fiscaleindeling.Van(rekening.NaarDomein()))})")}", administratie);
        return Uitkomst.Gelukt;
    }

    public static string Naam(Fiscaalsoort soort) => soort switch
    {
        Fiscaalsoort.NietAftrekbaar => "niet aftrekbaar",
        Fiscaalsoort.BeperktAftrekbaar => "beperkt aftrekbaar",
        Fiscaalsoort.Last => "vennootschapsbelasting (last)",
        Fiscaalsoort.Balans => "vennootschapsbelasting (balans)",
        _ => "gewoon",
    };

    private static async Task<Dictionary<string, decimal>> Aanpassingen(AdministratieDbContext db, int jaar) =>
        await db.Btwaanpassingen.AsNoTracking().Where(a => a.Jaar == jaar && a.Sleutel.StartsWith(Voorvoegsel))
            .ToDictionaryAsync(a => a.Sleutel, a => a.Bedrag, StringComparer.Ordinal);
}
