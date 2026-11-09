using System.Globalization;
using System.Net;
using System.Text;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Domain;
using ClosedXML.Excel;

namespace Boekerhouden.Administratie.Export;

/// <summary>
/// De grootboekkaart en de kolommenbalans naar Excel (bedragen als getal, met de opmaak van het pakket) en als
/// A4-document voor de PDF, in de huisstijl van de crediteurenlijst.
/// </summary>
public static class OverzichtExport
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    private const string Getalnotatie = "#,##0.00;-#,##0.00;";

    public static byte[] KaartExcel(Grootboekkaart kaart, string administratie)
    {
        ArgumentNullException.ThrowIfNull(kaart);
        return KaartenExcel([kaart], administratie, kaart.Van, kaart.Tot, $"Grootboekkaart {kaart.Rekening.Nummer} {kaart.Rekening.Naam}");
    }

    /// <summary>Alle grootboekkaarten onder elkaar op één blad: per rekening een kop, het beginsaldo, de mutaties en het eindsaldo.</summary>
    public static byte[] KaartenExcel(IReadOnlyList<Grootboekkaart> kaarten, string administratie, DateOnly van, DateOnly tot, string titel = "Grootboekkaarten")
    {
        ArgumentNullException.ThrowIfNull(kaarten);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Grootboekkaart");
        Kop(blad, titel, administratie, van, tot);
        string[] koppen = ["Datum", "Dagboek", "Boekstuk", "Omschrijving", "Btw", "Btw-bedrag", "Factuur", "Debet", "Credit", "Saldo"];
        var rij = Koppen(blad, 4, koppen);
        foreach (var kaart in kaarten)
        {
            if (kaarten.Count > 1)
            {
                blad.Cell(rij, 1).Value = $"{kaart.Rekening.Nummer} {kaart.Rekening.Naam}";
                blad.Row(rij).Style.Font.Bold = true;
                rij++;
            }

            blad.Cell(rij, 1).Value = kaart.Van.ToDateTime(TimeOnly.MinValue);
            blad.Cell(rij, 4).Value = "Beginsaldo";
            blad.Cell(rij, 10).Value = kaart.Beginsaldo;
            rij++;
            foreach (var m in kaart.Mutaties)
            {
                blad.Cell(rij, 1).Value = m.Datum.ToDateTime(TimeOnly.MinValue);
                blad.Cell(rij, 2).Value = m.Dagboek;
                blad.Cell(rij, 3).Value = m.Boekstuk;
                blad.Cell(rij, 4).Value = m.Omschrijving;
                blad.Cell(rij, 5).Value = m.BtwCode ?? "";
                if (m.Btw is { } btw)
                {
                    blad.Cell(rij, 6).Value = btw;
                }

                blad.Cell(rij, 7).Value = m.Factuurnummer ?? "";
                if (m.Bedrag > 0)
                {
                    blad.Cell(rij, 8).Value = m.Bedrag;
                }
                else if (m.Bedrag < 0)
                {
                    blad.Cell(rij, 9).Value = -m.Bedrag;
                }

                blad.Cell(rij, 10).Value = m.Saldo;
                rij++;
            }

            blad.Cell(rij, 1).Value = "Totaal en eindsaldo";
            blad.Cell(rij, 8).Value = kaart.TotaalDebet;
            blad.Cell(rij, 9).Value = kaart.TotaalCredit;
            blad.Cell(rij, 10).Value = kaart.Eindsaldo;
            blad.Row(rij).Style.Font.Bold = true;
            rij += kaarten.Count > 1 ? 2 : 1;
        }

        blad.Column(1).Style.DateFormat.Format = "dd-mm-yyyy";
        foreach (var kolom in new[] { 6, 8, 9, 10 })
        {
            blad.Column(kolom).Style.NumberFormat.Format = Getalnotatie;
        }

        return Bewaar(boek, blad);
    }

    public static byte[] KolommenbalansExcel(Kolommenbalans balans, string administratie)
    {
        ArgumentNullException.ThrowIfNull(balans);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Kolommenbalans");
        Kop(blad, "Kolommenbalans", administratie, balans.Van, balans.Tot);
        string[] koppen = ["Rekening", "Omschrijving", "Debet W&V", "Credit W&V", "Debet balans", "Credit balans"];
        var rij = Koppen(blad, 4, koppen);
        foreach (var r in Regels(balans))
        {
            blad.Cell(rij, 1).Value = r.Rekening.Nummer;
            blad.Cell(rij, 2).Value = r.Rekening.Naam;
            Zet(blad, rij, 3, r.WinstEnVerliesDebet);
            Zet(blad, rij, 4, r.WinstEnVerliesCredit);
            Zet(blad, rij, 5, r.BalansDebet);
            Zet(blad, rij, 6, r.BalansCredit);
            rij++;
        }

        foreach (var (naam, a, b, c, d) in Totalen(balans))
        {
            blad.Cell(rij, 1).Value = naam;
            Zet(blad, rij, 3, a);
            Zet(blad, rij, 4, b);
            Zet(blad, rij, 5, c);
            Zet(blad, rij, 6, d);
            blad.Row(rij).Style.Font.Bold = true;
            rij++;
        }

        foreach (var kolom in new[] { 3, 4, 5, 6 })
        {
            blad.Column(kolom).Style.NumberFormat.Format = Getalnotatie;
        }

        return Bewaar(boek, blad);
    }

    /// <summary>De kolommenbalans met vorig jaar ernaast: per rekening beide saldi (debet positief) en het verschil in procenten.</summary>
    public static byte[] VergelijkingExcel(IReadOnlyList<Vergelijkingsregel> regels, DateOnly van, DateOnly tot, string administratie)
    {
        ArgumentNullException.ThrowIfNull(regels);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Vergelijking");
        Kop(blad, "Kolommenbalans met vorig jaar", administratie, van, tot);
        string[] koppen = ["Rekening", "Omschrijving", Jaarvergelijking.Kolomkop(van, tot), Jaarvergelijking.Kolomkop(van.AddYears(-1), tot.AddYears(-1)), "Verschil"];
        var rij = Koppen(blad, 4, koppen);
        foreach (var r in Vergelijkingsregels(regels))
        {
            blad.Cell(rij, 1).Value = r.Rekening.Nummer;
            blad.Cell(rij, 2).Value = r.Rekening.Naam;
            Zet(blad, rij, 3, r.Huidig);
            Zet(blad, rij, 4, r.Vorig);
            ZetProcent(blad, rij, 5, r.Procent);
            rij++;
        }

        var (nu, toen) = Jaarvergelijking.Resultaat(regels);
        blad.Cell(rij, 1).Value = "Resultaat";
        blad.Cell(rij, 3).Value = nu;
        blad.Cell(rij, 4).Value = toen;
        ZetProcent(blad, rij, 5, Jaarvergelijking.Procent(nu, toen));
        blad.Row(rij).Style.Font.Bold = true;
        foreach (var kolom in new[] { 3, 4 })
        {
            blad.Column(kolom).Style.NumberFormat.Format = Getalnotatie;
        }

        blad.Column(5).Style.NumberFormat.Format = "+0.0%;-0.0%;0.0%";
        return Bewaar(boek, blad);
    }

    public static string VergelijkingHtml(IReadOnlyList<Vergelijkingsregel> regels, DateOnly van, DateOnly tot, string administratie)
    {
        ArgumentNullException.ThrowIfNull(regels);
        var html = Begin("Kolommenbalans met vorig jaar", administratie, van, tot);
        html.Append("""<table><colgroup><col style="width:8%"><col><col style="width:15%"><col style="width:15%"><col style="width:11%"></colgroup>""");
        html.Append(CultureInfo.InvariantCulture,
            $"""<thead><tr><th>Rekening</th><th>Omschrijving</th><th class="r">{E(Jaarvergelijking.Kolomkop(van, tot))}</th><th class="r">{E(Jaarvergelijking.Kolomkop(van.AddYears(-1), tot.AddYears(-1)))}</th><th class="r">Verschil</th></tr></thead><tbody>""");
        foreach (var r in Vergelijkingsregels(regels))
        {
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr><td>{E(r.Rekening.Nummer)}</td><td>{E(r.Rekening.Naam)}</td><td class="r">{Leeg(r.Huidig)}</td><td class="r">{Leeg(r.Vorig)}</td><td class="r zacht">{E(Weergave.Procent(r.Procent) ?? "")}</td></tr>""");
        }

        var (nu, toen) = Jaarvergelijking.Resultaat(regels);
        html.Append(CultureInfo.InvariantCulture,
            $"""<tr class="totaal"><td colspan="2">Resultaat</td><td class="r">{Bedrag(nu)}</td><td class="r">{Bedrag(toen)}</td><td class="r">{E(Weergave.Procent(Jaarvergelijking.Procent(nu, toen)) ?? "")}</td></tr>""");
        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    private static readonly string[] Activastaatkoppen =
        ["Actief", "Boekwaarde 1-1", "Investeringen", "Desinvesteringen", "Afschrijvingen", "Boekwaarde 31-12", "Aanschafwaarde", "Cum. afschrijving"];

    private static decimal[] Activastaatbedragen(Boekhouding.Activa.Activastaatregel r) =>
        [r.BoekwaardeBegin, r.Investering, r.DesinvesteringAanschaf - r.DesinvesteringAfschrijving, r.Afschrijving, r.BoekwaardeEind, r.AanschafEind, r.AfschrijvingEind];

    /// <summary>De activastaat van een jaar: per activarekening de activa met subtotaal, en het totaal; bedragen als getal.</summary>
    public static byte[] ActivastaatExcel(IReadOnlyList<Diensten.Activastaatgroep> groepen, int jaar, string administratie)
    {
        ArgumentNullException.ThrowIfNull(groepen);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Activastaat");
        Kop(blad, $"Activastaat {jaar}", administratie, new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        var rij = Koppen(blad, 4, Activastaatkoppen);
        void Regel(string naam, decimal[] bedragen, bool vet)
        {
            blad.Cell(rij, 1).Value = naam;
            for (var i = 0; i < bedragen.Length; i++)
            {
                blad.Cell(rij, i + 2).Value = bedragen[i];
            }

            blad.Row(rij).Style.Font.Bold = vet;
            rij++;
        }

        foreach (var g in groepen)
        {
            blad.Cell(rij, 1).Value = $"{g.Activarekening} {g.Naam}";
            blad.Cell(rij, 1).Style.Font.Bold = true;
            rij++;
            foreach (var p in g.Posten)
            {
                Regel($"   {p.Nummer} {p.Omschrijving}", Activastaatbedragen(p.Regel), false);
            }

            Regel($"Totaal {g.Naam.ToLowerInvariant()}", Activastaatbedragen(g.Totaal), true);
            if (!g.Sluit)
            {
                blad.Cell(rij, 1).Value = "Volgens het grootboek (sluit niet)";
                blad.Cell(rij, 7).Value = g.GrootboekAanschaf;
                blad.Cell(rij, 8).Value = g.GrootboekAfschrijving;
                rij++;
            }
        }

        if (groepen.Count > 1)
        {
            Regel("Totaal", Activastaatbedragen(groepen.Aggregate(Boekhouding.Activa.Activastaatregel.Nul, (som, g) => som + g.Totaal)), true);
        }

        for (var kolom = 2; kolom <= Activastaatkoppen.Length; kolom++)
        {
            blad.Column(kolom).Style.NumberFormat.Format = Getalnotatie;
        }

        return Bewaar(boek, blad);
    }

    /// <summary>De activastaat als A4-document (liggend) voor de PDF.</summary>
    public static string ActivastaatHtml(IReadOnlyList<Diensten.Activastaatgroep> groepen, int jaar, string administratie)
    {
        ArgumentNullException.ThrowIfNull(groepen);
        var html = Begin($"Activastaat {jaar}", administratie, new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        html.Append("""<table><colgroup><col>""");
        html.Append(string.Concat(Enumerable.Repeat("""<col style="width:10.5%">""", Activastaatkoppen.Length - 1)));
        html.Append("</colgroup><thead><tr>");
        html.Append(CultureInfo.InvariantCulture, $"<th>{E(Activastaatkoppen[0])}</th>");
        foreach (var kop in Activastaatkoppen.Skip(1))
        {
            // Op papier over twee regels, anders lopen de koppen in elkaar over.
            var delen = kop.Split(' ', 2);
            html.Append(CultureInfo.InvariantCulture, $"""<th class="r">{E(delen[0])}{(delen.Length > 1 ? $"<br>{E(delen[1])}" : "")}</th>""");
        }

        html.Append("</tr></thead><tbody>");
        void Regel(string naam, decimal[] bedragen, string? klasse)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<tr{(klasse is null ? "" : $" class=\"{klasse}\"")}><td>{E(naam)}</td>""");
            foreach (var b in bedragen)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{Leeg(b)}</td>""");
            }

            html.Append("</tr>");
        }

        foreach (var g in groepen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td colspan="{Activastaatkoppen.Length}"><strong>{E(g.Activarekening)} {E(g.Naam)}</strong></td></tr>""");
            foreach (var p in g.Posten)
            {
                Regel($"{p.Nummer} {p.Omschrijving}", Activastaatbedragen(p.Regel), null);
            }

            Regel($"Totaal {g.Naam.ToLowerInvariant()}", Activastaatbedragen(g.Totaal), "totaal");
            if (!g.Sluit)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"""<tr class="zacht"><td colspan="6">Volgens het grootboek (sluit niet)</td><td class="r">{Bedrag(g.GrootboekAanschaf)}</td><td class="r">{Bedrag(g.GrootboekAfschrijving)}</td></tr>""");
            }
        }

        if (groepen.Count > 1)
        {
            Regel("Totaal", Activastaatbedragen(groepen.Aggregate(Boekhouding.Activa.Activastaatregel.Nul, (som, g) => som + g.Totaal)), "totaal");
        }

        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    public static string KaartHtml(Grootboekkaart kaart, string administratie)
    {
        ArgumentNullException.ThrowIfNull(kaart);
        return KaartenHtml([kaart], administratie, kaart.Van, kaart.Tot, $"Grootboekkaart {kaart.Rekening.Nummer} {kaart.Rekening.Naam}");
    }

    /// <summary>Alle grootboekkaarten onder elkaar, per rekening een eigen tabel met kop.</summary>
    public static string KaartenHtml(IReadOnlyList<Grootboekkaart> kaarten, string administratie, DateOnly van, DateOnly tot, string titel = "Grootboekkaarten")
    {
        ArgumentNullException.ThrowIfNull(kaarten);
        var html = Begin(titel, administratie, van, tot);
        foreach (var kaart in kaarten)
        {
            if (kaarten.Count > 1)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<h2 style="font-size:10pt;margin:14px 0 4px">{E(kaart.Rekening.Nummer)} {E(kaart.Rekening.Naam)}</h2>""");
            }

            html.Append("""<table><colgroup><col style="width:8%"><col style="width:6%"><col style="width:9%"><col><col style="width:5%"><col style="width:8%"><col style="width:11%"><col style="width:9%"><col style="width:9%"><col style="width:9%"></colgroup>""");
            html.Append("""<thead><tr><th>Datum</th><th>Dagboek</th><th>Boekstuk</th><th>Omschrijving</th><th>Btw</th><th class="r">Btw-bedrag</th><th>Factuur</th><th class="r">Debet</th><th class="r">Credit</th><th class="r">Saldo</th></tr></thead><tbody>""");
            html.Append(CultureInfo.InvariantCulture, $"""<tr class="zacht"><td class="datum">{Datum(kaart.Van)}</td><td colspan="8">Beginsaldo</td><td class="r">{Bedrag(kaart.Beginsaldo)}</td></tr>""");
            foreach (var m in kaart.Mutaties)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"""<tr><td class="datum">{Datum(m.Datum)}</td><td>{E(m.Dagboek)}</td><td>{E(m.Boekstuk)}</td><td>{E(m.Omschrijving)}</td><td class="zacht">{E(m.BtwCode ?? "")}</td><td class="r">{(m.Btw is { } b ? Bedrag(b) : "")}</td><td class="zacht">{E(m.Factuurnummer ?? "")}</td><td class="r">{(m.Bedrag > 0 ? Bedrag(m.Bedrag) : "")}</td><td class="r">{(m.Bedrag < 0 ? Bedrag(-m.Bedrag) : "")}</td><td class="r">{Bedrag(m.Saldo)}</td></tr>""");
            }

            html.Append(CultureInfo.InvariantCulture,
                $"""<tr class="totaal"><td colspan="7">Totaal en eindsaldo</td><td class="r">{Bedrag(kaart.TotaalDebet)}</td><td class="r">{Bedrag(kaart.TotaalCredit)}</td><td class="r">{Bedrag(kaart.Eindsaldo)}</td></tr></tbody></table>""");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    public static string KolommenbalansHtml(Kolommenbalans balans, string administratie)
    {
        ArgumentNullException.ThrowIfNull(balans);
        var html = Begin("Kolommenbalans", administratie, balans.Van, balans.Tot);
        html.Append("""<table><colgroup><col style="width:8%"><col><col style="width:13%"><col style="width:13%"><col style="width:13%"><col style="width:13%"></colgroup>""");
        html.Append("""<thead><tr><th>Rekening</th><th>Omschrijving</th><th class="r">Debet W&amp;V</th><th class="r">Credit W&amp;V</th><th class="r">Debet balans</th><th class="r">Credit balans</th></tr></thead><tbody>""");
        foreach (var r in Regels(balans))
        {
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr><td>{E(r.Rekening.Nummer)}</td><td>{E(r.Rekening.Naam)}</td><td class="r">{Leeg(r.WinstEnVerliesDebet)}</td><td class="r">{Leeg(r.WinstEnVerliesCredit)}</td><td class="r">{Leeg(r.BalansDebet)}</td><td class="r">{Leeg(r.BalansCredit)}</td></tr>""");
        }

        foreach (var (naam, a, b, c, d) in Totalen(balans))
        {
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr class="totaal"><td colspan="2">{E(naam)}</td><td class="r">{Leeg(a)}</td><td class="r">{Leeg(b)}</td><td class="r">{Leeg(c)}</td><td class="r">{Leeg(d)}</td></tr>""");
        }

        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    /// <summary>Een overzicht zoals op het scherm als Excel: koppen en subtotalen vet, ingesprongen naar niveau.</summary>
    public static byte[] TabelExcel(Exporttabel tabel, string administratie)
    {
        ArgumentNullException.ThrowIfNull(tabel);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet(tabel.Titel.Length > 31 ? tabel.Titel[..31] : tabel.Titel);
        Kop(blad, tabel.Titel, administratie, tabel.Van, tabel.Tot);
        var rij = Koppen(blad, 4, ["Rekening", "Omschrijving", .. tabel.Koppen]);
        foreach (var r in tabel.Regels)
        {
            blad.Cell(rij, 1).Value = r.Nummer ?? "";
            blad.Cell(rij, 2).Value = r.Tekst;
            blad.Cell(rij, 2).Style.Alignment.Indent = r.Soort == Exportsoort.Rekening ? r.Niveau + 1 : r.Niveau;
            for (var k = 0; k < r.Bedragen.Count; k++)
            {
                if (r.Bedragen[k] is { } b)
                {
                    Zet(blad, rij, k + 3, b);
                }
            }

            if (r.Soort != Exportsoort.Rekening)
            {
                blad.Row(rij).Style.Font.Bold = true;
            }

            rij++;
        }

        for (var k = 0; k < tabel.Koppen.Count; k++)
        {
            blad.Column(k + 3).Style.NumberFormat.Format = Getalnotatie;
        }

        return Bewaar(boek, blad);
    }

    /// <summary>Een overzicht zoals op het scherm als HTML voor de PDF (A4 liggend).</summary>
    public static string TabelHtml(Exporttabel tabel, string administratie)
    {
        ArgumentNullException.ThrowIfNull(tabel);
        var html = Begin(tabel.Titel, administratie, tabel.Van, tabel.Tot);
        var klein = tabel.Koppen.Count > 8 ? " style=\"font-size:7pt\"" : null;
        html.Append(CultureInfo.InvariantCulture, $"""<table{klein}><thead><tr><th>Rekening</th><th>Omschrijving</th>""");
        foreach (var kop in tabel.Koppen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<th class="r">{E(kop)}</th>""");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var r in tabel.Regels)
        {
            var klasse = r.Soort switch { Exportsoort.Kop => "kopregel", Exportsoort.Rekening => null, _ => "totaal" };
            var inspringen = (r.Soort == Exportsoort.Rekening ? r.Niveau + 1 : r.Niveau) * 0.8;
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr{(klasse is null ? null : $" class=\"{klasse}\"")}><td>{E(r.Nummer ?? "")}</td><td style="padding-left:{inspringen.ToString("0.0", CultureInfo.InvariantCulture)}em{(r.Soort == Exportsoort.Kop ? ";font-weight:600" : null)}">{E(r.Tekst)}</td>""");
            for (var k = 0; k < tabel.Koppen.Count; k++)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{(k < r.Bedragen.Count && r.Bedragen[k] is { } b ? Leeg(b) : null)}</td>""");
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    /// <summary>Alleen de rekeningen met een saldo in een van beide jaren, op nummer.</summary>
    private static IEnumerable<Vergelijkingsregel> Vergelijkingsregels(IEnumerable<Vergelijkingsregel> regels) =>
        regels.Where(r => r.Huidig != 0 || r.Vorig != 0).OrderBy(r => r.Rekening.Nummer, Rekeningnummers.Volgorde);

    /// <summary>Een percentage als getal in Excel (20% = 0,2), zodat ermee te rekenen is; leeg als het niets zegt.</summary>
    private static void ZetProcent(IXLWorksheet blad, int rij, int kolom, decimal? procent)
    {
        if (procent is { } p)
        {
            blad.Cell(rij, kolom).Value = p / 100m;
        }
    }

    /// <summary>Alleen de rekeningen met een saldo of mutaties, op nummer.</summary>
    private static IEnumerable<Kolommenbalansregel> Regels(Kolommenbalans balans) =>
        balans.Regels.Where(r => r.Debet != 0 || r.Credit != 0).OrderBy(r => r.Rekening.Nummer, Rekeningnummers.Volgorde);

    /// <summary>Totaal, resultaat (winst in de W&amp;V debet en op de balans credit) en totaal generaal, zoals op het scherm.</summary>
    private static IEnumerable<(string Naam, decimal WvDebet, decimal WvCredit, decimal BalansDebet, decimal BalansCredit)> Totalen(Kolommenbalans b)
    {
        yield return ("Totaal", b.WinstEnVerliesDebet, b.WinstEnVerliesCredit, b.BalansDebet, b.BalansCredit);
        var winst = b.Resultaat;
        yield return (winst >= 0 ? "Resultaat (winst)" : "Resultaat (verlies)", winst > 0 ? winst : 0, winst < 0 ? -winst : 0, winst < 0 ? -winst : 0, winst > 0 ? winst : 0);
        yield return ("Totaal generaal", b.WinstEnVerliesDebet + Math.Max(winst, 0), b.WinstEnVerliesCredit + Math.Max(-winst, 0),
            b.BalansDebet + Math.Max(-winst, 0), b.BalansCredit + Math.Max(winst, 0));
    }

    private static void Kop(IXLWorksheet blad, string titel, string administratie, DateOnly van, DateOnly tot)
    {
        blad.Cell(1, 1).Value = titel;
        blad.Cell(1, 1).Style.Font.Bold = true;
        blad.Cell(1, 1).Style.Font.FontSize = 13;
        blad.Cell(2, 1).Value = $"{administratie} · {Datum(van)} t/m {Datum(tot)}";
        blad.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6c757d");
    }

    private static int Koppen(IXLWorksheet blad, int rij, string[] koppen)
    {
        for (var i = 0; i < koppen.Length; i++)
        {
            var cel = blad.Cell(rij, i + 1);
            cel.Value = koppen[i];
            cel.Style.Font.Bold = true;
            cel.Style.Fill.BackgroundColor = XLColor.FromHtml("#f4f6f8");
            cel.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cel.Style.Border.BottomBorderColor = XLColor.FromHtml("#1d5c63");
        }

        blad.SheetView.FreezeRows(rij);
        return rij + 1;
    }

    private static void Zet(IXLWorksheet blad, int rij, int kolom, decimal bedrag)
    {
        if (bedrag != 0)
        {
            blad.Cell(rij, kolom).Value = bedrag;
        }
    }

    private static byte[] Bewaar(XLWorkbook boek, IXLWorksheet blad)
    {
        blad.Columns().AdjustToContents(4, 10, 60);
        using var stroom = new MemoryStream();
        boek.SaveAs(stroom);
        return stroom.ToArray();
    }

    private static StringBuilder Begin(string titel, string administratie, DateOnly van, DateOnly tot)
    {
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="nl"><head><meta charset="utf-8"><title>{{E(titel)}}</title>
            <style>{{Exportstijl.Css}}
            </style></head><body>
            <h1>{{E(titel)}}</h1>
            <div class="kop"><div><span>Administratie:</span>{{E(administratie)}}</div><div><span>Periode:</span>{{Datum(van)}} t/m {{Datum(tot)}}</div></div>
            """);
        return html;
    }

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);

    private static string Datum(DateOnly datum) => datum.ToString("dd-MM-yyyy", Nederlands);

    private static string Bedrag(decimal bedrag) => Bedragen.Getal(bedrag);

    private static string Leeg(decimal bedrag) => bedrag == 0 ? "" : Bedragen.Getal(bedrag);
}
