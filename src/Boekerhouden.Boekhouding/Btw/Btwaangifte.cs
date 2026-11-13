using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Btw;

/// <summary>Eén rubriek van de aangifte: de grondslag (omzet of inkoop zonder btw) en de btw daarover.</summary>
public sealed record Aangifterubriek(string Rubriek, decimal Grondslag, decimal Btw);

/// <summary>Wat een bedrag in de aangifte is: de grondslag (omzet), de btw in een rubriek, of voorbelasting.</summary>
public enum Btwpost
{
    Grondslag,
    Btw,
    Voorbelasting,
}

/// <summary>Wat één boekingsregel bijdraagt aan de aangifte, met het teken van de aangifte (omzet en btw positief).</summary>
/// <param name="Herkomst">Bij voorbelasting: de rubriek van de grondslag van de code (bijv. 2a of 4b bij verlegde inkoop).</param>
public sealed record Btwbijdrage(Journaalpost Post, Journaalregel Regel, string Rubriek, Btwpost Soort, decimal Bedrag, string? Herkomst = null);

/// <summary>
/// De aangifte omzetbelasting over een periode, berekend uit de boekingen met hun btw-code. Per regel bepaalt de rekening
/// wat het bedrag is: op de rekening voor voorbelasting is het aftrekbare btw (5b), op de rekening voor af te dragen btw
/// de verschuldigde btw (in de rubriek van de code), en op elke andere rekening de grondslag (in de rubriek van de code).
/// Bedragen exact; afronden op hele euro's hoort bij het indienen.
/// </summary>
public sealed record Btwaangifte(DateOnly Van, DateOnly Tot, IReadOnlyList<Aangifterubriek> Rubrieken, decimal Voorbelasting)
{
    /// <summary>De volgorde van de rubrieken op het aangifteformulier.</summary>
    public static IReadOnlyList<string> Volgorde { get; } = ["1a", "1b", "1c", "1d", "1e", "2a", "3a", "3b", "3c", "4a", "4b"];

    /// <summary>De verschuldigde btw (5a): de btw van alle rubrieken.</summary>
    public decimal Verschuldigd => Rubrieken.Sum(r => r.Btw);

    /// <summary>Te betalen (positief) of terug te vragen (negatief).</summary>
    public decimal TeBetalen => Verschuldigd - Voorbelasting;

    public Aangifterubriek Rubriek(string rubriek) => Rubrieken.FirstOrDefault(r => r.Rubriek == rubriek) ?? new(rubriek, 0m, 0m);

    public static Btwaangifte Bereken(IEnumerable<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes, DateOnly van, DateOnly tot)
    {
        var grondslag = Volgorde.ToDictionary(r => r, _ => 0m, StringComparer.Ordinal);
        var btw = Volgorde.ToDictionary(r => r, _ => 0m, StringComparer.Ordinal);
        var voorbelasting = 0m;
        foreach (var b in Bijdragen(posten, schema, codes, van, tot))
        {
            switch (b.Soort)
            {
                case Btwpost.Voorbelasting:
                    voorbelasting += b.Bedrag;
                    break;
                case Btwpost.Btw:
                    btw[b.Rubriek] += b.Bedrag;
                    break;
                default:
                    grondslag[b.Rubriek] += b.Bedrag;
                    break;
            }
        }

        return new Btwaangifte(van, tot, [.. Volgorde.Select(r => new Aangifterubriek(r, grondslag[r], btw[r]))], voorbelasting);
    }

    /// <summary>
    /// Per boekingsregel met een btw-code wat die aan de aangifte bijdraagt: grondslag of btw in een rubriek, of
    /// voorbelasting (rubriek 5b). De som per rubriek is de aangifte; zo is elk getal terug te voeren op de boekingen.
    /// </summary>
    public static IEnumerable<Btwbijdrage> Bijdragen(IEnumerable<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(codes);
        foreach (var post in posten.Where(p => p.Datum >= van && p.Datum <= tot))
        {
            // Btw privégebruik zonder btw-code (bijv. de correctie voor de auto aan het eind van het jaar) telt in 1d.
            foreach (var regel in post.Regels.Where(r => IsPrivegebruikregel(post, r, schema)))
            {
                yield return new(post, regel, "1d", Btwpost.Btw, -regel.Bedrag);
            }

            foreach (var regel in post.Regels.Where(r => r.BtwCode is not null))
            {
                if (BtwCodes.Zoek(codes, regel.BtwCode) is not { } code)
                {
                    continue;
                }

                var rekening = schema.Zoek(regel.Rekeningnummer);
                switch (rekening is null ? (Rekeningfunctie?)null : Functie(rekening))
                {
                    case Rekeningfunctie.Voorbelasting:
                        yield return new(post, regel, "5b", Btwpost.Voorbelasting, regel.Bedrag, code.Rubriek);
                        break;
                    case Rekeningfunctie.AfTeDragenBtw when code.RubriekBtw is { } rubriek:
                        yield return new(post, regel, rubriek, Btwpost.Btw, -regel.Bedrag);
                        break;
                    case Rekeningfunctie.AfTeDragenBtw:
                        break;
                    default:
                        if (code.Rubriek is { } r)
                        {
                            yield return new(post, regel, r, Btwpost.Grondslag, code.Richting == BtwRichting.Verkoop ? -regel.Bedrag : regel.Bedrag);
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// Een btw-rekening voor het privégebruik (bijv. "Te betalen btw privégebruik"): wat erop wordt geboekt hoort in
    /// rubriek 1d, ook zonder btw-code. Een btw-rekening (functie of RGS) met "priv" in de naam, of een balansrekening
    /// met "btw" en "priv" in de naam (zelf aangemaakt of uit een auditfile, zonder functie of RGS-code).
    /// </summary>
    public static bool IsPrivegebruikrekening(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        var naam = rekening.Naam.ToLowerInvariant().Replace('é', 'e');
        return naam.Contains("priv", StringComparison.Ordinal)
               && (IsBtwrekening(rekening) || (rekening.IsBalans && (naam.Contains("btw", StringComparison.Ordinal) || naam.Contains("omzetbelasting", StringComparison.Ordinal))));
    }

    /// <summary>
    /// Een regel zonder btw-code op de btw-rekening voor privégebruik, die het privégebruik zelf boekt: niet een
    /// betaling (een post met een bank- of kasrekening) en niet een overboeking tussen btw-rekeningen.
    /// </summary>
    public static bool IsPrivegebruikregel(Journaalpost post, Journaalregel regel, Rekeningschema schema)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(regel);
        ArgumentNullException.ThrowIfNull(schema);
        if (regel.BtwCode is not null || post.Boekstuk == "begin" || schema.Zoek(regel.Rekeningnummer) is not { } rekening || !IsPrivegebruikrekening(rekening))
        {
            return false;
        }

        var tegen = post.Regels.Where(r => !ReferenceEquals(r, regel)).Select(r => schema.Zoek(r.Rekeningnummer)).ToList();
        return tegen.Count > 0
            && tegen.All(r => r is null || r.Functie is not (Rekeningfunctie.Bank or Rekeningfunctie.Kas))
            && !tegen.All(r => r is not null && IsBtwrekening(r));
    }

    /// <summary>
    /// Is dit een btw-rekening (te vorderen of te betalen)? Op functie, of op de RGS-groep: een uitgebreid schema heeft
    /// naast de rekening met de functie vaak meer btw-rekeningen (laag tarief, verlegd, afgedragen).
    /// </summary>
    public static bool IsBtwrekening(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        return Functie(rekening) is Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw;
    }

    private static Rekeningfunctie Functie(Rekening rekening) => rekening.Functie switch
    {
        Rekeningfunctie.Geen when rekening.RgsCode?.StartsWith("BVorVbk", StringComparison.Ordinal) == true => Rekeningfunctie.Voorbelasting,
        Rekeningfunctie.Geen when rekening.RgsCode?.StartsWith("BSchBepBtw", StringComparison.Ordinal) == true => Rekeningfunctie.AfTeDragenBtw,
        var f => f,
    };
}
