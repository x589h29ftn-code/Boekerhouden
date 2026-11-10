using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Btw;

/// <summary>
/// Een betaling aan (of teruggaaf van) de Belastingdienst op de btw-rekeningen: een post met een bank- of kasrekening en
/// een regel zonder btw-code op een btw-rekening. <paramref name="Bedrag"/>: betaald positief, ontvangen negatief (zoals
/// "te betalen" in de aangifte). <paramref name="Periode"/>: de aangifteperiode waar hij voor is, uit de omschrijving
/// ("Omzetbelasting 3e kwartaal 2026") of anders de laatste periode die bij de betaling voorbij was.
/// </summary>
public sealed record Btwbetaling(Journaalpost Post, decimal Bedrag, Aangifteperiode Periode);

public static partial class Btwbetalingen
{
    private static readonly string[] Maanden =
        ["januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december"];

    /// <summary>Alle betalingen en teruggaven van btw in de posten, met de periode waar ze bij horen.</summary>
    public static IReadOnlyList<Btwbetaling> Zoek(IEnumerable<Journaalpost> posten, Rekeningschema schema, Aangiftefrequentie frequentie)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        var uit = new List<Btwbetaling>();
        foreach (var post in posten.Where(p => p.Boekstuk != "begin"))
        {
            var rekeningen = post.Regels.Select(r => (Regel: r, Rekening: schema.Zoek(r.Rekeningnummer))).ToList();
            if (!rekeningen.Any(x => x.Rekening?.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Kas))
            {
                continue;
            }

            var btw = rekeningen.Where(x => x.Regel.BtwCode is null && x.Rekening is not null && Btwaangifte.IsBtwrekening(x.Rekening)).ToList();
            var bedrag = btw.Sum(x => x.Regel.Bedrag);
            if (btw.Count == 0 || bedrag == 0)
            {
                continue;
            }

            var tekst = string.Join(' ', btw.Select(x => x.Regel.Omschrijving).Prepend(post.Omschrijving).Where(t => !string.IsNullOrWhiteSpace(t)));
            uit.Add(new Btwbetaling(post, bedrag, Periode(tekst, frequentie) ?? Aangifteperiode.Afgelopen(post.Datum, frequentie)));
        }

        return uit;
    }

    /// <summary>
    /// De periode uit een omschrijving: "3e kwartaal 2026", "kwartaal 3 2026", "Q3 2026", "september 2026" of (bij een
    /// jaaraangifte) alleen het jaar. Leeg als er geen periode met jaar in staat.
    /// </summary>
    public static Aangifteperiode? Periode(string? tekst, Aangiftefrequentie frequentie)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return null;
        }

        var klein = tekst.ToLowerInvariant();
        if (Kwartaal().Match(klein) is { Success: true } k)
        {
            var nummer = int.Parse(k.Groups["k1"].Success ? k.Groups["k1"].Value : k.Groups["k2"].Success ? k.Groups["k2"].Value : k.Groups["k3"].Value, CultureInfo.InvariantCulture);
            var jaar = int.Parse(k.Groups["jaar"].Value, CultureInfo.InvariantCulture);
            return Aangifteperiode.Waarin(new DateOnly(jaar, ((nummer - 1) * 3) + 1, 1), Aangiftefrequentie.Kwartaal);
        }

        if (Maand().Match(klein) is { Success: true } m)
        {
            var naam = m.Groups["maand"].Value == "mrt" ? "maart" : m.Groups["maand"].Value;
            var maand = Array.FindIndex(Maanden, n => n.StartsWith(naam, StringComparison.Ordinal)) + 1;
            return Aangifteperiode.Waarin(new DateOnly(int.Parse(m.Groups["jaar"].Value, CultureInfo.InvariantCulture), maand, 1), Aangiftefrequentie.Maand);
        }

        if (frequentie == Aangiftefrequentie.Jaar && Jaartal().Match(klein) is { Success: true } j)
        {
            return Aangifteperiode.Waarin(new DateOnly(int.Parse(j.Value, CultureInfo.InvariantCulture), 1, 1), Aangiftefrequentie.Jaar);
        }

        return null;
    }

    [GeneratedRegex(@"(?:\b(?<k1>[1-4])(?:e|de|ste)?\s*(?:kwartaal|kw\.?)|\bkwartaal\s*(?<k2>[1-4])\b|\bq(?<k3>[1-4]))\D{0,5}(?<jaar>20\d\d)\b")]
    private static partial Regex Kwartaal();

    [GeneratedRegex(@"\b(?<maand>januari|februari|maart|april|mei|juni|juli|augustus|september|oktober|november|december|jan|feb|mrt|apr|jun|jul|aug|sep|okt|nov|dec)\.?\s*(?<jaar>20\d\d)\b")]
    private static partial Regex Maand();

    [GeneratedRegex(@"\b20\d\d\b")]
    private static partial Regex Jaartal();
}
