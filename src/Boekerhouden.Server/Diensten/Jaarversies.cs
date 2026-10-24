using Boekerhouden.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Server.Diensten;

/// <summary>
/// Instellingen die gelden vanaf een boekjaar (<see cref="IJaarversie"/>): lezen voor een jaar en wijzigen vanaf een
/// jaar. Zo veranderen eerdere jaren nooit met terugwerkende kracht, en gaan aanpassingen wel mee naar volgende jaren.
/// </summary>
public static class Jaarversies
{
    /// <summary>Per sleutel de versie die geldt in het boekjaar: die met het hoogste VanafBoekjaar tot en met dat jaar.</summary>
    public static Dictionary<TSleutel, T> Geldend<T, TSleutel>(IEnumerable<T> versies, Func<T, TSleutel> sleutel, int boekjaar, IEqualityComparer<TSleutel>? vergelijker = null)
        where T : IJaarversie
        where TSleutel : notnull =>
        versies.Where(v => v.VanafBoekjaar <= boekjaar)
            .GroupBy(sleutel, vergelijker)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.VanafBoekjaar)!, vergelijker);

    /// <summary>
    /// Legt een wijziging vast vanaf het boekjaar. De versie van dat jaar wordt aangepast, of aangemaakt als kopie van
    /// wat tot dan gold. Is de versie daarna gelijk aan wat ervoor gold, dan is hij overbodig en vervalt hij.
    /// </summary>
    /// <param name="versies">Alle versies van deze ene sleutel (alle jaren), gevolgd door de context.</param>
    /// <param name="maak">Een nieuwe versie voor het boekjaar met de waarden van de gegeven versie, of de standaard bij null.</param>
    /// <param name="waarde">De waarde om te vergelijken (zonder wie/wanneer), bijv. een tuple.</param>
    /// <returns>Of er iets is veranderd.</returns>
    public static bool Zet<T>(DbSet<T> set, IReadOnlyCollection<T> versies, int boekjaar, Func<T?, T> maak, Action<T> wijzig, Func<T, object> waarde)
        where T : class, IJaarversie
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(versies);
        var eigen = versies.SingleOrDefault(v => v.VanafBoekjaar == boekjaar);
        var vorige = versies.Where(v => v.VanafBoekjaar < boekjaar).MaxBy(v => v.VanafBoekjaar);
        var ervoor = waarde(vorige ?? maak(null));
        var oud = eigen is null ? ervoor : waarde(eigen);
        var versie = eigen ?? maak(vorige);
        versie.VanafBoekjaar = boekjaar;
        wijzig(versie);
        if (Equals(waarde(versie), oud))
        {
            return false;
        }

        if (Equals(waarde(versie), ervoor))
        {
            if (eigen is not null)
            {
                set.Remove(eigen);
            }
        }
        else if (eigen is null)
        {
            set.Add(versie);
        }

        return true;
    }
}
