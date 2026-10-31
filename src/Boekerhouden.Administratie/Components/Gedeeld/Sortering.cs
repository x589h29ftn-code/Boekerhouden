namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>Een kolom om op te sorteren; tekst gaat eerst A–Z, bedragen en datums eerst hoog–laag.</summary>
public sealed record Sorteerkolom<T>(string Naam, Func<T, IComparable?> Waarde, bool EerstOplopend = true);

/// <summary>Wat een kolomkop van de sortering wil weten (zonder het type van de regels).</summary>
public interface ISortering
{
    string? Kolom { get; }

    bool Oplopend { get; }

    void Wissel(string kolom);
}

/// <summary>
/// De sortering van een tabel: een kolomkop aanklikken sorteert daarop (zie <see cref="SorteerKop"/>), nog eens klikken
/// keert om. Zonder gekozen kolom blijft de volgorde van de lijst zelf.
/// </summary>
public sealed class Sortering<T>(params Sorteerkolom<T>[] kolommen) : ISortering
{
    public string? Kolom { get; private set; }

    public bool Oplopend { get; private set; } = true;

    public void Wissel(string kolom)
    {
        if (kolommen.FirstOrDefault(k => k.Naam == kolom) is not { } gekozen)
        {
            return;
        }

        (Kolom, Oplopend) = Kolom == kolom ? (Kolom, !Oplopend) : (kolom, gekozen.EerstOplopend);
    }

    /// <summary>De regels in de gekozen volgorde; bij gelijke waarde blijft de oorspronkelijke volgorde.</summary>
    public IEnumerable<T> Pas(IEnumerable<T> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        if (kolommen.FirstOrDefault(k => k.Naam == Kolom) is not { } kolom)
        {
            return regels;
        }

        return Oplopend ? regels.OrderBy(kolom.Waarde, Comparer<IComparable?>.Default) : regels.OrderByDescending(kolom.Waarde, Comparer<IComparable?>.Default);
    }
}
