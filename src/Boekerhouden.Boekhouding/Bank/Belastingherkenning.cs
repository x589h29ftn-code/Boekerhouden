using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Bank;

public enum Belastingsoort
{
    Omzetbelasting,
    Loonheffingen,
    Vennootschapsbelasting,
    Inkomstenbelasting,
}

/// <summary>Een betaling aan (of van) de Belastingdienst: welke belasting, en de rekening die erbij hoort (of leeg).</summary>
/// <param name="Aanslag">Wat het betalingskenmerk zegt (aanslagnummer, jaar, tijdvak), als er een is.</param>
public sealed record Belastingbetaling(Belastingsoort Soort, Rekening? Rekening, Aanslagkenmerk? Aanslag = null)
{
    /// <summary>De omschrijving van de boeking, bijv. "Omzetbelasting 3e kwartaal 2026" of "Loonheffingen augustus 2026".</summary>
    public string Omschrijving
    {
        get
        {
            var tekst = Kort();
            return char.ToUpperInvariant(tekst[0]) + tekst[1..];
        }
    }

    public string Reden => $"Belastingdienst: {Kort()}{(Aanslag?.Aanslagnummer is { } a ? $", aanslag {a}" : null)}";

    private string Kort()
    {
        var soort = Soort switch
        {
            Belastingsoort.Omzetbelasting => "omzetbelasting",
            Belastingsoort.Loonheffingen => "loonheffingen",
            Belastingsoort.Vennootschapsbelasting => "vennootschapsbelasting",
            _ => Aanslag?.Letter == 'W' ? "bijdrage Zvw" : "inkomstenbelasting",
        };
        var delen = new[] { Aanslag?.Naheffing == true ? $"naheffingsaanslag {soort}" : soort, Aanslag?.Tijdvak, Aanslag?.Jaar?.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            .Where(d => d is not null);
        return string.Join(' ', delen);
    }
}

/// <summary>Wat er in een betalingskenmerk van de Belastingdienst staat.</summary>
/// <param name="Letter">De letter van de belasting (het middel), bijv. B omzetbelasting of H inkomstenbelasting.</param>
/// <param name="Aanslagnummer">Bij omzetbelasting en loonheffingen het aanslagnummer, bijv. <c>1234.56.782.L.01.6080</c>.</param>
public sealed record Aanslagkenmerk(char Letter, string? Aanslagnummer = null, int? Jaar = null, string? Tijdvak = null)
{
    public bool Naheffing => Letter is 'F' or 'A' or 'Y';
}

/// <summary>
/// Herkent een betaling aan of teruggaaf van de Belastingdienst (op het IBAN of de naam) en welke belasting het is: eerst
/// de letter in het aanslag- of kenmerknummer (<c>1234.56.789.B.01</c>: B omzetbelasting, L loonheffingen, V
/// vennootschapsbelasting, H inkomstenbelasting), anders de woorden in de omschrijving. De rekening komt uit het schema:
/// een rekening "afgedragen …" als die er is, anders de rekening met de RGS-groep of de naam van die belasting.
/// Inkomstenbelasting is privé: bij een eenmanszaak de privé-opnamen, bij een BV de rekening-courant met de directeur.
/// </summary>
public static partial class Belastingherkenning
{
    /// <summary>Het rekeningnummer van de Belastingdienst voor betalingen van aanslagen en aangiften.</summary>
    public const string BelastingdienstIban = "NL86INGB0002445588";

    /// <param name="vast">Een vaste rekening per belasting (instelling van de administratie); gaat voor de eigen keuze.</param>
    public static Belastingbetaling? Herken(Bankmutatie mutatie, Rekeningschema schema, Ondernemingsvorm vorm, IReadOnlyDictionary<Belastingsoort, string>? vast = null)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        ArgumentNullException.ThrowIfNull(schema);
        var vanBelastingdienst = string.Equals(mutatie.TegenIban, BelastingdienstIban, StringComparison.OrdinalIgnoreCase)
                                 || mutatie.TegenNaam?.Contains("belastingdienst", StringComparison.OrdinalIgnoreCase) == true;
        if (!vanBelastingdienst)
        {
            return null;
        }

        var tekst = $"{mutatie.Omschrijving} {mutatie.Betalingskenmerk}";
        var aanslag = Betalingskenmerk().Matches(tekst).Select(m => Lees(m.Value, mutatie.Datum)).FirstOrDefault(a => a is not null);
        if (((aanslag is null ? null : Soort(aanslag.Letter)) ?? Soort(tekst)) is not { } soort)
        {
            return null;
        }

        var rekening = vast?.TryGetValue(soort, out var nummer) == true && schema.Zoek(nummer) is { } gekozen ? gekozen : Rekening(soort, schema, vorm);
        return new Belastingbetaling(soort, rekening, aanslag);
    }

    /// <summary>
    /// Het betalingskenmerk van 16 cijfers lezen. Bij omzetbelasting en loonheffingen: een controlecijfer, de eerste 8
    /// cijfers van het BSN of RSIN (het 9e volgt uit de elfproef), de soort als één cijfer (1 = B aangifte
    /// omzetbelasting, 5 = F naheffing omzetbelasting, 6 = L aangifte loonheffingen, 0 = A naheffing loonheffingen),
    /// het laatste cijfer van het jaar, een subnummer van 2, het tijdvak van 2 en een volgnummer. Zo is
    /// <c>4123456786601080</c> aanslag <c>1234.56.782.L.01.6080</c>: loonheffingen augustus 2026. Bij de andere belastingen
    /// staat de soort in twee cijfers (70 H inkomstenbelasting, 75 W Zvw, 74/80–84/92–96 V vennootschapsbelasting …);
    /// dan alleen de soort. Leeg als het kenmerk niet zo te lezen is.
    /// </summary>
    public static Aanslagkenmerk? Lees(string kenmerk, DateOnly datum)
    {
        if (kenmerk.Length != 16 || !kenmerk.All(char.IsAsciiDigit) || !ControlecijferKlopt(kenmerk))
        {
            return null;
        }

        if (kenmerk[9] is '1' or '5' or '6' or '0')
        {
            var letter = kenmerk[9] switch { '1' => 'B', '5' => 'F', '6' => 'L', _ => 'A' };
            var cijfers = kenmerk[1..9];
            var som = 0;
            for (var i = 0; i < 8; i++)
            {
                som += (cijfers[i] - '0') * (9 - i);
            }

            if (som % 11 == 10)
            {
                return null;
            }

            var nummer = cijfers + (char)('0' + (som % 11));
            var jaar = datum.Year - (((datum.Year % 10) - (kenmerk[10] - '0') + 10) % 10);
            var tijdvak = int.Parse(kenmerk[13..15], System.Globalization.CultureInfo.InvariantCulture);
            return new Aanslagkenmerk(letter, $"{nummer[..4]}.{nummer[4..6]}.{nummer[6..]}.{letter}.{kenmerk[11..13]}.{kenmerk[10]}{kenmerk[13..]}", jaar, Tijdvak(tijdvak));
        }

        var code = int.Parse(kenmerk[9..11], System.Globalization.CultureInfo.InvariantCulture);
        char? middel = code switch
        {
            70 => 'H',
            73 => 'N',
            71 => 'P',
            75 => 'W',
            74 or (>= 80 and <= 84) or (>= 92 and <= 96) => 'V',
            78 or 87 => 'M',
            76 or 88 => 'Y',
            85 or 86 or 97 => 'Z',
            >= 23 and <= 28 => 'T',
            _ => null,
        };
        return middel is { } m ? new Aanslagkenmerk(m) : null;
    }

    // De wegingen van rechts naar links over de 15 cijfers na het controlecijfer: machten van 2 modulo 11.
    private static readonly int[] Wegingen = [2, 4, 8, 5, 10, 9, 7, 3, 6, 1, 2, 4, 8, 5, 10];

    /// <summary>
    /// Klopt het controlecijfer (het eerste cijfer)? Modulus 11 over de andere 15 cijfers, van rechts naar links gewogen
    /// met 2, 4, 8, 5, 10, 9, 7, 3, 6, 1, …; het cijfer is 11 min de rest, waarbij 10 een 1 wordt en 11 een 0.
    /// </summary>
    public static bool ControlecijferKlopt(string kenmerk)
    {
        if (kenmerk.Length != 16 || !kenmerk.All(char.IsAsciiDigit))
        {
            return false;
        }

        var som = 0;
        for (var i = 0; i < 15; i++)
        {
            som += (kenmerk[15 - i] - '0') * Wegingen[i];
        }

        var cijfer = (11 - (som % 11)) switch
        {
            10 => 1,
            11 => 0,
            var c => c,
        };
        return kenmerk[0] - '0' == cijfer;
    }

    private static readonly string[] Maanden = ["januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december"];

    private static string? Tijdvak(int code) => code switch
    {
        >= 1 and <= 12 => Maanden[code - 1],
        21 => "1e kwartaal",
        24 => "2e kwartaal",
        27 => "3e kwartaal",
        30 => "4e kwartaal",
        31 => "1e halfjaar",
        32 => "2e halfjaar",
        40 => "heel jaar",
        >= 71 and <= 83 => $"vierwekenperiode {code - 70}",
        _ => null,
    };

    private static Belastingsoort? Soort(char letter) => char.ToUpperInvariant(letter) switch
    {
        'B' or 'F' => Belastingsoort.Omzetbelasting,
        'L' or 'A' => Belastingsoort.Loonheffingen,
        'V' => Belastingsoort.Vennootschapsbelasting,
        'H' or 'N' or 'P' or 'W' => Belastingsoort.Inkomstenbelasting,
        _ => null,
    };

    internal static Belastingsoort? Soort(string tekst)
    {
        if (Aanslagnummer().Match(tekst) is { Success: true } nummer && Soort(nummer.Groups["letter"].Value[0]) is { } uitNummer)
        {
            return uitNummer;
        }

        return tekst switch
        {
            _ when Woord(tekst, "omzetbelasting|btw|ob") => Belastingsoort.Omzetbelasting,
            _ when Woord(tekst, "loonheffing(en)?|loonbelasting|lh") => Belastingsoort.Loonheffingen,
            _ when Woord(tekst, "vennootschapsbelasting|vpb") => Belastingsoort.Vennootschapsbelasting,
            _ when Woord(tekst, "inkomstenbelasting|ib|zvw|premie zvw") => Belastingsoort.Inkomstenbelasting,
            _ => null,
        };
    }

    private static bool Woord(string tekst, string woorden) =>
        Regex.IsMatch(tekst, $@"\b({woorden})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Rekening? Rekening(Belastingsoort soort, Rekeningschema schema, Ondernemingsvorm vorm)
    {
        var balans = schema.Alle.Where(r => r.IsBalans).ToList();
        Rekening? Naam(params string[] woorden) => balans.FirstOrDefault(r => woorden.All(w => Namen.Normaliseer(r.Naam).Contains(w, StringComparison.Ordinal)));
        Rekening? Rgs(string code) => balans.FirstOrDefault(r => r.RgsCode?.StartsWith(code, StringComparison.Ordinal) == true);
        return soort switch
        {
            Belastingsoort.Omzetbelasting => Naam("afgedragen", "btw") ?? Naam("afgedragen", "omzetbelasting")
                                             ?? schema.MetFunctie(Rekeningfunctie.AfTeDragenBtw).FirstOrDefault() ?? Rgs("BSchBepBtw"),
            Belastingsoort.Loonheffingen => Naam("afgedragen", "loonheffing") ?? Rgs("BSchBepLhe") ?? Naam("loonheffing"),
            Belastingsoort.Vennootschapsbelasting => Rgs("BSchShb") ?? Naam("vennootschapsbelasting"),
            _ => vorm == Ondernemingsvorm.Eenmanszaak
                ? Rgs("BEivKapPro") ?? Naam("prive", "opnamen")
                : Rgs("BVorOvrRcb") ?? Naam("rekening", "courant"),
        };
    }

    [GeneratedRegex(@"\b\d{4}\.?\d{2}\.?\d{3}\.?(?<letter>[BLVH])\.?\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex Aanslagnummer();

    [GeneratedRegex(@"(?<!\d)\d{16}(?!\d)")]
    private static partial Regex Betalingskenmerk();
}
