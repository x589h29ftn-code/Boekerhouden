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
