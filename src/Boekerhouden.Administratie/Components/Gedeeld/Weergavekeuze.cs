namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>
/// Welke onderdelen van een scherm aan staan, zoals de browser dat onthoudt: <c>aan|bekend</c>, bijv.
/// <c>viewer,verval|viewer,btwbedrag,verval</c>. Een onderdeel dat er later bij komt (niet bij "bekend"), volgt de
/// standaard; zo verdwijnt een nieuw onderdeel niet bij wie de weergave al eens had aangepast.
/// </summary>
public static class Weergavekeuze
{
    /// <param name="opgeslagen">De tekst uit de browser, of leeg.</param>
    /// <param name="alle">Alle onderdelen die het scherm nu kent.</param>
    /// <param name="standaard">Wat aan staat voor wie nog niets koos.</param>
    /// <param name="eerderBekend">De onderdelen van vóór dit formaat (alleen <c>aan</c>, zonder "|").</param>
    public static HashSet<string> Lees(string? opgeslagen, IReadOnlyCollection<string> alle, IReadOnlyCollection<string> standaard, IReadOnlyCollection<string> eerderBekend)
    {
        ArgumentNullException.ThrowIfNull(alle);
        ArgumentNullException.ThrowIfNull(standaard);
        ArgumentNullException.ThrowIfNull(eerderBekend);
        if (opgeslagen is null)
        {
            return [.. standaard];
        }

        var delen = opgeslagen.Split('|');
        var aan = Lijst(delen[0]);
        var bekend = delen.Length > 1 ? Lijst(delen[1]) : [.. eerderBekend];
        return [.. aan.Where(alle.Contains).Concat(standaard.Where(s => !bekend.Contains(s)))];
    }

    public static string Tekst(IEnumerable<string> aan, IEnumerable<string> alle) =>
        $"{string.Join(",", aan)}|{string.Join(",", alle)}";

    private static HashSet<string> Lijst(string tekst) => [.. tekst.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
