using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie;

/// <summary>Nederlandse weergave van datums en keuzelijsten in de schermen.</summary>
public static class Weergave
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string DatumTijd(DateTimeOffset tijdstip) =>
        TimeZoneInfo.ConvertTime(tijdstip, Amsterdam).ToString("dd-MM-yyyy HH:mm", Nederlands);

    public static string Datum(DateOnly datum) => datum.ToString("dd-MM-yyyy", Nederlands);

    /// <summary>Een bedrag op het scherm: zoals <see cref="Domain.Bedragen.Getal"/>, maar negatief met een echt minteken (−) in plaats van een koppelteken.</summary>
    public static string Bedrag(decimal bedrag) => Domain.Bedragen.Getal(bedrag).Replace('-', '\u2212');

    /// <summary>Een bedrag in hele euro's op het scherm, negatief met een echt minteken.</summary>
    public static string BedragHeel(decimal bedrag) => Domain.Bedragen.Heel(bedrag).Replace('-', '\u2212');

    /// <summary>
    /// Een datum zoals een Nederlander hem typt: dag-maand-jaar (<c>30-10-2026</c>, <c>30/10/26</c>, <c>30.10</c> = dit
    /// jaar, <c>301026</c>, <c>30102026</c>, <c>3010</c>); ook <c>2026-10-30</c>. Een jaar van twee cijfers is 20xx.
    /// </summary>
    public static DateOnly? LeesDatum(string? tekst, int ditJaar)
    {
        var t = (tekst ?? "").Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso;
        }

        var delen = t.Split(['-', '/', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (delen.Length == 1 && delen[0].All(char.IsAsciiDigit))
        {
            delen = delen[0].Length switch
            {
                4 => [delen[0][..2], delen[0][2..]],
                6 => [delen[0][..2], delen[0][2..4], delen[0][4..]],
                8 => [delen[0][..2], delen[0][2..4], delen[0][4..]],
                _ => [],
            };
        }

        if (delen.Length is < 2 or > 3 || !delen.All(d => d.All(char.IsAsciiDigit) && d.Length is > 0 and <= 4)
            || !int.TryParse(delen[0], CultureInfo.InvariantCulture, out var dag) || !int.TryParse(delen[1], CultureInfo.InvariantCulture, out var maand))
        {
            return null;
        }

        var jaar = ditJaar;
        if (delen.Length == 3 && (!int.TryParse(delen[2], CultureInfo.InvariantCulture, out jaar) || delen[2].Length is not (2 or 4)))
        {
            return null;
        }

        jaar = jaar < 100 ? 2000 + jaar : jaar;
        return maand is >= 1 and <= 12 && jaar is >= 1900 and <= 2999 && dag >= 1 && dag <= DateTime.DaysInMonth(jaar, maand) ? new DateOnly(jaar, maand, dag) : null;
    }

    public static string Tekst(Ondernemingsvorm vorm) => vorm switch
    {
        Ondernemingsvorm.BV => "BV",
        _ => "Eenmanszaak",
    };

    public static string Tekst(Lidmaatschapsrol rol) => rol switch
    {
        Lidmaatschapsrol.Inzien => "inzien",
        Lidmaatschapsrol.Ondernemer => "ondernemer",
        _ => "kantoor",
    };

    public static string Tekst(Rekeningfunctie functie) => functie switch
    {
        Rekeningfunctie.Bank => "bank",
        Rekeningfunctie.Kas => "kas",
        Rekeningfunctie.Debiteuren => "debiteuren",
        Rekeningfunctie.Crediteuren => "crediteuren",
        Rekeningfunctie.Voorbelasting => "voorbelasting",
        Rekeningfunctie.AfTeDragenBtw => "af te dragen btw",
        Rekeningfunctie.OnverdeeldResultaat => "resultaat eerdere jaren",
        Rekeningfunctie.Vraagposten => "vraagposten",
        _ => "",
    };

    /// <summary>Een periode uit de adresregel (<c>van=2026-01-01&amp;tot=2026-12-31</c>), of leeg als die er niet (goed) staat.</summary>
    public static (DateOnly Van, DateOnly Tot)? Periode(string? van, string? tot) =>
        DateOnly.TryParseExact(van, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
        && DateOnly.TryParseExact(tot, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) && v <= t
            ? (v, t)
            : null;

    public static string Periodequery(DateOnly van, DateOnly tot) =>
        $"van={van.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&tot={tot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
