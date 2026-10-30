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
        var (exclusief, inclusief) = Totalen(regels, btw);

        // Geen btw-regel herkend (een onbekend label), maar wel drie bedragen die als exclusief, btw en totaal sluiten.
        if (btw.Count == 0 && !GeenBtwPatroon().IsMatch(tekst) && !VerlegdPatroon().IsMatch(tekst) && BtwUitDrietal(regels) is { } drietal
            && (inclusief is null || inclusief == drietal.Exclusief + drietal.Btw.Btw))
        {
            btw = [drietal.Btw];
            (exclusief, inclusief) = (drietal.Exclusief, drietal.Exclusief + drietal.Btw.Btw);
        }

        // Geen btw op de factuur (vrijgesteld, kleineondernemersregeling): 0% over het hele bedrag.
        if (btw.Count == 0 && GeenBtwPatroon().IsMatch(tekst) && (inclusief ?? exclusief) is { } zonderBtw)
        {
            btw = [new Btwregel(0m, zonderBtw, 0m)];
            (exclusief, inclusief) = (zonderBtw, zonderBtw);
        }
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
        var namen = eigenNamen?.Where(n => Relaties.Namen.Normaliseer(n).Length > 0).ToList() ?? [];
        return new HerkendeFactuur(
            bron,
            Naam: Naam(regels, namen),
            BtwNummer: BtwNummers(hoofdletters).FirstOrDefault(x => !eigenBtw.Contains(x)),
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
            OpEigenNaam: namen.Count > 0 ? regels.Any(r => namen.Any(n => Namen.LijktOp(n, r))) : null,
            Postcodes: Postcode.Zoek(tekst),
            Verlegd: VerlegdPatroon().IsMatch(tekst));
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
            // Zonder kopregel: na de regels met factuurnummer en datum, de eerste regel met tekst en een bedrag.
            kop = regels.FindLastIndex(r => FactuurnummerLabel().IsMatch(r) || FactuurdatumLabel().IsMatch(r) || VervaldatumLabel().IsMatch(r));
            if (kop < 0)
            {
                return null;
            }
        }

        var factuurregels = regels.Skip(kop + 1)
            .TakeWhile(r => !ExclusiefLabel().IsMatch(r) && !InclusiefLabel().IsMatch(r) && !BtwRegel().IsMatch(r) && !(BtwTabelKop().IsMatch(r) && Bedragen(r).Count == 0))
            .Where(r => BedragPatroon().IsMatch(r) && !KlantLabel().IsMatch(r) && Datums(r).Count == 0)
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

        // Engelse volgorde: "May 1, 2026".
        foreach (Match m in MaandEerst().Matches(regel))
        {
            var maand = Maand(m.Groups[1].Value);
            if (maand > 0)
            {
                Voegtoe(datums, m.Index, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), maand.ToString(CultureInfo.InvariantCulture), m.Groups[2].Value);
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

    /// <summary>
    /// Het totaal exclusief en inclusief btw, met de btw als controle. Een factuur kan meer "totalen" hebben (bijv. een
    /// specificatie met een kolom "Totaal" op een volgende pagina); daarom in deze volgorde:
    /// <list type="number">
    /// <item>een exclusief en inclusief bedrag na een label die sluiten (exclusief plus btw is inclusief);</item>
    /// <item>een totaal dat bij de btw-tabel past (grondslagen plus btw);</item>
    /// <item>een exclusief bedrag na een label waarvan exclusief plus btw ergens op de factuur staat (dat is het totaal),
    /// of een totaal na een label waarvan totaal min btw ergens op de factuur staat;</item>
    /// <item>zonder bruikbaar label: twee bedragen op de factuur die met de btw sluiten (het grootste totaal);</item>
    /// <item>anders het onderste bedrag na een label (een bedrag op dezelfde regel als het label gaat voor).</item>
    /// </list>
    /// Bij één tarief moet de btw ook dat percentage van het exclusieve bedrag zijn.
    /// </summary>
    private static (decimal? Exclusief, decimal? Inclusief) Totalen(List<string> regels, List<Btwregel> btw)
    {
        var exclusief = BedragenNaLabel(regels, ExclusiefLabel());
        var inclusief = BedragenNaLabel(regels, InclusiefLabel());
        if (btw.Count > 0)
        {
            var totaalBtw = btw.Sum(b => b.Btw);
            var marge = 0.01m * btw.Count;
            bool Past(decimal e) => btw is not [var enige] || Btw.BtwBerekening.Klopt(e, enige.Btw, enige.Percentage, 2);
            bool Sluit(decimal e, decimal i) => Math.Abs(e + totaalBtw - i) <= marge && Past(e);
            foreach (var i in inclusief)
            {
                if (exclusief.Where(e => Sluit(e, i)).Select(e => (decimal?)e).FirstOrDefault() is { } e)
                {
                    return (e, i);
                }
            }

            // Alleen het totaal past bij de btw-tabel (grondslagen plus btw).
            if (btw.All(b => b.Grondslag is not null) && btw.Sum(b => b.Grondslag!.Value) + totaalBtw is var somTabel
                && inclusief.Where(i => Math.Abs(i - somTabel) <= marge).Select(i => (decimal?)i).FirstOrDefault() is { } passend)
            {
                return (exclusief.Where(e => Sluit(e, passend)).Select(e => (decimal?)e).FirstOrDefault(), passend);
            }

            // Een label klopt, het andere bedrag staat (zonder herkend label) ergens op de factuur.
            var alle = AlleBedragen(regels);
            if (exclusief.Where(e => Past(e) && alle.Contains(e + totaalBtw)).Select(e => (decimal?)e).FirstOrDefault() is { } bevestigd)
            {
                return (bevestigd, bevestigd + totaalBtw);
            }

            if (inclusief.Where(i => Past(i - totaalBtw) && alle.Contains(i - totaalBtw)).Select(i => (decimal?)i).FirstOrDefault() is { } totaal)
            {
                return (totaal - totaalBtw, totaal);
            }

            // Geen label klopt: twee bedragen op de factuur die met de btw sluiten, het grootste totaal.
            if (alle.Where(i => i != totaalBtw && alle.Contains(i - totaalBtw) && Past(i - totaalBtw)).OrderByDescending(Math.Abs).Select(i => (decimal?)i).FirstOrDefault() is { } gevonden)
            {
                return (gevonden - totaalBtw, gevonden);
            }
        }

        return (exclusief.Count > 0 ? exclusief[0] : null, inclusief.Count > 0 ? inclusief[0] : null);
    }

    /// <summary>
    /// Zonder herkende btw-regel: staat er een drietal op de factuur dat sluit, met het grootste bedrag als totaal? Het
    /// totaal min een ander bedrag is een derde bedrag op de factuur, en dat is 21% of 9% van het andere (bijv. 200,00,
    /// 42,00 en 242,00). Drie bedragen die zo bij elkaar passen, zijn zelden toeval.
    /// </summary>
    private static (decimal Exclusief, Btwregel Btw)? BtwUitDrietal(List<string> regels)
    {
        var alle = AlleBedragen(regels);
        if (alle.Count < 3 || alle.MaxBy(Math.Abs) is var totaal && totaal == 0)
        {
            return null;
        }

        foreach (var e in alle.Where(e => e != totaal && Math.Sign(e) == Math.Sign(totaal)).OrderByDescending(Math.Abs))
        {
            var btw = totaal - e;
            if (btw != e && alle.Contains(btw) && Tarieven.Where(p => Btw.BtwBerekening.Klopt(e, btw, p)).Select(p => (decimal?)p).FirstOrDefault() is { } percentage)
            {
                return (e, new Btwregel(percentage, e, btw));
            }
        }

        return null;
    }

    private static readonly decimal[] Tarieven = [21m, 9m];

    private static HashSet<decimal> AlleBedragen(List<string> regels) => [.. regels.SelectMany(Bedragen)];

    /// <summary>
    /// De bedragen na een label, van onder naar boven (totalen staan onderaan): eerst die op dezelfde regel als het
    /// label, daarna die op de regel eronder (als het label achteraan de regel staat).
    /// </summary>
    private static List<decimal> BedragenNaLabel(List<string> regels, Regex label)
    {
        var zelfdeRegel = new List<decimal>();
        var regelEronder = new List<decimal>();
        for (var i = regels.Count - 1; i >= 0; i--)
        {
            if (label.Match(regels[i]) is not { Success: true } m)
            {
                continue;
            }

            var rest = regels[i][(m.Index + m.Length)..];
            if (Bedragen(rest) is [.., var bedrag])
            {
                zelfdeRegel.Add(bedrag);
            }
            else if (i + 1 < regels.Count && rest.Trim(' ', ':', '€', '\t', ')', '(').Length == 0 && Bedragen(regels[i + 1]) is [.., var onder])
            {
                regelEronder.Add(onder);
            }
        }

        return [.. zelfdeRegel, .. regelEronder];
    }

    /// <summary>
    /// Regels als "Btw 21% over € 20,65: € 4,34" of "21% btw 4,34": het percentage met de bedragen erachter. Twee
    /// bedragen = grondslag en btw; één bedrag = de btw. Per tarief de laatste regel (de totalen staan onderaan).
    /// </summary>
    private static List<Btwregel> Btwregels(List<string> regels)
    {
        var perTarief = new Dictionary<decimal, Btwregel>();
        var tabelTot = -1;
        for (var i = 0; i < regels.Count; i++)
        {
            var regel = regels[i];

            // Een btw-tabel: onder een kop als "Btw-percentage  Grondslag  Btw" regels als "21%  1.035,00  217,35".
            if (BtwTabelKop().IsMatch(regel) && Bedragen(regel).Count == 0)
            {
                tabelTot = i + 6;
                continue;
            }

            if (i <= tabelTot && TabelRegel().Match(regel) is { Success: true } rij
                && Bedragen(regel[(rij.Index + rij.Length)..]) is [.., var grondslag, var tabelBtw]
                && decimal.Parse(rij.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) is var tabelPercentage
                && Btw.BtwBerekening.Klopt(grondslag, tabelBtw, tabelPercentage, 5))
            {
                perTarief[tabelPercentage] = new Btwregel(tabelPercentage, grondslag, tabelBtw);
                continue;
            }

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

        // Alleen "Factuur" als label ("Faktuur   2026-10-01"), in de bovenste helft van de factuur.
        foreach (var regel in regels.Take(Math.Max(20, regels.Count / 2)))
        {
            if (KaalFactuurLabel().Match(regel) is { Success: true } m
                && regel[(m.Index + m.Length)..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.', ',', ';') is { Length: > 0 and <= 30 } waarde
                && waarde.Any(char.IsDigit))
            {
                return waarde;
            }
        }

        return null;
    }

    /// <summary>
    /// De naam van de leverancier. Elke regel wordt in kolommen gesplitst (en op "·" of "|", zoals in een voettekst); de
    /// eigen naam van de administratie (de klant op de factuur), het klantblok ("Klant:", "Bill to", "t.a.v.") en
    /// adressen tellen niet. Dan de eerste kolom met een rechtsvorm, eerst bovenaan en anders in de rest van de factuur
    /// (een leverancier die alleen in de voettekst staat); anders de eerste kolom met letters bovenaan.
    /// </summary>
    private static string? Naam(List<string> regels, IReadOnlyList<string> eigenNamen)
    {
        // Een regel direct boven "t.a.v." is de geadresseerde (de klant), niet de leverancier.
        bool BovenTav(int i) => i + 1 < regels.Count && TerAttentieVan().IsMatch(regels[i + 1]);
        var stukken = regels.Select((r, i) => (Regel: i, Delen: Kolommen().Split(r).Select(d => d.Trim()).Where(d => d.Length > 0).ToList()))
            .Where(x => !(x.Delen.Count == 1 && BovenTav(x.Regel)))
            .SelectMany(x => x.Delen.Select(d => (x.Regel, Tekst: d)))
            .Where(x => !IsEigen(x.Tekst, eigenNamen) && !KlantLabel().IsMatch(x.Tekst) && !IsAdres(x.Tekst) && !FactuurnummerLabel().IsMatch(x.Tekst)
                        && !ContactLabel().IsMatch(x.Tekst))
            .ToList();
        bool MetRechtsvorm(string t) => Rechtsvormen.Any(v => Regex.IsMatch(t, $@"(^|\s){Regex.Escape(v)}($|\s|,)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        return stukken.Where(x => x.Regel < 15).Select(x => x.Tekst).FirstOrDefault(MetRechtsvorm)
               ?? stukken.Select(x => x.Tekst).FirstOrDefault(MetRechtsvorm)
               ?? NaamUitDomein(regels, stukken.Select(x => x.Tekst), eigenNamen)
               ?? stukken.Where(x => x.Regel < 15).Select(x => x.Tekst).FirstOrDefault(t => t.Count(char.IsLetter) >= 3
                   && !Regex.IsMatch(t, "^(fa[ck]tuur|invoice|nota|creditnota|credit note)\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
                   && Datums(t).Count == 0 && Bedragen(t).Count == 0);
    }

    private static readonly string[] Maildiensten =
    [
        "gmail", "googlemail", "hotmail", "outlook", "live", "msn", "icloud", "me", "yahoo", "ziggo", "kpnmail", "kpnplanet", "planet",
        "home", "xs4all", "hetnet", "chello", "quicknet", "telfort", "online", "upcmail", "tele2", "caiway", "zeelandnet", "proton", "protonmail",
    ];

    private static readonly string[] AlgemeneAfzenders =
        ["info", "sales", "administratie", "factuur", "facturen", "billing", "invoice", "invoices", "contact", "office", "kantoor", "finance", "debiteuren", "boekhouding", "noreply", "no-reply", "mail", "service", "support"];

    /// <summary>
    /// De naam uit het domein van de leverancier, voor facturen waarop de naam alleen in het logo (een plaatje) staat:
    /// het domein van een e-mailadres of website op de factuur (liefst van een algemeen adres als info@), niet van een
    /// maildienst en niet van de eigen administratie. Staat de naam ergens als tekst (bijv. in de voettekst), dan die
    /// tekst; anders de naam uit het domein zelf ("p2-projecten.nl" wordt "P2 Projecten").
    /// </summary>
    private static string? NaamUitDomein(List<string> regels, IEnumerable<string> stukken, IReadOnlyList<string> eigenNamen)
    {
        var eigen = eigenNamen.Select(Compact).Where(n => n.Length >= 3).ToList();
        var domeinen = regels.SelectMany(r => Domein().Matches(r))
            .Select(m => (Afzender: m.Groups[1].Value.ToLowerInvariant(), Naam: m.Groups[2].Value.ToLowerInvariant()))
            .Where(d => d.Naam.Length >= 2 && !Maildiensten.Contains(d.Naam) && !eigen.Any(e => e.Contains(Compact(d.Naam), StringComparison.Ordinal) || Compact(d.Naam).Contains(e, StringComparison.Ordinal)))
            .ToList();
        if (domeinen.Count == 0)
        {
            return null;
        }

        var domein = (domeinen.FirstOrDefault(d => AlgemeneAfzenders.Contains(d.Afzender)) is { Naam: not null } algemeen ? algemeen : domeinen[0]).Naam;
        var kern = Compact(domein);
        if (stukken.FirstOrDefault(t => t.Length <= 60 && Compact(t).StartsWith(kern, StringComparison.Ordinal) && Compact(t).Length <= kern.Length + 12
                                        && !t.Contains('@', StringComparison.Ordinal) && !t.Contains("www", StringComparison.OrdinalIgnoreCase)) is { } tekst)
        {
            return tekst.Trim(' ', ',', '.', '-', '–', '|');
        }

        return string.Join(' ', domein.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(d => char.ToUpperInvariant(d[0]) + d[1..]));
    }

    private static string Compact(string tekst) => string.Concat(tekst.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>Is dit de eigen naam van de administratie (eventueel met rechtsvorm of een label als "Aan:")?</summary>
    private static bool IsEigen(string tekst, IReadOnlyList<string> eigenNamen)
    {
        var zonderLabel = KlantLabel().Replace(tekst, string.Empty).Trim();
        var genormaliseerd = Namen.Normaliseer(zonderLabel);
        return eigenNamen.Any(n => Namen.Normaliseer(n) is { Length: > 0 } eigen
                                   && (genormaliseerd == eigen || (eigen.Contains(' ', StringComparison.Ordinal) && genormaliseerd.Contains(eigen, StringComparison.Ordinal))));
    }

    /// <summary>Een adresregel: postcode met plaats, straat met huisnummer, of een postbus.</summary>
    private static bool IsAdres(string tekst) => Postcode.Zoek(tekst.ToUpperInvariant()).Count > 0 || Straat().IsMatch(tekst) || Postbus().IsMatch(tekst);

    [GeneratedRegex(@"^(?:postbus|p\.?\s?o\.?\s?box|antwoordnummer)\s*[0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex Postbus();

    [GeneratedRegex(@"^(?:t\.?\s?a\.?\s?v\.?|ter attentie van|attn\.?)\s", RegexOptions.IgnoreCase)]
    private static partial Regex TerAttentieVan();

    /// <summary>Contactgegevens met een label: "Telefoon 0612...", "Mail info@...", "KvK 1234...", "IBAN NL..."; geen naam.</summary>
    [GeneratedRegex(@"^(?:tel(?:efoon|\.)?|fax|mob(?:iel)?|gsm|e-?mail|mail|web(?:site)?|www\.|https?:|kvk|k\.v\.k|coc|iban|bic|swift|btw|vat|bank|rek(?:ening)?(?:nummer|nr)?)\b|@", RegexOptions.IgnoreCase)]
    private static partial Regex ContactLabel();

    /// <summary>Een e-mailadres (afzender en domein) of website (domein), bijv. info@p2-projecten.nl of www.p2-projecten.nl.</summary>
    [GeneratedRegex(@"(?:([a-z0-9._%+\-]+)@|www\.|https?://)(?:[a-z0-9\-]+\.)*?([a-z0-9][a-z0-9\-]*[a-z0-9])\.(?:[a-z]{2,6})(?![a-z0-9\-])", RegexOptions.IgnoreCase)]
    private static partial Regex Domein();

    /// <summary>Btw-nummers: Nederlandse overal in de tekst, buitenlandse (EU) na een label als "VAT" of "BTW".</summary>
    private static IEnumerable<string> BtwNummers(string hoofdletters) =>
        BtwPatroon().Matches(hoofdletters).Select(m => BtwNummer.Normaliseer(m.Value))
            .Concat(BuitenlandsBtw().Matches(hoofdletters).Select(m => BtwNummer.Normaliseer(m.Groups[1].Value)))
            .Where(BtwNummer.IsGeldig)
            .Where(n => n.StartsWith("NL", StringComparison.Ordinal) || BtwNummer.IsEuLand(n[..2]));

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,3})?\b")]
    private static partial Regex IbanPatroon();

    [GeneratedRegex(@"\b[A-Z]{2}\s?[0-9]{9}\s?B\s?[0-9]{2}\b")]
    private static partial Regex BtwPatroon();

    [GeneratedRegex(@"\b(?:VAT|BTW|TVA|IVA|UST-?IDNR\.?|USTID|MWST|VAT\s*(?:NO|NUMBER|REG\.?\s*NO)\.?)\s*(?:NR\.?|NUMMER|ID)?\s*:?\s*([A-Z]{2}\s?[0-9A-Z][0-9A-Z ]{6,13}[0-9A-Z])\b")]
    private static partial Regex BuitenlandsBtw();

    [GeneratedRegex(@"vrijgesteld\s+van\s+(?:de\s+)?(?:btw|omzetbelasting)|btw[- ]?vrijgesteld|geen\s+btw\s+(?:in\s+rekening|berekend)|kleine\s?ondernemersregeling|\bKOR\b|vat\s+exempt|exempt\s+from\s+vat", RegexOptions.IgnoreCase)]
    private static partial Regex GeenBtwPatroon();

    [GeneratedRegex(@"(?:btw|vat)[- ]?(?:%|perc|tarief|rate)?.*(?:grondslag|over|basis|excl|net)|(?:grondslag|basis).*(?:btw|vat)", RegexOptions.IgnoreCase)]
    private static partial Regex BtwTabelKop();

    [GeneratedRegex(@"^\s*([0-9]{1,2}(?:[.,][0-9]+)?)\s*%")]
    private static partial Regex TabelRegel();

    [GeneratedRegex(@"\s{3,}|\s+[·|]\s+")]
    private static partial Regex Kolommen();

    [GeneratedRegex(@"^(?:klant(?:nummer|nr\.?)?|debiteur(?:nummer)?|aan|t\.?\s?a\.?\s?v\.?|ter attentie van|factuuradres|afleveradres|leveradres|bill(?:ed)?\s*to|invoice\s*to|ship\s*to|sold\s*to|customer(?:\s*(?:no|number|id))?|deliver(?:ed)?\s*to)\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex KlantLabel();

    [GeneratedRegex(@"^[\p{L}][\p{L}'.\- ]{2,}\s[0-9]{1,5}\s?[a-zA-Z]?(?:[-/][0-9a-zA-Z]+)?(?:,|$)")]
    private static partial Regex Straat();

    [GeneratedRegex(@"btw\s*verlegd|verlegd\s*naar|verleggingsregeling|reverse[\s-]*charge|vat\s*reverse|art(?:icle|ikel)?\.?\s*196|autoliquidation|steuerschuldnerschaft", RegexOptions.IgnoreCase)]
    private static partial Regex VerlegdPatroon();

    [GeneratedRegex(@"(?:kvk|k\.v\.k\.|kamer van koophandel|coc|handelsregister)[^0-9\n]{0,20}([0-9]{8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Kvk();

    [GeneratedRegex(@"betalingskenmerk[^0-9\n]{0,5}([0-9][0-9 ]{14,22}[0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Kenmerk();

    [GeneratedRegex(@"(?:betalingstermijn|betaaltermijn|binnen|payment terms?)\D{0,15}([0-9]{1,3})\s*dagen", RegexOptions.IgnoreCase)]
    private static partial Regex Termijn();

    [GeneratedRegex(@"(?:fa[ck]tuurnummer|fa[ck]tuurnr\.?|fa[ck]tuur\s*nr\.?|fa[ck]tuur\s*#|notanummer|nota\s*nr\.?|invoice\s*(?:number|no\.?|nr\.?|#)|creditnotanummer)", RegexOptions.IgnoreCase)]
    private static partial Regex FactuurnummerLabel();

    /// <summary>Alleen "Factuur" als label vooraan een regel, met erachter een waarde met cijfers ("Factuur: 2026-117").</summary>
    [GeneratedRegex(@"^(?:fa[ck]tuur|invoice)\s*:?\s+(?=\S*[0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex KaalFactuurLabel();

    [GeneratedRegex(@"\b(?:fa[ck]tuurdatum|datum\s*fa[ck]tuur|notadatum|invoice\s*date|datum)\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex FactuurdatumLabel();

    [GeneratedRegex(@"(?:vervaldatum|uiterste\s*betaaldatum|te\s*betalen\s*v[oó]{2}r|betalen\s*v[oó]{2}r|due\s*date)\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex VervaldatumLabel();

    [GeneratedRegex(@"(?:totaal\s*\(?\s*(?:excl\.?|exclusief|ex\.?)\s*(?:btw)?\)?|subtotaal|sub\s*total|total\s*\(?\s*excl\.?\s*vat\)?|netto\s*bedrag)", RegexOptions.IgnoreCase)]
    private static partial Regex ExclusiefLabel();

    [GeneratedRegex(@"\b(?:totaal\s*\(?\s*(?:incl\.?|inclusief)\s*(?:btw)?\)?|te\s*betalen|totaalbedrag|fa[ck]tuurbedrag|total\s*\(?\s*(?:amount|due|incl\.?\s*vat)?\)?|totaal)(?!\s*\(?\s*(?:excl|exclusief|ex\b|btw\b|v[oó]{2}r))", RegexOptions.IgnoreCase)]
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

    [GeneratedRegex(@"\b(january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept?|oct|nov|dec)\.?\s+([0-9]{1,2}),?\s+([0-9]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex MaandEerst();
}
