namespace Boekerhouden.Boekhouding.Activa;

/// <summary>Een actief van een ingelezen staat zoals het in de vaste activa komt.</summary>
/// <param name="DatumGeschat">De staat noemde geen aanschafdatum: geschat uit wat al is afgeschreven.</param>
/// <param name="AfgeschrevenVoorBegin">Wat er op de staat al is afgeschreven (tot en met de einddatum van de staat).</param>
/// <param name="Looptijd">De resterende looptijd in maanden vanaf <paramref name="BeginAfschrijving"/>.</param>
public sealed record Overname(
    string Omschrijving,
    DateOnly Aanschafdatum,
    bool DatumGeschat,
    decimal Aanschafwaarde,
    decimal Restwaarde,
    Afschrijvingsmethode Methode,
    DateOnly BeginAfschrijving,
    int Looptijd,
    decimal AfgeschrevenVoorBegin);

/// <summary>
/// Van een regel op de staat naar een vast actief dat in deze administratie verder gaat: de aanschafwaarde en
/// restwaarde aan het eind van de staat, wat er al is afgeschreven, en de resterende looptijd tegen het percentage
/// van de staat (lineair over aanschaf min restwaarde). Zonder percentage en zonder afschrijving (kunst, grond): niet
/// afschrijven. Volledig afgeschreven: een looptijd van één maand zonder bedrag, dan staat het op de activastaat.
/// </summary>
public static class Staatovername
{
    /// <summary>De overname per de dag na de staat (bijv. 1 januari 2026 na een staat over 2025); leeg als er geen aanschafwaarde meer is.</summary>
    public static Overname? Van(Staatregel regel, DateOnly begin)
    {
        ArgumentNullException.ThrowIfNull(regel);
        if (regel.AanschafEind <= 0)
        {
            return null;
        }

        var aanschaf = regel.AanschafEind;
        var rest = Math.Clamp(regel.Restwaarde, 0m, aanschaf);
        var afschrijfbaar = aanschaf - rest;
        var afgeschreven = Math.Clamp(regel.AfschrijvingEind, 0m, afschrijfbaar);
        var eersteMaand = new DateOnly(begin.Year, begin.Month, 1);
        if (regel.Percentage is not { } percentage || percentage <= 0)
        {
            // Zonder percentage: niet afschrijven als er ook niets is afgeschreven (kunst, grond); anders is wat er al af
            // is alles (de rest staat open tot er een percentage is).
            var datum = regel.Datum ?? eersteMaand.AddYears(-1);
            return afgeschreven == 0
                ? new Overname(regel.Omschrijving, datum, regel.Datum is null, aanschaf, rest, Afschrijvingsmethode.Geen, eersteMaand, 0, 0m)
                : new Overname(regel.Omschrijving, datum, regel.Datum is null, aanschaf, aanschaf - afgeschreven, Afschrijvingsmethode.Lineair, eersteMaand, 1, afgeschreven);
        }

        var perMaand = afschrijfbaar * percentage / 100m / 12m;
        var over = afschrijfbaar - afgeschreven;
        var looptijd = over <= 0 || perMaand <= 0 ? 1 : (int)Math.Clamp(Math.Ceiling(over / perMaand), 1m, Activum.MaximaleLooptijd);

        // Geen datum: zo lang terug als er al is afgeschreven (afgeschreven / per maand), anders een jaar terug.
        var geschat = perMaand > 0 && afgeschreven > 0
            ? eersteMaand.AddMonths(-(int)Math.Clamp(Math.Round(afgeschreven / perMaand), 1m, 1200m))
            : eersteMaand.AddYears(-1);
        return new Overname(regel.Omschrijving, regel.Datum ?? geschat, regel.Datum is null, aanschaf, rest, Afschrijvingsmethode.Lineair, eersteMaand, looptijd, afgeschreven);
    }
}
