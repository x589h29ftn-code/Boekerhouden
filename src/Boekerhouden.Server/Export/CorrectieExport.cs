using System.Globalization;
using System.Net;
using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Correcties;
using Boekerhouden.Server.Diensten;
using ClosedXML.Excel;

namespace Boekerhouden.Server.Export;

/// <param name="AlleenTeVerwerken">Alleen correcties die de klant nog in de eigen administratie moet boeken.</param>
public sealed record ExportVerzoek(CorrectieOverzicht Overzicht, bool AlleenTeVerwerken, string Door, DateTimeOffset Op)
{
    public IReadOnlyList<CorrectieInfo> Boekingen => AlleenTeVerwerken
        ? [.. Overzicht.Boekingen.Where(b => b.Soort == CorrectieSoort.Correctie && !b.VerwerktBijKlant)]
        : Overzicht.Boekingen;

    public CorrectieEffect Effect => Boekingen.Aggregate(default(CorrectieEffect), (som, b) => som.Plus(b.Effect));

    public string Bestandsnaam(string extensie)
    {
        var naam = $"Correcties {Overzicht.Dossier.Klantnummer} {Overzicht.Dossier.Boekjaar}{(AlleenTeVerwerken ? " te verwerken" : null)}";
        return string.Concat(naam.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + "." + extensie;
    }
}

/// <summary>
/// De correctieboekingen van een dossier als Excel (om te filteren of door te geven aan de klant) en als HTML voor de
/// PDF. Bedragen staan exact, met centen: het is een journaal dat de klant zo moet kunnen boeken.
/// </summary>
public static class CorrectieExport
{
    private const string Bedragnotatie = "#,##0.00;-#,##0.00;\"\"";

    public static byte[] Excel(ExportVerzoek verzoek)
    {
        ArgumentNullException.ThrowIfNull(verzoek);
        var dossier = verzoek.Overzicht.Dossier;
        using var boek = new XLWorkbook();
        var blad = boek.Worksheets.Add("Correcties");
        Kop(blad, verzoek, $"Correcties {dossier.Naam}");

        string[] koppen = ["Boeking", "Omschrijving", "Soort", "Rekening", "Rekeningomschrijving", "Toelichting", "Debet", "Credit"];
        const int kopRij = 5;
        for (var k = 0; k < koppen.Length; k++)
        {
            blad.Cell(kopRij, k + 1).Value = koppen[k];
        }

        var rij = kopRij;
        foreach (var boeking in verzoek.Boekingen)
        {
            foreach (var regel in boeking.Regels)
            {
                rij++;
                blad.Cell(rij, 1).Value = boeking.Nummer;
                blad.Cell(rij, 2).Value = boeking.Omschrijving;
                blad.Cell(rij, 3).Value = Soort(boeking.Soort);
                blad.Cell(rij, 4).Value = regel.Nummer;
                blad.Cell(rij, 5).Value = regel.RekeningOmschrijving;
                if (regel.Toelichting is { } toelichting)
                {
                    blad.Cell(rij, 6).Value = toelichting;
                }

                if (regel.Bedrag > 0)
                {
                    blad.Cell(rij, 7).Value = regel.Bedrag;
                }
                else
                {
                    blad.Cell(rij, 8).Value = -regel.Bedrag;
                }
            }
        }

        blad.Column(4).Style.NumberFormat.Format = "@";
        if (rij > kopRij)
        {
            var tabel = blad.Range(kopRij, 1, rij, koppen.Length).CreateTable("TabelCorrectieregels");
            tabel.Theme = XLTableTheme.TableStyleLight1;
            tabel.ShowTotalsRow = true;
            tabel.Field("Boeking").TotalsRowLabel = "Totaal";
            tabel.Field("Debet").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabel.Field("Credit").TotalsRowFunction = XLTotalsRowFunction.Sum;
        }
        else
        {
            blad.Cell(kopRij + 1, 1).Value = "Geen correcties.";
        }

        blad.Columns(7, 8).Style.NumberFormat.Format = Bedragnotatie;
        Breedtes(blad, 9, 40, 15, 11, 36, 30, 14, 14);
        blad.SheetView.FreezeRows(kopRij);

        var overzicht = boek.Worksheets.Add("Overzicht");
        Kop(overzicht, verzoek, $"Effect van de correcties {dossier.Naam}");
        string[] overzichtKoppen = ["Boeking", "Omschrijving", "Soort", "Terugkerend", "Verwerkt bij klant", "Bedrag", "Effect resultaat", "Effect eigen vermogen", "Gewijzigd door", "Gewijzigd op"];
        for (var k = 0; k < overzichtKoppen.Length; k++)
        {
            overzicht.Cell(kopRij, k + 1).Value = overzichtKoppen[k];
        }

        rij = kopRij;
        foreach (var boeking in verzoek.Boekingen)
        {
            rij++;
            overzicht.Cell(rij, 1).Value = boeking.Nummer;
            overzicht.Cell(rij, 2).Value = boeking.Omschrijving;
            overzicht.Cell(rij, 3).Value = Soort(boeking.Soort);
            overzicht.Cell(rij, 4).Value = boeking.Terugkerend ? "ja" : "nee";
            overzicht.Cell(rij, 5).Value = boeking.VerwerktBijKlant ? "ja" : "nee";
            overzicht.Cell(rij, 6).Value = boeking.Totaal;
            overzicht.Cell(rij, 7).Value = boeking.Effect.Resultaat;
            overzicht.Cell(rij, 8).Value = boeking.Effect.EigenVermogen;
            overzicht.Cell(rij, 9).Value = boeking.GewijzigdDoor;
            overzicht.Cell(rij, 10).Value = Weergave.DatumTijd(boeking.GewijzigdOp);
        }

        if (rij > kopRij)
        {
            var tabel = overzicht.Range(kopRij, 1, rij, overzichtKoppen.Length).CreateTable("TabelCorrecties");
            tabel.Theme = XLTableTheme.TableStyleLight1;
            tabel.ShowTotalsRow = true;
            tabel.Field("Boeking").TotalsRowLabel = "Totaal";
            tabel.Field("Effect resultaat").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabel.Field("Effect eigen vermogen").TotalsRowFunction = XLTotalsRowFunction.Sum;
        }

        overzicht.Columns(6, 8).Style.NumberFormat.Format = "#,##0.00;-#,##0.00;0.00";
        if (verzoek.Effect.Ongekoppeld > 0)
        {
            overzicht.Cell(rij + 3, 1).Value = $"{verzoek.Effect.Ongekoppeld} regel(s) staan op een rekening die nog niet aan RGS is gekoppeld; het effect daarvan is nog niet meegeteld.";
        }

        Breedtes(overzicht, 9, 40, 15, 12, 17, 14, 16, 20, 18, 17);
        overzicht.SheetView.FreezeRows(kopRij);

        using var stroom = new MemoryStream();
        boek.SaveAs(stroom);
        return stroom.ToArray();
    }

    /// <summary>Een A4-document (HTML) voor de PDF, in de rustige stijl van de app; alle tekst is ge-escaped.</summary>
    public static string Html(ExportVerzoek verzoek)
    {
        ArgumentNullException.ThrowIfNull(verzoek);
        var dossier = verzoek.Overzicht.Dossier;
        var vorigJaar = dossier.Boekjaar - 1;
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="nl">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>{E($"Correcties {dossier.Naam} {dossier.KlantNaam}")}</title>
            <style>{Stijl}</style>
            </head>
            <body>
            <header>
              <div class="titel">Correcties {E(dossier.Naam)}</div>
              <div>{E($"{dossier.Klantnummer} {dossier.KlantNaam}")}</div>
              <div class="zacht">{E(Ondertitel(verzoek))}</div>
            </header>
            """);

        if (verzoek.Boekingen.Count == 0)
        {
            html.Append("<p class=\"zacht\">Geen correcties.</p>");
        }

        foreach (var boeking in verzoek.Boekingen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""
                <section class="boeking">
                  <h2><span class="nr">{boeking.Nummer}</span>{E(boeking.Omschrijving)}<span class="kenmerk">{E(Kenmerken(boeking, vorigJaar))}</span></h2>
                  <table>
                    <thead><tr><th class="rek">Rekening</th><th>Omschrijving</th><th class="bedrag">Debet</th><th class="bedrag">Credit</th></tr></thead>
                    <tbody>
                """);
            foreach (var regel in boeking.Regels)
            {
                var toelichting = regel.Toelichting is null ? string.Empty : $"<span class=\"zacht\"> · {E(regel.Toelichting)}</span>";
                html.Append(CultureInfo.InvariantCulture, $"""
                    <tr><td class="rek">{E(regel.Nummer)}</td><td>{E(regel.RekeningOmschrijving)}{toelichting}</td><td class="bedrag">{(regel.Bedrag > 0 ? Bedragen.Getal(regel.Bedrag) : string.Empty)}</td><td class="bedrag">{(regel.Bedrag < 0 ? Bedragen.Getal(-regel.Bedrag) : string.Empty)}</td></tr>
                    """);
            }

            html.Append(CultureInfo.InvariantCulture, $"""
                    <tr class="totaal"><td></td><td>Totaal</td><td class="bedrag">{Bedragen.Getal(boeking.Totaal)}</td><td class="bedrag">{Bedragen.Getal(boeking.Totaal)}</td></tr>
                    </tbody>
                  </table>
                  <div class="effect zacht">{E(Effecttekst(boeking.Effect))}</div>
                </section>
                """);
        }

        if (verzoek.Boekingen.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"""
                <footer>
                  <div><strong>Totaal</strong> · {E(Effecttekst(verzoek.Effect))}</div>
                  <div class="zacht">Correcties veranderen de cijfers in het dossier, niet de administratie van de klant.</div>
                </footer>
                """);
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    public static string Soort(CorrectieSoort soort) => soort == CorrectieSoort.Herrubricering ? "Herrubricering" : "Correctie";

    /// <summary>Korte kenmerken van een boeking, bijv. "herrubricering · terugkerend · verwerkt bij klant".</summary>
    public static string Kenmerken(CorrectieInfo boeking, int vorigJaar)
    {
        ArgumentNullException.ThrowIfNull(boeking);
        return string.Join(" · ", new[]
        {
            boeking.Soort == CorrectieSoort.Herrubricering ? "herrubricering" : null,
            boeking.Terugkerend ? (boeking.Overgenomen ? $"terugkerend, uit {vorigJaar}" : "terugkerend") : null,
            boeking.VerwerktBijKlant ? "verwerkt bij klant" : null,
        }.OfType<string>());
    }

    private static string Ondertitel(ExportVerzoek verzoek) =>
        $"Boekjaar {verzoek.Overzicht.Dossier.Boekjaar} · {(verzoek.AlleenTeVerwerken ? "alleen correcties die de klant nog moet verwerken · " : null)}"
        + $"geëxporteerd op {Weergave.DatumTijd(verzoek.Op)} door {verzoek.Door}";

    private static string Effecttekst(CorrectieEffect effect) =>
        $"effect op het resultaat {Bedragen.Getal(effect.Resultaat)}, op het eigen vermogen {Bedragen.Getal(effect.EigenVermogen)}"
        + (effect.Ongekoppeld > 0 ? $" ({effect.Ongekoppeld} regel(s) op een nog niet gekoppelde rekening, niet meegeteld)" : null);

    private static void Kop(IXLWorksheet blad, ExportVerzoek verzoek, string titel)
    {
        blad.Cell(1, 1).Value = titel;
        blad.Cell(1, 1).Style.Font.Bold = true;
        blad.Cell(1, 1).Style.Font.FontSize = 13;
        blad.Cell(2, 1).Value = $"{verzoek.Overzicht.Dossier.Klantnummer} {verzoek.Overzicht.Dossier.KlantNaam}";
        blad.Cell(3, 1).Value = Ondertitel(verzoek);
        blad.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
    }

    private static void Breedtes(IXLWorksheet blad, params double[] breedtes)
    {
        for (var k = 0; k < breedtes.Length; k++)
        {
            blad.Column(k + 1).Width = breedtes[k];
        }
    }

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);

    private const string Stijl = """
        @page { size: A4; margin: 16mm 15mm 18mm; @bottom-right { content: "Pagina " counter(page) " van " counter(pages); font: 7.5pt sans-serif; color: #8a939b; } }
        body { font-family: "Segoe UI", "Inter", "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 9pt; color: #212529; margin: 0; }
        header { margin-bottom: 8mm; line-height: 1.5; }
        .titel { font-size: 15pt; font-weight: 600; margin-bottom: 1mm; }
        .zacht { color: #6c757d; }
        .boeking { break-inside: avoid; margin-bottom: 6mm; }
        h2 { font-size: 10pt; font-weight: 600; margin: 0 0 1.5mm; }
        h2 .nr { display: inline-block; min-width: 7mm; color: #5a6670; font-weight: normal; }
        h2 .kenmerk { margin-left: 3mm; font-size: 8pt; font-weight: normal; color: #6c757d; }
        table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
        th { text-align: left; font-weight: normal; font-size: 7.5pt; color: #8a939b; border-bottom: 0.3mm solid #dee2e6; padding: 0.8mm 1.5mm; }
        td { padding: 0.8mm 1.5mm; border-bottom: 0.2mm solid #eef0f2; vertical-align: top; }
        .rek { width: 18mm; color: #5a6670; }
        .bedrag { text-align: right; width: 26mm; white-space: nowrap; }
        tr.totaal td { border-bottom: 0; border-top: 0.3mm solid #dee2e6; font-weight: 600; }
        .effect { font-size: 8pt; margin-top: 1mm; }
        footer { border-top: 0.3mm solid #dee2e6; padding-top: 2mm; line-height: 1.6; }
        """;
}
