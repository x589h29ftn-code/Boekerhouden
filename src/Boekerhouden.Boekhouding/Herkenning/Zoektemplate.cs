using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>De velden van een factuur die de gebruiker op de factuur kan aanwijzen en die het pakket per leverancier onthoudt.</summary>
public enum Factuurveld
{
    Factuurnummer,
    Factuurdatum,
    Vervaldatum,
    Betalingskenmerk,
    Omschrijving,
}

/// <summary>
/// Waar op de factuur van een leverancier een veld staat (pagina en rechthoek in PDF-punten), zoals de gebruiker het
/// aanwees. <paramref name="Voorrang"/>: de herkenning vond er iets anders dan de gebruiker aanwees, dus het aangewezen
/// gebied gaat voortaan voor; anders vult het alleen aan wat de herkenning niet vindt.
/// </summary>
public sealed record Zoekgebied(Factuurveld Veld, int Pagina, double X0, double Y0, double X1, double Y1, bool Voorrang = false);

/// <summary>
/// Het zoektemplate van een leverancier: de tekst op de aangewezen plekken vult aan wat de herkenning op een volgende
/// factuur van die leverancier niet vindt (of gaat voor, als de gebruiker de herkenning eerder corrigeerde). Zo leert
/// het pakket waar bijv. het factuurnummer staat bij een leverancier met een ongewone opmaak.
/// </summary>
public static partial class Zoektemplate
{
    /// <summary>Past de teksten uit de gebieden toe; geeft de factuur en de velden die het template invulde.</summary>
    public static (HerkendeFactuur Factuur, IReadOnlyList<Factuurveld> Ingevuld) Pas(HerkendeFactuur herkend, IEnumerable<(Zoekgebied Gebied, string Tekst)> teksten)
    {
        ArgumentNullException.ThrowIfNull(herkend);
        ArgumentNullException.ThrowIfNull(teksten);
        var factuur = herkend;
        var ingevuld = new List<Factuurveld>();
        foreach (var (gebied, tekst) in teksten)
        {
            var bijgewerkt = gebied.Veld switch
            {
                Factuurveld.Factuurnummer when Factuurnummer(tekst) is { } nummer && (factuur.Factuurnummer is null || gebied.Voorrang) => factuur with { Factuurnummer = nummer },
                Factuurveld.Factuurdatum when Datum(tekst) is { } d && (factuur.Factuurdatum is null || gebied.Voorrang) => factuur with { Factuurdatum = d },
                Factuurveld.Vervaldatum when Datum(tekst) is { } d && (factuur.Vervaldatum is null || gebied.Voorrang) => factuur with { Vervaldatum = d },
                Factuurveld.Betalingskenmerk when Kenmerk(tekst) is { } k && (factuur.Betalingskenmerk is null || gebied.Voorrang) => factuur with { Betalingskenmerk = k },
                Factuurveld.Omschrijving when Tekst(tekst) is { } o && (factuur.Omschrijving is null || gebied.Voorrang) => factuur with { Omschrijving = o },
                _ => null,
            };
            if (bijgewerkt is not null && bijgewerkt != factuur)
            {
                factuur = bijgewerkt;
                ingevuld.Add(gebied.Veld);
            }
        }

        return (factuur, ingevuld);
    }

    /// <summary>
    /// Het factuurnummer uit een aangewezen tekst, zonder label: "Factuurnummer: 2026-0412" en "Ref. 2026-0412" worden
    /// "2026-0412" (woorden zonder cijfers vooraan vallen weg).
    /// </summary>
    public static string? Factuurnummer(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var waarde = tekst.Contains(':', StringComparison.Ordinal) ? tekst[(tekst.LastIndexOf(':') + 1)..] : tekst;
        waarde = string.Join(' ', waarde.Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipWhile(w => !w.Any(char.IsAsciiDigit)));
        if (waarde.Length == 0 || !waarde.Any(char.IsAsciiDigit))
        {
            return null;
        }

        return waarde.Length > 50 ? waarde[..50].Trim() : waarde;
    }

    /// <summary>Het betalingskenmerk: de cijfers als het er genoeg zijn (16 cijfers met spaties), anders de tekst.</summary>
    public static string? Kenmerk(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var cijfers = string.Concat(tekst.Where(char.IsAsciiDigit));
        return cijfers.Length >= 7 ? cijfers : Tekst(tekst.Contains(':', StringComparison.Ordinal) ? tekst[(tekst.LastIndexOf(':') + 1)..] : tekst);
    }

    private static DateOnly? Datum(string tekst) => Factuurtekst.Datums(tekst) is [var d, ..] ? d : null;

    private static string? Tekst(string tekst)
    {
        var schoon = MeerdereSpaties().Replace(tekst, " ").Trim();
        return schoon.Length == 0 ? null : schoon.Length > 200 ? schoon[..200] : schoon;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MeerdereSpaties();
}
