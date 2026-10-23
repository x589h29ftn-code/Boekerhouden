namespace Boekerhouden.Domain;

/// <summary>
/// Afronden op hele euro's, zoals in de jaarrekening. Het afrondingsverschil wordt verdeeld over de bedragen met de
/// grootste afrondingsrest, zodat de som van de afgeronde bedragen gelijk is aan de afgeronde som van de exacte
/// bedragen. Een saldibalans met saldo nihil blijft zo na afronden ook nihil, en alle subtotalen sluiten.
/// </summary>
public static class Afronding
{
    public static decimal OpHeleEuro(decimal bedrag) => Math.Round(bedrag, 0, MidpointRounding.AwayFromZero);

    public static Dictionary<TSleutel, decimal> OpHeleEuros<TSleutel>(IEnumerable<KeyValuePair<TSleutel, decimal>> bedragen)
        where TSleutel : notnull
    {
        ArgumentNullException.ThrowIfNull(bedragen);
        var lijst = bedragen.ToList();
        var afgerond = lijst.ToDictionary(b => b.Key, b => OpHeleEuro(b.Value));
        var verschil = OpHeleEuro(lijst.Sum(b => b.Value)) - afgerond.Values.Sum();
        if (verschil == 0 || lijst.Count == 0)
        {
            return afgerond;
        }

        // Moet er bij (verschil > 0), dan eerst de bedragen die het meest naar beneden zijn afgerond, en omgekeerd.
        // Bij gelijke rest gaat het grootste bedrag voor; daar valt één euro het minst op.
        var stap = Math.Sign(verschil);
        var volgorde = lijst
            .OrderByDescending(b => stap * (b.Value - afgerond[b.Key]))
            .ThenByDescending(b => Math.Abs(b.Value))
            .ToList();
        for (var i = 0; i < Math.Abs(verschil); i++)
        {
            afgerond[volgorde[i % volgorde.Count].Key] += stap;
        }

        return afgerond;
    }
}
