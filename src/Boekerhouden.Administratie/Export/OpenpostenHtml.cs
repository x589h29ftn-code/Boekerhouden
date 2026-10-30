using System.Globalization;
using System.Net;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Domain;

namespace Boekerhouden.Administratie.Export;

/// <summary>
/// De lijst openstaande posten als A4-document (liggend) voor de PDF: een kop met administratie en peildatum, per
/// leverancier de posten met een totaalregel, en onderaan het totaal generaal. Alles in de HTML zelf (geen bestanden
/// van buiten).
/// </summary>
public static class OpenpostenHtml
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    public static string Maak(Openpostenlijst lijst)
    {
        ArgumentNullException.ThrowIfNull(lijst);
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="nl"><head><meta charset="utf-8"><title>Openstaande posten crediteuren</title>
            <style>
            @page { size: A4 landscape; margin: 14mm 12mm; }
            body { font-family: "Segoe UI", Arial, sans-serif; font-size: 9pt; color: #222; }
            .kop { display: flex; gap: 3rem; border: 1px solid #ccc; padding: 8pt 10pt; margin-bottom: 14pt; }
            .kop span { color: #555; margin-right: .6rem; }
            h1 { font-size: 13pt; margin: 0 0 10pt; }
            h2 { font-size: 11pt; margin: 14pt 0 4pt; }
            h2 .code { display: inline-block; min-width: 6rem; }
            table { width: 100%; border-collapse: collapse; table-layout: fixed; }
            th { color: #2a5f8f; font-weight: normal; text-align: left; border-bottom: 1px solid #999; padding: 2pt 4pt; vertical-align: bottom; }
            td { padding: 1.5pt 4pt; }
            .r { text-align: right; white-space: nowrap; }
            .datum { white-space: nowrap; }
            tr.totaal td { font-weight: bold; border-top: 1px solid #999; }
            table.generaal { margin-top: 12pt; }
            table.generaal td { font-weight: bold; font-size: 10pt; border-top: 1.5px solid #333; }
            .relatie { break-inside: avoid; }
            </style></head><body>
            """);
        html.Append(CultureInfo.InvariantCulture, $"""
            <h1>Openstaande posten crediteuren</h1>
            <div class="kop"><div><span>Administratie:</span>{E(lijst.Administratie)}</div><div><span>Peildatum:</span>{Datum(lijst.Peildatum)}</div></div>
            """);
        const string Kolommen = """<colgroup><col style="width:9%"><col style="width:9%"><col><col style="width:12%"><col style="width:11%"><col style="width:11%"><col style="width:11%"><col style="width:5%"></colgroup>""";
        foreach (var relatie in lijst.Relaties)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<div class="relatie"><h2><span class="code">{E(relatie.Relatie)}</span>{E(relatie.Naam)}</h2><table>{Kolommen}""");
            html.Append("""<thead><tr><th>Boekdatum</th><th>Vervaldatum</th><th>Omschrijving</th><th>Factuurnr.</th><th class="r">Factuurbedrag</th><th class="r">Betaald bedrag</th><th class="r">Openstaand bedrag</th><th class="r">Oud</th></tr></thead><tbody>""");
            foreach (var p in relatie.Posten)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"""<tr><td class="datum">{Datum(p.Datum)}</td><td class="datum">{Datum(p.Vervaldatum)}</td><td>{E(p.Omschrijving)}</td><td>{E(p.Factuurnummer)}</td><td class="r">{Bedrag(p.Factuurbedrag)}</td><td class="r">{Bedrag(p.Betaald)}</td><td class="r">{Bedrag(p.Openstaand)}</td><td class="r">{p.Ouderdom}</td></tr>""");
            }

            html.Append(CultureInfo.InvariantCulture,
                $"""<tr class="totaal"><td colspan="4">Totaal</td><td class="r">{Bedrag(relatie.Factuurbedrag)}</td><td class="r">{Bedrag(relatie.Betaald)}</td><td class="r">{Bedrag(relatie.Openstaand)}</td><td></td></tr></tbody></table></div>""");
        }

        html.Append(CultureInfo.InvariantCulture,
            $"""<table class="generaal">{Kolommen}<tr><td colspan="4">Totaal generaal</td><td class="r">{Bedrag(lijst.Factuurbedrag)}</td><td class="r">{Bedrag(lijst.Betaald)}</td><td class="r">{Bedrag(lijst.Openstaand)}</td><td></td></tr></table>""");
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);

    private static string Datum(DateOnly datum) => datum.ToString("dd-MM-yyyy", Nederlands);

    private static string Bedrag(decimal bedrag) => Bedragen.Getal(bedrag);
}
