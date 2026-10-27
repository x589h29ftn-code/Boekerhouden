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
}
