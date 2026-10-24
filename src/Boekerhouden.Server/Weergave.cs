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

    /// <summary>Bijvoorbeeld "24 oktober 2026", zoals in een rapport.</summary>
    public static string DatumVoluit(DateTimeOffset tijdstip) =>
        TimeZoneInfo.ConvertTime(tijdstip, Amsterdam).ToString("d MMMM yyyy", Nederlands);

    public static string Datum(DateOnly datum) => datum.ToString("dd-MM-yyyy", Nederlands);

    /// <summary>Bijvoorbeeld "28 juni 2024".</summary>
    public static string DatumVoluit(DateOnly datum) => datum.ToString("d MMMM yyyy", Nederlands);

    /// <summary>Straat, huisnummer en toevoeging, bijv. "Dorpsstraat 12 A"; null als er geen straat is.</summary>
    public static string? Straat(Klant klant)
    {
        ArgumentNullException.ThrowIfNull(klant);
        return string.IsNullOrWhiteSpace(klant.Straat) ? null
            : string.Join(' ', new[] { klant.Straat, klant.Huisnummer, klant.Toevoeging }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    /// <summary>Het volledige adres op één regel, bijv. "Dorpsstraat 12 A, 1234 AB Ergens"; null als er niets is.</summary>
    public static string? Adres(Klant klant)
    {
        ArgumentNullException.ThrowIfNull(klant);
        var plaats = string.Join(' ', new[] { klant.Postcode, klant.Plaats }.Where(t => !string.IsNullOrWhiteSpace(t)));
        var adres = string.Join(", ", new[] { Straat(klant), plaats }.Where(t => !string.IsNullOrWhiteSpace(t)));
        return adres.Length == 0 ? null : adres;
    }

    public static string Tekst(Rechtsvorm rechtsvorm) => rechtsvorm switch
    {
        Rechtsvorm.BV => "BV",
        Rechtsvorm.Eenmanszaak => "Eenmanszaak",
        _ => rechtsvorm.ToString(),
    };

    public static string Tekst(Erkenningswijze wijze) => wijze switch
    {
        Erkenningswijze.Bevestigingsbrief => "bevestigingsbrief",
        Erkenningswijze.Email => "e-mail",
        _ => "anders",
    };

    public static string Tekst(PostBeoordeling beoordeling) => beoordeling switch
    {
        PostBeoordeling.Materieel => "materieel",
        PostBeoordeling.Risicopost => "risicopost",
        _ => "n.v.t.",
    };

    public static string Tekst(Verslaggeving verslaggeving) => verslaggeving switch
    {
        Verslaggeving.Commercieel => "Commerciële grondslagen",
        Verslaggeving.Fiscaal => "Fiscale grondslagen",
        _ => verslaggeving.ToString(),
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
