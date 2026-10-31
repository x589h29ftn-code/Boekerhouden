using System.Globalization;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>
/// Doorlopende nummering van verkoopfacturen per jaar, zonder gaten: het jaar en een volgnummer van vier cijfers
/// (<c>20260001</c>, <c>20260002</c> …; na 9999 gewoon verder met vijf cijfers). Met een voorvoegsel naar keuze
/// (bijv. <c>F</c> geeft <c>F20260001</c>). Het jaar is dat van de factuurdatum; op 1 januari begint het weer bij 1.
/// </summary>
public static class Factuurnummering
{
    public static string Nummer(int jaar, int volgnummer, string? voorvoegsel = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volgnummer, 1);
        return $"{voorvoegsel?.Trim()}{jaar.ToString(CultureInfo.InvariantCulture)}{volgnummer.ToString("0000", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Het volgnummer van een nummer uit dit jaar (met dit voorvoegsel), of leeg als het er niet bij hoort.</summary>
    public static int? Volgnummer(string nummer, int jaar, string? voorvoegsel = null)
    {
        ArgumentNullException.ThrowIfNull(nummer);
        var begin = $"{voorvoegsel?.Trim()}{jaar.ToString(CultureInfo.InvariantCulture)}";
        if (!nummer.StartsWith(begin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = nummer[begin.Length..];
        return rest.Length >= 4 && rest.All(char.IsAsciiDigit) && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    /// <summary>Het volgende nummer in het jaar: één hoger dan het hoogste dat al is uitgegeven.</summary>
    public static string Volgende(int jaar, IEnumerable<string> uitgegeven, string? voorvoegsel = null)
    {
        ArgumentNullException.ThrowIfNull(uitgegeven);
        var hoogste = uitgegeven.Select(n => Volgnummer(n, jaar, voorvoegsel) ?? 0).DefaultIfEmpty(0).Max();
        return Nummer(jaar, hoogste + 1, voorvoegsel);
    }

    /// <summary>
    /// Ontbrekende volgnummers in een jaar (een gat in de reeks), om de nummering te controleren. De belastingdienst
    /// verlangt een doorlopende reeks.
    /// </summary>
    public static IReadOnlyList<int> Gaten(int jaar, IEnumerable<string> uitgegeven, string? voorvoegsel = null)
    {
        ArgumentNullException.ThrowIfNull(uitgegeven);
        var nummers = uitgegeven.Select(n => Volgnummer(n, jaar, voorvoegsel)).OfType<int>().ToHashSet();
        return nummers.Count == 0 ? [] : [.. Enumerable.Range(1, nummers.Max()).Where(n => !nummers.Contains(n))];
    }
}

/// <summary>
/// Het gestructureerde betalingskenmerk van 16 cijfers (Currence): een controlecijfer en vijftien cijfers. Betaalt de
/// klant met dit kenmerk, dan lettert de bank de factuur met zekerheid af. Het controlecijfer is 11 min de som van de
/// cijfers maal hun gewicht (de machten van 2 modulo 11, van rechts af: 2, 4, 8, 5, 10, 9, 7, 3, 6, 1 …) modulo 11;
/// 11 wordt 0 en 10 wordt 1.
/// </summary>
public static class Betalingskenmerk
{
    /// <summary>Een kenmerk uit de cijfers van het factuurnummer (de laatste vijftien, links aangevuld met nullen).</summary>
    public static string Maak(string factuurnummer)
    {
        ArgumentNullException.ThrowIfNull(factuurnummer);
        var cijfers = string.Concat(factuurnummer.Where(char.IsAsciiDigit));
        if (cijfers.Length == 0)
        {
            throw new ArgumentException("Het factuurnummer heeft geen cijfers.", nameof(factuurnummer));
        }

        var vijftien = (cijfers.Length > 15 ? cijfers[^15..] : cijfers).PadLeft(15, '0');
        return Controlecijfer(vijftien).ToString(CultureInfo.InvariantCulture) + vijftien;
    }

    public static bool IsGeldig(string? kenmerk)
    {
        var cijfers = kenmerk is null ? "" : string.Concat(kenmerk.Where(c => !char.IsWhiteSpace(c)));
        return cijfers.Length == 16 && cijfers.All(char.IsAsciiDigit) && cijfers[0] - '0' == Controlecijfer(cijfers[1..]);
    }

    /// <summary>In groepen van vier, zoals op de factuur: <c>1234 5678 9012 3456</c>.</summary>
    public static string Toon(string kenmerk)
    {
        ArgumentNullException.ThrowIfNull(kenmerk);
        return kenmerk.Length == 16 ? string.Join(' ', Enumerable.Range(0, 4).Select(i => kenmerk.Substring(i * 4, 4))) : kenmerk;
    }

    private static int Controlecijfer(string vijftien)
    {
        var som = 0;
        var gewicht = 2;
        for (var i = vijftien.Length - 1; i >= 0; i--)
        {
            som += (vijftien[i] - '0') * gewicht;
            gewicht = gewicht * 2 % 11;
        }

        var controle = 11 - som % 11;
        return controle switch
        {
            11 => 0,
            10 => 1,
            _ => controle,
        };
    }
}
