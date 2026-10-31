using System.Globalization;
using Boekerhouden.Boekhouding.Verkoop;
using Boekerhouden.Domain;

namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>Een regel van een verkoopfactuur zoals de gebruiker hem invult (tekst, nog niet gecontroleerd).</summary>
public sealed class Verkoopregelinvoer
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public string? Omschrijving { get; set; }

    public string? Aantal { get; set; }

    public string? Eenheid { get; set; }

    public string? Prijs { get; set; }

    public string? Korting { get; set; }

    public string? Rekening { get; set; }

    public string BtwCode { get; set; } = "V21";

    /// <summary>De regel als die te lezen is (aantal en prijs als getal), anders leeg.</summary>
    public Verkoopregel? Domein()
    {
        if (!BedragParser.TryParse(Aantal ?? "", out var aantal) || !BedragParser.TryParse(Prijs ?? "", out var prijs))
        {
            return null;
        }

        var korting = string.IsNullOrWhiteSpace(Korting) ? 0m : BedragParser.TryParse(Korting.TrimEnd('%', ' '), out var k) ? k : -1m;
        return new Verkoopregel(Omschrijving ?? "", aantal, prijs, Rekening ?? "", BtwCode, Eenheid, korting);
    }

    public static Verkoopregelinvoer Van(Verkoopregel r) => new()
    {
        Omschrijving = r.Omschrijving,
        Aantal = r.Aantal.ToString("0.####", Nl),
        Eenheid = r.Eenheid,
        Prijs = r.Prijs.ToString("0.00##", Nl),
        Korting = r.Korting == 0 ? null : r.Korting.ToString("0.##", Nl),
        Rekening = r.Rekeningnummer,
        BtwCode = r.BtwCode,
    };

    /// <summary>De regels als ze allemaal te lezen zijn; anders de fouten per regel.</summary>
    public static (IReadOnlyList<Verkoopregel> Regels, IReadOnlyList<string> Fouten) Lees(IEnumerable<Verkoopregelinvoer> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        var lijst = regels.ToList();
        var fouten = lijst.Select((r, i) => r.Domein() is null ? $"Regel {i + 1}: vul aantal en prijs in als getal." : null).OfType<string>().ToList();
        return (fouten.Count == 0 ? [.. lijst.Select(r => r.Domein()!)] : [], fouten);
    }
}
