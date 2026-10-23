using System.Globalization;

namespace Boekerhouden.Domain;

/// <summary>Nederlandse weergave van bedragen, bijvoorbeeld in meldingen.</summary>
public static class Bedragen
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Bijvoorbeeld <c>€ 1.234,56</c> of <c>€ -1.234,56</c>.</summary>
    public static string Euro(decimal bedrag) => "€ " + bedrag.ToString("#,##0.00", Nederlands);

    /// <summary>Getal zonder euroteken voor tabellen, bijvoorbeeld <c>1.234,56</c> of <c>-1.234,56</c>.</summary>
    public static string Getal(decimal bedrag) => bedrag.ToString("#,##0.00", Nederlands);

    /// <summary>Een bedrag in hele euro's zonder valutateken, bijv. "1.235" of "-81.300" (zoals in de jaarrekening).</summary>
    public static string Heel(decimal bedrag) => Afronding.OpHeleEuro(bedrag).ToString("#,##0", Nederlands);

    /// <summary>Een bedrag in hele euro's met valutateken, bijv. "€ 1.235".</summary>
    public static string EuroHeel(decimal bedrag) => "€ " + Heel(bedrag);

    /// <summary>Bijvoorbeeld <c>€ 125,40 debet</c>, <c>€ 125,40 credit</c> of <c>€ 0,00</c>.</summary>
    public static string DebetCredit(decimal bedrag) => bedrag switch
    {
        0m => Euro(0m),
        > 0m => $"{Euro(bedrag)} debet",
        _ => $"{Euro(-bedrag)} credit",
    };
}
