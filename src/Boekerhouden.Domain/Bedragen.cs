using System.Globalization;

namespace Boekerhouden.Domain;

/// <summary>Nederlandse weergave van bedragen, bijvoorbeeld in meldingen.</summary>
public static class Bedragen
{
    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Bijvoorbeeld <c>€ 1.234,56</c> of <c>€ -1.234,56</c>.</summary>
    public static string Euro(decimal bedrag) => "€ " + bedrag.ToString("#,##0.00", Nederlands);

    /// <summary>Bijvoorbeeld <c>€ 125,40 debet</c>, <c>€ 125,40 credit</c> of <c>€ 0,00</c>.</summary>
    public static string DebetCredit(decimal bedrag) => bedrag switch
    {
        0m => Euro(0m),
        > 0m => $"{Euro(bedrag)} debet",
        _ => $"{Euro(-bedrag)} credit",
    };
}
