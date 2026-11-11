using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Afletteren;

/// <summary>
/// Een pinbetaling op het afschrift herkennen als de betaling van een bon: zonder factuurnummer of IBAN blijven het
/// bedrag, de dag en de naam van de winkel over. De bank zet de naam in de omschrijving, vaak in hoofdletters, afgekapt,
/// met de plaats of een filiaalnummer erachter of een betaaldienst ervoor ("CCV*BAKKERIJ ZOET SNEEK"), en de dag van de
/// betaling staat er meestal ook in (de boekdatum kan een paar dagen later zijn).
/// </summary>
public static partial class Pinbetaling
{
    /// <summary>Zoveel dagen mag de boekdatum na de dag van de bon liggen (weekend, tanken, feestdagen).</summary>
    public const int Boekdagen = 4;

    /// <summary>Woorden die niets over de winkel zeggen: de branche, een rechtsvorm, een lidwoord.</summary>
    private static readonly HashSet<string> Algemeen = new(StringComparer.Ordinal)
    {
        "bakkerij", "bakker", "banketbakker", "banketbakkerij", "warme", "supermarkt", "super", "buurtsuper", "tankstation", "tank", "brandstof", "restaurant",
        "cafe", "eetcafe", "lunchroom", "grand", "bouwmarkt", "klusmarkt", "drogisterij", "drogist", "winkel", "shop", "store", "kiosk", "slagerij", "slager",
        "parkeergarage", "parkeren", "garage", "hotel", "nederland", "holland", "en", "van", "der", "den", "te", "in", "op", "aan", "to", "go",
    };

    /// <summary>
    /// De dag van de betaling volgens de omschrijving ("11-11-2026 10:19", "11.11.26/10.19", "2026-11-11"), als die
    /// binnen een week vóór de boekdatum ligt; anders leeg.
    /// </summary>
    public static DateOnly? Betaaldag(Bankmutatie mutatie)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        foreach (Match m in Dag().Matches(mutatie.Omschrijving))
        {
            var (dag, maand, jaar) = m.Groups["j4"].Success
                ? (Getal(m, "d2"), Getal(m, "m2"), Getal(m, "j4"))
                : (Getal(m, "d"), Getal(m, "m"), m.Groups["j"].Success ? Getal(m, "j") : mutatie.Datum.Year);
            jaar = jaar < 100 ? 2000 + jaar : jaar;
            if (maand is < 1 or > 12 || dag < 1 || dag > DateTime.DaysInMonth(Math.Clamp(jaar, 1, 9999), maand))
            {
                continue;
            }

            var datum = new DateOnly(jaar, maand, dag);

            // Rond de jaarwisseling zonder jaar in de omschrijving: 31-12 op een afschrift van januari is vorig jaar.
            if (!m.Groups["j"].Success && !m.Groups["j4"].Success && datum > mutatie.Datum)
            {
                datum = datum.AddYears(-1);
            }

            if (mutatie.Datum.DayNumber - datum.DayNumber is >= 0 and <= 7)
            {
                return datum;
            }
        }

        return null;
    }

    /// <summary>
    /// Staat de naam van de winkel in de tekst van de mutatie? De onderscheidende woorden van de naam (niet "bakkerij"
    /// of "B.V.") moeten er allemaal in staan, of minstens één ervan van vijf letters of meer als heel woord (de bank
    /// kort in: "Gamar 0412" voor Bouwhal Gamar). Aan elkaar geschreven mag vanaf vijf letters ("BRANDSTOFNOORD"),
    /// afgekapt ook ("ROOMHA" voor Roomhart, "KAD" voor Kade: hoogstens twee letters korter); een kort woord als
    /// "Spar" alleen als los woord.
    /// </summary>
    public static bool NaamKomtVoor(string? naam, string? tekst)
    {
        var alle = Namen.Woorden(naam);
        var woorden = alle.Where(w => !Algemeen.Contains(w) && !w.All(char.IsDigit)).ToList();
        if (woorden.Count == 0)
        {
            // Een naam die alleen uit algemene woorden bestaat ("De Bakkerij"): dan tellen die wel, allemaal.
            woorden = [.. alle.Where(w => !w.All(char.IsDigit))];
        }

        var tekstwoorden = Namen.Woorden(tekst);
        if (woorden.Count == 0 || tekstwoorden.Count == 0 || woorden.Sum(w => w.Length) < 3)
        {
            return false;
        }

        var aaneen = string.Concat(tekstwoorden);
        bool Heel(string woord) => tekstwoorden.Contains(woord);
        bool Staat(string woord) =>
            Heel(woord)
            || (woord.Length >= 5 && aaneen.Contains(woord, StringComparison.Ordinal))
            || (woord.Length >= 4 && tekstwoorden.Any(t => t.Length >= 3 && t.Length >= woord.Length - 2 && woord.StartsWith(t, StringComparison.Ordinal)));
        return woorden.All(Staat) || woorden.Any(w => w.Length >= 5 && Heel(w));
    }

    private static int Getal(Match m, string groep) => int.Parse(m.Groups[groep].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<![0-9])(?:(?<j4>20[0-9]{2})-(?<m2>[0-9]{2})-(?<d2>[0-9]{2})|(?<d>[0-9]{1,2})[-./](?<m>[0-9]{1,2})(?:[-./](?<j>20[0-9]{2}|[0-9]{2}))?)(?![0-9])")]
    private static partial Regex Dag();
}
