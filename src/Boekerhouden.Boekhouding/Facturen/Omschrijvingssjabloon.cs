using System.Globalization;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Facturen;

/// <summary>De gegevens van een factuur waarmee de variabelen in een omschrijving worden gevuld.</summary>
public sealed record Factuurvariabelen(
    string? Factuurnummer = null,
    string? Omschrijving = null,
    string? Leverancier = null,
    DateOnly? Datum = null,
    DateOnly? Vervaldatum = null,
    string? Kenmerk = null);

/// <summary>
/// Een omschrijving met variabelen, bijv. <c>Telefoon #maand #jaar (#factuurnummer)</c>. Bij het boeken worden de
/// variabelen gevuld met de gegevens van de factuur; zo kan een regel of het leergeheugen een vaste vorm onthouden.
/// Een onbekende variabele blijft staan zoals hij is.
/// </summary>
public static partial class Omschrijvingssjabloon
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>De variabelen die het pakket kent, met een korte uitleg (voor de keuzelijst en de hulp).</summary>
    public static IReadOnlyList<(string Naam, string Betekenis)> Variabelen { get; } =
    [
        ("#factuurnummer", "het factuurnummer"),
        ("#omschrijving", "de omschrijving van de factuur"),
        ("#leverancier", "de naam van de leverancier"),
        ("#datum", "de factuurdatum"),
        ("#vervaldatum", "de vervaldatum"),
        ("#maand", "de maand van de factuurdatum"),
        ("#kwartaal", "het kwartaal van de factuurdatum"),
        ("#jaar", "het jaar van de factuurdatum"),
        ("#kenmerk", "het betalingskenmerk"),
    ];

    /// <summary>Bevat de tekst minstens één bekende variabele?</summary>
    public static bool IsSjabloon(string? tekst) =>
        tekst is not null && Variabele().Matches(tekst).Any(m => Bekend(m.Groups[1].Value));

    /// <summary>De onbekende variabelen in de tekst (zonder #), bijv. bij een tikfout.</summary>
    public static IReadOnlyList<string> Onbekend(string? tekst) =>
        tekst is null ? [] : [.. Variabele().Matches(tekst).Select(m => m.Groups[1].Value).Where(n => !Bekend(n)).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>De tekst met de variabelen gevuld; een variabele zonder waarde wordt leeg, dubbele spaties verdwijnen.</summary>
    public static string Vul(string sjabloon, Factuurvariabelen gegevens)
    {
        ArgumentNullException.ThrowIfNull(sjabloon);
        ArgumentNullException.ThrowIfNull(gegevens);
        var gevuld = Variabele().Replace(sjabloon, m => Waarde(m.Groups[1].Value.ToLowerInvariant(), gegevens) ?? m.Value);
        return MeerdereSpaties().Replace(gevuld, " ").Trim();
    }

    private static bool Bekend(string naam) =>
        Variabelen.Any(v => string.Equals(v.Naam[1..], naam, StringComparison.OrdinalIgnoreCase));

    private static string? Waarde(string naam, Factuurvariabelen g) => naam switch
    {
        "factuurnummer" => g.Factuurnummer ?? string.Empty,
        "omschrijving" => g.Omschrijving ?? string.Empty,
        "leverancier" => g.Leverancier ?? string.Empty,
        "datum" => g.Datum?.ToString("dd-MM-yyyy", Nl) ?? string.Empty,
        "vervaldatum" => g.Vervaldatum?.ToString("dd-MM-yyyy", Nl) ?? string.Empty,
        "maand" => g.Datum?.ToString("MMMM", Nl) ?? string.Empty,
        "kwartaal" => g.Datum is { } d ? $"Q{((d.Month - 1) / 3) + 1}" : string.Empty,
        "jaar" => g.Datum?.Year.ToString(Nl) ?? string.Empty,
        "kenmerk" => g.Kenmerk ?? string.Empty,
        _ => null,
    };

    [GeneratedRegex(@"#([A-Za-z]+)")]
    private static partial Regex Variabele();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MeerdereSpaties();
}
