using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>
/// Gedeelde maatvoering van de grafieken op het dashboard (SVG zonder script): het vlak, een nette as in euro's,
/// staafjes met een afgeronde kop en asbijschriften.
/// </summary>
public sealed class Grafiek
{
    public const double Breedte = 720;
    public const double Hoogte = 240;
    public const double Links = 64;
    public const double Rechts = 8;
    public const double Boven = 10;
    public const double Onder = 26;

    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Een as over alle waarden, met 0 erin: 4 tot 6 stappen van 1, 2, 2,5 of 5 maal een macht van tien.</summary>
    public Grafiek(IEnumerable<decimal> waarden, int kolommen)
    {
        var lijst = waarden.Select(w => (double)w).Append(0).ToList();
        (Min, Max, Stap) = Schaal(lijst.Min(), lijst.Max());
        Kolommen = Math.Max(kolommen, 1);
    }

    public double Min { get; }

    public double Max { get; }

    public double Stap { get; }

    public int Kolommen { get; }

    /// <summary>De breedte van één maand.</summary>
    public double Band => (Breedte - Links - Rechts) / Kolommen;

    public double Midden(int kolom) => Links + ((kolom + .5) * Band);

    public double Y(double waarde) => Boven + ((Max - waarde) / (Max - Min) * (Hoogte - Boven - Onder));

    public double Y(decimal waarde) => Y((double)waarde);

    public IEnumerable<double> Ticks
    {
        get
        {
            for (var t = Min; t <= Max + (Stap / 2); t += Stap)
            {
                yield return Math.Round(t, 2);
            }
        }
    }

    internal static (double Min, double Max, double Stap) Schaal(double min, double max)
    {
        if (max - min < 1)
        {
            return (0, 1000, 250);
        }

        var ruw = (max - min) / 4;
        var macht = Math.Pow(10, Math.Floor(Math.Log10(ruw)));
        var stap = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * macht).First(s => s >= ruw);
        return (Math.Floor(min / stap) * stap, Math.Ceiling(max / stap) * stap, stap);
    }

    /// <summary>Een staaf vanaf 0 met afgeronde hoeken aan de kant van de waarde, recht op de nullijn.</summary>
    public string Staaf(double x, double breedte, decimal bedrag)
    {
        var nul = Y(0d);
        var eind = Y(bedrag);
        var hoogte = Math.Abs(nul - eind);
        if (hoogte < .5)
        {
            return "";
        }

        var r = Math.Min(4, Math.Min(hoogte, breedte / 2));
        var richting = eind < nul ? 1 : -1;
        return $"M{G(x)},{G(nul)} V{G(eind + (richting * r))} Q{G(x)},{G(eind)} {G(x + r)},{G(eind)} H{G(x + breedte - r)} " +
            $"Q{G(x + breedte)},{G(eind)} {G(x + breedte)},{G(eind + (richting * r))} V{G(nul)} Z";
    }

    /// <summary>Een asbijschrift; als markup omdat Razor een &lt;text&gt;-element met attributen niet toestaat.</summary>
    public static MarkupString Label(double x, double y, string anker, string tekst) =>
        new($"<text class=\"as\" x=\"{G(x)}\" y=\"{G(y)}\" text-anchor=\"{anker}\">{System.Net.WebUtility.HtmlEncode(tekst)}</text>");

    /// <summary>Een getal in een SVG-attribuut.</summary>
    public static string G(double getal) => getal.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Een getal op de as: 1.500, 25.000, 1,2 mln.</summary>
    public static string Asgetal(double waarde) => Math.Abs(waarde) >= 1_000_000
        ? (waarde / 1_000_000).ToString("0.#", Nederlands) + " mln"
        : waarde.ToString("#,0", Nederlands);

    public static string Maandnaam(DateOnly maand) => maand.ToString("MMMM", Nederlands);

    /// <summary>Kort onder de as: "jan", of "jan 26" als de grafiek over twee jaren loopt.</summary>
    public static string Maandkort(DateOnly maand, bool metJaar) =>
        metJaar && maand.Month == 1 ? maand.ToString("MMM yy", Nederlands).Replace(".", "", StringComparison.Ordinal) : Maandnaam(maand)[..3];
}
