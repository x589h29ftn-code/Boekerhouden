using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Beginbalans;

public enum Balanszijde
{
    Activa,
    Passiva,
}

/// <summary>Een post van de balans in een jaarrekening, met het bedrag zoals het er staat (een schuld positief).</summary>
/// <param name="Rubriek">De kop erboven, bijv. "Langlopende schulden"; helpt bij het kiezen van de rekening.</param>
public sealed record Balanspost(Balanszijde Zijde, string Rubriek, string Naam, decimal Bedrag)
{
    /// <summary>Het bedrag in de boeking: activa debet (positief), passiva credit (negatief).</summary>
    public decimal Boekbedrag => Zijde == Balanszijde.Activa ? Bedrag : -Bedrag;
}

/// <summary>De balans uit een jaarrekening: de balansdatum, de posten en de totalen zoals ze er staan.</summary>
public sealed record Jaarrekeningbalans(DateOnly Balansdatum, IReadOnlyList<Balanspost> Posten, decimal? TotaalActiva, decimal? TotaalPassiva)
{
    public decimal Activa => Posten.Where(p => p.Zijde == Balanszijde.Activa).Sum(p => p.Bedrag);

    public decimal Passiva => Posten.Where(p => p.Zijde == Balanszijde.Passiva).Sum(p => p.Bedrag);

    /// <summary>Wat niet klopt: de posten tellen niet op tot het totaal, of activa en passiva zijn niet gelijk.</summary>
    public IReadOnlyList<string> Controle()
    {
        var signalen = new List<string>();
        if (TotaalActiva is { } ta && ta != Activa)
        {
            signalen.Add($"De posten aan de activazijde tellen op tot {Domain.Bedragen.Euro(Activa)}, het totaal is {Domain.Bedragen.Euro(ta)}.");
        }

        if (TotaalPassiva is { } tp && tp != Passiva)
        {
            signalen.Add($"De posten aan de passivazijde tellen op tot {Domain.Bedragen.Euro(Passiva)}, het totaal is {Domain.Bedragen.Euro(tp)}.");
        }

        if (Activa != Passiva)
        {
            signalen.Add($"Activa ({Domain.Bedragen.Euro(Activa)}) en passiva ({Domain.Bedragen.Euro(Passiva)}) zijn niet gelijk.");
        }

        return signalen;
    }
}

/// <summary>
/// Leest de balans uit de tekst van een jaarrekening (regels zoals uit een PDF): de kop "Balans per 31 december 2024",
/// dan de activa en de passiva. Een post is een regel met een naam en daarachter bedragen; het eerste bedrag is dat
/// van het boekjaar (de kolom van het vorige jaar staat rechts). Een regel met alleen bedragen is een subtotaal, een
/// regel zonder bedragen een kop, en "Totaal" het totaal van een zijde.
/// </summary>
public static partial class Jaarrekeninglezer
{
    private static readonly CultureInfo Nl = new("nl-NL");

    public static Jaarrekeningbalans? Lees(IReadOnlyList<string> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        // De laatste kop "Balans per ..." met kort daarna "Activa": de inhoudsopgave noemt de balans ook.
        for (var i = regels.Count - 1; i >= 0; i--)
        {
            if (Balanskop().Match(regels[i]) is { Success: true } kop
                && Datum(kop) is { } datum
                && regels.Skip(i + 1).Take(12).Any(r => Zijdekop(r) == Balanszijde.Activa))
            {
                return LeesVanaf(regels, i + 1, datum);
            }
        }

        return null;
    }

    private static Jaarrekeningbalans? LeesVanaf(IReadOnlyList<string> regels, int start, DateOnly datum)
    {
        var posten = new List<Balanspost>();
        Balanszijde? zijde = null;
        var rubriek = string.Empty;
        decimal? totaalActiva = null, totaalPassiva = null;
        for (var i = start; i < regels.Count; i++)
        {
            var regel = regels[i].Trim();
            if (Zijdekop(regel) is { } nieuw)
            {
                (zijde, rubriek) = (nieuw, string.Empty);
                continue;
            }

            if (zijde is null || regel.Length == 0)
            {
                continue;
            }

            if (Ontleed(regel) is not var (naam, bedragen))
            {
                continue;
            }

            if (bedragen.Count == 0)
            {
                // Een kop, als er letters in staan (niet "€ €").
                if (naam.Count(char.IsLetter) >= 3 && !naam.Contains("resultaatbestemming", StringComparison.OrdinalIgnoreCase))
                {
                    rubriek = naam;
                }

                continue;
            }

            if (naam.Length == 0)
            {
                continue;
            }

            if (Totaalregel().IsMatch(naam))
            {
                if (zijde == Balanszijde.Activa)
                {
                    totaalActiva ??= bedragen[0];
                }
                else
                {
                    totaalPassiva ??= bedragen[0];
                    break;
                }

                continue;
            }

            // Een tussentelling met een naam ("Totaal vlottende activa") telt niet als post.
            if (naam.StartsWith("Totaal", StringComparison.OrdinalIgnoreCase) || naam.StartsWith("Subtotaal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (bedragen[0] != 0)
            {
                posten.Add(new Balanspost(zijde.Value, rubriek, naam, bedragen[0]));
            }
        }

        return posten.Count == 0 ? null : new Jaarrekeningbalans(datum, posten, totaalActiva, totaalPassiva);
    }

    /// <summary>Naam en bedragen van een regel; leeg als er na de bedragen nog tekst staat (bijv. een datumkop).</summary>
    private static (string Naam, List<decimal> Bedragen)? Ontleed(string regel)
    {
        var delen = regel.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var naam = new List<string>();
        var bedragen = new List<decimal>();
        foreach (var deel in delen)
        {
            if (Bedrag(deel) is { } bedrag)
            {
                bedragen.Add(bedrag);
            }
            else if (deel == "€" && bedragen.Count == 0)
            {
                continue;
            }
            else if (bedragen.Count > 0)
            {
                return null;
            }
            else
            {
                naam.Add(deel);
            }
        }

        return (string.Join(' ', naam), bedragen);
    }

    /// <summary>Een bedrag in de opmaak van een jaarrekening: 1.234, -1.234, (1.234), 1.234,56 of "-" voor nul.</summary>
    public static decimal? Bedrag(string tekst)
    {
        if (tekst is "-" or "–")
        {
            return 0m;
        }

        if (Bedragpatroon().Match(tekst) is not { Success: true } m)
        {
            return null;
        }

        var getal = decimal.Parse(m.Groups["getal"].Value.Replace(".", string.Empty, StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, Nl);
        return m.Groups["min"].Success || m.Groups["haak"].Success ? -getal : getal;
    }

    private static Balanszijde? Zijdekop(string regel) => regel.Trim().ToUpperInvariant() switch
    {
        "ACTIVA" => Balanszijde.Activa,
        "PASSIVA" => Balanszijde.Passiva,
        _ => null,
    };

    private static DateOnly? Datum(Match kop)
    {
        var maand = Array.FindIndex(Nl.DateTimeFormat.MonthNames, m => m.Length > 0 && string.Equals(m, kop.Groups["maand"].Value, StringComparison.OrdinalIgnoreCase)) + 1;
        var dag = int.Parse(kop.Groups["dag"].Value, CultureInfo.InvariantCulture);
        var jaar = int.Parse(kop.Groups["jaar"].Value, CultureInfo.InvariantCulture);
        return maand > 0 && dag <= DateTime.DaysInMonth(jaar, maand) ? new DateOnly(jaar, maand, dag) : null;
    }

    [GeneratedRegex(@"\bbalans\s+per\s+(?<dag>\d{1,2})\s+(?<maand>[a-z]+)\s+(?<jaar>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex Balanskop();

    [GeneratedRegex(@"^(?<min>[-–])?(?<haak>\()?(?<getal>\d{1,3}(\.\d{3})*(,\d{1,2})?)\)?$")]
    private static partial Regex Bedragpatroon();

    [GeneratedRegex(@"^totaal( activa| passiva)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Totaalregel();
}

/// <summary>
/// Kiest bij een balanspost de rekening uit het rekeningschema: eerst een rekening met (bijna) dezelfde naam, dan op
/// trefwoorden en de rubriek naar een RGS-groep die in het schema voorkomt. Geen keuze als niets past: dan kiest de
/// gebruiker (of maakt hij de rekening aan).
/// </summary>
public static class Balansrekeningkeuze
{
    // Trefwoord in de naam of de rubriek → het begin van de RGS-code (groepen uit RGS 3.8).
    private static readonly (string Trefwoord, string Rgs)[] Trefwoorden =
    [
        ("rekening-courant", "BVorOvrRcb"),
        ("aandelenkapitaal", "BEivGok"),
        ("geplaatst kapitaal", "BEivGok"),
        ("overige reserves", "BEivOvr"),
        ("algemene reserve", "BEivOvr"),
        ("ondernemingsvermogen", "BEivKap"),
        ("kas", "BLimKas"),
        ("liquide middelen", "BLimBan"),
        ("bank", "BLimBan"),
        ("debiteuren", "BVorDeb"),
        ("omzetbelasting", "BVorVbk"),
        ("vooruitbetaalde", "BVorOva"),
        ("overlopende activa", "BVorOva"),
        ("crediteuren", "BSchCre"),
        ("leveranciers", "BSchCre"),
        ("overlopende passiva", "BSchOpa"),
        ("nog te betalen", "BSchOpa"),
        ("belastingen en premies", "BSchBep"),
        ("deelneming", "BFva"),
        ("financiële vaste activa", "BFva"),
        ("materiële vaste activa", "BMva"),
        ("inventaris", "BMvaBei"),
        ("vervoermiddelen", "BMvaTev"),
        ("langlopende schulden", "BLas"),
        ("overige vorderingen", "BVorOvr"),
        ("vorderingen", "BVorOvr"),
        ("kortlopende schulden", "BSchOvs"),
        ("overige schulden", "BSchOvs"),
    ];

    public static Rekening? Kies(Balanspost post, Rekeningschema schema)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(schema);
        var balans = schema.Alle.Where(r => r.IsBalans).ToList();
        // Een schuld op lange termijn ("Overige schulden" onder Langlopende schulden) is geen kortlopende schuld,
        // ook niet als een kortlopende rekening zo heet.
        var lang = post.Rubriek.Contains("langlopend", StringComparison.OrdinalIgnoreCase);
        var naam = Namen.Normaliseer(post.Naam);
        if (balans.FirstOrDefault(r => Namen.Normaliseer(r.Naam) == naam && !(lang && r.RgsCode?.StartsWith("BSch", StringComparison.Ordinal) == true)) is { } gelijk)
        {
            return gelijk;
        }

        foreach (var tekst in new[] { post.Naam, post.Rubriek })
        {
            foreach (var (trefwoord, rgs) in Trefwoorden)
            {
                if (!Bevat(tekst, trefwoord))
                {
                    continue;
                }

                var code = lang && rgs.StartsWith("BSch", StringComparison.Ordinal) ? "BLas" : rgs;
                if (Eerste(balans, code) is { } rekening)
                {
                    return rekening;
                }
            }
        }

        return null;
    }

    private static bool Bevat(string tekst, string trefwoord) =>
        Regex.IsMatch(tekst, $@"(^|[\s\-]){Regex.Escape(trefwoord)}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>De eerste rekening in de RGS-groep; bij liquide middelen de bankrekening van de administratie.</summary>
    private static Rekening? Eerste(List<Rekening> balans, string rgs)
    {
        var groep = balans.Where(r => r.RgsCode?.StartsWith(rgs, StringComparison.Ordinal) == true).ToList();
        return rgs == "BLimBan" ? groep.FirstOrDefault(r => r.Functie == Rekeningfunctie.Bank) ?? groep.FirstOrDefault() : groep.FirstOrDefault();
    }
}
