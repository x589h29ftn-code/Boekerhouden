using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Activa;

namespace Boekerhouden.Documenten;

/// <summary>
/// Een staat van vaste activa uit een PDF (met tekstlaag) als tabel voor de <see cref="Activastaatlezer"/>. Woorden op
/// dezelfde hoogte vormen een rij. De bedragkolommen volgen uit de rechterkant van de bedragen (bedragen staan rechts
/// uitgelijnd); wat links van de eerste kolom staat, is de omschrijving, een datum krijgt een eigen kolom. De kop staat
/// boven de eerste regel met bedragen: elk kopwoord hoort bij de dichtstbijzijnde kolom, over meerdere regels
/// samengevoegd ("Aanschaf-" "fingen tot" "01-01-2025"). Een kop die op een volgende pagina terugkomt, wordt overgeslagen.
/// </summary>
public static partial class PdfActivastaat
{
    public static Ingelezenstaat Lees(byte[] pdf)
    {
        var woorden = PdfTekst.Woorden(pdf);
        if (woorden.Count == 0)
        {
            throw new DocumentException("Deze PDF heeft geen tekstlaag (een scan); lever de staat als Excel of als PDF uit het programma aan.");
        }

        return Activastaatlezer.Lees(Tabel(woorden));
    }

    internal static Staattabel Tabel(IReadOnlyList<Tekstwoord> woorden)
    {
        var rijen = Rijen(woorden);
        // Een bedrag: cijfers met punt of komma, of een kaal getal; niet een datum en niet een jaartal in de kop ("2025").
        bool IsBedrag(Tekstwoord w) => Getal().IsMatch(w.Tekst) && !Datum().IsMatch(w.Tekst) && !Jaartal().IsMatch(w.Tekst);

        // De bedragkolommen: rechterkanten van bedragen op regels met minstens twee bedragen, samengenomen als ze dicht
        // bij elkaar liggen.
        var kanten = rijen.Where(r => r.Count(IsBedrag) >= 2).SelectMany(r => r.Where(IsBedrag).Select(w => w.X1)).Order().ToList();
        var kolommen = new List<(double Links, double Rechts)>();
        foreach (var x in kanten)
        {
            if (kolommen.Count > 0 && x - kolommen[^1].Rechts < 12)
            {
                kolommen[^1] = (kolommen[^1].Links, Math.Max(kolommen[^1].Rechts, x));
            }
            else
            {
                kolommen.Add((x, x));
            }
        }

        if (kolommen.Count == 0)
        {
            return new Staattabel([]);
        }

        // Datums op regels met bedragen (niet de datums in de kop, zoals "tot 01-01-2025").
        var datums = rijen.Where(r => r.Any(IsBedrag)).SelectMany(r => r).Where(w => Datum().IsMatch(w.Tekst)).ToList();
        var metDatum = datums.Count > 0;
        var eersteBedrag = rijen.SelectMany(r => r.Where(IsBedrag)).Select(w => w.X0).DefaultIfEmpty(kolommen[0].Rechts).Min();
        var omschrijvingTot = Math.Min(eersteBedrag, metDatum ? datums.Min(d => d.X0) : double.MaxValue) - 2;

        int Kolom(Tekstwoord w)
        {
            if (metDatum && Datum().IsMatch(w.Tekst))
            {
                return 1;
            }

            if (IsBedrag(w))
            {
                var i = Enumerable.Range(0, kolommen.Count).MinBy(k => Math.Abs(kolommen[k].Rechts - w.X1));
                return (metDatum ? 2 : 1) + i;
            }

            return w.X1 <= omschrijvingTot + 2 ? 0 : -1;
        }

        // De kop: de regels boven de eerste regel met bedragen (op de eerste pagina met bedragen), tot een lege afstand.
        var eersteData = rijen.FindIndex(r => r.Count(IsBedrag) >= 2);
        // De kop: de regels direct boven de eerste regel met bedragen, zolang ze dicht op elkaar staan (niet de titel).
        // Groepsregels (alleen tekst links, zoals "Inventaris") tussen de kop en de eerste bedragen horen bij de gegevens.
        var kopRijen = new HashSet<int>();
        var kopEinde = eersteData;
        while (kopEinde > 0 && rijen[kopEinde - 1].All(w => w.X1 <= omschrijvingTot + 2))
        {
            kopEinde--;
        }

        var kopBegin = kopEinde;
        for (var r = kopEinde - 1; r >= 0 && r >= kopEinde - 6 && !rijen[r].Any(IsBedrag); r--)
        {
            if (r < kopEinde - 1 && Hoogte(rijen[r]) - Hoogte(rijen[r + 1]) > 2.2 * Math.Max(6, rijen[r][0].Y1 - rijen[r][0].Y0))
            {
                break;
            }

            kopBegin = r;
        }

        // Kolommen zonder bedragen (bijv. desinvesteringen): uit de kop, waar een kopwoord ver van alle bedragkolommen eindigt.
        var kopdelen = Enumerable.Range(kopBegin, kopEinde - kopBegin).ToDictionary(r => r, r => Delen(rijen[r]));
        foreach (var x in kopdelen.Values.SelectMany(d => d).Where(w => w.X0 > omschrijvingTot + 2 && !(metDatum && Math.Abs(w.X0 - datums.Min(d => d.X0)) < 30)).Select(w => w.X1).Order())
        {
            if (kolommen.All(k => Math.Abs(k.Rechts - x) > 15))
            {
                kolommen.Add((x, x));
                kolommen.Sort((a, b) => a.Rechts.CompareTo(b.Rechts));
            }
        }

        var breedte = (metDatum ? 2 : 1) + kolommen.Count;
        var kop = new List<string>[breedte];
        for (var k = 0; k < breedte; k++)
        {
            kop[k] = [];
        }

        for (var r = kopBegin; r < kopEinde; r++)
        {
            kopRijen.Add(r);
            foreach (var w in kopdelen[r])
            {
                // Een kopwoord hoort bij de kolom waar zijn midden het dichtst bij ligt (de omschrijving links).
                var midden = (w.X0 + w.X1) / 2;
                var k = midden <= omschrijvingTot ? 0
                    : metDatum && datums.Any(d => Math.Abs(((d.X0 + d.X1) / 2) - midden) < 30) ? 1
                    : (metDatum ? 2 : 1) + Enumerable.Range(0, kolommen.Count).MinBy(i => Math.Min(Math.Abs(kolommen[i].Rechts - w.X1), Math.Abs(kolommen[i].Rechts - 15 - midden)));
                kop[k].Add(w.Tekst);
            }

            for (var k = 0; k < breedte; k++)
            {
                kop[k].Add("\n");
            }
        }

        var tabel = new List<IReadOnlyList<Staatcel>>
        {
            kop.Select(delen => new Staatcel(Regex.Replace(string.Join(' ', delen), @"\s*\n\s*", "\n", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim('\n', ' '))).ToArray(),
        };
        var koptekst = string.Join(' ', kopRijen.Select(r => string.Join(' ', rijen[r].Select(w => w.Tekst))));
        for (var r = kopEinde; r < rijen.Count; r++)
        {
            // Een herhaalde kop op een volgende pagina: overslaan.
            if (!rijen[r].Any(IsBedrag) && rijen[r].Count > 2 && koptekst.Contains(string.Join(' ', rijen[r].Select(w => w.Tekst)), StringComparison.Ordinal))
            {
                continue;
            }

            var cellen = Enumerable.Repeat(Staatcel.Leeg, breedte).ToArray();
            foreach (var groep in rijen[r].GroupBy(Kolom).Where(g => g.Key >= 0))
            {
                var tekst = string.Join(' ', groep.Select(w => w.Tekst));
                cellen[groep.Key] = new Staatcel(groep.Key == 0 || (metDatum && groep.Key == 1) ? tekst : tekst.Replace("€", string.Empty, StringComparison.Ordinal).Trim());
            }

            tabel.Add(cellen);
        }

        // De regels boven de kop (naam, titel) als eerste rijen, voor het jaartal; de lezer zoekt zelf de kop.
        var boven = rijen.Take(kopBegin).Select(r => (IReadOnlyList<Staatcel>)[new Staatcel(string.Join(' ', r.Select(w => w.Tekst)))]);
        return new Staattabel([.. boven, .. tabel]);
    }

    private static double Hoogte(List<Tekstwoord> rij) => rij.Average(w => (w.Y0 + w.Y1) / 2);

    /// <summary>De woorden van een kopregel samengevoegd tot zinsdelen ("fingen tot"): woorden met een gewone spatie ertussen.</summary>
    private static List<Tekstwoord> Delen(List<Tekstwoord> rij)
    {
        var delen = new List<Tekstwoord>();
        foreach (var w in rij)
        {
            if (delen.Count > 0 && w.X0 - delen[^1].X1 < Math.Max(3, (w.Y1 - w.Y0) * 0.6))
            {
                delen[^1] = delen[^1] with { Tekst = delen[^1].Tekst + " " + w.Tekst, X1 = w.X1 };
            }
            else
            {
                delen.Add(w);
            }
        }

        return delen;
    }

    /// <summary>De woorden per regel (per pagina van boven naar beneden), elke regel van links naar rechts.</summary>
    private static List<List<Tekstwoord>> Rijen(IReadOnlyList<Tekstwoord> woorden)
    {
        var rijen = new List<List<Tekstwoord>>();
        foreach (var pagina in woorden.GroupBy(w => w.Pagina).OrderBy(p => p.Key))
        {
            var huidig = new List<Tekstwoord>();
            double? hoogte = null;
            foreach (var w in pagina.OrderByDescending(w => (w.Y0 + w.Y1) / 2).ThenBy(w => w.X0))
            {
                var midden = (w.Y0 + w.Y1) / 2;
                if (hoogte is { } h && Math.Abs(h - midden) > Math.Max(2, (w.Y1 - w.Y0) * 0.5))
                {
                    rijen.Add([.. huidig.OrderBy(x => x.X0)]);
                    huidig = [];
                }

                hoogte = huidig.Count == 0 ? midden : hoogte;
                huidig.Add(w);
            }

            if (huidig.Count > 0)
            {
                rijen.Add([.. huidig.OrderBy(x => x.X0)]);
            }
        }

        return rijen;
    }

    [GeneratedRegex(@"^-?€?-?\(?[0-9][0-9.,]*\)?%?$|^-$")]
    private static partial Regex Getal();

    [GeneratedRegex(@"^(19|20)\d\d$")]
    private static partial Regex Jaartal();

    [GeneratedRegex(@"^\d{1,2}[-/.]\d{1,2}[-/.](\d{4}|\d{2})$")]
    private static partial Regex Datum();
}
