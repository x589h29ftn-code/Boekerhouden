using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>Een adres zoals het op de factuur staat. <paramref name="Land"/> is de landcode (NL, BE, DE …); leeg = Nederland.</summary>
public sealed record Adres(string? Straat = null, string? Postcode = null, string? Plaats = null, string? Land = null)
{
    public bool IsLeeg => string.IsNullOrWhiteSpace(Straat) && string.IsNullOrWhiteSpace(Plaats);

    /// <summary>De landcode, Nederland als er niets is ingevuld.</summary>
    public string Landcode => string.IsNullOrWhiteSpace(Land) ? "NL" : Land.Trim().ToUpperInvariant();

    /// <summary>De regels van het adres: straat, postcode en plaats, en het land als het niet Nederland is.</summary>
    public IReadOnlyList<string> Regels =>
    [
        .. new[]
        {
            Straat?.Trim(),
            string.Join("  ", new[] { Postcode?.Trim(), Plaats?.Trim() }.Where(s => !string.IsNullOrEmpty(s))),
            Landcode == "NL" ? null : Landen.Naam(Landcode),
        }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!),
    ];
}

/// <summary>Een partij op de factuur: de eigen onderneming (afzender) of de klant (afnemer).</summary>
public sealed record Partij(
    string Naam,
    Adres Adres,
    string? BtwNummer = null,
    string? KvkNummer = null,
    string? Email = null,
    IReadOnlyList<string>? Ibans = null,
    string? Telefoon = null,
    string? Website = null,
    string? Contactpersoon = null);

/// <summary>
/// Eén regel van een verkoopfactuur: aantal × prijs (exclusief btw), eventueel met een korting in procenten, op een
/// omzetrekening met een btw-code. Een creditfactuur heeft een negatief aantal.
/// </summary>
public sealed record Verkoopregel(
    string Omschrijving,
    decimal Aantal,
    decimal Prijs,
    string Rekeningnummer,
    string BtwCode,
    string? Eenheid = null,
    decimal Korting = 0m)
{
    /// <summary>Het regelbedrag exclusief btw, op centen afgerond (half van nul af).</summary>
    public decimal Bedrag => Math.Round(Aantal * Prijs * (100m - Korting) / 100m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>De btw van één code op de factuur: de grondslag (som van de regels) en de btw daarover.</summary>
public sealed record Btwregel(BtwCode Code, decimal Grondslag, decimal Btw);

/// <summary>
/// Een verkoopfactuur zoals die wordt gemaakt: de klant (zoals op de factuur, ook als de relatie later verandert), de
/// regels en de datums. Het nummer krijgt hij pas bij het definitief maken (<see cref="Factuurnummering"/>). De btw
/// wordt per btw-code over de som van de regels berekend, zoals op de factuur; de boeking volgt precies die bedragen.
/// </summary>
/// <param name="Relatie">De code van de klant in de administratie.</param>
/// <param name="CreditVoor">Bij een creditfactuur: het nummer van de factuur die hij crediteert.</param>
public sealed record Verkoopfactuur(
    string Relatie,
    Partij Klant,
    string? Nummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Verkoopregel> Regels,
    string? Betalingskenmerk = null,
    string? CreditVoor = null)
{
    public decimal TotaalExclusief => Regels.Sum(r => r.Bedrag);

    /// <summary>Per btw-code (in de volgorde van de regels) de grondslag en de btw.</summary>
    public IReadOnlyList<Btwregel> Btw(IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        return
        [
            .. Regels.GroupBy(r => r.BtwCode, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                var code = BtwCodes.Zoek(codes, g.Key) ?? throw new ArgumentException($"Btw-code {g.Key} bestaat niet.", nameof(codes));
                var grondslag = g.Sum(r => r.Bedrag);
                return new Btwregel(code, grondslag, code.Verlegd ? 0m : code.Bereken(grondslag));
            }),
        ];
    }

    public decimal TotaalBtw(IReadOnlyList<BtwCode> codes) => Btw(codes).Sum(b => b.Btw);

    public decimal Totaal(IReadOnlyList<BtwCode> codes) => TotaalExclusief + TotaalBtw(codes);

    public bool IsCredit => TotaalExclusief < 0 || CreditVoor is not null;

    /// <summary>
    /// De factuur om te boeken: één boekregel per factuurregel, met de btw zo verdeeld over de regels van een code dat
    /// de som precies de btw per code van de factuur is (de laatste regel van een code krijgt het afrondingsverschil).
    /// </summary>
    public Factuur NaarFactuur(IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (Nummer is null)
        {
            throw new InvalidOperationException("De factuur heeft nog geen nummer.");
        }

        var btwPerCode = Btw(codes).ToDictionary(b => b.Code.Code, b => b.Btw, StringComparer.OrdinalIgnoreCase);
        var verdeeld = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var laatste = Regels.Select((r, i) => (r.BtwCode, i)).GroupBy(x => x.BtwCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Max(x => x.i), StringComparer.OrdinalIgnoreCase);
        var regels = new List<Factuurregel>();
        for (var i = 0; i < Regels.Count; i++)
        {
            var r = Regels[i];
            var code = BtwCodes.Zoek(codes, r.BtwCode)!;
            var gehad = verdeeld.GetValueOrDefault(r.BtwCode);
            var btw = laatste[r.BtwCode] == i ? btwPerCode[r.BtwCode] - gehad : code.Verlegd ? 0m : code.Bereken(r.Bedrag);
            verdeeld[r.BtwCode] = gehad + btw;
            regels.Add(new Factuurregel(Regelomschrijving(r), r.Rekeningnummer, code.Code, r.Bedrag, btw));
        }

        return new Factuur(Factuursoort.Verkoop, Relatie, Nummer, Datum, Vervaldatum, Omschrijving, regels, Betalingskenmerk);
    }

    private static string Regelomschrijving(Verkoopregel r)
    {
        var tekst = r.Omschrijving.Trim();
        return tekst.Length > 200 ? tekst[..200] : tekst;
    }
}

/// <summary>De namen van de landen die op een factuur kunnen staan (landcode → naam).</summary>
public static class Landen
{
    private static readonly Dictionary<string, string> Namen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NL"] = "Nederland",
        ["BE"] = "België",
        ["DE"] = "Duitsland",
        ["FR"] = "Frankrijk",
        ["LU"] = "Luxemburg",
        ["AT"] = "Oostenrijk",
        ["DK"] = "Denemarken",
        ["ES"] = "Spanje",
        ["IT"] = "Italië",
        ["PT"] = "Portugal",
        ["IE"] = "Ierland",
        ["SE"] = "Zweden",
        ["FI"] = "Finland",
        ["PL"] = "Polen",
        ["CZ"] = "Tsjechië",
        ["GB"] = "Verenigd Koninkrijk",
        ["CH"] = "Zwitserland",
        ["NO"] = "Noorwegen",
        ["US"] = "Verenigde Staten",
    };

    public static string Naam(string code) => Namen.GetValueOrDefault(code) ?? code.ToUpperInvariant();
}
