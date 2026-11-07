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
    /// De regel van een actief in een jaar, uit de geboekte afschrijvingen (dus zoals het in het grootboek staat). Leeg
    /// als het actief in dat jaar niet op de balans stond (nog niet gekocht of al eerder buiten gebruik).
    /// </summary>
    /// <param name="geboekt">De geboekte afschrijvingen per maand.</param>
    /// <param name="buitenGebruik">De datum van buiten gebruik stellen, of leeg.</param>
    public static Activastaatregel? Regel(Activum activum, IReadOnlyDictionary<DateOnly, decimal> geboekt, DateOnly? buitenGebruik, int jaar)
    {
        ArgumentNullException.ThrowIfNull(activum);
        ArgumentNullException.ThrowIfNull(geboekt);
        var (begin, eind) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        if (activum.Aanschafdatum > eind || buitenGebruik < begin)
        {
            return null;
        }

        var al = activum.Aanschafdatum < begin;
        var afschrijvingBegin = (al ? activum.AfgeschrevenVoorBegin : 0m) + geboekt.Where(g => g.Key < begin).Sum(g => g.Value);
        var afschrijving = (al ? 0m : activum.AfgeschrevenVoorBegin) + geboekt.Where(g => g.Key >= begin && g.Key <= eind).Sum(g => g.Value);
        var weg = buitenGebruik is { } d && d <= eind;
        return new Activastaatregel(
            al ? activum.Aanschafwaarde : 0m,
            afschrijvingBegin,
            al ? 0m : activum.Aanschafwaarde,
            afschrijving,
            weg ? activum.Aanschafwaarde : 0m,
            weg ? afschrijvingBegin + afschrijving : 0m);
    }
}
