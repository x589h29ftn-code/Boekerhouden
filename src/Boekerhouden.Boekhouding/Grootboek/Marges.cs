namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Omzet en inkoop (kostprijs) als positieve bedragen, met de brutomarge en de marge in procenten van de omzet.</summary>
public sealed record Marge(decimal Omzet, decimal Inkoop)
{
    public static Marge Nul { get; } = new(0m, 0m);

    public decimal Brutomarge => Omzet - Inkoop;

    /// <summary>De brutomarge in procenten van de omzet, op één decimaal; leeg zonder omzet.</summary>
    public decimal? Percentage => Omzet == 0 ? null : Math.Round(Brutomarge / Omzet * 100m, 1, MidpointRounding.AwayFromZero);

    public static Marge operator +(Marge a, Marge b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new Marge(a.Omzet + b.Omzet, a.Inkoop + b.Inkoop);
    }

    public Marge Add(Marge ander) => this + ander;
}

/// <summary>
/// Brutomarge per margegroep (bijv. "Product X"): een resultaatrekening met een <see cref="Rekening.Margegroep"/> telt
/// als omzet als zijn RGS-code met <c>WOmz</c> begint (netto-omzet), anders als inkoop (kostprijs van die omzet).
/// Rekeningen zonder groep tellen niet mee.
/// </summary>
public static class Marges
{
    public static bool IsOmzet(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        return rekening.RgsCode?.StartsWith(Maandcijfers.Omzet, StringComparison.Ordinal) == true;
    }

    /// <summary>Of een rekening een margegroep kan hebben: alleen resultaatrekeningen.</summary>
    public static bool KanGroepHebben(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        return !rekening.IsBalans;
    }

    /// <summary>De marge per groep uit de mutaties per rekening (debet positief), op naam van de groep.</summary>
    public static IReadOnlyDictionary<string, Marge> PerGroep(IEnumerable<(Rekening Rekening, decimal Bedrag)> mutaties)
    {
        ArgumentNullException.ThrowIfNull(mutaties);
        var groepen = new SortedDictionary<string, Marge>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (rekening, bedrag) in mutaties)
        {
            if (rekening.IsBalans || string.IsNullOrWhiteSpace(rekening.Margegroep))
            {
                continue;
            }

            var deel = IsOmzet(rekening) ? new Marge(-bedrag, 0m) : new Marge(0m, bedrag);
            groepen[rekening.Margegroep] = groepen.GetValueOrDefault(rekening.Margegroep, Marge.Nul) + deel;
        }

        return groepen;
    }

    /// <summary>De marge per groep over de maanden <paramref name="vanaf"/> tot en met <paramref name="tot"/> (indexen) van een maandoverzicht.</summary>
    public static IReadOnlyDictionary<string, Marge> PerGroep(Maandoverzicht overzicht, int vanaf, int tot)
    {
        ArgumentNullException.ThrowIfNull(overzicht);
        return PerGroep(overzicht.Regels.Select(r => (r.Rekening, r.PerMaand.Skip(vanaf).Take(tot - vanaf + 1).Sum())));
    }
}
