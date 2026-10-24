using System.Globalization;
using System.Net;
using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Export;

/// <summary>Het bespreekverslag als A4-document (HTML) voor de PDF: de bespreekpunten met hun uitkomst en de erkenning.</summary>
public static class BespreekverslagHtml
{
    public static string Bestandsnaam(Bespreekverslag verslag)
    {
        ArgumentNullException.ThrowIfNull(verslag);
        var naam = $"Bespreekverslag {verslag.Dossier.Boekjaar} {verslag.Dossier.KlantNaam}";
        return string.Concat(naam.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".pdf";
    }

    /// <summary>De tekst van de erkenning, in eigen woorden; bij de BV het bestuur, bij de eenmanszaak de ondernemer.</summary>
    public static string Erkenningstekst(SoortOpdracht soort) => soort == SoortOpdracht.SamenstellenJaarrekening
        ? "Het bestuur bevestigt dat het verantwoordelijk is voor de jaarrekening en dat de verstrekte gegevens en inlichtingen juist en volledig zijn."
        : "De ondernemer bevestigt dat de verstrekte gegevens en inlichtingen voor de cijfers en de aangifte juist en volledig zijn.";

    public static string Html(Bespreekverslag verslag)
    {
        ArgumentNullException.ThrowIfNull(verslag);
        var (dossier, b) = (verslag.Dossier, verslag.Bespreking);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="nl">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>{E(Path.GetFileNameWithoutExtension(Bestandsnaam(verslag)))}</title>
            <style>{Stijl}</style>
            </head>
            <body>
            <header>
              <div class="titel">Bespreekverslag {E(dossier.Naam)}</div>
              <div>{E(dossier.KlantNaam)}</div>
              <div class="zacht">{E(b.Datum is { } datum ? $"Besproken op {Weergave.DatumVoluit(datum)}" : "Nog niet besproken")}</div>
            """);
        if (b.Aanwezigen is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"zacht\">Aanwezig: {E(b.Aanwezigen)}</div>");
        }

        html.Append("</header>");
        if (b.Notitie is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"notitie\">{Regels(b.Notitie)}</p>");
        }

        html.Append("<h2>Bespreekpunten</h2>");
        if (b.Punten.Count == 0)
        {
            html.Append("<p class=\"zacht\">Geen bespreekpunten.</p>");
        }

        foreach (var (p, i) in b.Punten.Select((p, i) => (p, i + 1)))
        {
            html.Append(CultureInfo.InvariantCulture, $"""
                <section class="punt">
                  <h3><span class="nr">{i}</span>{E(p.Onderwerp)}{(p.Significant ? "<span class=\"kenmerk\">significante aangelegenheid</span>" : null)}</h3>
                  {(p.Toelichting is null ? null : $"<div class=\"zacht\">{Regels(p.Toelichting)}</div>")}
                  <div class="uitkomst">{(p.Uitkomst is null ? "<span class=\"zacht\">Nog niet besproken.</span>" : Regels(p.Uitkomst))}</div>
                </section>
                """);
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            <h2>Erkenning</h2>
            <p>{E(Erkenningstekst(dossier.Soort))}</p>
            <p class="zacht">{E(b.IsErkend
                ? $"Erkend door {b.ErkendDoor} op {Weergave.DatumVoluit(b.ErkendOp!.Value)}{(b.Wijze is { } w ? $" ({Weergave.Tekst(w)})" : null)}."
                : "Nog niet vastgelegd.")}</p>
            <footer class="zacht">{E($"{verslag.Kantoor.Naam} · opgesteld door {verslag.Door}, {Weergave.Datum(verslag.Op)}")}</footer>
            </body></html>
            """);
        return html.ToString();
    }

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);

    private static string Regels(string tekst) => string.Join("<br>", tekst.ReplaceLineEndings("\n").Split('\n').Select(E));

    private const string Stijl = """
        @page { size: A4; margin: 16mm 15mm 18mm; @bottom-right { content: "Pagina " counter(page) " van " counter(pages); font: 7.5pt sans-serif; color: #8a939b; } }
        body { font-family: "Segoe UI", "Inter", "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 9.5pt; color: #212529; margin: 0; line-height: 1.5; }
        header { margin-bottom: 6mm; }
        .titel { font-size: 15pt; font-weight: 600; margin-bottom: 1mm; }
        .zacht { color: #6c757d; }
        .notitie { margin: 0 0 4mm; }
        h2 { font-size: 10.5pt; font-weight: 600; margin: 6mm 0 2mm; border-bottom: 0.3mm solid #dee2e6; padding-bottom: 1mm; }
        .punt { break-inside: avoid; margin-bottom: 4mm; }
        h3 { font-size: 9.5pt; font-weight: 600; margin: 0 0 0.5mm; }
        h3 .nr { display: inline-block; min-width: 6mm; color: #5a6670; font-weight: normal; }
        h3 .kenmerk { margin-left: 3mm; font-size: 8pt; font-weight: normal; color: #6c757d; }
        .punt .zacht, .uitkomst { margin-left: 6mm; }
        footer { margin-top: 8mm; border-top: 0.3mm solid #dee2e6; padding-top: 2mm; font-size: 8pt; }
        """;
}
