using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Domain.Rapport;

/// <summary>Een variabele voor teksten in het rapport, bijv. <c>{nettoresultaat}</c>.</summary>
/// <param name="Naam">Zoals in de tekst, zonder accolades; met <c>:CODE</c> erachter bij een post.</param>
public sealed record Variabele(string Naam, string Omschrijving, string Groep);

/// <summary>
/// Variabelen in de teksten van het rapport (grondslagen, toelichtingen): <c>{naam}</c>, <c>{boekjaar}</c>,
/// <c>{nettoresultaat}</c> enz. worden bij het maken van het rapport ingevuld, zodat een tekst elk jaar klopt
/// zonder hem aan te passen. Bedragen in hele euro's zoals in het rapport (positief aan de normale kant van de post).
/// <c>{post:BMva}</c> geeft het bedrag van een RGS-post, <c>{post_vorig:BMva}</c> dat van vorig jaar.
/// </summary>
public static partial class RapportVariabelen
{
    private const string Algemeen = "Algemeen";
    private const string Periode = "Periode";
    private const string Bedragen = "Bedragen";

    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>De posten van de brutomarge, zoals in de W&amp;V: opbrengsten min de kostprijs.</summary>
    private static readonly string[] Brutomarge = ["WOmz", "WWiv", "WOvb", "WKpr"];

    public static IReadOnlyList<Variabele> Lijst { get; } =
    [
        new("naam", "naam van de klant", Algemeen),
        new("plaats", "vestigingsplaats", Algemeen),
        new("kvk", "KvK-nummer", Algemeen),
        new("adres", "straat en huisnummer van de vestiging", Algemeen),
        new("postcode", "postcode van de vestiging", Algemeen),
        new("rsin", "RSIN", Algemeen),
        new("sbi", "SBI-code (hoofdactiviteit)", Algemeen),
        new("statutaire_naam", "naam volgens de statuten (anders de naam)", Algemeen),
        new("statutaire_zetel", "statutaire zetel (anders de plaats)", Algemeen),
        new("oprichtingsdatum", "datum van oprichting, bijv. 28 juni 2024", Algemeen),
        new("valuta", "het valutateken (€)", Algemeen),
        new("boekjaar", "het boekjaar, bijv. 2025", Periode),
        new("boekjaar_vorig", "het vorige boekjaar", Periode),
        new("boekjaar_volgend", "het volgende boekjaar", Periode),
        new("begin_boekjaar", "eerste dag, bijv. 1 januari 2025", Periode),
        new("einde_boekjaar", "balansdatum, bijv. 31 december 2025", Periode),
        new("einde_boekjaar_vorig", "balansdatum vorig jaar", Periode),
        new("nettoresultaat", "resultaat na belastingen (negatief bij verlies)", Bedragen),
        new("nettoresultaat_vorig", "resultaat na belastingen vorig jaar", Bedragen),
        new("nettoresultaat_absoluut", "resultaat na belastingen zonder minteken", Bedragen),
        new("winst_of_verlies", "\"winst\" of \"verlies\"", Bedragen),
        new("resultaat_voor_belastingen", "resultaat voor belastingen", Bedragen),
        new("resultaat_voor_belastingen_vorig", "resultaat voor belastingen vorig jaar", Bedragen),
        new("belastingen", "belastingen over het resultaat", Bedragen),
        new("netto_omzet", "netto-omzet", Bedragen),
        new("netto_omzet_vorig", "netto-omzet vorig jaar", Bedragen),
        new("brutomarge", "brutomarge", Bedragen),
        new("brutomarge_vorig", "brutomarge vorig jaar", Bedragen),
        new("eigen_vermogen", "eigen vermogen (met het resultaat)", Bedragen),
        new("eigen_vermogen_vorig", "eigen vermogen vorig jaar", Bedragen),
        new("balanstotaal", "balanstotaal", Bedragen),
        new("balanstotaal_vorig", "balanstotaal vorig jaar", Bedragen),
        new("solvabiliteit", "eigen vermogen in % van het balanstotaal", Bedragen),
        new("solvabiliteit_vorig", "solvabiliteit vorig jaar", Bedragen),
        new("post:CODE", "bedrag van een RGS-post, bijv. {post:BMva}", Bedragen),
        new("post_vorig:CODE", "bedrag van een RGS-post vorig jaar", Bedragen),
    ];

    /// <summary>De waarden van de variabelen voor dit rapport (zonder de posten, die gaan via <see cref="Vul"/>).</summary>
    /// <param name="balans">De tabellen Activa en Passiva, voor het balanstotaal zoals afgedrukt.</param>
    public static IReadOnlyDictionary<string, string> Bepaal(RapportGegevens g, IReadOnlyList<Tabel> balans)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(balans);
        var resultaat = (Huidig: g.Balans.ResultaatHuidig, Vorig: g.Balans.ResultaatVorig);
        var belastingen = g.Balans.Totaal("WBel");
        var omzet = g.Balans.Totaal("WOmz");
        var bruto = Brutomarge.Select(g.Balans.Totaal).Aggregate((Huidig: 0m, Vorig: 0m), (s, t) => (s.Huidig - t.Huidig, s.Vorig - t.Vorig));
        var eigen = (Huidig: -g.Balans.Totaal("BEiv").Huidig + resultaat.Huidig, Vorig: -g.Balans.Totaal("BEiv").Vorig + resultaat.Vorig);
        var totaal = balans.FirstOrDefault(t => t.Titel == "Activa")?.Regels.LastOrDefault(r => r.Soort == RegelSoort.Eindtotaal);
        var balansHuidig = totaal?.Totaal ?? 0;
        var balansVorig = totaal?.TotaalVorig ?? 0;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["naam"] = g.Klantnaam,
            ["plaats"] = string.IsNullOrWhiteSpace(g.Plaats) ? "[vestigingsplaats]" : g.Plaats.Trim(),
            ["kvk"] = string.IsNullOrWhiteSpace(g.Kvk) ? "[KvK-nummer]" : g.Kvk.Trim(),
            ["adres"] = Of(g.Kaart?.Adres, "[vestigingsadres]"),
            ["postcode"] = Of(g.Kaart?.Postcode, "[postcode]"),
            ["rsin"] = Of(g.Kaart?.Rsin, "[RSIN]"),
            ["sbi"] = Of(g.Kaart?.SbiCode, "[SBI-code]"),
            ["statutaire_naam"] = Of(g.Kaart?.StatutaireNaam, g.Klantnaam),
            ["statutaire_zetel"] = Of(g.Kaart?.StatutaireZetel, Of(g.Plaats, "[statutaire zetel]")),
            ["oprichtingsdatum"] = g.Kaart?.Oprichtingsdatum is { } opgericht ? opgericht.ToString("d MMMM yyyy", Nederlands) : "[datum oprichting]",
            ["valuta"] = "€",
            ["boekjaar"] = Jaar(g.Boekjaar),
            ["boekjaar_vorig"] = Jaar(g.Boekjaar - 1),
            ["boekjaar_volgend"] = Jaar(g.Boekjaar + 1),
            ["begin_boekjaar"] = $"1 januari {Jaar(g.Boekjaar)}",
            ["einde_boekjaar"] = $"31 december {Jaar(g.Boekjaar)}",
            ["einde_boekjaar_vorig"] = $"31 december {Jaar(g.Boekjaar - 1)}",
            ["nettoresultaat"] = Domain.Bedragen.Heel(resultaat.Huidig),
            ["nettoresultaat_vorig"] = Domain.Bedragen.Heel(resultaat.Vorig),
            ["nettoresultaat_absoluut"] = Domain.Bedragen.Heel(Math.Abs(resultaat.Huidig)),
            ["winst_of_verlies"] = resultaat.Huidig < 0 ? "verlies" : "winst",
            ["resultaat_voor_belastingen"] = Domain.Bedragen.Heel(resultaat.Huidig + belastingen.Huidig),
            ["resultaat_voor_belastingen_vorig"] = Domain.Bedragen.Heel(resultaat.Vorig + belastingen.Vorig),
            ["belastingen"] = Domain.Bedragen.Heel(belastingen.Huidig),
            ["netto_omzet"] = Domain.Bedragen.Heel(-omzet.Huidig),
            ["netto_omzet_vorig"] = Domain.Bedragen.Heel(-omzet.Vorig),
            ["brutomarge"] = Domain.Bedragen.Heel(bruto.Huidig),
            ["brutomarge_vorig"] = Domain.Bedragen.Heel(bruto.Vorig),
            ["eigen_vermogen"] = Domain.Bedragen.Heel(eigen.Huidig),
            ["eigen_vermogen_vorig"] = Domain.Bedragen.Heel(eigen.Vorig),
            ["balanstotaal"] = Domain.Bedragen.Heel(balansHuidig),
            ["balanstotaal_vorig"] = Domain.Bedragen.Heel(balansVorig),
            ["solvabiliteit"] = Procent(eigen.Huidig, balansHuidig),
            ["solvabiliteit_vorig"] = Procent(eigen.Vorig, balansVorig),
        };
    }

    /// <summary>
    /// Vult de variabelen in een tekst in. Een onbekende variabele (of post) blijft staan zoals hij is en komt in
    /// <paramref name="onbekend"/>, zodat het rapport erover kan waarschuwen.
    /// </summary>
    public static string Vul(string tekst, RapportGegevens g, IReadOnlyDictionary<string, string> waarden, ICollection<string>? onbekend = null)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(waarden);
        return Patroon().Replace(tekst, m =>
        {
            var naam = m.Groups[1].Value;
            var code = m.Groups[2].Success ? m.Groups[2].Value : null;
            var waarde = code is null ? waarden.GetValueOrDefault(naam)
                : naam is "post" or "post_vorig" && g.Schema.Zoek(code) is { } post ? Domain.Bedragen.Heel(PostBedrag(g, post, vorig: naam == "post_vorig"))
                : null;
            if (waarde is null)
            {
                onbekend?.Add(m.Value);
            }

            return waarde ?? m.Value;
        });
    }

    /// <summary>Een variabele in een tekst: <c>{naam}</c> of <c>{post:CODE}</c>.</summary>
    [GeneratedRegex(@"\{([a-z_]+)(?::([A-Za-z]{1,20}))?\}")]
    public static partial Regex Patroon();

    /// <summary>Het bedrag van een post zoals in het rapport: positief aan de normale kant (bij het eigen vermogen met het resultaat).</summary>
    private static decimal PostBedrag(RapportGegevens g, RgsCode post, bool vorig)
    {
        var totaal = g.Balans.Totaal(post.Code);
        var bedrag = (post.Kant == Saldokant.Credit ? -1 : 1) * (vorig ? totaal.Vorig : totaal.Huidig);
        return post.Code == "BEiv" ? bedrag + (vorig ? g.Balans.ResultaatVorig : g.Balans.ResultaatHuidig) : bedrag;
    }

    private static string Jaar(int jaar) => jaar.ToString(CultureInfo.InvariantCulture);

    private static string Of(string? waarde, string anders) => string.IsNullOrWhiteSpace(waarde) ? anders : waarde.Trim();

    private static string Procent(decimal deel, decimal geheel) =>
        geheel == 0 ? "-" : Math.Round(deel / geheel * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", Nederlands) + "%";
}
