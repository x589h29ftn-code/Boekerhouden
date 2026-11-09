using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Loon;

/// <summary>Een rekening uit het journaal van het salarispakket: nummer en naam zoals daar, en het bedrag (debet positief).</summary>
public sealed record Loonjournaalregel(string Nummer, string Naam, decimal Bedrag);

/// <summary>
/// Het (cumulatieve) loonjournaal uit een salarispakket: per rekening van het salarispakket het totaal (de regels per
/// kostenplaats opgeteld), met de totalen debet en credit zoals vermeld, en de periode en werkgever als die erin staan.
/// </summary>
public sealed record Loonjournaal(string? Werkgever, string? PeriodeVan, string? PeriodeTot, IReadOnlyList<Loonjournaalregel> Regels, decimal? TotaalDebet, decimal? TotaalCredit)
{
    private static readonly System.Globalization.CultureInfo Nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    public decimal Debet => Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);

    public decimal Credit => -Regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag);

    /// <summary>Wat er niet klopt: niet in balans, of de som van de regels is niet het vermelde totaal.</summary>
    public IEnumerable<string> Controle()
    {
        if (Regels.Count == 0)
        {
            yield return "Er zijn geen rekeningen met bedragen gelezen.";
            yield break;
        }

        if (Debet != Credit)
        {
            yield return string.Create(Nl, $"Niet in balans: debet {Debet:N2}, credit {Credit:N2}.");
        }

        if (TotaalDebet is { } d && d != Debet)
        {
            yield return string.Create(Nl, $"Debet opgeteld {Debet:N2}, maar het journaal vermeldt {d:N2}: mogelijk is een bedrag niet goed gelezen.");
        }

        if (TotaalCredit is { } c && c != Credit)
        {
            yield return string.Create(Nl, $"Credit opgeteld {Credit:N2}, maar het journaal vermeldt {c:N2}: mogelijk is een bedrag niet goed gelezen.");
        }
    }
}

/// <summary>Hoe een rekening van het salarispakket aan een rekening van de administratie is gekoppeld.</summary>
public enum Loonkoppelbron
{
    /// <summary>Nog te kiezen.</summary>
    Geen,

    /// <summary>Eerder zo gekoppeld (onthouden bij deze administratie).</summary>
    Onthouden,

    /// <summary>Hetzelfde rekeningnummer in het rekeningschema.</summary>
    ZelfdeNummer,

    /// <summary>Een rekening met dezelfde naam.</summary>
    ZelfdeNaam,

    /// <summary>
    /// Nieuw in het salarispakket (bijv. een reiskostenvergoeding via de verloning): de andere rekeningen zijn al eerder
    /// gekoppeld, deze niet. De gebruiker kiest zelf; een voorstel staat erbij.
    /// </summary>
    Nieuw,
}

/// <summary>Een regel van het loonjournaal met de voorgestelde rekening van de administratie.</summary>
/// <param name="Voorstel">Bij een nieuwe rekening: wat op nummer of naam zou passen (de gebruiker kiest).</param>
public sealed record Loonkoppeling(Loonjournaalregel Regel, string? Rekening, Loonkoppelbron Bron, string? Voorstel = null);

/// <summary>
/// De rekeningen van het salarispakket koppelen aan het rekeningschema: eerst wat bij deze administratie is onthouden,
/// dan hetzelfde nummer (het standaardschema), dan dezelfde naam; wat overblijft kiest de gebruiker. Daarna één
/// memoriaalboeking.
/// </summary>
public static class Loonkoppelingen
{
    public static IReadOnlyList<Loonkoppeling> Stel(Loonjournaal journaal, Rekeningschema schema, IReadOnlyDictionary<string, string> onthouden)
    {
        ArgumentNullException.ThrowIfNull(journaal);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(onthouden);
        return [.. journaal.Regels.Select(r =>
        {
            if (onthouden.TryGetValue(r.Nummer, out var nummer) && schema.Zoek(nummer) is not null)
            {
                return new Loonkoppeling(r, nummer, Loonkoppelbron.Onthouden);
            }

            var (rekening, bron) = Voorstel(r, schema);

            // Al eerder geïmporteerd, maar deze rekening niet: nieuw in het salarispakket, de gebruiker kiest.
            return onthouden.Count > 0
                ? new Loonkoppeling(r, null, Loonkoppelbron.Nieuw, rekening)
                : new Loonkoppeling(r, rekening, bron);
        })];
    }

    /// <summary>Een rekening op nummer (als de naam erop lijkt), dezelfde naam of één gelijkende naam.</summary>
    private static (string? Rekening, Loonkoppelbron Bron) Voorstel(Loonjournaalregel r, Rekeningschema schema)
    {
        // Hetzelfde nummer alleen als de naam erop lijkt: 1500 in het salarispakket kan in het schema iets heel anders zijn.
        if (schema.Zoek(r.Nummer) is { } zelfde && Lijkt(r.Naam, zelfde.Naam))
        {
            return (zelfde.Nummer, Loonkoppelbron.ZelfdeNummer);
        }

        var naam = Normaliseer(r.Naam);
        if (schema.Alle.FirstOrDefault(x => Normaliseer(x.Naam) == naam) is { } opNaam)
        {
            return (opNaam.Nummer, Loonkoppelbron.ZelfdeNaam);
        }

        // Eén rekening met een gelijkende naam (bijv. "Af te dragen loonheffing" en "Te betalen loonheffing").
        var lijkend = schema.Alle.Where(x => Lijkt(r.Naam, x.Naam)).Take(2).ToList();
        return lijkend.Count == 1 ? (lijkend[0].Nummer, Loonkoppelbron.ZelfdeNaam) : (null, Loonkoppelbron.Geen);
    }

    /// <summary>De memoriaalboeking: per regel de gekozen rekening, het bedrag en de naam uit het salarispakket als omschrijving.</summary>
    public static Journaalpost Boeking(string dagboek, DateOnly datum, string omschrijving, IEnumerable<(string Rekening, Loonjournaalregel Regel)> regels)
    {
        ArgumentNullException.ThrowIfNull(regels);
        return new Journaalpost(dagboek, "", datum, omschrijving, [.. regels.Select(x => new Journaalregel(x.Rekening, x.Regel.Bedrag, x.Regel.Naam))]);
    }

    /// <summary>Woorden die niets zeggen over welke rekening het is.</summary>
    private static readonly string[] Stopwoorden = ["dragen", "betalen", "vorderen", "overige", "reservering", "kosten", "premies"];

    /// <summary>Twee namen lijken op elkaar als een betekenisvol woord (vijf letters of meer) van de een in de ander voorkomt.</summary>
    public static bool Lijkt(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        static IEnumerable<string> Woorden(string naam) => naam.ToLowerInvariant().Split([' ', '-', '/', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetter)])).Where(w => w.Length >= 5 && !Stopwoorden.Contains(w));
        var (na, nb) = (Normaliseer(a), Normaliseer(b));
        return Woorden(a).Any(w => nb.Contains(w, StringComparison.Ordinal)) || Woorden(b).Any(w => na.Contains(w, StringComparison.Ordinal));
    }

    private static string Normaliseer(string naam) =>
        new([.. naam.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
