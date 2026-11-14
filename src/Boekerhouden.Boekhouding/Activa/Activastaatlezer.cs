using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Activa;

/// <summary>Een cel van een ingelezen staat: tekst, of een getal of datum (uit Excel al als waarde).</summary>
public sealed record Staatcel(string? Tekst, decimal? Getal = null, DateOnly? Datum = null)
{
    public static Staatcel Leeg { get; } = new(Tekst: null);

    public bool IsLeeg => string.IsNullOrWhiteSpace(Tekst) && Getal is null && Datum is null;
}

/// <summary>Een staat als tabel: rijen met cellen, zoals uit Excel of uit een PDF gehaald.</summary>
public sealed record Staattabel(IReadOnlyList<IReadOnlyList<Staatcel>> Rijen);

/// <summary>Wat een kolom van een staat van vaste activa bevat.</summary>
public enum Staatkolom
{
    Omschrijving,
    Datum,
    AanschafBegin,
    Investeringen,
    Desinvesteringen,
    AanschafEind,
    AfschrijvingBegin,
    AfschrijvingJaar,
    AfschrijvingDesinvesteringen,
    AfschrijvingEind,
    BoekwaardeBegin,
    BoekwaardeEind,
    Restwaarde,
    Percentage,
    Jaren,
    Groep,
    Activarekening,
    Afschrijvingsrekening,
    Kostenrekening,
    Privegebruik,
    Cataloguswaarde,
    Notitie,
}

/// <summary>Een actief op de staat, met de bedragen zoals ze er staan (wat ontbreekt is aangevuld als het volgt uit de rest).</summary>
public sealed record Staatregel(
    int Rij,
    string Groep,
    string Omschrijving,
    DateOnly? Datum,
    decimal AanschafBegin,
    decimal Investeringen,
    decimal Desinvesteringen,
    decimal AanschafEind,
    decimal AfschrijvingBegin,
    decimal AfschrijvingJaar,
    decimal AfschrijvingDesinvesteringen,
    decimal AfschrijvingEind,
    decimal Boekwaarde,
    decimal Restwaarde,
    decimal? Percentage,
    string? Activarekening = null,
    string? Afschrijvingsrekening = null,
    string? Kostenrekening = null,
    Privegebruik Privegebruik = Privegebruik.Onbekend,
    decimal? Cataloguswaarde = null,
    string? Notitie = null);

/// <summary>Een groep (bijv. inventaris, vervoermiddelen) met het subtotaal van de staat, als dat er stond.</summary>
public sealed record Staatgroep(string Naam, IReadOnlyList<Staatregel> Regels, Staattotaal? Subtotaal)
{
    public Staattotaal Som => Staattotaal.Van(Regels);
}

/// <summary>De totalen van een aantal regels of een totaalregel van de staat.</summary>
public sealed record Staattotaal(decimal AanschafEind, decimal AfschrijvingEind, decimal Boekwaarde, decimal AfschrijvingJaar)
{
    public static Staattotaal Van(IEnumerable<Staatregel> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        var lijst = regels.ToList();
        return new(lijst.Sum(r => r.AanschafEind), lijst.Sum(r => r.AfschrijvingEind), lijst.Sum(r => r.Boekwaarde), lijst.Sum(r => r.AfschrijvingJaar));
    }
}

/// <summary>Een opmerking bij het inlezen: een regel of totaal dat niet sluit, of iets wat niet te lezen was.</summary>
public sealed record Staatsignaal(string Tekst, int? Rij = null);

/// <summary>De ingelezen staat van vaste activa per <see cref="Einddatum"/>.</summary>
public sealed record Ingelezenstaat(
    int? Jaar,
    IReadOnlyList<Staatkolom> Kolommen,
    IReadOnlyList<Staatgroep> Groepen,
    Staattotaal? Eindtotaal,
    IReadOnlyList<Staatsignaal> Signalen)
{
    public IEnumerable<Staatregel> Regels => Groepen.SelectMany(g => g.Regels);

    /// <summary>De datum van de staat: 31 december van het jaar.</summary>
    public DateOnly? Einddatum => Jaar is { } j ? new DateOnly(j, 12, 31) : null;

    public Staattotaal Totaal => Staattotaal.Van(Regels);

    /// <summary>Sluiten alle regels, subtotalen en het eindtotaal?</summary>
    public bool Sluit => Signalen.Count == 0;
}

/// <summary>
/// Leest een staat van vaste activa (mva-staat) uit een tabel, ongeacht het pakket of sjabloon waar hij uit komt:
/// de kopregel wordt gezocht op woorden (omschrijving, datum, aanschaf, investeringen, afschrijving, boekwaarde,
/// restwaarde, %), ook als de kop over twee of drie regels loopt ("Aanschafwaarde" boven "tot 01-01-2025"). Daaronder:
/// een regel met tekst zonder bedragen is een groep (inventaris, vervoermiddelen), een regel met een omschrijving en
/// bedragen een actief, een regel met bedragen zonder omschrijving (of "Totaal …") een subtotaal of het eindtotaal.
/// Per regel en per groep wordt gecontroleerd of het sluit: aanschaf begin + investeringen − desinvesteringen = eind,
/// afschrijving begin + jaar − desinvesteringen = eind, aanschaf − afschrijving = boekwaarde, en de som van de regels
/// tegen het subtotaal en het eindtotaal (verschillen onder een euro tellen niet: staten rekenen vaak in hele euro's).
/// </summary>
public static partial class Activastaatlezer
{
    private const decimal Speling = 1m;

    public static Ingelezenstaat Lees(Staattabel tabel)
    {
        ArgumentNullException.ThrowIfNull(tabel);
        var signalen = new List<Staatsignaal>();
        if (Kop(tabel) is not var (kopRij, kolommen))
        {
            return new Ingelezenstaat(null, [], [], null, [new("Er is geen kopregel gevonden met omschrijving, aanschafwaarde en afschrijvingen.")]);
        }

        var jaar = Jaar(tabel, kopRij);
        Staatcel Cel(IReadOnlyList<Staatcel> rij, Staatkolom kolom) =>
            kolommen.TryGetValue(kolom, out var i) && i < rij.Count ? rij[i] : Staatcel.Leeg;
        decimal? Getal(IReadOnlyList<Staatcel> rij, Staatkolom kolom) => Bedrag(Cel(rij, kolom));
        var bedragkolommen = kolommen.Keys.Where(k => k is not (Staatkolom.Omschrijving or Staatkolom.Datum or Staatkolom.Percentage or Staatkolom.Jaren or Staatkolom.Groep
            or Staatkolom.Activarekening or Staatkolom.Afschrijvingsrekening or Staatkolom.Kostenrekening or Staatkolom.Privegebruik or Staatkolom.Cataloguswaarde or Staatkolom.Notitie)).ToList();

        var groepen = new List<(string Naam, List<Staatregel> Regels, Staattotaal? Subtotaal)>();
        string? groep = null;
        Staattotaal? eindtotaal = null;
        var sindsTotaal = false;
        for (var r = kopRij + 1; r < tabel.Rijen.Count; r++)
        {
            var rij = tabel.Rijen[r];
            var tekst = Cel(rij, Staatkolom.Omschrijving).Tekst?.Trim();
            var heeftBedrag = bedragkolommen.Any(k => Getal(rij, k) is { } g && g != 0);
            if (!heeftBedrag)
            {
                // Een tekst zonder bedragen: een (nieuwe) groep. Lege sjabloonregels ("Gebouwen 3") zijn geen groep als er
                // al een groep met dat begin is.
                if (!string.IsNullOrWhiteSpace(tekst) && !Eenheid(tekst) && !(groep is not null && Volgnummer().Match(tekst) is { Success: true } v
                        && groep.StartsWith(v.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    groep = tekst;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(tekst) || Totaalwoord().IsMatch(tekst))
            {
                // Een totaal: na regels van de huidige groep het subtotaal van die groep, anders een (eind)totaal.
                var totaal = new Staattotaal(Getal(rij, Staatkolom.AanschafEind) ?? 0m, Getal(rij, Staatkolom.AfschrijvingEind) ?? 0m,
                    Getal(rij, Staatkolom.BoekwaardeEind) ?? ((Getal(rij, Staatkolom.AanschafEind) ?? 0m) - (Getal(rij, Staatkolom.AfschrijvingEind) ?? 0m)),
                    Getal(rij, Staatkolom.AfschrijvingJaar) ?? 0m);
                // Een subtotaal: de som van de groep, of een regel zonder tekst of met de naam van de groep ("Totaal
                // inventaris"); "Totaal" of "Totaal vaste activa" met de som van alles is het eindtotaal.
                var groepssom = groepen.Count > 0 ? Staattotaal.Van(groepen[^1].Regels) : null;
                var alles = Staattotaal.Van(groepen.SelectMany(g => g.Regels));
                var isSubtotaal = groepssom is not null && (Gelijk(totaal, groepssom)
                    || (!Gelijk(totaal, alles) && (string.IsNullOrWhiteSpace(tekst) || tekst.Contains(groepen[^1].Naam, StringComparison.OrdinalIgnoreCase))));
                if (sindsTotaal && groepen.Count > 0 && groepen[^1].Subtotaal is null && isSubtotaal)
                {
                    groepen[^1] = groepen[^1] with { Subtotaal = totaal };
                }
                else
                {
                    eindtotaal = totaal;
                }

                sindsTotaal = false;
                continue;
            }

            // Een kolom Groep (zoals in het sjabloon) gaat voor de groepsregels.
            var naam = Cel(rij, Staatkolom.Groep).Tekst?.Trim() is { Length: > 0 } eigenGroep ? eigenGroep : groep ?? "Vaste activa";
            if (groepen.Count == 0 || groepen[^1].Naam != naam || groepen[^1].Subtotaal is not null)
            {
                groepen.Add((naam, [], null));
            }

            var regel = Regel(r + 1, naam, tekst, rij, Cel, Getal);
            groepen[^1].Regels.Add(regel);
            sindsTotaal = true;
            signalen.AddRange(Controleer(regel));
        }

        var resultaat = groepen.Select(g => new Staatgroep(g.Naam, g.Regels, g.Subtotaal)).ToList();
        foreach (var g in resultaat.Where(g => g.Subtotaal is not null))
        {
            signalen.AddRange(Vergelijk(g.Som, g.Subtotaal!, $"Subtotaal {g.Naam}"));
        }

        // Het eindtotaal: het laatste totaal van de staat tegen alle regels.
        if (eindtotaal is not null)
        {
            signalen.AddRange(Vergelijk(Staattotaal.Van(resultaat.SelectMany(g => g.Regels)), eindtotaal, "Totaal van de staat"));
        }

        if (resultaat.Count == 0)
        {
            signalen.Add(new("Er staan geen activa met bedragen op de staat."));
        }

        return new Ingelezenstaat(jaar, [.. kolommen.Keys.Order()], resultaat, eindtotaal, signalen);
    }

    private static Staatregel Regel(int rij, string groep, string tekst, IReadOnlyList<Staatcel> cellen,
        Func<IReadOnlyList<Staatcel>, Staatkolom, Staatcel> cel, Func<IReadOnlyList<Staatcel>, Staatkolom, decimal?> getal)
    {
        decimal? G(Staatkolom k) => getal(cellen, k);
        var begin = G(Staatkolom.AanschafBegin) ?? 0m;
        var investeringen = G(Staatkolom.Investeringen) ?? 0m;
        var desinvesteringen = G(Staatkolom.Desinvesteringen) ?? 0m;
        var aanschafEind = G(Staatkolom.AanschafEind) ?? (begin + investeringen - desinvesteringen);
        var afschrijvingBegin = G(Staatkolom.AfschrijvingBegin) ?? 0m;
        var afschrijvingJaar = G(Staatkolom.AfschrijvingJaar) ?? 0m;
        var afschrijvingDesinvesteringen = G(Staatkolom.AfschrijvingDesinvesteringen) ?? 0m;
        var boekwaarde = G(Staatkolom.BoekwaardeEind);
        var afschrijvingEind = G(Staatkolom.AfschrijvingEind)
                               ?? (G(Staatkolom.AfschrijvingBegin) is not null || G(Staatkolom.AfschrijvingJaar) is not null
                                   ? afschrijvingBegin + afschrijvingJaar - afschrijvingDesinvesteringen
                                   : aanschafEind - (boekwaarde ?? aanschafEind));
        var percentage = G(Staatkolom.Percentage) is { } p and > 0 ? (p <= 1m ? p * 100m : p)
            : G(Staatkolom.Jaren) is { } jaren and > 0 ? Math.Round(100m / jaren, 2) : (decimal?)null;
        var datum = cel(cellen, Staatkolom.Datum) is { } d ? d.Datum ?? Datumtekst(d.Tekst) : null;
        string? T(Staatkolom k) => cel(cellen, k) is { } c && (c.Tekst?.Trim() is { Length: > 0 } t ? t : c.Getal is { } g ? g.ToString("0.##", CultureInfo.InvariantCulture) : null) is { } w ? w : null;
        return new Staatregel(rij, groep, tekst, datum, begin, investeringen, desinvesteringen, aanschafEind, afschrijvingBegin, afschrijvingJaar,
            afschrijvingDesinvesteringen, afschrijvingEind, boekwaarde ?? aanschafEind - afschrijvingEind, G(Staatkolom.Restwaarde) ?? 0m, percentage,
            T(Staatkolom.Activarekening), T(Staatkolom.Afschrijvingsrekening), T(Staatkolom.Kostenrekening), Privegebruiktekst(T(Staatkolom.Privegebruik)),
            G(Staatkolom.Cataloguswaarde) is > 0 and var catalogus ? catalogus : null, T(Staatkolom.Notitie));
    }

    private static IEnumerable<Staatsignaal> Controleer(Staatregel r)
    {
        if (Math.Abs(r.AanschafBegin + r.Investeringen - r.Desinvesteringen - r.AanschafEind) >= Speling && (r.AanschafBegin != 0 || r.Investeringen != 0))
        {
            yield return new($"{r.Omschrijving}: aanschaf {B(r.AanschafBegin)} + investeringen {B(r.Investeringen)} − desinvesteringen {B(r.Desinvesteringen)} is niet {B(r.AanschafEind)}.", r.Rij);
        }

        if (Math.Abs(r.AfschrijvingBegin + r.AfschrijvingJaar - r.AfschrijvingDesinvesteringen - r.AfschrijvingEind) >= Speling && (r.AfschrijvingBegin != 0 || r.AfschrijvingJaar != 0))
        {
            yield return new($"{r.Omschrijving}: afschrijving {B(r.AfschrijvingBegin)} + {B(r.AfschrijvingJaar)} − {B(r.AfschrijvingDesinvesteringen)} is niet {B(r.AfschrijvingEind)}.", r.Rij);
        }

        if (Math.Abs(r.AanschafEind - r.AfschrijvingEind - r.Boekwaarde) >= Speling)
        {
            yield return new($"{r.Omschrijving}: aanschaf {B(r.AanschafEind)} − afschrijving {B(r.AfschrijvingEind)} is niet de boekwaarde {B(r.Boekwaarde)}.", r.Rij);
        }

        if (r.Restwaarde > r.AanschafEind && r.AanschafEind > 0)
        {
            yield return new($"{r.Omschrijving}: de restwaarde {B(r.Restwaarde)} is hoger dan de aanschafwaarde.", r.Rij);
        }
    }

    private static bool Gelijk(Staattotaal a, Staattotaal b) =>
        Math.Abs(a.AanschafEind - b.AanschafEind) < Speling && Math.Abs(a.AfschrijvingEind - b.AfschrijvingEind) < Speling && Math.Abs(a.Boekwaarde - b.Boekwaarde) < Speling;

    private static IEnumerable<Staatsignaal> Vergelijk(Staattotaal som, Staattotaal totaal, string wat)
    {
        foreach (var (naam, a, b) in new[] { ("aanschafwaarde", som.AanschafEind, totaal.AanschafEind), ("afschrijving", som.AfschrijvingEind, totaal.AfschrijvingEind), ("boekwaarde", som.Boekwaarde, totaal.Boekwaarde) })
        {
            if (Math.Abs(a - b) >= Speling)
            {
                yield return new($"{wat}: {naam} van de regels {B(a)}, op de staat {B(b)}.");
            }
        }
    }

    /// <summary>De kopregel (de rij met de meeste herkende kolommen) en per soort de kolom.</summary>
    private static (int Rij, Dictionary<Staatkolom, int> Kolommen)? Kop(Staattabel tabel)
    {
        (int Rij, Dictionary<Staatkolom, int> Kolommen)? beste = null;
        for (var r = 0; r < Math.Min(tabel.Rijen.Count, 80); r++)
        {
            var breedte = tabel.Rijen[r].Count;
            var kolommen = new Dictionary<Staatkolom, int>();
            for (var c = 0; c < breedte; c++)
            {
                if (Koptekst(tabel, r, c) is not { Length: > 0 } eigen)
                {
                    continue;
                }

                // De kop van deze kolom, met wat er boven staat (ook een kop over meerdere kolommen, die links begint).
                var boven = Enumerable.Range(Math.Max(0, r - 3), Math.Min(3, r)).Select(b => Boven(tabel, b, c)).Where(t => t is { Length: > 0 });
                if (Soort(eigen, string.Join(' ', boven)) is { } soort && !kolommen.ContainsKey(soort))
                {
                    kolommen[soort] = c;
                }
            }

            var bruikbaar = kolommen.ContainsKey(Staatkolom.Omschrijving)
                            && (kolommen.ContainsKey(Staatkolom.AanschafEind) || kolommen.ContainsKey(Staatkolom.AanschafBegin))
                            && kolommen.Keys.Any(k => k is Staatkolom.AfschrijvingEind or Staatkolom.AfschrijvingBegin or Staatkolom.AfschrijvingJaar or Staatkolom.BoekwaardeEind);
            if (bruikbaar && (beste is null || kolommen.Count > beste.Value.Kolommen.Count))
            {
                beste = (r, kolommen);
            }
        }

        return beste;
    }

    private static string? Koptekst(Staattabel tabel, int r, int c) =>
        c < tabel.Rijen[r].Count && tabel.Rijen[r][c].Tekst is { } t ? Normaliseer(t) : null;

    /// <summary>De kop boven een kolom: de cel zelf, of de dichtstbijzijnde gevulde cel links ervan (een samengevoegde kop).</summary>
    private static string? Boven(Staattabel tabel, int r, int c)
    {
        for (var k = c; k >= 0 && k >= c - 8; k--)
        {
            if (Koptekst(tabel, r, k) is { Length: > 0 } tekst)
            {
                return tekst;
            }
        }

        return null;
    }

    private static string Normaliseer(string tekst) =>
        Regex.Replace(tekst.Replace("-\n", string.Empty, StringComparison.Ordinal).Replace("-\r\n", string.Empty, StringComparison.Ordinal), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Trim().ToLowerInvariant().Replace('é', 'e').Replace('ë', 'e');

    private static readonly string[] Beginwoorden = ["tot ", "01-01", "1-1-", "1 januari", "begin", "reeds", "cumulatief", "vorig", "per 1"];
    private static readonly string[] Eindwoorden = ["t/m", "31-12", "31 december", "eind", "ultimo"];

    /// <summary>Welke kolom een kop is, met de kop erboven als die iets toevoegt ("Aanschafwaarde" boven "Begin").</summary>
    internal static Staatkolom? Soort(string eigen, string boven)
    {
        bool Heeft(string t, params string[] woorden) => woorden.Any(w => t.Contains(w, StringComparison.Ordinal));
        var tekst = $"{eigen} {(Heeft(eigen, "aanschaf", "afschrij", "afgeschreven", "boekwaarde", "investering") ? string.Empty : boven)}".Trim();
        var begin = Heeft(eigen, Beginwoorden);
        var eind = Heeft(eigen, Eindwoorden) && !Heeft(eigen, "vorig");
        return tekst switch
        {
            _ when Heeft(eigen, "rekening") && Heeft(eigen, "afschrij") => Staatkolom.Afschrijvingsrekening,
            _ when Heeft(eigen, "rekening") && Heeft(eigen, "kosten") => Staatkolom.Kostenrekening,
            _ when Heeft(eigen, "activarekening", "balansrekening", "grootboekrekening") => Staatkolom.Activarekening,
            _ when Heeft(eigen, "groep", "categorie") => Staatkolom.Groep,
            _ when Heeft(eigen, "privegebruik", "prive-gebruik", "prive gebruik") => Staatkolom.Privegebruik,
            _ when Heeft(eigen, "catalogus") => Staatkolom.Cataloguswaarde,
            _ when Heeft(eigen, "notitie", "opmerking", "toelichting") => Staatkolom.Notitie,
            _ when Heeft(eigen, "omschrijving", "activum", "activa", "object") && !Heeft(eigen, "aanschaf", "afschrij") => Staatkolom.Omschrijving,
            _ when Heeft(eigen, "datum", "aankoopdatum", "in gebruik") => Staatkolom.Datum,
            _ when Heeft(eigen, "restwaarde", "residuwaarde", "residu") => Staatkolom.Restwaarde,
            _ when eigen is "%" or "perc." || Heeft(eigen, "percentage", "%") => Staatkolom.Percentage,
            _ when Heeft(eigen, "levensduur", "looptijd", "jaren", "termijn") => Staatkolom.Jaren,
            _ when Heeft(tekst, "boekwaarde") => begin && !eind ? Staatkolom.BoekwaardeBegin : Staatkolom.BoekwaardeEind,
            _ when Heeft(tekst, "afschrij", "afgeschreven") && Heeft(tekst, "desinv") => Staatkolom.AfschrijvingDesinvesteringen,
            _ when Heeft(tekst, "afschrij", "afgeschreven") => eind ? Staatkolom.AfschrijvingEind : begin ? Staatkolom.AfschrijvingBegin : Staatkolom.AfschrijvingJaar,
            _ when Heeft(tekst, "desinv", "verkopen", "buiten gebruik") => Staatkolom.Desinvesteringen,
            _ when Heeft(tekst, "investering") => Staatkolom.Investeringen,
            _ when Heeft(tekst, "aanschaf", "kostprijs", "verkrijgingsprijs") => begin && !eind ? Staatkolom.AanschafBegin : Staatkolom.AanschafEind,
            _ => null,
        };
    }

    /// <summary>Het jaar van de staat: het jaar dat het vaakst in de kop staat (bijv. 01-01-2025, 2025, 31-12-2025).</summary>
    private static int? Jaar(Staattabel tabel, int kopRij)
    {
        var jaren = new List<int>();
        for (var r = Math.Max(0, kopRij - 4); r <= kopRij; r++)
        {
            foreach (var cel in tabel.Rijen[r])
            {
                foreach (Match m in Jaartal().Matches(cel.Tekst ?? string.Empty))
                {
                    jaren.Add(int.Parse(m.Value, CultureInfo.InvariantCulture));
                }
            }
        }

        return jaren.Count == 0 ? null : jaren.GroupBy(j => j).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
    }

    private static decimal? Bedrag(Staatcel cel) =>
        cel.Getal ?? (cel.Tekst is { } t && t.Any(char.IsAsciiDigit) && BedragParser.TryParse(t.Replace("%", string.Empty, StringComparison.Ordinal), out var b) ? b : null);

    private static DateOnly? Datumtekst(string? tekst) =>
        tekst is not null && DatumPatroon().Match(tekst) is { Success: true } m
        && DateOnly.TryParseExact($"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):00}-{int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture):00}-{(m.Groups[3].Value.Length == 2 ? "20" + m.Groups[3].Value : m.Groups[3].Value)}",
            "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    /// <summary>Privégebruik zoals in het sjabloon: ja (forfait), ja werkelijk, nee, bestelauto, rittenregistratie, verklaring.</summary>
    private static Privegebruik Privegebruiktekst(string? tekst) => (tekst ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" => Privegebruik.Onbekend,
        var t when t.Contains("werkelijk", StringComparison.Ordinal) => Privegebruik.JaWerkelijk,
        var t when t.Contains("zonder btw", StringComparison.Ordinal) => Privegebruik.JaForfaitZonderBtw,
        var t when t.StartsWith("ja", StringComparison.Ordinal) || t.Contains("forfait", StringComparison.Ordinal) => Privegebruik.JaForfait,
        var t when t.Contains("bestel", StringComparison.Ordinal) => Privegebruik.NeeBestelauto,
        var t when t.Contains("ritten", StringComparison.Ordinal) => Privegebruik.NeeRittenregistratie,
        var t when t.Contains("verklaring", StringComparison.Ordinal) => Privegebruik.NeeVerklaring,
        var t when t.StartsWith("nee", StringComparison.Ordinal) => Privegebruik.NeeAnders,
        _ => Privegebruik.Onbekend,
    };

    /// <summary>Alleen een eenheid of teken in de omschrijvingskolom (een rij met € onder de kop): geen groep.</summary>
    private static bool Eenheid(string tekst) => tekst.Trim() is "€" or "%" or "EUR";

    private static string B(decimal bedrag) => bedrag.ToString("N2", CultureInfo.GetCultureInfo("nl-NL"));

    [GeneratedRegex(@"^\s*(sub)?totaal\b", RegexOptions.IgnoreCase)]
    private static partial Regex Totaalwoord();

    [GeneratedRegex(@"^(.*\D)\s*\d+$")]
    private static partial Regex Volgnummer();

    [GeneratedRegex(@"(?<!\d)(19[89]\d|20[0-9]\d)(?!\d)")]
    private static partial Regex Jaartal();

    [GeneratedRegex(@"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4}|\d{2})")]
    private static partial Regex DatumPatroon();
}
