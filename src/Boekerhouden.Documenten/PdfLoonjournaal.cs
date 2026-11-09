using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Loon;

namespace Boekerhouden.Documenten;

/// <summary>
/// Het journaal van een salarispakket als PDF lezen: links de debetrekeningen, rechts de creditrekeningen, elk als
/// "nummer naam" met daaronder (of ernaast) de bedragen, eventueel per kostenplaats; onderaan de totalen. De grens
/// tussen debet en credit is de tweede "Totaal" op de totaalregel (of anders het midden van de pagina).
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

        var grens = Grens(regels, woorden);
        var start = regels.FindIndex(r => r.Any(w => Is(w, "debet")) && r.Any(w => Is(w, "credit")));
        var rekeningen = new List<(string Nummer, string Naam, bool Credit, decimal Bedrag)>();
        int? huidigLinks = null, huidigRechts = null;
        decimal? totaalDebet = null, totaalCredit = null;
        foreach (var regel in regels.Skip(start + 1))
        {
            var links = regel.Where(w => w.X0 < grens).ToList();
            var rechts = regel.Where(w => w.X0 >= grens).ToList();
            if (regel.Any(w => Is(w, "totaal")))
            {
                totaalDebet = links.Select(Bedrag).LastOrDefault(b => b is not null);
                totaalCredit = rechts.Select(Bedrag).LastOrDefault(b => b is not null);
                break;
            }

            huidigLinks = Verwerk(links, credit: false, huidigLinks);
            huidigRechts = Verwerk(rechts, credit: true, huidigRechts);
        }

        if (rekeningen.Count == 0)
        {
            return null;
        }

        var tekst = regels.Select(r => r.Select(w => w.Tekst).ToList()).ToList();
        return new Loonjournaal(Na(tekst, "werkgever", "groepsindeling", "periode", "clientnummer"), Periode(tekst, "vanaf"), Periode(tekst, "met"),
            [.. rekeningen.Select(r => new Loonjournaalregel(r.Nummer, r.Naam, r.Credit ? -r.Bedrag : r.Bedrag))], totaalDebet, totaalCredit);

        // Een kolom van één regel: een nieuwe rekening ("4020 Brutolonen"), of bedragen bij de rekening erboven.
        int? Verwerk(List<Tekstwoord> kolom, bool credit, int? huidig)
        {
            if (kolom.Count == 0)
            {
                return huidig;
            }

            var bedragen = kolom.Select(Bedrag).Where(b => b is not null).Select(b => b!.Value).ToList();
            if (Rekeningnummer().IsMatch(kolom[0].Tekst) && kolom.Count > 1 && Bedrag(kolom[1]) is null)
            {
                var naam = string.Join(' ', kolom.Skip(1).Where(w => Bedrag(w) is null).Select(w => w.Tekst));
                rekeningen.Add((kolom[0].Tekst, naam, credit, bedragen.Sum()));
                return rekeningen.Count - 1;
            }

            if (huidig is { } i && bedragen.Count > 0)
            {
                rekeningen[i] = rekeningen[i] with { Bedrag = rekeningen[i].Bedrag + bedragen.Sum() };
            }

            return huidig;
        }
    }

    /// <summary>Een bedrag als 1.234,56 (of -1.234,56 / 1.234,56-).</summary>
    private static decimal? Bedrag(Tekstwoord woord)
    {
        var tekst = woord.Tekst.Trim();
        if (!BedragPatroon().IsMatch(tekst))
        {
            return null;
        }

        var negatief = tekst.StartsWith('-') || tekst.EndsWith('-');
        var getal = decimal.Parse(tekst.Trim('-').Replace(".", "", StringComparison.Ordinal).Replace(',', '.'), CultureInfo.InvariantCulture);
        return negatief ? -getal : getal;
    }

    /// <summary>De grens tussen debet en credit: vlak voor de tweede "Totaal" op de totaalregel, anders het midden.</summary>
    private static double Grens(List<List<Tekstwoord>> regels, IReadOnlyList<Tekstwoord> woorden)
    {
        if (regels.FirstOrDefault(r => r.Count(w => Is(w, "totaal")) >= 2) is { } totaal)
        {
            return totaal.Where(w => Is(w, "totaal")).ElementAt(1).X0 - 2;
        }

        return woorden.Max(w => w.X1) / 2;
    }

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

    [GeneratedRegex(@"^\d{3,6}$")]
    private static partial Regex Rekeningnummer();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})*,\d{2}-?$")]
    private static partial Regex BedragPatroon();

    [GeneratedRegex(@"^\d{4}-\d{1,2}$")]
    private static partial Regex PeriodePatroon();
}
