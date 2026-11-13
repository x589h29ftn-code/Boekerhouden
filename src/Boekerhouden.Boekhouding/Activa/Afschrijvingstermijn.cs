using System.Globalization;

namespace Boekerhouden.Boekhouding.Activa;

/// <summary>Hoe de termijn van een lineaire afschrijving wordt ingevuld; vastgelegd wordt altijd het aantal maanden.</summary>
public enum Termijneenheid
{
    /// <summary>In jaren (5 jaar = 60 maanden).</summary>
    Jaren,

    /// <summary>Als percentage per jaar (20% per jaar = 60 maanden, 3,33% = 360 maanden).</summary>
    ProcentPerJaar,

    /// <summary>In maanden.</summary>
    Maanden,
}

/// <summary>
/// De termijn van een afschrijving omrekenen tussen maanden (wat vastligt) en wat de gebruiker invult: jaren of een
/// percentage per jaar. Het verandert niets aan de afschrijving zelf.
/// </summary>
public static class Afschrijvingstermijn
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Het aantal maanden bij een waarde in de eenheid; leeg als het geen positief getal is of meer dan 100 jaar.</summary>
    public static int? NaarMaanden(string? tekst, Termijneenheid eenheid)
    {
        var schoon = tekst?.Trim().TrimEnd('%').Trim();
        if (string.IsNullOrEmpty(schoon)
            || !decimal.TryParse(schoon.Replace('.', ','), NumberStyles.Number, Nl, out var waarde) || waarde <= 0)
        {
            return null;
        }

        var maanden = eenheid switch
        {
            Termijneenheid.Jaren => waarde * 12m,
            Termijneenheid.ProcentPerJaar => 1200m / waarde,
            _ => waarde,
        };
        var afgerond = Math.Round(maanden, MidpointRounding.AwayFromZero);
        return afgerond is >= 1 and <= 1200 ? (int)afgerond : null;
    }

    /// <summary>Het aantal maanden als tekst in de eenheid: "5", "20", "3,33", "60".</summary>
    public static string Tekst(int maanden, Termijneenheid eenheid) => eenheid switch
    {
        Termijneenheid.Jaren => (maanden / 12m).ToString("0.##", Nl),
        Termijneenheid.ProcentPerJaar => (1200m / maanden).ToString("0.##", Nl),
        _ => maanden.ToString(Nl),
    };

    /// <summary>De eenheid die bij een termijn past: jaren bij hele jaren, anders maanden.</summary>
    public static Termijneenheid Passend(int maanden) => maanden > 0 && maanden % 12 == 0 ? Termijneenheid.Jaren : Termijneenheid.Maanden;
}
