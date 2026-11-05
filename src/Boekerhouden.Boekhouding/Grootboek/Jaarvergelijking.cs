namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Eén rekening in de vergelijking met vorig jaar: het saldo nu en een jaar eerder (debet positief).</summary>
public sealed record Vergelijkingsregel(Rekening Rekening, decimal Huidig, decimal Vorig)
{
    public decimal? Procent => Jaarvergelijking.Procent(Huidig, Vorig);
}

/// <summary>
/// De kolommenbalans van een periode naast die van dezelfde periode een jaar eerder: per rekening beide saldi en het
/// verschil in procenten.
/// </summary>
public static class Jaarvergelijking
{
    /// <summary>De rekeningen van beide periodes (ook die maar in één voorkomt), in de volgorde van het rekeningnummer.</summary>
    public static IReadOnlyList<Vergelijkingsregel> Maak(Kolommenbalans huidig, Kolommenbalans vorig)
    {
        ArgumentNullException.ThrowIfNull(huidig);
        ArgumentNullException.ThrowIfNull(vorig);
        var nu = huidig.Regels.ToDictionary(r => r.Rekening.Nummer, StringComparer.Ordinal);
        var toen = vorig.Regels.ToDictionary(r => r.Rekening.Nummer, StringComparer.Ordinal);
        return
        [
            .. nu.Keys.Union(toen.Keys, StringComparer.Ordinal)
                .Select(n => new Vergelijkingsregel(
                    nu.TryGetValue(n, out var h) ? h.Rekening : toen[n].Rekening,
                    h?.Saldo ?? 0m,
                    toen.TryGetValue(n, out var v) ? v.Saldo : 0m))
                .OrderBy(r => r.Rekening.Nummer, StringComparer.Ordinal),
        ];
    }

    /// <summary>Het resultaat van een jaar in de vergelijking: winst positief (de saldi van de resultaatrekeningen, omgekeerd).</summary>
    public static (decimal Huidig, decimal Vorig) Resultaat(IEnumerable<Vergelijkingsregel> regels)
    {
        var resultaat = regels.Where(r => !r.Rekening.IsBalans).ToList();
        return (-resultaat.Sum(r => r.Huidig), -resultaat.Sum(r => r.Vorig));
    }

    /// <summary>De kop van een jaarkolom: het jaar bij een heel kalenderjaar (2026), anders de periode (01-01 t/m 30-06-2026).</summary>
    public static string Kolomkop(DateOnly van, DateOnly tot) =>
        van.Day == 1 && van.Month == 1 && tot == new DateOnly(van.Year, 12, 31)
            ? van.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{van.ToString("dd-MM", System.Globalization.CultureInfo.InvariantCulture)} t/m {tot.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Hoeveel het bedrag groter of kleiner werd, in procenten van vorig jaar en op één decimaal: omzet van 1.000 naar
    /// 1.200 credit is +20%, kosten van 500 naar 400 debet is −20%. Leeg als vorig jaar 0 was of het saldo van kant
    /// wisselde (debet werd credit): dan zegt een percentage niets.
    /// </summary>
    public static decimal? Procent(decimal huidig, decimal vorig)
    {
        if (vorig == 0 || (huidig != 0 && Math.Sign(huidig) != Math.Sign(vorig)))
        {
            return null;
        }

        return Math.Round((Math.Abs(huidig) - Math.Abs(vorig)) / Math.Abs(vorig) * 100m, 1, MidpointRounding.AwayFromZero);
    }
}
