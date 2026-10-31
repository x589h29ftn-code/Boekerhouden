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
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Grootboekkaart");
        Kop(blad, $"Grootboekkaart {kaart.Rekening.Nummer} {kaart.Rekening.Naam}", administratie, kaart.Van, kaart.Tot);
        string[] koppen = ["Datum", "Dagboek", "Boekstuk", "Omschrijving", "Btw", "Btw-bedrag", "Factuur", "Debet", "Credit", "Saldo"];
        var rij = Koppen(blad, 4, koppen);
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

    public static string KaartHtml(Grootboekkaart kaart, string administratie)
    {
        ArgumentNullException.ThrowIfNull(kaart);
        var html = Begin($"Grootboekkaart {kaart.Rekening.Nummer} {kaart.Rekening.Naam}", administratie, kaart.Van, kaart.Tot);
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
