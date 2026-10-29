namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een periode van datum tot en met datum, zoals in de overzichten (kolommenbalans, grootboekkaart).</summary>
public static class Periode
{
    public static (DateOnly Van, DateOnly Tot) Jaar(int jaar) => (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));

    /// <summary>
    /// De vorige (<paramref name="stappen"/> = -1) of volgende (+1) periode van dezelfde lengte. Hele maanden (zoals een
    /// kwartaal of een jaar) schuiven per maand, zodat februari en schrikkeljaren goed gaan; anders per dag.
    /// </summary>
    public static (DateOnly Van, DateOnly Tot) Schuif(DateOnly van, DateOnly tot, int stappen)
    {
        if (tot < van)
        {
            (van, tot) = (tot, van);
        }

        if (van.Day == 1 && tot.AddDays(1).Day == 1)
        {
            var maanden = ((tot.Year - van.Year) * 12) + tot.Month - van.Month + 1;
            var nieuw = van.AddMonths(maanden * stappen);
            return (nieuw, nieuw.AddMonths(maanden).AddDays(-1));
        }

        var dagen = tot.DayNumber - van.DayNumber + 1;
        return (van.AddDays(dagen * stappen), tot.AddDays(dagen * stappen));
    }
}
