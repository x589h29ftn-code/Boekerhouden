namespace Boekerhouden.Import;

/// <summary>
/// Eén cel uit een ingelezen bestand. <see cref="Getal"/> is alleen gevuld bij een numerieke Excel-cel;
/// bij CSV is alles tekst.
/// </summary>
internal sealed record Cel(string Tekst, decimal? Getal = null)
{
    public static readonly Cel Leeg = new(string.Empty);

    public bool IsLeeg => Getal is null && Tekst.Length == 0;
}

/// <summary>Een rij uit een ingelezen bestand, met het rijnummer zoals de gebruiker het ziet (1-based).</summary>
internal sealed record Rij(int Rijnummer, IReadOnlyList<Cel> Cellen)
{
    public Cel this[int kolom] => kolom >= 0 && kolom < Cellen.Count ? Cellen[kolom] : Cel.Leeg;

    public bool IsLeeg => Cellen.All(c => c.IsLeeg);
}
