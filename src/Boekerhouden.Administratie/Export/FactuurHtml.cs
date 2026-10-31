using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Export;

/// <summary>Alles wat er op een verkoopfactuur staat, klaar om op te maken.</summary>
/// <param name="Codes">De btw-codes met de tarieven op de factuurdatum.</param>
/// <param name="Concept">Een concept krijgt "CONCEPT" over de pagina.</param>
public sealed record Verkoopbeeld(
    Verkoopfactuur Factuur,
    Partij Afzender,
    IReadOnlyList<BtwCode> Codes,
    string? Referentie = null,
    string? Tekst = null,
    bool Concept = false)
{
    public IReadOnlyList<Btwtotaal> Btw => Factuur.Btw(Codes);

    public decimal Totaal => Factuur.TotaalExclusief + Btw.Sum(b => b.Btw);

    public IReadOnlyList<string> Vermeldingen => Verkoopcontrole.Vermeldingen(Factuur, Codes);

    /// <summary>Een voorbeeldfactuur voor de ontwerper, met de gegevens van de eigen onderneming.</summary>
    public static Verkoopbeeld Voorbeeld(Partij afzender, IReadOnlyList<BtwCode> codes, DateOnly datum) => new(
        new Verkoopfactuur("VOORBEELD", new Partij("Voorbeeld Klant B.V.", new("Voorbeeldlaan 12", "1234 AB", "Voorbeeldstad"), Contactpersoon: "mevrouw J. Jansen"),
            Factuurnummering.Nummer(datum.Year, 1), datum, datum.AddDays(30), "Werkzaamheden",
            [
                new Verkoopregel("Advies en ontwerp", 12m, 85m, "8000", "V21", "uur"),
                new Verkoopregel("Materiaal volgens offerte", 1m, 240m, "8010", "V21"),
                new Verkoopregel("Broodjes voor de bijeenkomst", 20m, 2.5m, "8010", "V9", "stuk"),
            ],
            Betalingskenmerk.Maak(Factuurnummering.Nummer(datum.Year, 1))),
        afzender, codes, "PO-2026-118");
}

/// <summary>
/// De verkoopfactuur in HTML, volgens het ontwerp: dezelfde opmaak voor de PDF, de afdruk, het voorbeeld en de ontwerper.
/// Een A4 met de blokken op hun plek (in millimeters vanaf de linkerbovenhoek); de regels lopen door naar een volgende
/// pagina, de voettekst staat op elke pagina. Alles staat in de HTML zelf (lettertype en logo als data), zodat de PDF
/// niets van buiten laadt.
/// </summary>
public static class FactuurHtml
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Het hele document, voor de PDF of de afdruk.</summary>
    public static string Document(Verkoopbeeld beeld, Factuurontwerp ontwerp, Logo? logo)
    {
        ArgumentNullException.ThrowIfNull(beeld);
        ArgumentNullException.ThrowIfNull(ontwerp);
        ontwerp = ontwerp.Compleet();
        var titel = $"{Titel(beeld, ontwerp)} {beeld.Factuur.Nummer}";
        return $$"""
            <!doctype html>
            <html lang="nl"><head><meta charset="utf-8"><title>{{E(titel)}}</title>
            <style>
            {{Lettertypen(ontwerp)}}
            @page { size: A4; margin: 15mm 0 {{Mm(Ondermarge(ontwerp))}} 0; {{Voettekst(beeld, ontwerp)}} }
            @page :first { margin-top: 0; }
            html, body { margin: 0; padding: 0; background: #fff; }
            {{Css(ontwerp)}}
            .fp-document { min-height: 0; }
            .fp-papier .fp-voet { display: none; }
            </style></head><body class="fp-papier">
            {{Pagina(beeld, ontwerp, logo, ontwerper: false)}}
            </body></html>
            """;
    }

    /// <summary>De opmaak van een pagina, alleen binnen <c>.fp</c> (zodat hij ook in een scherm past).</summary>
    public static string Css(Factuurontwerp ontwerp)
    {
        ArgumentNullException.ThrowIfNull(ontwerp);
        var a = ontwerp.Accentkleur;
        var streep = ontwerp.Tabelstijl switch
        {
            Tabelstijl.Strepen => ".fp-regels tbody tr:nth-child(even) td { background: color-mix(in srgb, " + a + " 7%, #fff); } .fp-regels tbody td { border: 0; }",
            Tabelstijl.Rustig => ".fp-regels tbody td { border: 0; }",
            _ => string.Empty,
        };
        return $$"""
            .fp { position: relative; width: 210mm; min-height: 297mm; box-sizing: border-box; color: {{ontwerp.Tekstkleur}}; font-family: {{ontwerp.LettertypeCss}}; font-size: {{Pt(ontwerp.Lettergrootte)}}; line-height: 1.4; background: #fff; }
            .fp * { box-sizing: border-box; }
            .fp-blok { position: absolute; overflow-wrap: anywhere; }
            .fp-vet { font-weight: 600; }
            .fp-b-titel { color: {{a}}; line-height: 1.15; }
            .fp-b-logo img { max-width: 100%; max-height: 100%; object-fit: contain; object-position: left top; display: block; }
            .fp-b-logo.rechts img { object-position: right top; margin-left: auto; }
            .fp-gegevens { display: grid; grid-template-columns: auto 1fr; column-gap: 4mm; row-gap: .4mm; }
            .fp-gegevens .label { color: #666; }
            .fp-stroom { position: relative; pointer-events: none; }
            .fp-stroom > * { pointer-events: auto; }
            .fp-regels { width: 100%; border-collapse: collapse; }
            .fp-regels th { text-align: left; font-weight: 600; color: {{a}}; border-bottom: .4mm solid {{a}}; padding: 1.2mm 1.5mm; white-space: nowrap; }
            .fp-regels td { padding: 1.2mm 1.5mm; border-bottom: .2mm solid #ddd; vertical-align: top; }
            .fp-regels .r, .fp-totalen .r { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
            .fp-regels tr { break-inside: avoid; }
            {{streep}}
            .fp-totalen { width: 75mm; margin: 2mm 0 0 auto; border-collapse: collapse; break-inside: avoid; }
            .fp-totalen td { padding: .8mm 1.5mm; }
            .fp-totalen .totaal td { font-weight: 600; border-top: .4mm solid {{a}}; padding-top: 1.5mm; }
            .fp-vermelding { margin-top: 3mm; }
            .fp-tekst { margin-top: 4mm; white-space: pre-line; }
            .fp-betaal { margin-top: 6mm; break-inside: avoid; }
            .fp-voet { position: absolute; }
            .fp-concept { position: absolute; left: 0; top: 120mm; width: 210mm; text-align: center; font-size: 72pt; font-weight: 600; color: rgba(0,0,0,.06); transform: rotate(-30deg); pointer-events: none; }
            .fp-ontwerp .fp-kies { cursor: move; outline: .25mm dashed transparent; outline-offset: .8mm; }
            .fp-ontwerp .fp-kies:hover { outline-color: #9bb; }
            .fp-ontwerp .fp-kies.gekozen { outline: .35mm solid {{a}}; }
            .fp-ontwerp .fp-kies.uit { opacity: .3; }
            .fp-ontwerp .fp-greep { position: absolute; right: -2.2mm; top: 50%; width: 2mm; height: 6mm; margin-top: -3mm; background: {{a}}; border-radius: 1mm; cursor: ew-resize; display: none; }
            .fp-ontwerp .gekozen > .fp-greep { display: block; }
            .fp-ontwerp .fp-leeg { border: .3mm dashed #bbb; color: #999; height: 100%; display: flex; align-items: center; justify-content: center; }
            """;
    }

    /// <summary>
    /// De pagina zelf. In de ontwerper krijgt elk blok <c>data-blok</c> (om te kiezen en te verslepen), en blijven
    /// uitgezette blokken vaag zichtbaar.
    /// </summary>
    public static string Pagina(Verkoopbeeld beeld, Factuurontwerp ontwerp, Logo? logo, bool ontwerper, string? gekozen = null)
    {
        ArgumentNullException.ThrowIfNull(beeld);
        ArgumentNullException.ThrowIfNull(ontwerp);
        ontwerp = ontwerp.Compleet();
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""<div class="fp {(ontwerper ? "fp-ontwerp" : "fp-document")}">""");
        if (beeld.Concept && !ontwerper)
        {
            html.Append("""<div class="fp-concept">CONCEPT</div>""");
        }

        foreach (var blok in ontwerp.Blokken.Where(b => b.Soort is not (Bloksoort.Regels or Bloksoort.Betaalinstructie or Bloksoort.Voettekst)))
        {
            if (!blok.Zichtbaar && !ontwerper)
            {
                continue;
            }

            var inhoud = Inhoud(blok, beeld, ontwerp, logo, ontwerper);
            if (inhoud is null)
            {
                continue;
            }

            html.Append(Blok(blok, inhoud, ontwerper, gekozen, $"top:{Mm(blok.Y)};"));
        }

        // De regels lopen in de tekst: ze beginnen op hun hoogte op de eerste pagina en gaan door op de volgende.
        var regels = ontwerp.Blok(Bloksoort.Regels)!;
        var betaal = ontwerp.Blok(Bloksoort.Betaalinstructie)!;
        var voet = ontwerp.Blok(Bloksoort.Voettekst);

        html.Append(CultureInfo.InvariantCulture, $"""<div class="fp-stroom" style="padding-top:{Mm(regels.Y)}">""");
        html.Append(Blok(regels, Regels(beeld, ontwerp), ontwerper, gekozen, "position:relative;"));
        if ((betaal.Zichtbaar || ontwerper) && !beeld.Factuur.IsCredit && betaal.Tekst is { Length: > 0 } instructie)
        {
            html.Append(Blok(betaal, Tekst(Factuurteksten.Vul(instructie, beeld.Factuur, beeld.Afzender, beeld.Totaal)), ontwerper, gekozen, "position:relative;", "fp-betaal"));
        }

        html.Append("</div>");
        // De voettekst: op het scherm op zijn plek; op papier in de marge onder elke pagina (zie Voettekst).
        if (voet is { Tekst.Length: > 0 } && (voet.Zichtbaar || ontwerper))
        {
            html.Append(Blok(voet, Tekst(Factuurteksten.Vul(voet.Tekst, beeld.Factuur, beeld.Afzender, beeld.Totaal)), ontwerper, gekozen, $"top:{Mm(voet.Y)};", "fp-voet"));
        }

        html.Append("</div>");
        return html.ToString();
    }

    /// <summary>De marge onder elke pagina: de voettekst staat daarin, vanaf zijn plek.</summary>
    private static decimal Ondermarge(Factuurontwerp ontwerp) =>
        ontwerp.Blok(Bloksoort.Voettekst) is { Zichtbaar: true, Tekst.Length: > 0 } voet ? Math.Max(8m, Factuurontwerp.PaginaHoogte - voet.Y) : 12m;

    /// <summary>
    /// De voettekst als inhoud van de paginamarge (CSS Paged Media): zo staat hij op elke pagina op dezelfde plek, en loopt
    /// de tekst er nooit overheen. Links, midden of rechts binnen de breedte van het blok.
    /// </summary>
    private static string Voettekst(Verkoopbeeld beeld, Factuurontwerp ontwerp)
    {
        if (ontwerp.Blok(Bloksoort.Voettekst) is not { Zichtbaar: true, Tekst.Length: > 0 } voet)
        {
            return string.Empty;
        }

        var tekst = Factuurteksten.Vul(voet.Tekst, beeld.Factuur, beeld.Afzender, beeld.Totaal);
        var inhoud = new StringBuilder();
        foreach (var teken in tekst.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            inhoud.Append(teken switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\A ",
                '<' or '>' or '{' or '}' => " ",
                _ when char.IsControl(teken) => " ",
                _ => teken.ToString(),
            });
        }

        var uitlijning = voet.Uitlijning switch
        {
            Uitlijning.Midden => "center",
            Uitlijning.Rechts => "right",
            _ => "left",
        };
        var rechts = Math.Max(0m, Factuurontwerp.PaginaBreedte - voet.X - voet.Breedte);
        return $"@bottom-center {{ content: \"{inhoud}\"; white-space: pre-wrap; vertical-align: top; text-align: {uitlijning}; "
            + $"padding: 0 {Mm(rechts)} 0 {Mm(voet.X)}; font-family: {ontwerp.LettertypeCss}; font-size: {Pt(voet.Lettergrootte ?? ontwerp.Lettergrootte)}; "
            + $"font-weight: {(voet.Vet ? 600 : 400)}; color: {(voet.Kleur is { } k && Factuurontwerp.IsKleur(k) ? k : ontwerp.Tekstkleur)}; line-height: 1.4; }}";
    }

    private static string Blok(Ontwerpblok blok, string inhoud, bool ontwerper, string? gekozen, string plek, string? klasse = null)
    {
        var stijl = new StringBuilder(plek);
        stijl.Append(CultureInfo.InvariantCulture, $"left:{Mm(blok.X)};width:{Mm(blok.Breedte)};");
        if (blok.Hoogte is { } hoogte && blok.Soort == Bloksoort.Logo)
        {
            stijl.Append(CultureInfo.InvariantCulture, $"height:{Mm(hoogte)};");
        }

        if (blok.Lettergrootte is { } grootte)
        {
            stijl.Append(CultureInfo.InvariantCulture, $"font-size:{Pt(grootte)};");
        }

        if (blok.Kleur is { } kleur && Factuurontwerp.IsKleur(kleur))
        {
            stijl.Append(CultureInfo.InvariantCulture, $"color:{kleur};");
        }

        stijl.Append(blok.Uitlijning switch
        {
            Uitlijning.Midden => "text-align:center;",
            Uitlijning.Rechts => "text-align:right;",
            _ => string.Empty,
        });
        var klassen = string.Join(' ', new[]
        {
            blok.Soort is Bloksoort.Regels ? "fp-stroomblok" : "fp-blok",
            $"fp-b-{blok.Soort.ToString().ToLowerInvariant()}",
            klasse,
            blok.Vet ? "fp-vet" : null,
            blok.Soort == Bloksoort.Logo && blok.Uitlijning == Uitlijning.Rechts ? "rechts" : null,
            ontwerper ? "fp-kies" : null,
            ontwerper && blok.Id == gekozen ? "gekozen" : null,
            ontwerper && !blok.Zichtbaar ? "uit" : null,
        }.Where(k => k is not null));
        var data = ontwerper ? $" data-blok=\"{E(blok.Id)}\"{(blok.Stroomt || blok.Soort == Bloksoort.Regels ? " data-stroom=\"1\"" : null)}" : null;
        var greep = ontwerper ? """<span class="fp-greep" data-greep="1"></span>""" : null;
        return $"""<div class="{klassen}" style="{stijl}"{data}>{inhoud}{greep}</div>""";
    }

    private static string? Inhoud(Ontwerpblok blok, Verkoopbeeld beeld, Factuurontwerp ontwerp, Logo? logo, bool ontwerper)
    {
        var f = beeld.Factuur;
        var a = beeld.Afzender;
        return blok.Soort switch
        {
            Bloksoort.Logo => logo is { } l
                ? $"""<img src="data:{l.Inhoudstype};base64,{Convert.ToBase64String(l.Inhoud)}" alt="{E(a.Naam)}">"""
                : ontwerper ? """<div class="fp-leeg">Logo</div>""" : null,
            Bloksoort.Afzender => Regels([$"<span class=\"fp-vet\">{E(a.Naam)}</span>", .. a.Adres.Regels.Select(E), .. new[] { a.Telefoon, a.Email, a.Website }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => E(x!))]),
            Bloksoort.Klant => Regels([
                $"<span class=\"fp-vet\">{E(f.Klant.Naam)}</span>",
                .. f.Klant.Contactpersoon is { Length: > 0 } t ? [$"t.a.v. {E(t)}"] : Array.Empty<string>(),
                .. f.Klant.Adres.Regels.Select(E)]),
            Bloksoort.Titel => E(Titel(beeld, ontwerp)),
            Bloksoort.Factuurgegevens => Gegevens(beeld),
            Bloksoort.Tekst => blok.Tekst is { Length: > 0 } tekst ? Tekst(Factuurteksten.Vul(tekst, f, a, beeld.Totaal)) : ontwerper ? """<div class="fp-leeg">Tekst</div>""" : null,
            _ => null,
        };
    }

    private static string Titel(Verkoopbeeld beeld, Factuurontwerp ontwerp)
    {
        var tekst = ontwerp.Blok(Bloksoort.Titel)?.Tekst;
        return beeld.Factuur.IsCredit && (string.IsNullOrWhiteSpace(tekst) || tekst == "Factuur") ? "Creditfactuur"
            : string.IsNullOrWhiteSpace(tekst) ? "Factuur"
            : Factuurteksten.Vul(tekst, beeld.Factuur, beeld.Afzender, beeld.Totaal);
    }

    private static string Gegevens(Verkoopbeeld beeld)
    {
        var f = beeld.Factuur;
        var rijen = new List<(string Label, string Waarde)>
        {
            (f.IsCredit ? "Creditfactuur" : "Factuurnummer", f.Nummer ?? "concept"),
            ("Factuurdatum", Datum(f.Datum)),
        };
        if (!f.IsCredit)
        {
            rijen.Add(("Vervaldatum", Datum(f.Vervaldatum)));
        }

        if (f.CreditVoor is { } bij)
        {
            rijen.Add(("Bij factuur", bij));
        }

        if (beeld.Referentie is { Length: > 0 } referentie)
        {
            rijen.Add(("Uw referentie", referentie));
        }

        if (f.Klant.BtwNummer is { Length: > 0 } btw)
        {
            rijen.Add(("Uw btw-nummer", btw));
        }

        return $"""<div class="fp-gegevens">{string.Concat(rijen.Select(r => $"""<span class="label">{E(r.Label)}</span><span>{E(r.Waarde)}</span>"""))}</div>""";
    }

    private static string Regels(Verkoopbeeld beeld, Factuurontwerp o)
    {
        var f = beeld.Factuur;
        var html = new StringBuilder("""<table class="fp-regels"><thead><tr><th>Omschrijving</th>""");
        if (o.KolomAantal)
        {
            html.Append("""<th class="r">Aantal</th>""");
        }

        if (o.KolomEenheid)
        {
            html.Append("<th>Eenheid</th>");
        }

        if (o.KolomPrijs)
        {
            html.Append("""<th class="r">Prijs</th>""");
        }

        if (o.KolomKorting)
        {
            html.Append("""<th class="r">Korting</th>""");
        }

        if (o.KolomBtw)
        {
            html.Append("""<th class="r">Btw</th>""");
        }

        html.Append("""<th class="r">Bedrag</th></tr></thead><tbody>""");
        foreach (var r in f.Regels)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Tekst(r.Omschrijving)}</td>");
            if (o.KolomAantal)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{r.Aantal.ToString("#,0.####", Nl).Replace('-', '−')}</td>""");
            }

            if (o.KolomEenheid)
            {
                html.Append(CultureInfo.InvariantCulture, $"<td>{E(r.Eenheid ?? "")}</td>");
            }

            if (o.KolomPrijs)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{Prijs(r.Prijs)}</td>""");
            }

            if (o.KolomKorting)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{(r.Korting == 0 ? "" : r.Korting.ToString("0.##", Nl) + "%")}</td>""");
            }

            if (o.KolomBtw)
            {
                html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{E(Btwtarief(BtwCodes.Zoek(beeld.Codes, r.BtwCode)))}</td>""");
            }

            html.Append(CultureInfo.InvariantCulture, $"""<td class="r">{Bedrag(r.Bedrag)}</td></tr>""");
        }

        html.Append("""</tbody></table><table class="fp-totalen">""");
        var btw = beeld.Btw;
        html.Append(CultureInfo.InvariantCulture, $"""<tr><td>Totaal exclusief btw</td><td class="r">{Bedrag(f.TotaalExclusief)}</td></tr>""");
        foreach (var b in btw.Where(b => b.Btw != 0 || b.Code.Percentage != 0))
        {
            html.Append(CultureInfo.InvariantCulture, $"""<tr><td>Btw {E(Btwpercentage.Tekst(b.Code.Percentage))} over {Bedrag(b.Grondslag)}</td><td class="r">{Bedrag(b.Btw)}</td></tr>""");
        }

        html.Append(CultureInfo.InvariantCulture, $"""<tr class="totaal"><td>Totaal</td><td class="r">€ {Bedrag(beeld.Totaal)}</td></tr></table>""");
        foreach (var v in beeld.Vermeldingen)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<div class="fp-vermelding">{E(v)}</div>""");
        }

        if (beeld.Tekst is { Length: > 0 } tekst)
        {
            html.Append(CultureInfo.InvariantCulture, $"""<div class="fp-tekst">{E(tekst)}</div>""");
        }

        return html.ToString();
    }

    /// <summary>Het tarief zoals op de factuur: 21%, 9%, 0%, verlegd, vrijgesteld.</summary>
    private static string Btwtarief(BtwCode? code) => code?.Soort switch
    {
        null => "",
        Btwsoort.VerkoopVerlegd or Btwsoort.VerkoopBinnenEu => "verlegd",
        Btwsoort.VerkoopVrijgesteld => "vrijgesteld",
        _ => Btwpercentage.Tekst(code.Percentage),
    };

    private static string Regels(IEnumerable<string> regels) => string.Join("<br>", regels);

    private static string Tekst(string tekst) => E(tekst).Replace("\r\n", "<br>", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

    private static string Lettertypen(Factuurontwerp ontwerp)
    {
        if (ontwerp.Lettertype != "IBM Plex Sans")
        {
            return string.Empty;
        }

        return string.Concat(new[] { (400, "ibm-plex-sans-latin-400-normal.woff2"), (600, "ibm-plex-sans-latin-600-normal.woff2") }.Select(l =>
            Lettertype(l.Item2) is { } data
                ? $"@font-face {{ font-family: 'IBM Plex Sans'; font-weight: {l.Item1}; src: url(data:font/woff2;base64,{data}) format('woff2'); }}\n"
                : string.Empty));
    }

    /// <summary>Een meegeleverd lettertype (OFL) als base64, of leeg als het er niet is.</summary>
    private static string? Lettertype(string bestand)
    {
        using var stroom = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Lettertype.{bestand}");
        if (stroom is null)
        {
            return null;
        }

        using var geheugen = new MemoryStream();
        stroom.CopyTo(geheugen);
        return Convert.ToBase64String(geheugen.ToArray());
    }

    private static string Bedrag(decimal bedrag) => bedrag.ToString("#,0.00", Nl).Replace('-', '−');

    private static string Prijs(decimal prijs) => (decimal.Round(prijs, 2) == prijs ? prijs.ToString("#,0.00", Nl) : prijs.ToString("#,0.00##", Nl)).Replace('-', '−');

    private static string Datum(DateOnly datum) => datum.ToString("d MMMM yyyy", Nl);

    private static string Mm(decimal mm) => mm.ToString("0.##", CultureInfo.InvariantCulture) + "mm";

    private static string Pt(decimal pt) => pt.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst);
}

/// <summary>Een logo: PNG of JPEG.</summary>
public sealed record Logo(byte[] Inhoud, string Inhoudstype)
{
    public const int MaximumGrootte = 1024 * 1024;

    /// <summary>Het soort afbeelding aan de hand van de eerste bytes (niet de bestandsnaam), of leeg als het geen PNG of JPEG is.</summary>
    public static string? Soort(ReadOnlySpan<byte> inhoud) =>
        inhoud.Length > 8 && inhoud[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? "image/png"
        : inhoud.Length > 3 && inhoud[0] == 0xFF && inhoud[1] == 0xD8 && inhoud[2] == 0xFF ? "image/jpeg"
        : null;
}
