using System.Globalization;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Een journaalpost kopiëren naar een andere maand (bijv. de loonjournaalpost van oktober voor november): dezelfde dag
/// in die maand (of de laatste dag als de maand korter is), en in de omschrijvingen de maand en het jaar mee
/// ("Lonen oktober 2026" wordt "Lonen november 2026"). Het boekstuk vult de dienst in.
/// </summary>
public static class Journaalkopie
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static Journaalpost NaarMaand(Journaalpost post, int jaar, int maand)
    {
        ArgumentNullException.ThrowIfNull(post);
        var datum = new DateOnly(jaar, maand, Math.Min(post.Datum.Day, DateTime.DaysInMonth(jaar, maand)));
        string Vervang(string tekst) => Omschrijving(tekst, post.Datum, datum);
        return post with
        {
            Datum = datum,
            Omschrijving = Vervang(post.Omschrijving),
            Regels = [.. post.Regels.Select(r => r with { Omschrijving = r.Omschrijving is { } o ? Vervang(o) : null })],
        };
    }

    /// <summary>
    /// De maand (voluit of afgekort) en het jaar van de oude datum vervangen door die van de nieuwe, en de variabelen
    /// <c>#maand</c>/<c>#periode</c> (oktober 2026), <c>#maandnr</c> (maart is 3), <c>#kwartaal</c> en <c>#jaar</c> invullen voor de nieuwe datum.
    /// </summary>
    public static string Omschrijving(string tekst, DateOnly van, DateOnly naar)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        tekst = Regex.Replace(tekst, "#(maandnr|maand|periode|kwartaal|jaar)", m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "kwartaal" => $"{((naar.Month - 1) / 3) + 1}e kwartaal {naar.Year}",
            "jaar" => naar.Year.ToString(CultureInfo.InvariantCulture),
            "maandnr" => naar.Month.ToString(CultureInfo.InvariantCulture),
            _ => naar.ToString("MMMM yyyy", Nl),
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var oudeMaand = Nl.DateTimeFormat.MonthNames[van.Month - 1];
        var nieuweMaand = Nl.DateTimeFormat.MonthNames[naar.Month - 1];
        var oudKort = Nl.DateTimeFormat.AbbreviatedMonthNames[van.Month - 1].TrimEnd('.');
        var nieuwKort = Nl.DateTimeFormat.AbbreviatedMonthNames[naar.Month - 1].TrimEnd('.');
        var uit = Regex.Replace(tekst, $@"\b{oudeMaand}\b", m => Hoofdletter(m.Value, nieuweMaand), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (uit == tekst && !string.Equals(oudKort, oudeMaand, StringComparison.OrdinalIgnoreCase))
        {
            uit = Regex.Replace(tekst, $@"\b{oudKort}\b", m => Hoofdletter(m.Value, nieuwKort), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // "10-2026" of "10/2026" (maand-jaar), en het jaar als het mee verandert.
        uit = Regex.Replace(uit, $@"\b0?{van.Month}([-/]){van.Year}\b", m => $"{naar.Month:00}{m.Groups[1].Value}{naar.Year}");
        return van.Year == naar.Year ? uit : Regex.Replace(uit, $@"\b{van.Year}\b", naar.Year.ToString(CultureInfo.InvariantCulture));
    }

    private static string Hoofdletter(string voorbeeld, string woord) =>
        char.IsUpper(voorbeeld[0]) ? char.ToUpper(woord[0], Nl) + woord[1..] : woord;
}
