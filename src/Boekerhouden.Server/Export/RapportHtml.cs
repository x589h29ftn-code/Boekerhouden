using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Export;

/// <summary>
/// Het rapport als HTML: op het scherm als voorbeeld (pagina's onder elkaar), en via de browser zonder venster als PDF
/// (A4, met paginanummers in de voettekst). Bedragen in hele euro's, nul als "-". Alle tekst is ge-escaped; het
/// document laadt niets van buiten (het logo staat er als data-URI in).
/// </summary>
public static partial class RapportHtml
{
    public const string Beveiliging = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";

    private const string StandaardKleur = "#1f4e79";

    private static readonly HashSet<string> LogoTypes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

    public static string Maak(RapportInhoud inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var rapport = inhoud.Rapport;
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="nl">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="{Beveiliging}">
            <title>{E($"{rapport.Titel} {rapport.Klantnaam}")}</title>
            <style>{Stijl(inhoud)}</style>
            </head>
            <body>
            """);

        Voorblad(html, inhoud);
        Inhoudsopgave(html, rapport);
        foreach (var deel in rapport.Delen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<section class="tussenblad"><div class="deel">{E(deel.Titel)}</div></section>""");
            foreach (var hoofdstuk in deel.Hoofdstukken)
            {
                Hoofdstuk(html, hoofdstuk);
            }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>De bestandsnaam van de PDF, bijv. "Jaarrekening 2025 K001 (concept).pdf".</summary>
    public static string Bestandsnaam(RapportInhoud inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = $"{inhoud.Rapport.Titel} {inhoud.Dossier.Klantnummer}{(inhoud.Rapport.Concept ? " (concept)" : null)}";
        return string.Concat(naam.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + ".pdf";
    }

    private static void Voorblad(StringBuilder html, RapportInhoud inhoud)
    {
        var rapport = inhoud.Rapport;
        var kantoor = inhoud.Kantoor;
        var logo = kantoor.Logo is { Length: > 0 } bytes && kantoor.LogoType is { } type && LogoTypes.Contains(type)
            ? $"""<img class="logo" src="data:{type.ToLowerInvariant()};base64,{Convert.ToBase64String(bytes)}" alt="">"""
            : string.Empty;
        var adres = string.Join(" · ", new[] { kantoor.Adres, $"{kantoor.Postcode} {kantoor.Plaats}".Trim() }.Where(t => !string.IsNullOrWhiteSpace(t)));
        html.Append(CultureInfo.InvariantCulture, $"""
            <section class="voorblad">
              <div class="boven">{logo}</div>
              <div class="midden">
                <div class="klant">{E(rapport.Klantnaam)}</div>
                {(rapport.Plaats is { Length: > 0 } plaats ? $"<div class=\"plaats\">te {E(plaats)}</div>" : null)}
                <div class="titel">{E(rapport.Titel)}</div>
                {(rapport.Concept ? $"<div class=\"concept\">Concept, {E(Weergave.DatumVoluit(inhoud.Op))}</div>" : null)}
              </div>
              <div class="onder">
                <div>{E(kantoor.Naam)}</div>
                <div>{E(adres)}</div>
              </div>
            </section>
            """);
    }

    private static void Inhoudsopgave(StringBuilder html, Rapport rapport)
    {
        html.Append("""<section class="inhoud"><h1>Inhoudsopgave</h1>""");
        foreach (var deel in rapport.Delen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<div class="inhoud-deel">{E(deel.Titel)}</div><ol>""");
            foreach (var hoofdstuk in deel.Hoofdstukken)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<li><span class="nr">{hoofdstuk.Nummer}</span>{E(hoofdstuk.Titel)}</li>""");
            }

            html.Append("</ol>");
        }

        if (rapport.Concept && rapport.Waarschuwingen.Count > 0)
        {
            html.Append("""<div class="aandacht"><div>Nog niet af</div><ul>""");
            foreach (var waarschuwing in rapport.Waarschuwingen)
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{E(waarschuwing)}</li>");
            }

            html.Append("</ul></div>");
        }

        html.Append("</section>");
    }

    private static void Hoofdstuk(StringBuilder html, Hoofdstuk hoofdstuk)
    {
        html.Append(CultureInfo.InvariantCulture, $"""<section class="hoofdstuk" id="{Anker(hoofdstuk)}"><h1><span class="nr">{hoofdstuk.Nummer}</span>{E(hoofdstuk.Titel)}</h1>""");
        var paragraaf = 0;
        foreach (var blok in hoofdstuk.Blokken)
        {
            switch (blok)
            {
                case TekstBlok tekst:
                    html.Append(CultureInfo.InvariantCulture, $"<p>{E(tekst.Tekst)}</p>");
                    break;
                case TabelBlok { Tabel: var tabel }:
                    var titel = tabel.Titel is null ? null
                        : hoofdstuk.Paragrafen ? $"""<h2><span class="nr">{hoofdstuk.Nummer}.{++paragraaf}</span>{E(tabel.Titel)}</h2>"""
                        : $"<h2>{E(tabel.Titel)}</h2>";
                    var anker = tabel.Code is null ? null : $" id=\"{Anker(tabel.Code)}\"";
                    html.Append(CultureInfo.InvariantCulture, $"""<div class="blok"{anker}>{titel}""");
                    Tabel(html, tabel);
                    html.Append("</div>");
                    break;
            }
        }

        html.Append("</section>");
    }

    /// <summary>
    /// Een tabel: tweekoloms (per jaar een detail- en een totaalkolom) of één kolom per jaar. Een lijn staat onder het
    /// laatste bedrag vóór een (sub)totaal: onder de detailkolom als het subtotaal detailregels optelt, anders onder
    /// de totaalkolom. Een eindtotaal krijgt een dubbele lijn.
    /// </summary>
    private static void Tabel(StringBuilder html, Tabel tabel)
    {
        var kolommen = tabel.Tweekoloms ? 5 : 3;
        html.Append(CultureInfo.InvariantCulture, $"""<table class="{(tabel.Tweekoloms ? "tweekoloms" : "eenkoloms")}"><thead><tr><th></th>""");
        var span = tabel.Tweekoloms ? " colspan=\"2\"" : string.Empty;
        html.Append(CultureInfo.InvariantCulture, $"""<th class="jaar"{span}>{E(tabel.KopHuidig)}<br>€</th><th class="jaar vorig"{span}>{E(tabel.KopVorig)}<br>€</th></tr></thead><tbody>""");

        for (var i = 0; i < tabel.Regels.Count; i++)
        {
            var regel = tabel.Regels[i];
            var volgende = i + 1 < tabel.Regels.Count ? tabel.Regels[i + 1].Soort : (RegelSoort?)null;
            if (regel.Soort == RegelSoort.Kop)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<tr class="kop"><td colspan="{kolommen}">{E(regel.Omschrijving)}</td></tr>""");
                continue;
            }

            // Waar de lijn komt: onder de detail- of de totaalkolom van deze regel.
            var lijn = volgende switch
            {
                RegelSoort.Eindtotaal => tabel.Tweekoloms ? "totaal" : "detail",
                RegelSoort.Subtotaal => regel.Soort == RegelSoort.Detail || !tabel.Tweekoloms ? "detail" : "totaal",
                _ => null,
            };
            var klasse = regel.Soort switch
            {
                RegelSoort.Post => "post",
                RegelSoort.Detail => "detail",
                RegelSoort.Subtotaal => "subtotaal",
                _ => "eindtotaal",
            };
            html.Append(CultureInfo.InvariantCulture, $"""<tr class="{klasse}"><td class="omschrijving">{E(regel.Omschrijving)}</td>""");
            if (tabel.Tweekoloms)
            {
                html.Append(Cel(regel.Detail, lijn == "detail"))
                    .Append(Cel(regel.Totaal, lijn == "totaal", regel.Soort == RegelSoort.Eindtotaal))
                    .Append(Cel(regel.DetailVorig, lijn == "detail", vorig: true))
                    .Append(Cel(regel.TotaalVorig, lijn == "totaal", regel.Soort == RegelSoort.Eindtotaal, vorig: true));
            }
            else
            {
                html.Append(Cel(regel.Detail ?? regel.Totaal, lijn is not null, regel.Soort == RegelSoort.Eindtotaal))
                    .Append(Cel(regel.DetailVorig ?? regel.TotaalVorig, lijn is not null, regel.Soort == RegelSoort.Eindtotaal, vorig: true));
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
    }

    private static string Cel(decimal? bedrag, bool lijn, bool dubbel = false, bool vorig = false)
    {
        var klassen = string.Join(' ', new[] { "bedrag", vorig ? "vorig" : null, lijn ? "lijn" : null, dubbel && bedrag is not null ? "dubbel" : null }.OfType<string>());
        return $"""<td class="{klassen}">{Bedrag(bedrag)}</td>""";
    }

    /// <summary>Een bedrag in hele euro's; nul als "-", leeg als er geen bedrag is.</summary>
    public static string Bedrag(decimal? bedrag) => bedrag switch
    {
        null => string.Empty,
        var b when Afronding.OpHeleEuro(b.Value) == 0 => "-",
        var b => Bedragen.Heel(b.Value),
    };

    /// <summary>Het anker van een hoofdstuk in het voorbeeld, bijv. "h3".</summary>
    public static string Anker(Hoofdstuk hoofdstuk)
    {
        ArgumentNullException.ThrowIfNull(hoofdstuk);
        return string.Create(CultureInfo.InvariantCulture, $"h{hoofdstuk.Nummer}");
    }

    /// <summary>Het anker van de specificatie van een post in het voorbeeld, bijv. "post-BMva".</summary>
    public static string Anker(string code) => "post-" + string.Concat(code.Where(char.IsAsciiLetterOrDigit));

    private static string E(string? tekst) => WebUtility.HtmlEncode(tekst ?? string.Empty);

    /// <summary>Tekst als CSS-string (voor de voettekst), zonder dat hij uit de string of het stijlblok kan breken.</summary>
    internal static string CssTekst(string tekst)
    {
        var uit = new StringBuilder("\"");
        foreach (var c in tekst)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            uit.Append(c is '"' or '\\' or '<' or '>' or '&' ? $"\\{(int)c:X} " : c.ToString());
        }

        return uit.Append('"').ToString();
    }

    private static string Stijl(RapportInhoud inhoud)
    {
        var rapport = inhoud.Rapport;
        var kleur = inhoud.Kantoor.Kleur is { } k && KleurPatroon().IsMatch(k) ? k : StandaardKleur;
        var links = CssTekst(rapport.Plaats is { Length: > 0 } plaats ? $"{rapport.Klantnaam}, {plaats}" : rapport.Klantnaam);
        // Bij de aangifte staat de datum van het rapport naast "Geen accountantscontrole toegepast" (docs §9.2).
        var datum = rapport.Soort == RapportSoort.AangifteBijlage ? Weergave.DatumVoluit(inhoud.Op) : null;
        var midden = CssTekst(string.Join(" · ", new[] { rapport.Concept ? "Concept" : null, rapport.Statustekst, datum }.Where(t => !string.IsNullOrEmpty(t))));
        return $$"""
            @page { size: A4; margin: 22mm 20mm 20mm;
              @bottom-left { content: {{links}}; font: 7.5pt sans-serif; color: #6c757d; }
              @bottom-center { content: {{midden}}; font: 7.5pt sans-serif; color: #6c757d; }
              @bottom-right { content: "Pagina " counter(page); font: 7.5pt sans-serif; color: #6c757d; } }
            @page voorblad { @bottom-left { content: none; } @bottom-center { content: none; } @bottom-right { content: none; } }
            * { box-sizing: border-box; }
            body { font-family: "Segoe UI", "Inter", "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 9pt; color: #212529; margin: 0; }
            section { break-before: page; }
            section:first-child { break-before: auto; }
            h1 { font-size: 12pt; font-weight: 600; text-transform: uppercase; letter-spacing: .3pt; color: {{kleur}}; margin: 0 0 7mm; }
            h2 { font-size: 9pt; font-weight: 600; text-transform: uppercase; letter-spacing: .2pt; margin: 0 0 2mm; }
            .nr { display: inline-block; min-width: 9mm; }
            .voorblad { page: voorblad; display: flex; flex-direction: column; height: 253mm; }
            .voorblad .boven { height: 30mm; text-align: right; }
            .voorblad .logo { max-height: 22mm; max-width: 60mm; }
            .voorblad .midden { flex: 1; padding-top: 70mm; }
            .voorblad .klant { font-size: 16pt; font-weight: 600; color: {{kleur}}; }
            .voorblad .plaats { font-size: 11pt; margin-top: 1mm; }
            .voorblad .titel { font-size: 20pt; margin-top: 16mm; }
            .voorblad .concept { font-size: 10pt; color: #6c757d; margin-top: 3mm; }
            .voorblad .onder { font-size: 8.5pt; color: #6c757d; line-height: 1.5; }
            .inhoud ol { list-style: none; padding: 0; margin: 0 0 6mm; line-height: 2; }
            .inhoud-deel { font-weight: 600; margin-bottom: 1mm; }
            .aandacht { margin-top: 12mm; color: #6c757d; font-size: 8.5pt; }
            .aandacht div { font-weight: 600; }
            .aandacht ul { padding-left: 4mm; }
            .tussenblad .deel { padding-top: 90mm; font-size: 16pt; letter-spacing: 0.5pt; color: {{kleur}}; }
            .blok { break-inside: avoid; margin-bottom: 8mm; }
            table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
            th { font-weight: normal; font-size: 8pt; color: #6c757d; padding: 0 1.5mm 1.5mm; }
            th.jaar { text-align: right; }
            td { padding: 0.6mm 1.5mm; vertical-align: top; }
            .tweekoloms td.bedrag { width: 21mm; }
            .eenkoloms td.bedrag { width: 28mm; }
            td.bedrag { text-align: right; white-space: nowrap; }
            .vorig { color: #5a6670; }
            th.vorig, td.bedrag.vorig { padding-left: 5mm; }
            tr.kop td { padding-top: 2.5mm; font-weight: 600; }
            tr.detail td.omschrijving { padding-left: 5mm; }
            .tweekoloms tr.detail td { color: #495057; }
            tr.subtotaal td, tr.eindtotaal td { font-weight: 600; }
            tr.eindtotaal td { padding-top: 1mm; }
            td.lijn { border-bottom: 0.25mm solid #212529; }
            td.dubbel { border-bottom: 0.7mm double #212529; }
            @media screen {
              body { background: #e9ecef; padding: 6mm 0; }
              section { background: #fff; width: 210mm; min-height: 297mm; margin: 0 auto 6mm; padding: 22mm 20mm 20mm; box-shadow: 0 1px 3px rgba(0, 0, 0, .12); }
              .voorblad { height: 297mm; }
            }
            """;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex KleurPatroon();
}
