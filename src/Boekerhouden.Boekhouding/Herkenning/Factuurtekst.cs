using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>
/// Haalt de gegevens uit de tekst van een factuur (uit de tekstlaag van een PDF of uit OCR): factuurnummer, datums,
/// IBAN, btw- en KvK-nummer, totalen, btw per tarief en betalingskenmerk. Werkt met Nederlandse en Engelse
/// labels. De eigen IBAN's en btw-nummers van de administratie (die ook op de factuur kunnen staan, bij de
/// klantgegevens) worden overgeslagen.
/// </summary>
public static partial class Factuurtekst
{
    private static readonly string[] Rechtsvormen = ["B.V.", "BV", "V.O.F.", "VOF", "N.V.", "C.V.", "Holding", "GmbH", "Ltd"];

    public static HerkendeFactuur Analyseer(
        string tekst,
        Herkenningsbron bron = Herkenningsbron.Tekst,
        IEnumerable<string>? eigenIbans = null,
        IEnumerable<string>? eigenBtwNummers = null,
        IEnumerable<string>? eigenNamen = null)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var regels = tekst.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        var eigenIban = (eigenIbans ?? []).Select(Iban.Normaliseer).ToHashSet(StringComparer.Ordinal);
        var eigenBtw = (eigenBtwNummers ?? []).Select(BtwNummer.Normaliseer).ToHashSet(StringComparer.Ordinal);

        var factuurdatum = DatumNaLabel(regels, FactuurdatumLabel()) ?? regels.SelectMany(r => Datums(r)).Cast<DateOnly?>().FirstOrDefault();
        var vervaldatum = DatumNaLabel(regels, VervaldatumLabel());
        if (vervaldatum is null && factuurdatum is { } d && Termijn().Match(tekst) is { Success: true } termijn)
        {
            vervaldatum = d.AddDays(int.Parse(termijn.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        var btw = Btwregels(regels);
        var exclusief = BedragNaLabel(regels, ExclusiefLabel());
        var inclusief = BedragNaLabel(regels, InclusiefLabel());
        var totaalBtw = btw.Sum(b => b.Btw);
        if (inclusief is null && exclusief is { } e)
        {
            inclusief = e + totaalBtw;
        }
        else if (exclusief is null && inclusief is { } i && btw.Count > 0)
        {
            exclusief = i - totaalBtw;
        }

        var hoofdletters = tekst.ToUpperInvariant();
        return new HerkendeFactuur(
            bron,
            Naam: Naam(regels),
            BtwNummer: BtwPatroon().Matches(hoofdletters).Select(m => BtwNummer.Normaliseer(m.Value)).FirstOrDefault(x => BtwNummer.IsGeldig(x) && !eigenBtw.Contains(x)),
            KvkNummer: Kvk().Match(tekst) is { Success: true } kvk ? kvk.Groups[1].Value : null,
            Iban: IbanPatroon().Matches(hoofdletters).Select(m => Iban.Normaliseer(m.Value)).FirstOrDefault(x => Iban.IsGeldig(x) && !eigenIban.Contains(x)),
            Factuurnummer: Factuurnummer(regels),
            Factuurdatum: factuurdatum,
            Vervaldatum: vervaldatum,
            TotaalExclusief: exclusief,
            TotaalInclusief: inclusief,
            Btw: btw,
            Betalingskenmerk: Kenmerk().Match(tekst) is { Success: true } kenmerk ? string.Concat(kenmerk.Groups[1].Value.Where(char.IsAsciiDigit)) : null,
            Omschrijving: Omschrijving(regels),
            OpEigenNaam: eigenNamen?.ToList() is { Count: > 0 } namen ? regels.Any(r => namen.Any(n => Namen.LijktOp(n, r))) : null,
            Postcodes: Postcode.Zoek(tekst));
    }

    /// <summary>
    /// De omschrijving van de eerste factuurregel: onder de kopregel met "Omschrijving" (of "Artikel", "Description")
    /// de eerste regel met een bedrag, zonder de bedragen en aantallen erachter. Bij meer regels met een bedrag komt
    /// er "e.a." achter.
    /// </summary>
    private static string? Omschrijving(List<string> regels)
    {
        var kop = regels.FindIndex(r => KopregelOmschrijving().IsMatch(r));
        if (kop < 0)
        {
            return null;
        }

        var factuurregels = regels.Skip(kop + 1)
            .TakeWhile(r => !ExclusiefLabel().IsMatch(r) && !InclusiefLabel().IsMatch(r) && !BtwRegel().IsMatch(r))
            .Where(r => BedragPatroon().IsMatch(r))
            .ToList();
        if (factuurregels.Count == 0)
        {
            return null;
        }

        var eerste = factuurregels[0];
        var tekst = eerste[..BedragPatroon().Match(eerste).Index];
        tekst = AantalAchteraan().Replace(tekst, string.Empty).Trim();
        if (tekst.Count(char.IsLetter) < 2)
        {
            return null;
        }

        return factuurregels.Count > 1 ? $"{tekst} e.a." : tekst;
    }

    /// <summary>Alle bedragen met twee decimalen in een regel, in volgorde (geen percentages).</summary>
    public static IReadOnlyList<decimal> Bedragen(string regel) =>
        BedragPatroon().Matches(regel)
            .Select(m => BedragParser.TryParse(m.Value, out var b) ? b : (decimal?)null)
            .Where(b => b is not null).Select(b => b!.Value).ToList();

    /// <summary>Alle datums in een regel: 12-03-2026, 12/3/26, 2026-03-12, 12 maart 2026, 12 mrt. 2026.</summary>
    public static IReadOnlyList<DateOnly> Datums(string regel)
    {
        var datums = new List<(int Positie, DateOnly Datum)>();
        foreach (Match m in NumeriekeDatum().Matches(regel))
        {
            var jaar = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            Voegtoe(datums, m.Index, jaar < 100 ? 2000 + jaar : jaar, m.Groups[2].Value, m.Groups[1].Value);
        }

        foreach (Match m in IsoDatum().Matches(regel))
        {
            Voegtoe(datums, m.Index, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, m.Groups[3].Value);
        }

        foreach (Match m in DatumMetMaand().Matches(regel))
        {
            var maand = Maand(m.Groups[2].Value);
            if (maand > 0)
            {
                Voegtoe(datums, m.Index, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), maand.ToString(CultureInfo.InvariantCulture), m.Groups[1].Value);
            }
        }

        return datums.OrderBy(d => d.Positie).Select(d => d.Datum).ToList();
    }

    private static void Voegtoe(List<(int, DateOnly)> datums, int positie, int jaar, string maand, string dag)
    {
        if (jaar is >= 1990 and <= 2100
            && int.TryParse(maand, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12
            && int.TryParse(dag, CultureInfo.InvariantCulture, out var d) && d >= 1 && d <= DateTime.DaysInMonth(jaar, m))
        {
            datums.Add((positie, new DateOnly(jaar, m, d)));
        }
    }

    private static int Maand(string naam) => naam.ToLowerInvariant().TrimEnd('.')[..3] switch
    {
        "jan" => 1,
        "feb" => 2,
        "maa" or "mrt" or "mar" => 3,
        "apr" => 4,
        "mei" or "may" => 5,
        "jun" => 6,
        "jul" => 7,
        "aug" => 8,
        "sep" => 9,
        "okt" or "oct" => 10,
        "nov" => 11,
        "dec" => 12,
        _ => 0,
    };

    private static DateOnly? DatumNaLabel(List<string> regels, Regex label)
    {
        for (var i = 0; i < regels.Count; i++)
        {
            if (label.Match(regels[i]) is { Success: true } m)
            {
                var rest = regels[i][(m.Index + m.Length)..];
                var datums = Datums(rest);
                if (datums.Count == 0 && i + 1 < regels.Count && rest.Trim().Trim(':').Length == 0)
                {
                    datums = Datums(regels[i + 1]);
                }

                if (datums.Count > 0)
                {
                    return datums[0];
                }
            }
        }

        return null;
    }

    /// <summary>Het bedrag na het laatste label dat past (totalen staan onderaan).</summary>
    private static decimal? BedragNaLabel(List<string> regels, Regex label)
    {
        for (var i = regels.Count - 1; i >= 0; i--)
        {
            if (label.Match(regels[i]) is { Success: true } m)
            {
                var bedragen = Bedragen(regels[i][(m.Index + m.Length)..]);
                if (bedragen.Count == 0 && i + 1 < regels.Count && regels[i][(m.Index + m.Length)..].Trim(' ', ':', '€', '\t').Length == 0)
                {
                    bedragen = Bedragen(regels[i + 1]);
                }

                if (bedragen.Count > 0)
                {
                    return bedragen[^1];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Regels als "Btw 21% over € 20,65: € 4,34" of "21% btw 4,34": het percentage met de bedragen erachter. Twee
    /// bedragen = grondslag en btw; één bedrag = de btw. Per tarief de laatste regel (de totalen staan onderaan).
    /// </summary>
    private static List<Btwregel> Btwregels(List<string> regels)
    {
        var perTarief = new Dictionary<decimal, Btwregel>();
        foreach (var regel in regels)
        {
            var m = BtwRegel().Match(regel);
            if (!m.Success)
            {
                continue;
            }

            var percentage = decimal.Parse((m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var bedragen = Bedragen(regel[(m.Index + m.Length)..]);
            if (percentage is not (21m or 9m or 0m or 6m or 19m or 20m) || bedragen.Count == 0)
            {
                continue;
            }

            var regelBtw = bedragen.Count >= 2 ? new Btwregel(percentage, bedragen[^2], bedragen[^1]) : new Btwregel(percentage, null, bedragen[0]);

            // "21% over 20,65 = 4,34": als de twee bedragen niet kloppen, is de laatste toch de btw.
            if (regelBtw.Grondslag is { } g && !Btw.BtwBerekening.Klopt(g, regelBtw.Btw, percentage, 5))
            {
                regelBtw = regelBtw with { Grondslag = null };
            }

            perTarief[percentage] = regelBtw;
        }

        return perTarief.Values.Where(b => b.Btw != 0 || b.Percentage == 0).OrderByDescending(b => b.Percentage).ToList();
    }

    private static string? Factuurnummer(List<string> regels)
    {
        for (var i = 0; i < regels.Count; i++)
        {
            if (FactuurnummerLabel().Match(regels[i]) is { Success: true } m)
            {
                var rest = regels[i][(m.Index + m.Length)..].TrimStart(' ', ':', '#', '.', '\t');
                if (rest.Length == 0 && i + 1 < regels.Count)
                {
                    rest = regels[i + 1];
                }

                var waarde = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.', ',', ';');
                if (waarde is { Length: > 0 } && waarde.Any(char.IsDigit))
                {
                    return waarde;
                }
            }
        }

        return null;
    }

    /// <summary>De naam van de leverancier: de eerste regel met een rechtsvorm, anders de eerste regel met letters.</summary>
    private static string? Naam(List<string> regels)
    {
        var bovenaan = regels.Take(15).ToList();
        var metRechtsvorm = bovenaan.FirstOrDefault(r => Rechtsvormen.Any(v => Regex.IsMatch(r, $@"(^|\s){Regex.Escape(v)}($|\s|,)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                                                         && !FactuurnummerLabel().IsMatch(r));
        return metRechtsvorm ?? bovenaan.FirstOrDefault(r => r.Count(char.IsLetter) >= 3 && !Regex.IsMatch(r, "^(factuur|invoice|nota|creditnota)\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
    }

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,3})?\b")]
    private static partial Regex IbanPatroon();

    [GeneratedRegex(@"\b[A-Z]{2}\s?[0-9]{9}\s?B\s?[0-9]{2}\b")]
    private static partial Regex BtwPatroon();

    [GeneratedRegex(@"(?:kvk|k\.v\.k\.|kamer van koophandel|coc|handelsregister)[^0-9\n]{0,20}([0-9]{8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Kvk();

    [GeneratedRegex(@"betalingskenmerk[^0-9\n]{0,5}([0-9][0-9 ]{14,22}[0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Kenmerk();

    [GeneratedRegex(@"(?:betalingstermijn|betaaltermijn|binnen|payment terms?)\D{0,15}([0-9]{1,3})\s*dagen", RegexOptions.IgnoreCase)]
    private static partial Regex Termijn();

    [GeneratedRegex(@"(?:factuurnummer|factuurnr\.?|factuur\s*nr\.?|factuur\s*#|notanummer|nota\s*nr\.?|invoice\s*(?:number|no\.?|nr\.?|#)|creditnotanummer)", RegexOptions.IgnoreCase)]
    private static partial Regex FactuurnummerLabel();

    [GeneratedRegex(@"\b(?:factuurdatum|datum\s*factuur|notadatum|invoice\s*date|datum)\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex FactuurdatumLabel();

    [GeneratedRegex(@"(?:vervaldatum|uiterste\s*betaaldatum|te\s*betalen\s*v[oó]{2}r|betalen\s*v[oó]{2}r|due\s*date)\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex VervaldatumLabel();

    [GeneratedRegex(@"(?:totaal\s*(?:excl\.?|exclusief|ex\.?)\s*(?:btw)?|subtotaal|sub\s*total|total\s*excl\.?\s*vat|netto\s*bedrag)", RegexOptions.IgnoreCase)]
    private static partial Regex ExclusiefLabel();

    [GeneratedRegex(@"\b(?:totaal\s*(?:incl\.?|inclusief)\s*(?:btw)?|te\s*betalen|totaalbedrag|total\s*(?:amount|due|incl\.?\s*vat)?|totaal)(?!\s*(?:excl|exclusief|ex\b|btw\b|v[oó]{2}r))", RegexOptions.IgnoreCase)]
    private static partial Regex InclusiefLabel();

    [GeneratedRegex(@"(?:(?:btw|omzetbelasting|vat|b\.t\.w\.)\s*(?:hoog|laag)?\s*\(?\s*([0-9]{1,2}(?:[.,][0-9]+)?)\s*%\)?|([0-9]{1,2}(?:[.,][0-9]+)?)\s*%\s*(?:btw|vat|omzetbelasting))", RegexOptions.IgnoreCase)]
    private static partial Regex BtwRegel();

    [GeneratedRegex(@"(?<![0-9.,])-?(?:[0-9]{1,3}(?:[.,][0-9]{3})+|[0-9]+)[.,][0-9]{2}(?![0-9]|[.,][0-9]|\s?%)")]
    private static partial Regex BedragPatroon();

    [GeneratedRegex(@"(?<![0-9])([0-9]{1,2})[-/.]([0-9]{1,2})[-/.]([0-9]{4}|[0-9]{2})(?![0-9])")]
    private static partial Regex NumeriekeDatum();

    [GeneratedRegex(@"^\s*(?:omschrijving|artikel|product|description|item)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KopregelOmschrijving();

    [GeneratedRegex(@"(?:\s+(?:€|eur)|\s{2,}[0-9]+(?:[.,][0-9]+)?\s*(?:x|st\.?|stuks?)?)+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AantalAchteraan();

    [GeneratedRegex(@"(?<![0-9])([0-9]{4})-([0-9]{2})-([0-9]{2})(?![0-9])")]
    private static partial Regex IsoDatum();

    [GeneratedRegex(@"(?<![0-9])([0-9]{1,2})\s+(januari|februari|maart|april|mei|juni|juli|augustus|september|oktober|november|december|january|february|march|may|june|july|august|october|jan|feb|mrt|mar|apr|jun|jul|aug|sept?|okt|oct|nov|dec)\.?\s+([0-9]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DatumMetMaand();
}
