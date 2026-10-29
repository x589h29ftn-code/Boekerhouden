using System.Globalization;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>
/// Hoe een herkende waarde op de factuur kan staan, om hem daar terug te vinden (en op te laten lichten): een datum als
/// <c>01-04-2026</c>, <c>1-4-2026</c>, <c>2026-04-01</c> of <c>1 april 2026</c>, een bedrag als <c>1.234,56</c> of
/// <c>1,234.56</c>. Vergelijken gebeurt zonder spaties en hoofdletters (<see cref="Compact"/>).
/// </summary>
public static class Tekstvormen
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly CultureInfo Engels = CultureInfo.GetCultureInfo("en-GB");

    // Cijfers met een vast scheidingsteken (niet dat van de cultuur: in nl-NL zou "/" een "-" worden).
    private static readonly string[] DatumCijfers =
        ["dd-MM-yyyy", "d-M-yyyy", "dd'/'MM'/'yyyy", "d'/'M'/'yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd-MM-yy"];

    private static readonly string[] DatumNederlands = ["d MMMM yyyy", "d MMM yyyy"];

    private static readonly string[] DatumEngels = ["d MMMM yyyy", "d MMM yyyy", "MMMM d, yyyy", "MMM d, yyyy"];

    public static IReadOnlyList<string> Tekst(string? waarde) =>
        string.IsNullOrWhiteSpace(waarde) ? [] : [waarde.Trim()];

    public static IReadOnlyList<string> Datum(DateOnly? datum)
    {
        if (datum is not { } d)
        {
            return [];
        }

        var vormen = DatumCijfers.Select(p => d.ToString(p, CultureInfo.InvariantCulture))
            .Concat(DatumNederlands.Select(p => d.ToString(p, Nederlands)))
            .Concat(DatumEngels.Select(p => d.ToString(p, Engels)))
            .Append(d.ToString("d MMM. yyyy", Nederlands));
        return [.. vormen.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> Bedrag(decimal? bedrag)
    {
        if (bedrag is not { } b)
        {
            return [];
        }

        var absoluut = Math.Abs(b);
        return [.. new[]
        {
            absoluut.ToString("#,##0.00", Nederlands),
            absoluut.ToString("0.00", Nederlands),
            absoluut.ToString("#,##0.00", CultureInfo.InvariantCulture),
            absoluut.ToString("0.00", CultureInfo.InvariantCulture),
        }.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Zonder spaties en hoofdletters, en zonder leestekens of een euroteken aan het begin of eind.</summary>
    public static string Compact(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var zonder = string.Concat(tekst.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        return zonder.Trim('€', ':', ';', ',', '.', '(', ')', '[', ']', '"', '\'', '*', '#');
    }
}
