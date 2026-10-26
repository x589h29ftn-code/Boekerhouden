namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Het rekeningschema van één administratie.</summary>
public sealed class Rekeningschema
{
    private readonly Dictionary<string, Rekening> rekeningen = new(StringComparer.Ordinal);

    public Rekeningschema(IEnumerable<Rekening> rekeningen)
    {
        ArgumentNullException.ThrowIfNull(rekeningen);
        foreach (var rekening in rekeningen)
        {
            Voegtoe(rekening);
        }
    }

    /// <summary>Alle rekeningen op nummer, numeriek waar dat kan (<c>800</c> vóór <c>1000</c>).</summary>
    public IEnumerable<Rekening> Alle => rekeningen.Values.OrderBy(r => r.Nummer, Rekeningnummers.Volgorde);

    public void Voegtoe(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        ArgumentException.ThrowIfNullOrWhiteSpace(rekening.Nummer);
        ArgumentException.ThrowIfNullOrWhiteSpace(rekening.Naam);
        if (rekening.RgsCode is { Length: > 0 } rgs && (rgs[0] == 'B') != rekening.IsBalans)
        {
            throw new ArgumentException(
                $"Rekening {rekening.Nummer} is een {(rekening.IsBalans ? "balans" : "resultaat")}rekening, maar RGS-code {rgs} niet.",
                nameof(rekening));
        }

        if (!rekeningen.TryAdd(rekening.Nummer, rekening))
        {
            throw new ArgumentException($"Rekening {rekening.Nummer} bestaat al.", nameof(rekening));
        }
    }

    public Rekening? Zoek(string? nummer) => nummer is not null && rekeningen.TryGetValue(nummer, out var r) ? r : null;

    public IEnumerable<Rekening> MetFunctie(Rekeningfunctie functie) => Alle.Where(r => r.Functie == functie);

    /// <summary>De rekening met deze functie; een fout als die er niet (of meer dan eens) is.</summary>
    public Rekening Enige(Rekeningfunctie functie)
    {
        var lijst = MetFunctie(functie).Take(2).ToList();
        return lijst.Count switch
        {
            1 => lijst[0],
            0 => throw new InvalidOperationException($"Het rekeningschema heeft geen rekening voor {functie}."),
            _ => throw new InvalidOperationException($"Het rekeningschema heeft meer dan één rekening voor {functie}."),
        };
    }
}

/// <summary>Sorteren van rekeningnummers: numeriek als beide nummers alleen cijfers zijn, anders als tekst.</summary>
public static class Rekeningnummers
{
    public static IComparer<string> Volgorde { get; } = Comparer<string>.Create(Vergelijk);

    private static int Vergelijk(string? a, string? b)
    {
        if (a is not null && b is not null && a.All(char.IsAsciiDigit) && b.All(char.IsAsciiDigit))
        {
            var lengte = Math.Max(a.Length, b.Length);
            var verschil = string.CompareOrdinal(a.PadLeft(lengte, '0'), b.PadLeft(lengte, '0'));
            return verschil != 0 ? verschil : a.Length.CompareTo(b.Length);
        }

        return string.CompareOrdinal(a, b);
    }
}
