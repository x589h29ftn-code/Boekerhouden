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
            body { font-family: system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; font-size: 9pt; color: #212529; }
            h1 { font-size: 14pt; font-weight: 600; margin: 0 0 8pt; color: #212529; }
            .kop { display: flex; gap: 3rem; padding: 7pt 10pt; margin-bottom: 14pt; background: #f4f6f8; border: 1px solid #e3e7eb; border-radius: 4pt; }
            .kop span { color: #6c757d; margin-right: .5rem; }
            h2 { font-size: 10.5pt; font-weight: 600; margin: 14pt 0 4pt; }
            h2 .code { display: inline-block; min-width: 6rem; color: #6c757d; }
            table { width: 100%; border-collapse: collapse; table-layout: fixed; border: 1px solid #e3e7eb; }
            th { font-size: 7pt; font-weight: 600; letter-spacing: .05em; text-transform: uppercase; color: #6c757d; text-align: left;
                 background: #f4f6f8; border-bottom: 1px solid #1f5f8b; padding: 4pt 5pt; vertical-align: bottom; }
            td { padding: 2.5pt 5pt; border-bottom: 1px solid #eef0f2; }
            .r { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
            .datum { white-space: nowrap; }
            .zacht { color: #6c757d; }
            tr.totaal td { font-weight: 600; background: #f4f6f8; border-bottom: 0; }
            table.generaal { margin-top: 12pt; }
            table.generaal td { font-weight: 600; font-size: 10pt; background: #f4f6f8; border-top: 1px solid #1f5f8b; border-bottom: 0; }
            .relatie { break-inside: avoid; }
            .voet { margin-top: 10pt; font-size: 7.5pt; color: #6c757d; }
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
            html.Append("""<thead><tr><th>Boekdatum</th><th>Vervaldatum</th><th>Omschrijving</th><th>Factuurnr.</th><th class="r">Factuurbedrag</th><th class="r">Betaald</th><th class="r">Openstaand</th><th class="r">Oud</th></tr></thead><tbody>""");
            foreach (var p in relatie.Posten)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"""<tr><td class="datum">{Datum(p.Datum)}</td><td class="datum">{Datum(p.Vervaldatum)}</td><td>{E(p.Omschrijving)}</td><td>{E(p.Factuurnummer)}</td><td class="r">{Bedrag(p.Factuurbedrag)}</td><td class="r">{Bedrag(p.Betaald)}</td><td class="r">{Bedrag(p.Openstaand)}</td><td class="r zacht">{p.Ouderdom}</td></tr>""");
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
