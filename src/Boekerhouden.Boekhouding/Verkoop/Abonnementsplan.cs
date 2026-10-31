using System.Globalization;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>Hoe vaak een terugkerende factuur komt.</summary>
public enum Herhaling
{
    Maand,
    Kwartaal,
    Halfjaar,
    Jaar,
}

/// <summary>
/// De planning van een terugkerende factuur (huur, abonnement, vaste dienst): de eerste factuurdatum en hoe vaak. Elke
/// factuur hoort bij een periode die op zijn factuurdatum begint (vooraf factureren): de maand, het kwartaal, het
/// halfjaar of het jaar. Datums tellen steeds vanaf het begin, zodat de 31e niet langzaam naar de 28e schuift.
/// </summary>
/// <param name="Einde">De laatste dag waarop nog een factuur komt; leeg = doorlopend.</param>
public sealed partial record Abonnementsplan(DateOnly Begin, Herhaling Herhaling, DateOnly? Einde = null)
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public int Maanden => Herhaling switch
    {
        Herhaling.Kwartaal => 3,
        Herhaling.Halfjaar => 6,
        Herhaling.Jaar => 12,
        _ => 1,
    };

    /// <summary>De factuurdatum van de factuur met dit volgnummer (de eerste is 0).</summary>
    public DateOnly Datum(int volgnummer) => Begin.AddMonths(volgnummer * Maanden);

    /// <summary>De periode die de factuur dekt: van de factuurdatum tot de volgende.</summary>
    public (DateOnly Van, DateOnly Tot) Periode(int volgnummer) => (Datum(volgnummer), Datum(volgnummer + 1).AddDays(-1));

    /// <summary>De volgnummers die tot en met de peildatum gemaakt moeten worden, vanaf het eerste dat nog niet gemaakt is.</summary>
    public IReadOnlyList<int> TeMaken(int volgende, DateOnly peildatum, int maximum = 120)
    {
        var lijst = new List<int>();
        for (var n = Math.Max(0, volgende); lijst.Count < maximum; n++)
        {
            var datum = Datum(n);
            if (datum > peildatum || (Einde is { } einde && datum > einde))
            {
                break;
            }

            lijst.Add(n);
        }

        return lijst;
    }

    /// <summary>De volgende factuurdatum, of leeg als het abonnement afgelopen is.</summary>
    public DateOnly? Volgende(int volgende) => Einde is { } einde && Datum(volgende) > einde ? null : Datum(volgende);

    public IReadOnlyList<string> Controleer() =>
        Einde is { } einde && einde < Begin ? ["De einddatum ligt vóór de eerste factuurdatum."] : [];

    /// <summary>
    /// Variabelen in een omschrijving: <c>#periode</c> (oktober 2026, 4e kwartaal 2026, 1e halfjaar 2026, 2026 of
    /// 1-10-2026 t/m 30-9-2027 als de periode niet samenvalt met een maand, kwartaal of jaar), <c>#maand</c>,
    /// <c>#kwartaal</c>, <c>#jaar</c>, <c>#van</c> en <c>#tot</c>.
    /// </summary>
    public string Vul(string tekst, int volgnummer)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var (van, tot) = Periode(volgnummer);
        return Variabele().Replace(tekst, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "periode" => Periodetekst(van, tot),
            "maand" => van.ToString("MMMM yyyy", Nl),
            "kwartaal" => $"{(van.Month + 2) / 3}e kwartaal {van.Year}",
            "jaar" => van.Year.ToString(Nl),
            "van" => van.ToString("d-M-yyyy", Nl),
            "tot" => tot.ToString("d-M-yyyy", Nl),
            _ => m.Value,
        });
    }

    public static IReadOnlyList<(string Naam, string Betekenis)> Variabelen { get; } =
    [
        ("#periode", "de periode van de factuur (oktober 2026, 4e kwartaal 2026, 2026)"),
        ("#maand", "de maand waarin de periode begint"),
        ("#kwartaal", "het kwartaal waarin de periode begint"),
        ("#jaar", "het jaar waarin de periode begint"),
        ("#van", "de eerste dag van de periode"),
        ("#tot", "de laatste dag van de periode"),
    ];

    private string Periodetekst(DateOnly van, DateOnly tot)
    {
        var heleMaand = van.Day == 1;
        return Herhaling switch
        {
            Herhaling.Maand when heleMaand => van.ToString("MMMM yyyy", Nl),
            Herhaling.Kwartaal when heleMaand && van.Month % 3 == 1 => $"{(van.Month + 2) / 3}e kwartaal {van.Year}",
            Herhaling.Halfjaar when heleMaand && van.Month is 1 or 7 => $"{(van.Month == 1 ? 1 : 2)}e halfjaar {van.Year}",
            Herhaling.Jaar when heleMaand && van.Month == 1 => van.Year.ToString(Nl),
            _ => $"{van.ToString("d-M-yyyy", Nl)} t/m {tot.ToString("d-M-yyyy", Nl)}",
        };
    }

    [GeneratedRegex(@"#([a-zA-Z]+)")]
    private static partial Regex Variabele();
}
