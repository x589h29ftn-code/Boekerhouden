using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Eén maand in het overzicht van de perioden: hoeveel boekingen er zijn en of hij afgesloten is.</summary>
public sealed record Periodemaand(DateOnly Begin, DateOnly Eind, int Boekingen, bool Afgesloten);

/// <summary>De stand van de perioden van een administratie in een jaar.</summary>
/// <param name="AfgeslotenTot">Tot en met deze datum is de administratie afgesloten; leeg = alles open.</param>
public sealed record Periodestand(DateOnly? AfgeslotenTot, DateTimeOffset? AfgeslotenOp, string? AfgeslotenDoor, IReadOnlyList<Periodemaand> Maanden);

/// <summary>
/// Perioden afsluiten en heropenen (Inrichting, Perioden). Afsluiten gaat per maand: tot en met het eind van een maand
/// ligt alles vast (<see cref="Afsluiting"/>; de diensten die boeken controleren het). Heropenen kan alleen met een
/// reden; beide komen in het wijzigingslog. Alleen het kantoor (recht Boekhouden).
/// </summary>
public sealed class PeriodeDienst(Toegang toegang, AuditDienst audit, TimeProvider tijd)
{
    public async Task<Periodestand> Stand(Uitvoerder wie, Guid administratie, int jaar)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var a = await db.Administraties.AsNoTracking().Where(x => x.Id == administratie)
            .Select(x => new { x.AfgeslotenTot, x.AfgeslotenOp, x.AfgeslotenDoor }).SingleAsync();
        var (van, tot) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        var datums = await db.Geldend.AsNoTracking().Where(b => b.Datum >= van && b.Datum <= tot && b.Regels.Any()).Select(b => b.Datum).ToListAsync();
        var perMaand = datums.GroupBy(d => d.Month).ToDictionary(g => g.Key, g => g.Count());
        var maanden = Enumerable.Range(1, 12).Select(m =>
        {
            var begin = new DateOnly(jaar, m, 1);
            var eind = Afsluiting.Maandeinde(begin);
            return new Periodemaand(begin, eind, perMaand.GetValueOrDefault(m), a.AfgeslotenTot is { } t && eind <= t);
        }).ToList();
        return new Periodestand(a.AfgeslotenTot, a.AfgeslotenOp, a.AfgeslotenDoor, maanden);
    }

    /// <summary>
    /// Wat er tot en met een datum nog openstaat en bij afsluiten blijft liggen: bankmutaties en inkoopfacturen te
    /// verwerken, conceptfacturen en een saldo op vraagposten. Afsluiten mag toch; de pagina vraagt het eerst.
    /// </summary>
    public async Task<IReadOnlyList<string>> Openstaand(Uitvoerder wie, Guid administratie, DateOnly tot)
    {
        await using var db = await toegang.Open(wie, administratie, Recht.Lezen);
        var signalen = new List<string>();
        var bank = await db.Banktransacties.CountAsync(t => t.Status == Banktransactiestatus.TeVerwerken && t.Datum <= tot);
        if (bank > 0)
        {
            signalen.Add($"{bank} {(bank == 1 ? "bankmutatie" : "bankmutaties")} tot en met {Weergave.Datum(tot)} nog te verwerken");
        }

        var inkoop = await db.Documenten.CountAsync(d => d.Status == Documentstatus.TeVerwerken);
        if (inkoop > 0)
        {
            signalen.Add($"{inkoop} {(inkoop == 1 ? "document" : "documenten")} in Te verwerken bij inkoop");
        }

        var concepten = await db.Verkoopdocumenten.CountAsync(v => v.Status == Verkoopstatus.Concept && v.Datum <= tot);
        if (concepten > 0)
        {
            signalen.Add($"{concepten} {(concepten == 1 ? "conceptfactuur" : "conceptfacturen")} met een datum tot en met {Weergave.Datum(tot)}");
        }

        var activa = await ActivaDienst.Achterstallig(db, tot);
        if (activa > 0)
        {
            signalen.Add($"Afschrijvingen tot en met {Weergave.Datum(Afsluiting.Maandeinde(tot))} nog niet geboekt ({activa} {(activa == 1 ? "actief" : "activa")})");
        }

        var (schema, _) = await GrootboekDienst.Inrichting(db);
        foreach (var rekening in schema.MetFunctie(Rekeningfunctie.Vraagposten))
        {
            var bedragen = await db.GeldendeRegels.AsNoTracking()
                .Where(r => r.Rekeningnummer == rekening.Nummer && db.Boekingen.Any(b => b.Id == r.BoekingId && b.Datum <= tot)).Select(r => r.Bedrag).ToListAsync();
            if (bedragen.Sum() is var saldo && saldo != 0)
            {
                signalen.Add($"Saldo op {rekening.Nummer} {rekening.Naam}: {Weergave.Bedrag(saldo)}");
            }
        }

        return signalen;
    }

    /// <summary>Sluit de administratie af tot en met het eind van de maand van <paramref name="tot"/>.</summary>
    public async Task<Uitkomst> SluitAf(Uitvoerder wie, Guid administratie, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        var eind = Afsluiting.Maandeinde(tot);
        if (eind > Afsluiting.Maandeinde(DateOnly.FromDateTime(tijd.GetLocalNow().DateTime)))
        {
            return Uitkomst.Mislukt("Een maand die nog moet komen, sluit je niet af.");
        }

        await db.Vergrendel(administratie);
        var a = await db.Administraties.SingleAsync(x => x.Id == administratie);
        if (a.AfgeslotenTot is { } al && eind <= al)
        {
            return Uitkomst.Mislukt($"Tot en met {Weergave.Datum(al)} is al afgesloten.");
        }

        var vorige = a.AfgeslotenTot;
        (a.AfgeslotenTot, a.AfgeslotenOp, a.AfgeslotenDoor) = (eind, tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.PeriodeAfgesloten, $"t/m {Weergave.Datum(eind)}", vorige is { } v ? $"was t/m {Weergave.Datum(v)}" : "was alles open", administratie);
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// Heropent vanaf de maand van <paramref name="vanaf"/>: daarna is de administratie afgesloten tot en met het eind
    /// van de maand ervóór. Leeg = alles weer open. Een reden is verplicht en komt in het log.
    /// </summary>
    public async Task<Uitkomst> Heropen(Uitvoerder wie, Guid administratie, DateOnly? vanaf, string reden)
    {
        ArgumentNullException.ThrowIfNull(wie);
        if (string.IsNullOrWhiteSpace(reden))
        {
            return Uitkomst.Mislukt("Geef een reden waarom de periode weer open moet.");
        }

        await using var db = await toegang.Open(wie, administratie, Recht.Boekhouden);
        await db.Vergrendel(administratie);
        var a = await db.Administraties.SingleAsync(x => x.Id == administratie);
        if (a.AfgeslotenTot is not { } was)
        {
            return Uitkomst.Mislukt("Er is geen periode afgesloten.");
        }

        DateOnly? begin = vanaf is { } v ? new DateOnly(v.Year, v.Month, 1) : null;
        if (begin > was)
        {
            return Uitkomst.Mislukt($"{Weergave.Datum(begin.Value)} is niet afgesloten.");
        }

        (a.AfgeslotenTot, a.AfgeslotenOp, a.AfgeslotenDoor) = (begin?.AddDays(-1), tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync();
        var kort = reden.Trim();
        await audit.Log(wie, AuditActies.PeriodeHeropend, begin is { } b ? $"vanaf {Weergave.Datum(b)}" : "alles",
            $"was t/m {Weergave.Datum(was)}; reden: {(kort.Length > 300 ? kort[..300] : kort)}", administratie);
        return Uitkomst.Gelukt;
    }
}
