using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Btw;

/// <summary>Eén rubriek van de aangifte: de grondslag (omzet of inkoop zonder btw) en de btw daarover.</summary>
public sealed record Aangifterubriek(string Rubriek, decimal Grondslag, decimal Btw);

/// <summary>
/// De aangifte omzetbelasting over een periode, berekend uit de boekingen met hun btw-code. Per regel bepaalt de rekening
/// wat het bedrag is: op de rekening voor voorbelasting is het aftrekbare btw (5b), op de rekening voor af te dragen btw
/// de verschuldigde btw (in de rubriek van de code), en op elke andere rekening de grondslag (in de rubriek van de code).
/// Bedragen exact; afronden op hele euro's hoort bij het indienen.
/// </summary>
public sealed record Btwaangifte(DateOnly Van, DateOnly Tot, IReadOnlyList<Aangifterubriek> Rubrieken, decimal Voorbelasting)
{
    /// <summary>De volgorde van de rubrieken op het aangifteformulier.</summary>
    public static IReadOnlyList<string> Volgorde { get; } = ["1a", "1b", "1c", "1d", "1e", "2a", "3a", "3b", "3c", "4a", "4b"];

    /// <summary>De verschuldigde btw (5a): de btw van alle rubrieken.</summary>
    public decimal Verschuldigd => Rubrieken.Sum(r => r.Btw);

    /// <summary>Te betalen (positief) of terug te vragen (negatief).</summary>
    public decimal TeBetalen => Verschuldigd - Voorbelasting;

    public Aangifterubriek Rubriek(string rubriek) => Rubrieken.FirstOrDefault(r => r.Rubriek == rubriek) ?? new(rubriek, 0m, 0m);

    public static Btwaangifte Bereken(IEnumerable<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(codes);
        var grondslag = Volgorde.ToDictionary(r => r, _ => 0m, StringComparer.Ordinal);
        var btw = Volgorde.ToDictionary(r => r, _ => 0m, StringComparer.Ordinal);
        var voorbelasting = 0m;
        foreach (var regel in posten.Where(p => p.Datum >= van && p.Datum <= tot).SelectMany(p => p.Regels).Where(r => r.BtwCode is not null))
        {
            if (BtwCodes.Zoek(codes, regel.BtwCode) is not { } code)
            {
                continue;
            }

            switch (schema.Zoek(regel.Rekeningnummer)?.Functie)
            {
                case Rekeningfunctie.Voorbelasting:
                    voorbelasting += regel.Bedrag;
                    break;
                case Rekeningfunctie.AfTeDragenBtw when code.RubriekBtw is { } rubriek:
                    btw[rubriek] -= regel.Bedrag;
                    break;
                case Rekeningfunctie.AfTeDragenBtw:
                    break;
                default:
                    if (code.Rubriek is { } r)
                    {
                        grondslag[r] += code.Richting == BtwRichting.Verkoop ? -regel.Bedrag : regel.Bedrag;
                    }

                    break;
            }
        }

        return new Btwaangifte(van, tot, [.. Volgorde.Select(r => new Aangifterubriek(r, grondslag[r], btw[r]))], voorbelasting);
    }
}
