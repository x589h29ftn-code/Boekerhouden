using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Export;

/// <summary>De kostenplaatsen als Excel of PDF: het totaaloverzicht, en per kostenplaats de regels per rekening.</summary>
public static class Kostenplaatsexport
{
    /// <summary>Per kostenplaats de kosten, opbrengsten en het resultaat, met het totaal.</summary>
    public static Exporttabel Totaal(IReadOnlyList<Kostenplaatstotaal> totalen, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(totalen);
        var regels = totalen.Select(t => new Exportregel(Exportsoort.Rekening, t.Kostenplaats.Code,
            t.Kostenplaats.Afgesloten ? $"{t.Kostenplaats.Naam} (afgesloten)" : t.Kostenplaats.Naam, 0, [t.Kosten, t.Opbrengsten, t.Resultaat])).ToList();
        regels.Add(new(Exportsoort.Totaal, null, "Totaal", 0, [totalen.Sum(t => t.Kosten), totalen.Sum(t => t.Opbrengsten), totalen.Sum(t => t.Resultaat)]));
        return new Exporttabel("Kostenplaatsen", van, tot, ["Kosten", "Opbrengsten", "Resultaat"], regels) { Staand = true };
    }

    /// <summary>De regels op één kostenplaats, per rekening met een subtotaal, en het totaal (debet en credit).</summary>
    public static Exporttabel Kostenplaats(Kostenplaatsregel kostenplaats, IReadOnlyList<Kostenplaatsboeking> boekingen, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(kostenplaats);
        ArgumentNullException.ThrowIfNull(boekingen);
        static decimal? Debet(decimal b) => b > 0 ? b : null;
        static decimal? Credit(decimal b) => b < 0 ? -b : null;
        var regels = new List<Exportregel>();
        foreach (var groep in boekingen.GroupBy(b => b.Rekeningnummer).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            regels.Add(new(Exportsoort.Kop, groep.Key, groep.First().Rekeningnaam, 0, []));
            regels.AddRange(groep.Select(b => new Exportregel(Exportsoort.Rekening, $"{b.Dagboek} {b.Boekstuk}",
                $"{Weergave.Datum(b.Datum)}  {b.Omschrijving}{(b.Relatie is null ? null : $" ({b.Relatie})")}", 1, [Debet(b.Bedrag), Credit(b.Bedrag)])));
            var saldo = groep.Sum(b => b.Bedrag);
            regels.Add(new(Exportsoort.Subtotaal, null, $"Totaal {groep.Key}", 1, [Debet(saldo), Credit(saldo)]));
        }

        regels.Add(new(Exportsoort.Totaal, null, "Totaal", 0, [boekingen.Where(b => b.Bedrag > 0).Sum(b => b.Bedrag), -boekingen.Where(b => b.Bedrag < 0).Sum(b => b.Bedrag)]));
        return new Exporttabel($"Kostenplaats {kostenplaats.Code} {kostenplaats.Naam}", van, tot, ["Debet", "Credit"], regels) { Staand = true };
    }
}
