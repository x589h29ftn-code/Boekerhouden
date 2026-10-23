using System.Globalization;
using Boekerhouden.Data;

namespace Boekerhouden.Server;

/// <summary>Nederlandse weergave van datums en keuzelijsten in de schermen.</summary>
public static class Weergave
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string DatumTijd(DateTimeOffset tijdstip) =>
        TimeZoneInfo.ConvertTime(tijdstip, Amsterdam).ToString("dd-MM-yyyy HH:mm", Nederlands);

    public static string Datum(DateTimeOffset tijdstip) =>
        TimeZoneInfo.ConvertTime(tijdstip, Amsterdam).ToString("dd-MM-yyyy", Nederlands);

    public static string Tekst(Rechtsvorm rechtsvorm) => rechtsvorm switch
    {
        Rechtsvorm.BV => "BV",
        Rechtsvorm.Eenmanszaak => "Eenmanszaak",
        _ => rechtsvorm.ToString(),
    };

    public static string Tekst(SoortOpdracht soort) => soort switch
    {
        SoortOpdracht.SamenstellenJaarrekening => "Samenstellen jaarrekening",
        SoortOpdracht.WinstaangifteOpstellen => "Winstaangifte opstellen",
        _ => soort.ToString(),
    };

    public static string Tekst(DossierStatus status) => status switch
    {
        DossierStatus.Nieuw => "Nieuw",
        DossierStatus.InUitvoering => "In uitvoering",
        _ => status.ToString(),
    };

    public static string Tekst(StapStatus status) => status switch
    {
        StapStatus.Open => "open",
        StapStatus.Gereed => "gereed",
        StapStatus.NietVanToepassing => "niet van toepassing",
        _ => status.ToString(),
    };

    public static string Tekst(Fase fase) => fase switch
    {
        Fase.Voorbereiding => "Voorbereiding",
        Fase.Uitvoering => "Uitvoering",
        Fase.Afronding => "Afronding",
        _ => fase.ToString(),
    };
}
