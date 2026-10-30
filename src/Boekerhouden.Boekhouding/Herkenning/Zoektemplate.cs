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

    /// <summary>Het totaal inclusief btw.</summary>
    Totaal,

    /// <summary>Het totaal exclusief btw (bij één btw-tarief ook de grondslag).</summary>
    Exclusief,

    /// <summary>De btw (bij één btw-tarief).</summary>
    Btw,
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
/// het pakket waar bijv. het factuurnummer staat bij een leverancier met een ongewone opmaak. Bedragen van een
/// aangewezen plek gaan alleen voor als de herkende bedragen niet kloppen en het aangewezen bedrag wel bij de btw past:
/// een factuur met een langere specificatie kan de bedragen een stukje lager hebben staan.
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
        var klopt = Klopt(herkend);
        foreach (var (gebied, tekst) in teksten)
        {
            var bijgewerkt = gebied.Veld switch
            {
                Factuurveld.Factuurnummer when Factuurnummer(tekst) is { } nummer && (factuur.Factuurnummer is null || gebied.Voorrang) => factuur with { Factuurnummer = nummer },
                Factuurveld.Factuurdatum when Datum(tekst) is { } d && (factuur.Factuurdatum is null || gebied.Voorrang) => factuur with { Factuurdatum = d },
                Factuurveld.Vervaldatum when Datum(tekst) is { } d && (factuur.Vervaldatum is null || gebied.Voorrang) => factuur with { Vervaldatum = d },
                Factuurveld.Betalingskenmerk when Kenmerk(tekst) is { } k && (factuur.Betalingskenmerk is null || gebied.Voorrang) => factuur with { Betalingskenmerk = k },
                Factuurveld.Omschrijving when Tekst(tekst) is { } o && (factuur.Omschrijving is null || gebied.Voorrang) => factuur with { Omschrijving = o },
                Factuurveld.Totaal when Bedrag(tekst) is { } t && (factuur.TotaalInclusief is null || (gebied.Voorrang && !klopt && PastBijBtw(factuur, t - factuur.TotaalBtw)))
                    => factuur with { TotaalInclusief = t },
                Factuurveld.Exclusief when Bedrag(tekst) is { } e && (factuur.TotaalExclusief is null || (gebied.Voorrang && !klopt && PastBijBtw(factuur, e))) => factuur with
                {
                    TotaalExclusief = e,
                    Btw = factuur.Btwregels is [var enige] ? [enige with { Grondslag = e }] : factuur.Btw,
                },
                Factuurveld.Btw when Bedrag(tekst) is { } b && factuur.Btwregels.Count <= 1 && (factuur.Btwregels.Count == 0 || (gebied.Voorrang && !klopt))
                    && (factuur.Btwregels is [var regel] ? regel.Percentage : Tarief(factuur.TotaalExclusief, b)) is { } percentage
                    => factuur with { Btw = [new Btwregel(percentage, factuur.Btwregels is [var r] ? r.Grondslag : factuur.TotaalExclusief, b)] },
                _ => null,
            };
            if (bijgewerkt is not null && bijgewerkt != factuur)
            {
                factuur = bijgewerkt;
                ingevuld.Add(gebied.Veld);
            }
        }

        // Kwam maar één van de twee totalen van een aangewezen plek, dan volgt het andere uit de btw.
        if (factuur.Btwregels.Count > 0 && ingevuld.Contains(Factuurveld.Exclusief) != ingevuld.Contains(Factuurveld.Totaal) && !Klopt(factuur))
        {
            factuur = ingevuld.Contains(Factuurveld.Exclusief)
                ? factuur with { TotaalInclusief = factuur.TotaalExclusief + factuur.TotaalBtw }
                : factuur with
                {
                    TotaalExclusief = factuur.TotaalInclusief - factuur.TotaalBtw,
                    Btw = factuur.Btwregels is [var enige] ? [enige with { Grondslag = factuur.TotaalInclusief - factuur.TotaalBtw }] : factuur.Btw,
                };
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

    /// <summary>Het (laatste) bedrag in een aangewezen tekst, bijv. "Totaal (excl. BTW)  €135,52" wordt 135,52.</summary>
    private static decimal? Bedrag(string tekst) => Factuurtekst.Bedragen(tekst) is [.., var b] ? b : null;

    /// <summary>
    /// Kloppen de herkende bedragen: exclusief plus btw is het totaal, en bij één tarief is de btw dat percentage van
    /// het exclusieve bedrag? (Een totaal uit een specificatie met het exclusieve bedrag daaruit teruggerekend, sluit
    /// wel, maar past niet bij het tarief.)
    /// </summary>
    private static bool Klopt(HerkendeFactuur f) => Sluitend(f) && PastBijBtw(f, f.TotaalExclusief!.Value);

    /// <summary>Past een exclusief bedrag bij de btw van de factuur (bij één tarief; anders valt er niets na te gaan)?</summary>
    private static bool PastBijBtw(HerkendeFactuur f, decimal exclusief) =>
        f.Btwregels is not [var enige] || Btw.BtwBerekening.Klopt(exclusief, enige.Btw, enige.Percentage, 5);

    /// <summary>Sluiten de herkende bedragen: exclusief plus btw is het totaal?</summary>
    private static bool Sluitend(HerkendeFactuur f) =>
        f.TotaalExclusief is { } e && f.TotaalInclusief is { } i && Math.Abs(e + f.TotaalBtw - i) <= 0.01m * Math.Max(1, f.Btwregels.Count);

    /// <summary>Het Nederlandse tarief (21%, 9% of 0%) dat bij de btw over het bedrag past, of leeg.</summary>
    private static decimal? Tarief(decimal? exclusief, decimal btw) =>
        exclusief is { } e && e != 0 ? Tarieven.Cast<decimal?>().FirstOrDefault(p => Btw.BtwBerekening.Klopt(e, btw, p!.Value, 5)) : null;

    private static readonly decimal[] Tarieven = [21m, 9m, 0m];

    private static string? Tekst(string tekst)
    {
        var schoon = MeerdereSpaties().Replace(tekst, " ").Trim();
        return schoon.Length == 0 ? null : schoon.Length > 200 ? schoon[..200] : schoon;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MeerdereSpaties();
}
