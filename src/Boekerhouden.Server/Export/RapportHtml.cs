using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Boekerhouden.Data;
using Boekerhouden.Domain;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Components.Rapportweergave;
using Boekerhouden.Server.Diensten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Boekerhouden.Server.Export;

/// <summary>
/// Het rapport als HTML. De inhoud maakt één Razor-component (<see cref="RapportDocument"/>): in de app bewerkbaar,
/// voor de PDF statisch gerenderd en verpakt in een A4-document met voettekst en paginanummers. Zo is de PDF precies
/// wat je in de viewer ziet, zonder de knoppen. Bedragen in hele euro's, nul als "-". Het document laadt niets van
/// buiten (het logo staat er als data-URI in).
/// </summary>
public static partial class RapportHtml
{
    public const string Beveiliging = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";

    public const string StandaardKleur = "#1f4e79";

    private static readonly HashSet<string> LogoTypes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

    /// <summary>Het rapport als volledig HTML-document voor de PDF.</summary>
    public static async Task<string> Maak(IServiceProvider services, RapportInhoud inhoud)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(inhoud);
        await using var scope = services.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var lichaam = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(RapportDocument.Inhoud)] = inhoud });
            return (await renderer.RenderComponentAsync<RapportDocument>(parameters)).ToHtmlString();
        });
        return Document(inhoud, lichaam);
    }

    public static string Document(RapportInhoud inhoud, string lichaam)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        return $"""
            <!doctype html>
            <html lang="nl">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="{Beveiliging}">
            <title>{E($"{inhoud.Rapport.Titel} {inhoud.Rapport.Klantnaam}")}</title>
            <style>{PaginaStijl(inhoud)}{Stijl}</style>
            </head>
            <body>{lichaam}</body>
            </html>
            """;
    }

    /// <summary>De bestandsnaam van de PDF, bijv. "Jaarrekening 2025 K001 (concept).pdf".</summary>
    public static string Bestandsnaam(RapportInhoud inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = $"{inhoud.Rapport.Titel} {inhoud.Dossier.Klantnummer}{(inhoud.Rapport.Concept ? " (concept)" : null)}";
        return string.Concat(naam.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + ".pdf";
    }

    /// <summary>De huisstijlkleur van het kantoor, of de standaardkleur als die ontbreekt of niet klopt.</summary>
    public static string Kleur(Kantoorinstellingen kantoor)
    {
        ArgumentNullException.ThrowIfNull(kantoor);
        return kantoor.Kleur is { } kleur && KleurPatroon().IsMatch(kleur) ? kleur : StandaardKleur;
    }

    /// <summary>Het logo als data-URI; null zonder (geldig) logo.</summary>
    public static string? Logo(Kantoorinstellingen kantoor)
    {
        ArgumentNullException.ThrowIfNull(kantoor);
        return kantoor.Logo is { Length: > 0 } logo && kantoor.LogoType is { } type && LogoTypes.Contains(type)
            ? $"data:{type.ToLowerInvariant()};base64,{Convert.ToBase64String(logo)}"
            : null;
    }

    /// <summary>Het anker van een hoofdstuk, bijv. "h3".</summary>
    public static string Anker(Hoofdstuk hoofdstuk)
    {
        ArgumentNullException.ThrowIfNull(hoofdstuk);
        return string.Create(CultureInfo.InvariantCulture, $"h{hoofdstuk.Nummer}");
    }

    /// <summary>Het anker van de specificatie van een post, bijv. "post-BMva".</summary>
    public static string Anker(string code) => "post-" + string.Concat(code.Where(char.IsAsciiLetterOrDigit));

    /// <summary>Een bedrag in hele euro's; nul als "-", leeg als er geen bedrag is.</summary>
    public static string Bedrag(decimal? bedrag) => bedrag switch
    {
        null => string.Empty,
        var b when Afronding.OpHeleEuro(b.Value) == 0 => "-",
        var b => Bedragen.Heel(b.Value),
    };

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

    /// <summary>De pagina-instellingen voor de PDF: A4, voettekst met klant, status en paginanummer, geen voettekst op het voorblad.</summary>
    private static string PaginaStijl(RapportInhoud inhoud)
    {
        var rapport = inhoud.Rapport;
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
            body { margin: 0; }
            .rapport-document .voorblad { page: voorblad; }

            """;
    }

    /// <summary>
    /// De opmaak van het rapport, voor de viewer en de PDF. Alles valt onder <c>.rapport-document</c>, zodat het in de
    /// app niets anders raakt; de huisstijlkleur staat als <c>--huisstijl</c> op dat element.
    /// </summary>
    public const string Stijl = """
        .rapport-document { --huisstijl: #1f4e79; font-family: "Segoe UI", "Inter", "DejaVu Sans", "Liberation Sans", Arial, sans-serif; font-size: 9pt; line-height: 1.35; color: #212529; }
        .rapport-document * { box-sizing: border-box; }
        .rapport-document section { break-before: page; }
        .rapport-document section:first-child { break-before: auto; }
        .rapport-document h1 { font-size: 12pt; font-weight: 600; line-height: 1.3; text-transform: uppercase; letter-spacing: .3pt; color: var(--huisstijl); margin: 0 0 7mm; }
        .rapport-document h2 { font-size: 9pt; font-weight: 600; line-height: 1.3; text-transform: uppercase; letter-spacing: .2pt; margin: 0 0 2mm; }
        .rapport-document p { margin: 0 0 2mm; }
        .rapport-document .nr { display: inline-block; min-width: 9mm; }
        .rapport-document .voorblad { display: flex; flex-direction: column; height: 253mm; }
        .rapport-document .voorblad .boven { height: 30mm; text-align: right; }
        .rapport-document .voorblad .logo { max-height: 22mm; max-width: 60mm; }
        .rapport-document .voorblad .midden { flex: 1; padding-top: 70mm; }
        .rapport-document .voorblad .klant { font-size: 16pt; font-weight: 600; color: var(--huisstijl); }
        .rapport-document .voorblad .plaats { font-size: 11pt; margin-top: 1mm; }
        .rapport-document .voorblad .titel { font-size: 20pt; margin-top: 16mm; }
        .rapport-document .voorblad .concept { font-size: 10pt; color: #6c757d; margin-top: 3mm; }
        .rapport-document .voorblad .onder { font-size: 8.5pt; color: #6c757d; line-height: 1.5; }
        .rapport-document .inhoud ol { list-style: none; padding: 0; margin: 0 0 6mm; line-height: 2; }
        .rapport-document .inhoud-deel { font-weight: 600; margin-bottom: 1mm; }
        .rapport-document .aandacht { margin-top: 12mm; color: #6c757d; font-size: 8.5pt; }
        .rapport-document .aandacht div { font-weight: 600; }
        .rapport-document .aandacht ul { padding-left: 4mm; margin: 1mm 0; }
        .rapport-document .tussenblad .deel { padding-top: 90mm; font-size: 16pt; letter-spacing: .5pt; color: var(--huisstijl); }
        .rapport-document .blok { break-inside: avoid; margin-bottom: 8mm; }
        .rapport-document .toelichting { margin-top: 3mm; white-space: pre-line; }
        .rapport-document table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
        .rapport-document th { font-weight: normal; font-size: 8pt; color: #6c757d; padding: 0 1.5mm 1.5mm; text-align: left; }
        .rapport-document th.jaar { text-align: right; }
        .rapport-document td { padding: .6mm 1.5mm; vertical-align: top; border: 0; }
        .rapport-document td.omschrijving { position: relative; }
        .rapport-document .tweekoloms td.bedrag { width: 21mm; }
        .rapport-document .eenkoloms td.bedrag { width: 28mm; }
        .rapport-document td.bedrag { text-align: right; white-space: nowrap; }
        .rapport-document .vorig { color: #5a6670; }
        .rapport-document th.vorig, .rapport-document td.bedrag.vorig { padding-left: 5mm; }
        .rapport-document tr.kop td { padding-top: 2.5mm; font-weight: 600; }
        .rapport-document tr.detail td.omschrijving { padding-left: 5mm; }
        .rapport-document .tweekoloms tr.detail td { color: #495057; }
        .rapport-document tr.subtotaal td, .rapport-document tr.eindtotaal td { font-weight: 600; }
        .rapport-document tr.eindtotaal td { padding-top: 1mm; }
        .rapport-document td.lijn { border-bottom: .25mm solid #212529; }
        .rapport-document td.dubbel { border-bottom: .7mm double #212529; }
        @media screen {
          .rapport-document section { background: #fff; width: 210mm; min-height: 297mm; margin: 0 auto 6mm; padding: 22mm 20mm 20mm; box-shadow: 0 1px 3px rgba(0, 0, 0, .12); }
          .rapport-document .voorblad { height: 297mm; }
          .rapport-document .klap { position: absolute; left: -5mm; top: .3mm; width: 4.5mm; padding: 0; border: 0; background: none; color: #8a939b; font-size: 8pt; line-height: 1.6; cursor: pointer; }
          .rapport-document tr.detail .klap { left: 0; }
          .rapport-document tr.kop .klap { top: 2.8mm; }
          .rapport-document .klap:hover { color: #212529; }
          .rapport-document .bewerkbaar { cursor: text; border-bottom: 1px dotted transparent; }
          .rapport-document .bewerkbaar:hover { border-bottom-color: #adb5bd; }
          .rapport-document .bewerk-actie { visibility: hidden; margin-left: 2mm; padding: 0; border: 0; background: none; font-size: 7.5pt; color: #6c757d; text-decoration: underline; cursor: pointer; }
          .rapport-document tr:hover .bewerk-actie, .rapport-document .blok:hover > .bewerk-actie, .rapport-document .bewerk-actie:focus { visibility: visible; }
          .rapport-document .inline-invoer { width: 100%; font: inherit; color: inherit; padding: 0 1mm; border: 1px solid #86b7fe; border-radius: 2px; outline: none; }
          .rapport-document textarea.inline-invoer { min-height: 22mm; white-space: pre-wrap; }
          .rapport-document tr.verschil td { color: #8a6d1f; }
          .rapport-document tr.bewerker td { padding: 2mm 0 3mm 5mm; }
        }
        @media print {
          .rapport-document .klap, .rapport-document .bewerk-actie { display: none !important; }
        }
        """;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex KleurPatroon();
}
