namespace Boekerhouden.Boekhouding.Activa;

/// <summary>
/// Eén actief in het verloopoverzicht van een jaar (de activastaat): aanschafwaarde en cumulatieve afschrijving aan
/// het begin, investeringen, desinvesteringen en afschrijvingen in het jaar, en de stand aan het eind.
/// </summary>
public sealed record Activastaatregel(
    decimal AanschafBegin,
    decimal AfschrijvingBegin,
    decimal Investering,
    decimal Afschrijving,
    decimal DesinvesteringAanschaf,
    decimal DesinvesteringAfschrijving)
{
    public decimal BoekwaardeBegin => AanschafBegin - AfschrijvingBegin;

    public decimal AanschafEind => AanschafBegin + Investering - DesinvesteringAanschaf;

    public decimal AfschrijvingEind => AfschrijvingBegin + Afschrijving - DesinvesteringAfschrijving;

    public decimal BoekwaardeEind => AanschafEind - AfschrijvingEind;

    public static Activastaatregel Nul { get; } = new(0, 0, 0, 0, 0, 0);

    public static Activastaatregel operator +(Activastaatregel a, Activastaatregel b) =>
        new(a.AanschafBegin + b.AanschafBegin, a.AfschrijvingBegin + b.AfschrijvingBegin, a.Investering + b.Investering, a.Afschrijving + b.Afschrijving,
            a.DesinvesteringAanschaf + b.DesinvesteringAanschaf, a.DesinvesteringAfschrijving + b.DesinvesteringAfschrijving);

    /// <summary>Optellen als methode (CA2225).</summary>
    public static Activastaatregel Add(Activastaatregel a, Activastaatregel b) => a + b;
}

public static class Activastaat
{
    /// <summary>
    /// De regel van een actief in een jaar, uit de geboekte afschrijvingen en desinvesteringen (dus zoals het in het
    /// grootboek staat). Leeg als het actief in dat jaar niet op de balans stond (nog niet gekocht of al helemaal weg).
    /// </summary>
    /// <param name="geboekt">De geboekte afschrijvingen per maand.</param>
    public static Activastaatregel? Regel(Activum activum, IReadOnlyDictionary<DateOnly, decimal> geboekt, IReadOnlyList<Desinvesteringsstap> desinvesteringen, int jaar)
    {
        ArgumentNullException.ThrowIfNull(activum);
        ArgumentNullException.ThrowIfNull(geboekt);
        ArgumentNullException.ThrowIfNull(desinvesteringen);
        var (begin, eind) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        var ervoor = desinvesteringen.Where(d => d.Datum < begin).ToList();
        var erin = desinvesteringen.Where(d => d.Datum >= begin && d.Datum <= eind).ToList();
        if (activum.Aanschafdatum > eind || (ervoor.Count > 0 && ervoor.Sum(d => d.Aanschaf) >= activum.Aanschafwaarde))
        {
            return null;
        }

        var al = activum.Aanschafdatum < begin;
        var afschrijvingBegin = (al ? activum.AfgeschrevenVoorBegin : 0m) + geboekt.Where(g => g.Key < begin).Sum(g => g.Value) - ervoor.Sum(d => d.Afschrijving);
        var afschrijving = (al ? 0m : activum.AfgeschrevenVoorBegin) + geboekt.Where(g => g.Key >= begin && g.Key <= eind).Sum(g => g.Value);
        return new Activastaatregel(
            al ? activum.Aanschafwaarde - ervoor.Sum(d => d.Aanschaf) : 0m,
            afschrijvingBegin,
            al ? 0m : activum.Aanschafwaarde,
            afschrijving,
            erin.Sum(d => d.Aanschaf),
            erin.Sum(d => d.Afschrijving));
    }
}
