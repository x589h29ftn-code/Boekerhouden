using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Facturen;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>Eén pagina uit een stapel (een PDF met veel facturen achter elkaar).</summary>
/// <param name="Regels">
/// De tekst van de pagina in regels; <c>null</c> als de tekst nog niet bekend is (een scan die nog niet door de
/// tekstherkenning is geweest), leeg als de pagina echt leeg is.
/// </param>
public sealed record Stapelpagina(int Nummer, IReadOnlyList<string>? Regels);

/// <summary>Het voorstel voor één pagina van de stapel.</summary>
/// <param name="Begin">Hier begint een nieuwe factuur (er staat een knip vóór deze pagina).</param>
/// <param name="Twijfel">Het pakket is niet zeker van de keuze (knip of geen knip) vóór deze pagina.</param>
/// <param name="Leeg">Een (bijna) lege pagina: voorstel om hem weg te laten.</param>
/// <param name="Reden">Waarom, in gewone taal (voor de tooltip), bijv. "ander factuurnummer; pagina 1 van 3".</param>
/// <param name="Factuurnummer">Het factuurnummer dat op deze pagina is gevonden.</param>
/// <param name="Naam">De naam van de afzender, als die op deze pagina is herkend.</param>
public sealed record Paginavoorstel(int Nummer, bool Begin, bool Twijfel, bool Leeg, string? Reden, string? Factuurnummer = null, string? Naam = null);

/// <summary>
/// Stelt voor waar in een stapel de ene factuur ophoudt en de volgende begint. Per overgang tussen twee pagina's worden
/// aanwijzingen opgeteld: paginanummering ("pagina 2 van 3"), een ander of hetzelfde factuurnummer, een andere afzender,
/// een factuurkop bovenaan, een totaal onderaan de vorige pagina, "vervolg" of "transport". Een hoge score geeft een
/// knip, een lage geen knip; daartussen een keuze met twijfel, zodat de gebruiker alleen die hoeft na te lopen.
/// </summary>
public static partial class Stapelsplitser
{
    /// <summary>Vanaf deze score een knip waar het pakket zeker van is.</summary>
    private const int Zeker = 3;

    /// <summary>Minder woorden dan dit: een lege pagina (scheidingsblad, achterkant).</summary>
    private const int Leegwoorden = 4;

    private sealed record Kenmerken(
        int Nummer, bool Onbekend, bool Leeg, int? Blad, int? Van, bool Vervolg, string? Factuurnummer, string? Afzender, string? Naam, bool Kop, bool Totaal, bool Bedragen);

    public static IReadOnlyList<Paginavoorstel> Stel(IReadOnlyList<Stapelpagina> paginas, Factuursoort soort, IEnumerable<string>? eigenIbans = null, IEnumerable<string>? eigenBtwNummers = null)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        var (ibans, btw) = (eigenIbans?.ToList() ?? [], eigenBtwNummers?.ToList() ?? []);
        var kenmerken = paginas.Select(p => Lees(p, soort, ibans, btw)).ToList();
        var voorstel = new List<Paginavoorstel>(paginas.Count);
        Kenmerken? vorige = null;
        var leegErtussen = false;
        foreach (var k in kenmerken)
        {
            if (k.Leeg)
            {
                // Een leeg blad hoort nergens bij: weglaten; de keuze schuift door naar de volgende pagina.
                voorstel.Add(new Paginavoorstel(k.Nummer, Begin: vorige is null, Twijfel: false, Leeg: true, "lege pagina"));
                leegErtussen = vorige is not null;
                continue;
            }

            if (vorige is null)
            {
                // De eerste pagina met inhoud begint altijd een factuur (ook na lege bladen vooraan).
                voorstel.Add(new Paginavoorstel(k.Nummer, Begin: true, Twijfel: false, Leeg: false, null, k.Factuurnummer, k.Naam));
            }
            else
            {
                var (score, redenen) = Score(vorige, k, soort, leegErtussen);
                var begin = score > 0 || (score == 0 && (k.Onbekend || vorige.Onbekend));
                var twijfel = score is > -2 and < Zeker;
                voorstel.Add(new Paginavoorstel(k.Nummer, begin, twijfel, Leeg: false, redenen.Count == 0 ? null : string.Join("; ", redenen), k.Factuurnummer, k.Naam));
            }

            (vorige, leegErtussen) = (k, false);
        }

        return voorstel;
    }

    private static (int Score, List<string> Redenen) Score(Kenmerken vorige, Kenmerken deze, Factuursoort soort, bool leegErtussen)
    {
        var redenen = new List<string>();
        var score = 0;
        void Tel(int punten, string reden)
        {
            score += punten;
            redenen.Add(reden);
        }

        if (deze.Onbekend || vorige.Onbekend)
        {
            // Zonder tekst is er niets te vergelijken: elke pagina een eigen factuur, met twijfel.
            return (leegErtussen ? 2 : 0, ["tekst nog niet gelezen"]);
        }

        if (leegErtussen)
        {
            Tel(2, "leeg blad ertussen");
        }

        if (deze.Blad is { } blad)
        {
            var van = deze.Van is { } n ? $" van {n}" : null;
            Tel(blad == 1 ? 4 : -5, $"pagina {blad}{van}");
        }

        if (vorige is { Blad: { } vorigBlad, Van: { } vorigVan })
        {
            Tel(vorigBlad >= vorigVan ? 3 : -3, vorigBlad >= vorigVan ? "vorige pagina is de laatste" : $"vorige pagina is {vorigBlad} van {vorigVan}");
        }

        if (deze.Vervolg)
        {
            Tel(-3, "vervolg van de vorige pagina");
        }

        if (deze.Factuurnummer is { } nummer && vorige.Factuurnummer is { } vorigNummer)
        {
            var zelfde = string.Equals(nummer, vorigNummer, StringComparison.OrdinalIgnoreCase);
            Tel(zelfde ? -5 : 4, zelfde ? $"zelfde factuurnummer {nummer}" : $"ander factuurnummer ({nummer})");
        }
        else if (deze.Factuurnummer is not null)
        {
            Tel(1, $"factuurnummer {deze.Factuurnummer}");
        }

        // Bij verkoop is de afzender steeds de eigen onderneming: dat zegt niets.
        if (soort == Factuursoort.Inkoop && deze.Afzender is { } afzender && vorige.Afzender is { } vorigeAfzender && afzender != vorigeAfzender)
        {
            Tel(3, "andere afzender");
        }

        if (deze.Kop)
        {
            Tel(2, "factuurkop bovenaan");
        }

        if (vorige.Totaal)
        {
            Tel(2, "totaal op de vorige pagina");
        }

        if (!deze.Kop && !deze.Bedragen && deze.Factuurnummer is null)
        {
            Tel(-3, "geen bedragen (bijlage of voorwaarden)");
        }

        return (score, redenen);
    }

    private static Kenmerken Lees(Stapelpagina pagina, Factuursoort soort, List<string> eigenIbans, List<string> eigenBtw)
    {
        if (pagina.Regels is not { } regels)
        {
            return new Kenmerken(pagina.Nummer, Onbekend: true, Leeg: false, null, null, false, null, null, null, false, false, false);
        }

        var woorden = regels.Sum(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (woorden < Leegwoorden)
        {
            return new Kenmerken(pagina.Nummer, Onbekend: false, Leeg: true, null, null, false, null, null, null, false, false, false);
        }

        var tekst = string.Join('\n', regels);
        var herkend = soort == Factuursoort.Inkoop ? Factuurtekst.Analyseer(tekst, Herkenningsbron.Tekst, eigenIbans, eigenBtw) : Factuurtekst.Analyseer(tekst);
        var (blad, van) = Paginering(regels);
        var bovenaan = regels.Take(15).ToList();
        var kop = bovenaan.Any(r => Kopwoord().IsMatch(r)) && (herkend.Factuurdatum is not null || herkend.Factuurnummer is not null);
        var totaal = regels.Any(r => Totaalwoord().IsMatch(r) && Factuurtekst.Bedragen(r).Count > 0);
        var bedragen = regels.Any(r => Factuurtekst.Bedragen(r).Count > 0);
        var vervolg = bovenaan.Take(6).Any(r => Vervolgwoord().IsMatch(r));
        return new Kenmerken(pagina.Nummer, false, false, blad, van, vervolg, Leeg(herkend.Factuurnummer),
            Leeg(herkend.Iban) ?? Leeg(herkend.BtwNummer) ?? Leeg(herkend.KvkNummer), Leeg(herkend.Naam), kop, totaal, bedragen);
    }

    /// <summary>"Pagina 2 van 3", "blad 1/4", "page 2 of 2" of alleen "pagina 2".</summary>
    private static (int? Blad, int? Van) Paginering(IReadOnlyList<string> regels)
    {
        foreach (var regel in regels)
        {
            if (BladVan().Match(regel) is { Success: true } m
                && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var blad and > 0
                && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) is var van && blad <= van)
            {
                return (blad, van);
            }
        }

        foreach (var regel in regels)
        {
            if (AlleenBlad().Match(regel) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var blad and > 0)
            {
                return (blad, null);
            }
        }

        return (null, null);
    }

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    /// <summary>
    /// De groepen (facturen) die uit de knippen volgen: elke groep begint bij een pagina met een knip en loopt tot de
    /// volgende; weggelaten pagina's doen niet mee, een groep zonder pagina's vervalt.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Groepen(int paginas, IReadOnlySet<int> begin, IReadOnlySet<int> weggelaten)
    {
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(weggelaten);
        var groepen = new List<IReadOnlyList<int>>();
        var huidig = new List<int>();
        for (var p = 1; p <= paginas; p++)
        {
            if (begin.Contains(p) && huidig.Count > 0)
            {
                groepen.Add(huidig);
                huidig = [];
            }

            if (!weggelaten.Contains(p))
            {
                huidig.Add(p);
            }
        }

        if (huidig.Count > 0)
        {
            groepen.Add(huidig);
        }

        return groepen;
    }

    [GeneratedRegex(@"\b(?:pagina|blad|page|pag\.?|blz\.?)\s*:?\s*(\d{1,3})\s*(?:van|of|/|-)\s*(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BladVan();

    [GeneratedRegex(@"\b(?:pagina|blad|page|blz\.?)\s*:?\s*(\d{1,3})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlleenBlad();

    [GeneratedRegex(@"\b(?:factuur|creditfactuur|creditnota|invoice|nota|rekening|declaratie|kassabon)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Kopwoord();

    [GeneratedRegex(@"\b(?:totaal|te\s+betalen|te\s+voldoen|total|amount\s+due|eindbedrag)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Totaalwoord();

    [GeneratedRegex(@"\b(?:vervolg|transport|vervolgblad|continued)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Vervolgwoord();
}
