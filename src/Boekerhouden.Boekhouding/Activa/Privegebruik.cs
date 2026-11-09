using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Activa;

/// <summary>Of een vervoermiddel ook privé wordt gebruikt, en hoe dat is onderbouwd (één keuze voor beide).</summary>
public enum Privegebruik
{
    /// <summary>Nog niet vastgelegd.</summary>
    Onbekend,

    /// <summary>Ja; btw volgens het forfait: 2,7% van de cataloguswaarde, vanaf het vijfde jaar na ingebruikname 1,5%.</summary>
    JaForfait,

    /// <summary>Ja; btw over het werkelijke privégebruik (kilometeradministratie).</summary>
    JaWerkelijk,

    /// <summary>Nee; onderbouwd met een sluitende rittenregistratie.</summary>
    NeeRittenregistratie,

    /// <summary>Nee; er is een verklaring geen privégebruik (of geen privégebruik bestelauto).</summary>
    NeeVerklaring,

    /// <summary>Nee; een bestelauto die alleen zakelijk wordt gebruikt.</summary>
    NeeBestelauto,

    /// <summary>Nee; anders onderbouwd (zie de notitie).</summary>
    NeeAnders,
}

/// <summary>Een vervoermiddel uit de vaste activa, voor de controle op de btw privégebruik.</summary>
public sealed record Vervoermiddel(
    Guid Id, int Nummer, string Omschrijving, DateOnly Aanschafdatum, DateOnly InGebruik, DateOnly? BuitenGebruikOp, Privegebruik Privegebruik, decimal? Cataloguswaarde);

/// <summary>Een opmerking bij de btw privégebruik, eventueel over één vervoermiddel.</summary>
public sealed record Privegebruiksignaal(string Tekst, Guid? Activum = null);

/// <summary>
/// De btw privégebruik van auto's: welke activa vervoermiddelen zijn, wat het forfait verwacht en de controle van wat er
/// in een jaar in rubriek 1d is geboekt tegen wat er bij de vervoermiddelen is vastgelegd.
/// </summary>
public static class Privegebruikcontrole
{
    private static readonly System.Globalization.CultureInfo Nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    private static string B(decimal bedrag) => bedrag.ToString("N2", Nl);

    public static bool IsJa(Privegebruik p) => p is Privegebruik.JaForfait or Privegebruik.JaWerkelijk;

    public static bool IsNee(Privegebruik p) => p is >= Privegebruik.NeeRittenregistratie;

    public static string Naam(Privegebruik p) => p switch
    {
        Privegebruik.JaForfait => "Ja, forfait (2,7% / 1,5%)",
        Privegebruik.JaWerkelijk => "Ja, werkelijk privégebruik",
        Privegebruik.NeeRittenregistratie => "Nee, rittenregistratie",
        Privegebruik.NeeVerklaring => "Nee, verklaring geen privégebruik",
        Privegebruik.NeeBestelauto => "Nee, bestelauto alleen zakelijk",
        Privegebruik.NeeAnders => "Nee, anders onderbouwd",
        _ => "Niet vastgelegd",
    };

    /// <summary>
    /// Een vervoermiddel: op een activarekening in de RGS-groep transportmiddelen (<c>BMvaTev</c>), of met vervoer,
    /// transport of auto in de naam van de rekening of het actief.
    /// </summary>
    public static bool IsVervoermiddel(Rekening? activarekening, string omschrijving)
    {
        ArgumentNullException.ThrowIfNull(omschrijving);
        if (activarekening?.RgsCode?.StartsWith("BMvaTev", StringComparison.Ordinal) == true)
        {
            return true;
        }

        var naam = $"{activarekening?.Naam} {omschrijving}".ToLowerInvariant();
        return naam.Contains("vervoer", StringComparison.Ordinal) || naam.Contains("transport", StringComparison.Ordinal)
            || naam.Split([' ', '-', ',', '.', '/', '('], StringSplitOptions.RemoveEmptyEntries).Any(w => w is "auto" or "auto's" or "bestelauto" or "personenauto");
    }

    /// <summary>Het percentage van het forfait in een jaar: 2,7%, of 1,5% vanaf het vijfde jaar na het jaar van ingebruikname.</summary>
    public static decimal Forfaitpercentage(DateOnly inGebruik, int jaar) => jaar - inGebruik.Year >= 5 ? 1.5m : 2.7m;

    /// <summary>De maanden van het jaar dat het vervoermiddel in gebruik was (de maand van ingebruikname en van verkoop tellen mee).</summary>
    public static int Maanden(Vervoermiddel v, int jaar)
    {
        ArgumentNullException.ThrowIfNull(v);
        var van = v.InGebruik.Year < jaar ? 1 : v.InGebruik.Year == jaar ? v.InGebruik.Month : 13;
        var tot = v.BuitenGebruikOp is { } weg ? (weg.Year > jaar ? 12 : weg.Year == jaar ? weg.Month : 0) : 12;
        return Math.Max(0, tot - van + 1);
    }

    /// <summary>De btw privégebruik volgens het forfait over het jaar (naar rato van de maanden); leeg zonder cataloguswaarde of forfait.</summary>
    public static decimal? Verwacht(Vervoermiddel v, int jaar)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Privegebruik != Privegebruik.JaForfait || v.Cataloguswaarde is not { } waarde)
        {
            return null;
        }

        return Math.Round(waarde * Forfaitpercentage(v.InGebruik, jaar) / 100m * Maanden(v, jaar) / 12m, 2);
    }

    /// <summary>
    /// De opmerkingen bij de btw privégebruik van een jaar: wat er in rubriek 1d is geboekt (<paramref name="geboekt"/>)
    /// tegen de vervoermiddelen die in het jaar in gebruik waren en wat daarbij is vastgelegd.
    /// </summary>
    public static IReadOnlyList<Privegebruiksignaal> Signalen(int jaar, IEnumerable<Vervoermiddel> vervoermiddelen, decimal geboekt)
    {
        ArgumentNullException.ThrowIfNull(vervoermiddelen);
        var signalen = new List<Privegebruiksignaal>();
        var inGebruik = vervoermiddelen.Where(v => Maanden(v, jaar) > 0).OrderBy(v => v.Nummer).ToList();
        foreach (var v in inGebruik)
        {
            var naam = $"{v.Nummer} {v.Omschrijving}";
            switch (v.Privegebruik)
            {
                case Privegebruik.Onbekend when v.Aanschafdatum.Year == jaar:
                    signalen.Add(new($"Nieuwe auto {naam}: leg vast of er privégebruik is en hoe dat is onderbouwd.", v.Id));
                    break;
                case Privegebruik.Onbekend when geboekt == 0:
                    signalen.Add(new($"{naam}: privégebruik niet vastgelegd en geen btw privégebruik geboekt in {jaar}. Is dat juist?", v.Id));
                    break;
                case Privegebruik.Onbekend:
                    signalen.Add(new($"{naam}: leg vast of er privégebruik is en hoe dat is onderbouwd.", v.Id));
                    break;
                case Privegebruik.JaForfait when v.Cataloguswaarde is null:
                    signalen.Add(new($"{naam}: privégebruik volgens het forfait, maar zonder cataloguswaarde.", v.Id));
                    break;
            }

            if (IsJa(v.Privegebruik) && geboekt == 0)
            {
                var verwacht = Verwacht(v, jaar) is { } b ? $" (forfait {Forfaitpercentage(v.InGebruik, jaar).ToString("0.0", Nl)}%: {B(b)})" : null;
                signalen.Add(new($"{naam}: privégebruik ja, maar geen btw privégebruik geboekt in {jaar}{verwacht}.", v.Id));
            }
        }

        var forfait = inGebruik.Where(v => v.Privegebruik == Privegebruik.JaForfait).Select(v => Verwacht(v, jaar)).ToList();
        if (geboekt != 0 && forfait.Count > 0 && forfait.All(b => b is not null) && inGebruik.All(v => v.Privegebruik != Privegebruik.JaWerkelijk))
        {
            var verwacht = forfait.Sum()!.Value;
            if (Math.Abs(verwacht - geboekt) >= 1m)
            {
                signalen.Add(new($"Btw privégebruik {jaar}: geboekt {B(geboekt)}, volgens het forfait {B(verwacht)}."));
            }
        }

        if (geboekt != 0 && !inGebruik.Any(v => IsJa(v.Privegebruik) || v.Privegebruik == Privegebruik.Onbekend))
        {
            signalen.Add(new($"Btw privégebruik {jaar} geboekt ({B(geboekt)}), maar geen vervoermiddel met privégebruik in de vaste activa."));
        }

        return signalen;
    }
}
