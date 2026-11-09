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
public sealed record Belastingbetaling(Belastingsoort Soort, Rekening? Rekening)
{
    public string Reden => $"Belastingdienst: {Soort switch
    {
        Belastingsoort.Omzetbelasting => "omzetbelasting",
        Belastingsoort.Loonheffingen => "loonheffingen",
        Belastingsoort.Vennootschapsbelasting => "vennootschapsbelasting",
        _ => "inkomstenbelasting",
    }}";
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

    public static Belastingbetaling? Herken(Bankmutatie mutatie, Rekeningschema schema, Ondernemingsvorm vorm)
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
        if (Soort(tekst) is not { } soort)
        {
            return null;
        }

        return new Belastingbetaling(soort, Rekening(soort, schema, vorm));
    }

    internal static Belastingsoort? Soort(string tekst)
    {
        if (Aanslagnummer().Match(tekst) is { Success: true } nummer)
        {
            switch (char.ToUpperInvariant(nummer.Groups["letter"].Value[0]))
            {
                case 'B':
                    return Belastingsoort.Omzetbelasting;
                case 'L':
                    return Belastingsoort.Loonheffingen;
                case 'V':
                    return Belastingsoort.Vennootschapsbelasting;
                case 'H':
                    return Belastingsoort.Inkomstenbelasting;
            }
        }

        // Het betalingskenmerk van 16 cijfers: een controlecijfer, de eerste 8 cijfers van het BSN of RSIN, dan de
        // belastingsoort als cijfer (1 = omzetbelasting, de B van het aanslagnummer), het jaar en het tijdvak.
        if (Betalingskenmerk().Match(tekst) is { Success: true } kenmerk && kenmerk.Value[9] == '1')
        {
            return Belastingsoort.Omzetbelasting;
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
