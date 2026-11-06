namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// De kerncijfers van één maand, als positieve bedragen zoals een ondernemer ze leest: omzet, inkoop (kostprijs van
/// de omzet), de overige kosten (alles daarna, ook financiële baten en lasten, per saldo) en het resultaat (winst
/// positief). Omzet min inkoop min overige kosten is het resultaat.
/// </summary>
public sealed record Maandcijfer(DateOnly Maand, decimal Omzet, decimal Inkoop, decimal OverigeKosten, decimal Resultaat)
{
    /// <summary>Inkoop en overige kosten samen.</summary>
    public decimal Kosten => Inkoop + OverigeKosten;
}

/// <summary>Omzet, inkoop en resultaat per maand uit het <see cref="Maandoverzicht"/>, ingedeeld op de RGS-code.</summary>
public static class Maandcijfers
{
    /// <summary>RGS-hoofdpost van de netto-omzet.</summary>
    public const string Omzet = "WOmz";

    /// <summary>RGS-hoofdpost van de kostprijs van de omzet (inkoopwaarde, uitbesteed werk).</summary>
    public const string Inkoop = "WKpr";

    public static IReadOnlyList<Maandcijfer> Uit(Maandoverzicht overzicht)
    {
        ArgumentNullException.ThrowIfNull(overzicht);
        var resultaat = overzicht.ResultaatPerMaand;
        return
        [
            .. overzicht.Maanden.Select((maand, i) =>
            {
                var omzet = -Som(overzicht, Omzet, i);
                var inkoop = Som(overzicht, Inkoop, i);
                return new Maandcijfer(maand, omzet, inkoop, omzet - inkoop - resultaat[i], resultaat[i]);
            }),
        ];
    }

    private static decimal Som(Maandoverzicht overzicht, string hoofdpost, int maand) =>
        overzicht.Regels.Where(r => r.Rekening.RgsCode?.StartsWith(hoofdpost, StringComparison.Ordinal) == true).Sum(r => r.PerMaand[maand]);
}
