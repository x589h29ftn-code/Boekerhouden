using System.Globalization;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Facturen;

/// <summary>Een geboekt document, om de map en de bestandsnaam voor de kopie te bepalen.</summary>
/// <param name="Soort">Bijv. <c>Inkoop</c> of <c>Verkoop</c>.</param>
/// <param name="Extensie">Van het originele bestand, bijv. <c>.pdf</c>.</param>
public sealed record Documentgegevens(
    string Administratie,
    string Soort,
    DateOnly Datum,
    string? Relatie,
    string? Factuurnummer,
    string Extensie);

/// <summary>
/// Waar een kopie van een geboekt document komt, bijvoorbeeld in een OneDrive-map per klant en boekjaar:
/// <c>C:\Users\naam\OneDrive\Klanten\#administratie\#jaar\#soort</c>. De variabelen worden gevuld met veilige
/// waarden: zonder tekens die in een bestandsnaam niet mogen en zonder padscheiding, zodat een leveranciersnaam
/// nooit buiten de gekozen map kan schrijven. De map zelf moet een volledig pad zijn, zonder <c>..</c>.
/// </summary>
public static partial class Documentpad
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly string[] Gereserveerd = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3"];

    /// <summary>De variabelen voor de map, met een korte uitleg.</summary>
    public static IReadOnlyList<(string Naam, string Betekenis)> Variabelen { get; } =
    [
        ("#administratie", "de naam van de administratie"),
        ("#jaar", "het boekjaar (jaar van de factuurdatum)"),
        ("#kwartaal", "het kwartaal, bijv. Q2"),
        ("#maand", "de maand als getal, bijv. 04"),
        ("#soort", "Inkoop of Verkoop"),
        ("#leverancier", "de naam van de relatie"),
    ];

    /// <summary>Wat er niet klopt aan een map; leeg als die goed is of leeg (geen kopie).</summary>
    public static IReadOnlyList<string> Controleer(string? sjabloon)
    {
        if (string.IsNullOrWhiteSpace(sjabloon))
        {
            return [];
        }

        var fouten = new List<string>();
        var vast = sjabloon.Split('#', 2)[0];
        if (!IsVolledig(vast))
        {
            fouten.Add("De documentmap moet een volledig pad zijn, bijv. C:\\Users\\naam\\OneDrive\\Klanten\\#administratie\\#jaar\\#soort.");
        }

        if (Delen(sjabloon).Any(d => d == ".."))
        {
            fouten.Add("De documentmap mag geen '..' bevatten.");
        }

        if (Variabele().Matches(sjabloon).Select(m => m.Groups[1].Value).Where(n => Waarde(n, Voorbeeld) is null).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } onbekend)
        {
            fouten.Add($"Onbekende variabele in de documentmap: {string.Join(", ", onbekend.Select(o => "#" + o))}.");
        }

        return fouten;
    }

    /// <summary>De map voor dit document: het sjabloon met de variabelen ingevuld.</summary>
    public static string Map(string sjabloon, Documentgegevens gegevens)
    {
        ArgumentNullException.ThrowIfNull(sjabloon);
        ArgumentNullException.ThrowIfNull(gegevens);
        if (Controleer(sjabloon) is [var fout, ..])
        {
            throw new ArgumentException(fout, nameof(sjabloon));
        }

        if (!Path.IsPathFullyQualified(sjabloon.Split('#', 2)[0]))
        {
            throw new ArgumentException("De documentmap is op deze computer geen geldig pad.", nameof(sjabloon));
        }

        var map = Variabele().Replace(sjabloon.Trim(), m => Veilig(Waarde(m.Groups[1].Value, gegevens) ?? m.Value));
        return Path.GetFullPath(map);
    }

    /// <summary>De bestandsnaam: datum, relatie en factuurnummer, bijv. <c>2026-04-01 Belnet B.V. BN-2026-0411.pdf</c>.</summary>
    public static string Bestandsnaam(Documentgegevens gegevens)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        var naam = Veilig(string.Join(' ', new[] { gegevens.Datum.ToString("yyyy-MM-dd", Nl), gegevens.Relatie, gegevens.Factuurnummer }
            .Where(d => !string.IsNullOrWhiteSpace(d))));
        var extensie = Veilig(gegevens.Extensie.TrimStart('.'));
        return extensie is "onbekend" ? naam : $"{naam}.{extensie.ToLowerInvariant()}";
    }

    /// <summary>Een tekst als één deel van een pad: zonder verboden tekens, scheidingen of punten aan het eind.</summary>
    public static string Veilig(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var schoon = new string([.. tekst.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? ' ' : c)]);
        schoon = MeerdereSpaties().Replace(schoon, " ").Trim().TrimEnd('.', ' ').TrimStart('.', ' ');
        if (schoon.Length > 120)
        {
            schoon = schoon[..120].TrimEnd('.', ' ');
        }

        if (schoon.Length == 0)
        {
            return "onbekend";
        }

        return Gereserveerd.Contains(schoon.Split('.')[0], StringComparer.OrdinalIgnoreCase) ? $"{schoon}_" : schoon;
    }

    private static readonly Documentgegevens Voorbeeld = new("A", "Inkoop", new(2026, 1, 1), "R", "1", ".pdf");

    private static string? Waarde(string naam, Documentgegevens g) => naam.ToLowerInvariant() switch
    {
        "administratie" => g.Administratie,
        "jaar" or "boekjaar" => g.Datum.Year.ToString(Nl),
        "kwartaal" => $"Q{((g.Datum.Month - 1) / 3) + 1}",
        "maand" => g.Datum.Month.ToString("00", Nl),
        "soort" => g.Soort,
        "leverancier" or "relatie" => g.Relatie ?? string.Empty,
        _ => null,
    };

    private static bool IsVolledig(string pad) =>
        Path.IsPathFullyQualified(pad) || WindowsPad().IsMatch(pad);

    private static IEnumerable<string> Delen(string pad) => pad.Split('/', '\\').Select(d => d.Trim());

    [GeneratedRegex(@"#([A-Za-z]+)")]
    private static partial Regex Variabele();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MeerdereSpaties();

    /// <summary>Een Windows-pad (C:\ of \\server\share), ook als de controle op een ander besturingssysteem draait.</summary>
    [GeneratedRegex(@"^([A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+)")]
    private static partial Regex WindowsPad();
}
