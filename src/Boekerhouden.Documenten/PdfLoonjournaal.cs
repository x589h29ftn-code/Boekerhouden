using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Loon;

namespace Boekerhouden.Documenten;

/// <summary>
/// Het journaal van een salarispakket als PDF lezen. Elk pakket ziet er anders uit, dus er zijn meerdere manieren van
/// lezen, en de lezing die sluit (debet gelijk aan credit, en gelijk aan de totaalregel als die er is) wint:
/// <list type="bullet">
/// <item><b>Twee kolommen</b>: links de debetrekeningen, rechts de creditrekeningen, elk als "nummer naam" met de
/// bedragen (eventueel per kostenplaats) eronder; de grens is de tweede "Totaal" op de totaalregel of het midden.</item>
/// <item><b>Lijst</b>: per regel "nummer naam" met het bedrag in een kolom Debet of Credit (op de plek van de kolomkop),
/// met D/C erachter, of met een teken (min of een min erachter = credit).</item>
/// </list>
/// </summary>
public static partial class PdfLoonjournaal
{
    public static Loonjournaal? Lees(byte[] pdf) => Lees(PdfTekst.Woorden(pdf));

    public static Loonjournaal? Lees(IReadOnlyList<Tekstwoord> woorden)
    {
        ArgumentNullException.ThrowIfNull(woorden);
        var regels = Regels(woorden);
        if (regels.Count == 0)
        {
            return null;
        }

        var tekst = regels.Select(r => r.Select(w => w.Tekst).ToList()).ToList();
        var (werkgever, van, tot) = (Na(tekst, "werkgever", "groepsindeling", "periode", "clientnummer"), Periode(tekst, "vanaf"), Periode(tekst, "met"));
        if (van is null && tot is null && Maand(tekst) is { } maand)
        {
            (van, tot) = (maand, maand);
        }
        var lezingen = new[] { TweeKolommen(regels, woorden), Lijst(regels) }
            .Where(l => l is { Rekeningen.Count: > 0 })
            .Select(l => new Loonjournaal(werkgever, van, tot, [.. l!.Rekeningen.Where(r => r.Bedrag != 0)], l.TotaalDebet, l.TotaalCredit))
            .ToList();

        // De lezing die sluit en bij de totaalregel past; anders die sluit; anders die met de meeste rekeningen.
        return lezingen.FirstOrDefault(l => Sluit(l) && !l.Controle().Any())
            ?? lezingen.FirstOrDefault(Sluit)
            ?? lezingen.OrderByDescending(l => l.Regels.Count).FirstOrDefault();
    }

    private static bool Sluit(Loonjournaal j) => j.Debet > 0 && j.Debet == j.Credit;

    private sealed record Lezing(List<Loonjournaalregel> Rekeningen, decimal? TotaalDebet, decimal? TotaalCredit);

    /// <summary>Debet links en credit rechts, elk met eigen rekeningen.</summary>
    private static Lezing? TweeKolommen(List<List<Tekstwoord>> regels, IReadOnlyList<Tekstwoord> woorden)
    {
        var grens = regels.FirstOrDefault(r => r.Count(IsTotaal) >= 2) is { } totaalregel
            ? totaalregel.Where(IsTotaal).ElementAt(1).X0 - 2
            : woorden.Max(w => w.X1) / 2;
        var rekeningen = new List<(string Nummer, string Naam, bool Credit, decimal Bedrag)>();
        int? links = null, rechts = null;
        decimal? totaalDebet = null, totaalCredit = null;
        foreach (var regel in regels.Skip(Start(regels)))
        {
            var l = regel.Where(w => w.X0 < grens).ToList();
            var r = regel.Where(w => w.X0 >= grens).ToList();
            if (regel.Any(IsTotaal))
            {
                totaalDebet = l.Select(w => Bedrag(w)).LastOrDefault(b => b is not null);
                totaalCredit = r.Select(w => Bedrag(w)).LastOrDefault(b => b is not null);
                break;
            }

            links = Kolom(l, credit: false, links);
            rechts = Kolom(r, credit: true, rechts);
        }

        return new Lezing([.. rekeningen.Select(x => new Loonjournaalregel(x.Nummer, x.Naam, x.Credit ? -x.Bedrag : x.Bedrag))], totaalDebet, totaalCredit);

        int? Kolom(List<Tekstwoord> kolom, bool credit, int? huidig)
        {
            if (kolom.Count == 0)
            {
                return huidig;
            }

            var bedragen = kolom.Select(w => Bedrag(w)).Where(b => b is not null).Select(b => b!.Value).ToList();
            if (Rekeningkop(kolom) is { } kop)
            {
                rekeningen.Add((kop.Nummer, kop.Naam, credit, bedragen.Sum()));
                return rekeningen.Count - 1;
            }

            if (huidig is { } i && bedragen.Count > 0)
            {
                rekeningen[i] = rekeningen[i] with { Bedrag = rekeningen[i].Bedrag + bedragen.Sum() };
            }

            return huidig;
        }
    }

    /// <summary>
    /// Eén rekening per regel (met bedragen op die regel of de regels eronder): debet of credit volgens de kolomkop
    /// boven het bedrag, een D of C erachter, of het teken.
    /// </summary>
    private static Lezing? Lijst(List<List<Tekstwoord>> regels)
    {
        var start = Start(regels);
        var kop = start > 0 ? regels[start - 1] : null;
        var debetX = kop?.FirstOrDefault(w => Is(w, "debet") || Is(w, "debit")) is { } d ? (d.X0 + d.X1) / 2 : (double?)null;
        var creditX = kop?.FirstOrDefault(w => Is(w, "credit")) is { } c ? (c.X0 + c.X1) / 2 : (double?)null;
        var rekeningen = new List<Loonjournaalregel>();
        decimal? totaalDebet = null, totaalCredit = null;
        foreach (var regel in regels.Skip(start))
        {
            var bedragen = Getekend(regel);
            if (regel.Any(IsTotaal))
            {
                var debet = bedragen.Where(b => b > 0).Sum();
                var credit = -bedragen.Where(b => b < 0).Sum();
                (totaalDebet, totaalCredit) = (debet == 0 ? null : debet, credit == 0 ? null : credit);
                break;
            }

            if (Rekeningkop(regel) is { } r)
            {
                rekeningen.Add(new Loonjournaalregel(r.Nummer, r.Naam, bedragen.Sum()));
            }
            else if (rekeningen.Count > 0 && bedragen.Count > 0)
            {
                rekeningen[^1] = rekeningen[^1] with { Bedrag = rekeningen[^1].Bedrag + bedragen.Sum() };
            }
        }

        // Bij een totaalregel met één bedrag (debet = credit) is dat het totaal van beide kanten.
        if (totaalDebet is { } td && totaalCredit is null && rekeningen.Any(r => r.Bedrag < 0))
        {
            totaalCredit = td;
        }

        return new Lezing(rekeningen, totaalDebet, totaalCredit);

        // De bedragen van een regel met hun teken: credit negatief.
        List<decimal> Getekend(List<Tekstwoord> regel)
        {
            var uit = new List<decimal>();
            for (var i = 0; i < regel.Count; i++)
            {
                if (Bedrag(regel[i]) is not { } bedrag)
                {
                    continue;
                }

                var na = i + 1 < regel.Count ? regel[i + 1].Tekst.Trim('.', ':').ToUpperInvariant() : "";
                var midden = (regel[i].X0 + regel[i].X1) / 2;
                var credit = na is "C" or "CR" or "CREDIT" ? true
                    : na is "D" or "DT" or "DEBET" or "DEBIT" ? false
                    : debetX is { } dx && creditX is { } cx ? Math.Abs(midden - cx) < Math.Abs(midden - dx)
                    : (bool?)null;
                uit.Add(credit switch { true => -Math.Abs(bedrag), false => Math.Abs(bedrag), _ => bedrag });
            }

            return uit;
        }
    }

    /// <summary>"4020 Brutolonen": een rekeningnummer met een naam erachter (de naam zonder bedragen en zonder D/C).</summary>
    private static (string Nummer, string Naam)? Rekeningkop(List<Tekstwoord> woorden)
    {
        if (woorden.Count < 2 || !Rekeningnummer().IsMatch(woorden[0].Tekst) || Bedrag(woorden[1]) is not null)
        {
            return null;
        }

        var naam = string.Join(' ', woorden.Skip(1).TakeWhile(w => Bedrag(w) is null).Select(w => w.Tekst)
            .Where(w => w.ToUpperInvariant() is not ("D" or "C" or "DT" or "CR")));
        return naam.Any(char.IsLetter) ? (woorden[0].Tekst, naam) : null;
    }

    /// <summary>De eerste regel na de kolomkoppen (Debet en Credit), of na het begin als die er niet zijn.</summary>
    private static int Start(List<List<Tekstwoord>> regels) =>
        regels.FindIndex(r => r.Any(w => Is(w, "debet") || Is(w, "debit")) && r.Any(w => Is(w, "credit"))) + 1;

    /// <summary>Een bedrag als 1.234,56 (of -1.234,56 / 1.234,56-), ook met € ervoor.</summary>
    private static decimal? Bedrag(Tekstwoord woord)
    {
        var tekst = woord.Tekst.Trim().TrimStart('€').Trim();
        if (!BedragPatroon().IsMatch(tekst))
        {
            return null;
        }

        var negatief = tekst.StartsWith('-') || tekst.EndsWith('-');
        var getal = decimal.Parse(tekst.Trim('-').Replace(".", "", StringComparison.Ordinal).Replace(',', '.'), CultureInfo.InvariantCulture);
        return negatief ? -getal : getal;
    }

    private static bool IsTotaal(Tekstwoord woord) => Is(woord, "totaal") || Is(woord, "total") || Is(woord, "totalen");

    private static bool Is(Tekstwoord woord, string tekst) => string.Equals(woord.Tekst.Trim(':'), tekst, StringComparison.OrdinalIgnoreCase);

    /// <summary>De woorden na een label tot het volgende label, bijv. de werkgever.</summary>
    private static string? Na(List<List<string>> regels, string label, params string[] stop)
    {
        foreach (var regel in regels)
        {
            var i = regel.FindIndex(w => string.Equals(w.Trim(':'), label, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                var waarde = regel.Skip(i + 1).TakeWhile(w => !stop.Contains(w.Trim(':'), StringComparer.OrdinalIgnoreCase)).ToList();
                return waarde.Count == 0 ? null : string.Join(' ', waarde);
            }
        }

        return null;
    }

    /// <summary>De periode na "vanaf" of "(tot en) met", bijv. 2025-1 of 2025-13.</summary>
    private static string? Periode(List<List<string>> regels, string na)
    {
        foreach (var regel in regels)
        {
            var i = regel.FindIndex(w => string.Equals(w, na, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && regel.Skip(i + 1).FirstOrDefault(w => PeriodePatroon().IsMatch(w)) is { } periode)
            {
                return periode;
            }
        }

        return null;
    }

    private static readonly string[] Maandnamen =
        ["januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december"];

    /// <summary>Een maand met jaartal in de tekst, bijv. "lonen maart 2026", als periode 2026-3.</summary>
    private static string? Maand(List<List<string>> regels)
    {
        foreach (var regel in regels)
        {
            for (var i = 0; i + 1 < regel.Count; i++)
            {
                var maand = Array.IndexOf(Maandnamen, regel[i].ToLowerInvariant());
                if (maand >= 0 && int.TryParse(regel[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var jaar) && jaar is > 1990 and < 2100)
                {
                    return $"{jaar}-{maand + 1}";
                }
            }
        }

        return null;
    }

    /// <summary>De woorden per regel (per pagina van boven naar beneden, van links naar rechts).</summary>
    private static List<List<Tekstwoord>> Regels(IReadOnlyList<Tekstwoord> woorden)
    {
        var regels = new List<(int Pagina, double Y, List<Tekstwoord> Woorden)>();
        foreach (var pagina in woorden.GroupBy(w => w.Pagina).OrderBy(g => g.Key))
        {
            foreach (var woord in pagina.OrderByDescending(w => w.Y1).ThenBy(w => w.X0))
            {
                var midden = (woord.Y0 + woord.Y1) / 2;
                var hoogte = Math.Max(4, woord.Y1 - woord.Y0);
                var regel = regels.FindLastIndex(r => r.Pagina == woord.Pagina && Math.Abs(r.Y - midden) < hoogte * 0.5);
                if (regel < 0)
                {
                    regels.Add((woord.Pagina, midden, [woord]));
                }
                else
                {
                    regels[regel].Woorden.Add(woord);
                }
            }
        }

        return [.. regels.OrderBy(r => r.Pagina).ThenByDescending(r => r.Y).Select(r => r.Woorden.OrderBy(w => w.X0).ToList())];
    }

    [GeneratedRegex(@"^\d{3,8}$")]
    private static partial Regex Rekeningnummer();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})*,\d{2}-?$")]
    private static partial Regex BedragPatroon();

    [GeneratedRegex(@"^\d{4}-\d{1,2}$")]
    private static partial Regex PeriodePatroon();
}
