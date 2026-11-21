using System.Globalization;
using System.Net;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Domain;

namespace Boekerhouden.Administratie.Export;

/// <summary>
/// De lijst openstaande posten als A4-document (liggend) voor de PDF: een kop met administratie en peildatum, per
/// relatie de posten met een subtotaal (of alleen de subtotalen, of alle posten op datum), en onderaan het totaal
/// generaal. Alles in de HTML zelf (geen bestanden van buiten).
/// </summary>
public static class OpenpostenHtml
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    public static string Maak(Openpostenlijst lijst, Openpostenweergave weergave = Openpostenweergave.PerRelatie)
    {
        ArgumentNullException.ThrowIfNull(lijst);
        var titel = lijst.Soort == Boekhouding.Facturen.Factuursoort.Inkoop ? "Openstaande posten crediteuren" : "Openstaande posten debiteuren";
        var relatienaam = lijst.Soort == Boekhouding.Facturen.Factuursoort.Inkoop ? "Leverancier" : "Klant";
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="nl"><head><meta charset="utf-8"><title>{{titel}}</title>
            <style>{{Exportstijl.Css}}
            </style></head><body>
            """);
        html.Append(CultureInfo.InvariantCulture, $"""
            <h1>{titel}</h1>
            <div class="kop"><div><span>Administratie:</span>{E(lijst.Administratie)}</div><div><span>Peildatum:</span>{Datum(lijst.Peildatum)}</div></div>
            """);
        switch (weergave)
        {
            case Openpostenweergave.Totalen:
                Totalen(html, lijst, relatienaam);
                break;
            case Openpostenweergave.Posten:
                Posten(html, lijst, relatienaam);
                break;
            default:
                PerRelatie(html, lijst);
                break;
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>Per relatie de posten met een subtotaal, en onderaan het totaal generaal.</summary>
    private static void PerRelatie(StringBuilder html, Openpostenlijst lijst)
    {
        const string Kolommen = """<colgroup><col style="width:9%"><col style="width:9%"><col><col style="width:12%"><col style="width:11%"><col style="width:11%"><col style="width:11%"><col style="width:5%"></colgroup>""";
        foreach (var relatie in lijst.Relaties)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<div class="relatie{(relatie.Posten.Count <= 20 ? " kort" : null)}"><h2><span class="code">{E(relatie.Relatie)}</span>{E(relatie.Naam)}</h2><table>{Kolommen}""");
            html.Append("""<thead><tr><th>Boekdatum</th><th>Vervaldatum</th><th>Omschrijving</th><th>Factuurnr.</th><th class="r">Factuurbedrag</th><th class="r">Betaald</th><th class="r">Openstaand</th><th class="r">Oud</th></tr></thead><tbody>""");
            foreach (var p in relatie.Posten)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"""<tr><td class="datum">{Datum(p.Datum)}</td><td class="datum">{Datum(p.Vervaldatum)}</td><td>{E(p.Omschrijving)}</td><td>{E(p.Factuurnummer)}</td><td class="r">{Bedrag(p.Factuurbedrag)}</td><td class="r">{Bedrag(p.Betaald)}</td><td class="r">{Bedrag(p.Openstaand)}</td><td class="r zacht">{p.Ouderdom}</td></tr>""");
            }

            html.Append(CultureInfo.InvariantCulture,
                $"""<tr class="totaal"><td colspan="4">Totaal {E(relatie.Naam)}</td><td class="r">{Bedrag(relatie.Factuurbedrag)}</td><td class="r">{Bedrag(relatie.Betaald)}</td><td class="r">{Bedrag(relatie.Openstaand)}</td><td></td></tr></tbody></table></div>""");
        }

        html.Append(CultureInfo.InvariantCulture,
            $"""<table class="generaal">{Kolommen}<tr><td colspan="4">Totaal generaal</td><td class="r">{Bedrag(lijst.Factuurbedrag)}</td><td class="r">{Bedrag(lijst.Betaald)}</td><td class="r">{Bedrag(lijst.Openstaand)}</td><td></td></tr></table>""");
    }

    /// <summary>Eén regel per relatie met het aantal posten, de subtotalen en de oudste post.</summary>
    private static void Totalen(StringBuilder html, Openpostenlijst lijst, string relatienaam)
    {
        const string Kolommen = """<colgroup><col style="width:12%"><col><col style="width:7%"><col style="width:13%"><col style="width:13%"><col style="width:13%"><col style="width:7%"></colgroup>""";
        html.Append(CultureInfo.InvariantCulture,
            $"""<table>{Kolommen}<thead><tr><th>Code</th><th>{relatienaam}</th><th class="r">Posten</th><th class="r">Factuurbedrag</th><th class="r">Betaald</th><th class="r">Openstaand</th><th class="r">Oudste</th></tr></thead><tbody>""");
        foreach (var r in lijst.Relaties)
        {
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr><td>{E(r.Relatie)}</td><td>{E(r.Naam)}</td><td class="r">{r.Posten.Count}</td><td class="r">{Bedrag(r.Factuurbedrag)}</td><td class="r">{Bedrag(r.Betaald)}</td><td class="r">{Bedrag(r.Openstaand)}</td><td class="r zacht">{(r.Posten.Count == 0 ? null : r.Posten.Max(p => p.Ouderdom))}</td></tr>""");
        }

        html.Append("</tbody></table>");
        html.Append(CultureInfo.InvariantCulture,
            $"""<table class="generaal">{Kolommen}<tr><td colspan="2">Totaal generaal</td><td class="r">{lijst.Relaties.Sum(r => r.Posten.Count)}</td><td class="r">{Bedrag(lijst.Factuurbedrag)}</td><td class="r">{Bedrag(lijst.Betaald)}</td><td class="r">{Bedrag(lijst.Openstaand)}</td><td></td></tr></table>""");
    }

    /// <summary>Alle posten in één lijst op boekdatum, met de relatie in een eigen kolom.</summary>
    private static void Posten(StringBuilder html, Openpostenlijst lijst, string relatienaam)
    {
        const string Kolommen = """<colgroup><col style="width:8%"><col style="width:8%"><col style="width:19%"><col><col style="width:10%"><col style="width:10%"><col style="width:9%"><col style="width:10%"><col style="width:5%"></colgroup>""";
        html.Append(CultureInfo.InvariantCulture,
            $"""<table>{Kolommen}<thead><tr><th>Boekdatum</th><th>Vervaldatum</th><th>{relatienaam}</th><th>Omschrijving</th><th>Factuurnr.</th><th class="r">Factuurbedrag</th><th class="r">Betaald</th><th class="r">Openstaand</th><th class="r">Oud</th></tr></thead><tbody>""");
        foreach (var (r, p) in lijst.Relaties.SelectMany(r => r.Posten.Select(p => (r, p))).OrderBy(x => x.p.Datum).ThenBy(x => x.p.Factuurnummer, StringComparer.Ordinal))
        {
            html.Append(CultureInfo.InvariantCulture,
                $"""<tr><td class="datum">{Datum(p.Datum)}</td><td class="datum">{Datum(p.Vervaldatum)}</td><td>{E(r.Naam)}</td><td>{E(p.Omschrijving)}</td><td>{E(p.Factuurnummer)}</td><td class="r">{Bedrag(p.Factuurbedrag)}</td><td class="r">{Bedrag(p.Betaald)}</td><td class="r">{Bedrag(p.Openstaand)}</td><td class="r zacht">{p.Ouderdom}</td></tr>""");
        }

        html.Append("</tbody></table>");
        html.Append(CultureInfo.InvariantCulture,
            $"""<table class="generaal">{Kolommen}<tr><td colspan="5">Totaal generaal</td><td class="r">{Bedrag(lijst.Factuurbedrag)}</td><td class="r">{Bedrag(lijst.Betaald)}</td><td class="r">{Bedrag(lijst.Openstaand)}</td><td></td></tr></table>""");
    }

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);

    private static string Datum(DateOnly datum) => datum.ToString("dd-MM-yyyy", Nederlands);

    private static string Bedrag(decimal bedrag) => Bedragen.Getal(bedrag);
}
