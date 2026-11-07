using System.Globalization;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Afgesloten perioden: tot en met een datum ligt de administratie vast (na de btw-aangifte of de jaarrekening). Op of
/// vóór die datum kan niets meer geboekt, aangepast, teruggedraaid of herboekt worden; heropenen kan, met een reden.
/// </summary>
public static class Afsluiting
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Waarom er op deze datum niet geboekt kan worden, of leeg als dat wel kan.</summary>
    public static string? Controleer(DateOnly? afgeslotenTot, DateOnly datum) =>
        afgeslotenTot is { } tot && datum <= tot
            ? $"De periode tot en met {tot.ToString("dd-MM-yyyy", Nederlands)} is afgesloten: op {datum.ToString("dd-MM-yyyy", Nederlands)} kan niets meer worden geboekt of gewijzigd. Heropen de periode eerst (Inrichting, Perioden)."
            : null;

    /// <summary>De eerste fout voor een reeks datums (bijv. de oude en de nieuwe datum van een aangepaste boeking).</summary>
    public static string? Controleer(DateOnly? afgeslotenTot, IEnumerable<DateOnly> datums) =>
        datums.Select(d => Controleer(afgeslotenTot, d)).FirstOrDefault(f => f is not null);

    /// <summary>De laatste dag van de maand van een datum: perioden sluiten per maand af.</summary>
    public static DateOnly Maandeinde(DateOnly datum) => new(datum.Year, datum.Month, DateTime.DaysInMonth(datum.Year, datum.Month));
}

/// <summary>Een boeking in een afgesloten periode; de diensten controleren vooraf, dit is het vangnet bij de keten.</summary>
public sealed class PeriodeAfgeslotenException(string melding) : InvalidOperationException(melding);
