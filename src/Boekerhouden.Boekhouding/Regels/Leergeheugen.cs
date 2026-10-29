using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Regels;

/// <summary>Een keuze van de gebruiker: grootboekrekening en btw-code.</summary>
public sealed record Keuze(string Rekeningnummer, string? BtwCode);

/// <summary>
/// Een bevestigde verwerking: de keuze, met wat er op de factuur of mutatie stond. Daaraan herkent het pakket later
/// een afwijking.
/// </summary>
/// <param name="Bedrag">Het totaal inclusief btw, of leeg als dat onbekend is.</param>
/// <param name="Postcodes">De (Nederlandse) postcodes op de factuur, of leeg als dat onbekend is.</param>
/// <param name="Omschrijving">Het sjabloon voor de omschrijving van de boeking (met variabelen), als de gebruiker dat gebruikte.</param>
public sealed record Bevestiging(
    Keuze Keuze,
    decimal? Bedrag = null,
    string? Iban = null,
    IReadOnlyList<string>? Postcodes = null,
    string? Omschrijving = null);

public enum Afwijkingssoort
{
    Bedrag,
    Iban,
    Adres,
}

/// <summary>Waarom een factuur die het pakket kent, toch ter controle naar boven komt.</summary>
public sealed record Afwijking(Afwijkingssoort Soort, string Melding);

/// <summary>Wat het geheugen voorstelt, en hoe vaak die keuze al achter elkaar is bevestigd.</summary>
/// <param name="Bovengrens">Tot welk bedrag het pakket zelf verwerkt (zodra de keuze vaak genoeg bevestigd is).</param>
public sealed record Geleerd(
    Keuze Keuze,
    int OpRij,
    bool Automatisch,
    IReadOnlyList<Afwijking> Afwijkingen,
    decimal? Bovengrens = null,
    string? Omschrijving = null);

/// <summary>
/// Onthoudt per relatie (of tegenrekening of naam) de bevestigde keuzes, en leert daarvan:
/// <list type="number">
/// <item>de eerste keer kiest de gebruiker zelf (geen voorstel);</item>
/// <item>daarna volgt een voorstel: de laatste keuze;</item>
/// <item>is dezelfde keuze <see cref="Drempel"/> keer op rij bevestigd, dan wordt de volgende automatisch verwerkt,
/// zolang die in het patroon past: een bedrag tot <see cref="Bandbreedte"/> keer het hoogste bevestigde bedrag, hetzelfde
/// IBAN en geen nieuw adres.</item>
/// </list>
/// Past een factuur niet, dan komt die ter controle naar boven. Na de bevestiging begint de telling opnieuw: pas na
/// weer <see cref="Drempel"/> bevestigingen verwerkt het pakket zelf. Een andere keuze begint de telling ook opnieuw.
/// Zolang de telling de drempel niet heeft gehaald, telt elk bedrag mee. Facturen en bankmutaties worden apart onthouden.
/// </summary>
public sealed class Leergeheugen
{
    /// <summary>Hoeveel ruimte boven het hoogste bevestigde bedrag nog automatisch gaat.</summary>
    public const decimal Bandbreedte = 1.25m;

    private readonly Dictionary<string, List<Bevestiging>> bevestigingen = new(StringComparer.Ordinal);

    public Leergeheugen(int drempel = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(drempel, 1);
        Drempel = drempel;
    }

    /// <summary>Na hoeveel gelijke bevestigingen op rij het pakket zelf verwerkt.</summary>
    public int Drempel { get; }

    /// <summary>
    /// De sleutel waaronder een keuze wordt onthouden: de relatie, anders de tegenrekening, anders de naam. Leeg als
    /// er niets is om op te herkennen.
    /// </summary>
    public static string? Sleutel(Verwerkingsgegevens gegevens)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        var soort = gegevens.Soort == Verwerkingssoort.Inkoopfactuur ? "F" : "B";
        if (!string.IsNullOrWhiteSpace(gegevens.Relatie))
        {
            return $"{soort}|relatie|{gegevens.Relatie}";
        }

        if (!string.IsNullOrWhiteSpace(gegevens.TegenIban))
        {
            return $"{soort}|iban|{Iban.Normaliseer(gegevens.TegenIban)}";
        }

        var naam = Namen.Normaliseer(gegevens.Naam);
        return naam.Length > 0 ? $"{soort}|naam|{naam}" : null;
    }

    /// <summary>De bevestiging zoals die bij deze gegevens hoort.</summary>
    public static Bevestiging Bij(Verwerkingsgegevens gegevens, Keuze keuze, string? omschrijving = null)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        return new Bevestiging(keuze, gegevens.Bedrag, gegevens.TegenIban is { } iban ? Iban.Normaliseer(iban) : null, gegevens.Postcodes, omschrijving);
    }

    public void Onthoud(Verwerkingsgegevens gegevens, Keuze keuze, string? omschrijving = null)
    {
        if (Sleutel(gegevens) is { } sleutel)
        {
            Onthoud(sleutel, Bij(gegevens, keuze, omschrijving));
        }
    }

    /// <summary>Een bevestiging onder een bekende sleutel, bijv. bij het inlezen van eerdere keuzes uit de database.</summary>
    public void Onthoud(string sleutel, Bevestiging bevestiging)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        ArgumentNullException.ThrowIfNull(bevestiging);
        if (!bevestigingen.TryGetValue(sleutel, out var lijst))
        {
            bevestigingen[sleutel] = lijst = [];
        }

        lijst.Add(bevestiging);
    }

    public Geleerd? Voorstel(Verwerkingsgegevens gegevens)
    {
        if (Sleutel(gegevens) is not { } sleutel || !bevestigingen.TryGetValue(sleutel, out var lijst) || lijst.Count == 0)
        {
            return null;
        }

        var reeks = Reeks(lijst);
        var laatste = reeks[^1];
        var bovengrens = reeks.Count >= Drempel ? Bovengrens(reeks) : null;
        var afwijkingen = Afwijkingen(reeks, Bij(gegevens, laatste.Keuze), bovengrens);
        var omschrijving = reeks.LastOrDefault(b => b.Omschrijving is not null)?.Omschrijving;
        return new Geleerd(laatste.Keuze, reeks.Count, reeks.Count >= Drempel && afwijkingen.Count == 0, afwijkingen, bovengrens, omschrijving);
    }

    /// <summary>De laatste reeks bevestigingen die bij elkaar horen: dezelfde keuze, en (zodra de drempel gehaald is) in het patroon.</summary>
    private List<Bevestiging> Reeks(List<Bevestiging> lijst)
    {
        var reeks = new List<Bevestiging>();
        foreach (var bevestiging in lijst)
        {
            var past = reeks.Count > 0
                       && bevestiging.Keuze == reeks[^1].Keuze
                       && Afwijkingen(reeks, bevestiging, reeks.Count >= Drempel ? Bovengrens(reeks) : null).Count == 0;
            if (!past)
            {
                reeks.Clear();
            }

            reeks.Add(bevestiging);
        }

        return reeks;
    }

    private static decimal? Bovengrens(List<Bevestiging> reeks)
    {
        var hoogste = reeks.Where(b => b.Bedrag is not null).Select(b => Math.Abs(b.Bedrag!.Value)).DefaultIfEmpty().Max();
        return hoogste > 0 ? decimal.Round(hoogste * Bandbreedte, 2) : null;
    }

    private static List<Afwijking> Afwijkingen(List<Bevestiging> reeks, Bevestiging nu, decimal? bovengrens)
    {
        var afwijkingen = new List<Afwijking>();
        if (bovengrens is { } grens && nu.Bedrag is { } bedrag && Math.Abs(bedrag) > grens)
        {
            afwijkingen.Add(new(Afwijkingssoort.Bedrag, $"Het bedrag is hoger dan bij de eerdere facturen (automatisch tot {Domain.Bedragen.Euro(grens)})."));
        }

        if (nu.Iban is { } iban && reeks.LastOrDefault(b => b.Iban is not null)?.Iban is { } bekend && iban != bekend)
        {
            afwijkingen.Add(new(Afwijkingssoort.Iban, $"Ander IBAN dan bij de eerdere facturen ({Iban.Toon(iban)} in plaats van {Iban.Toon(bekend)})."));
        }

        var bekendePostcodes = reeks.Where(b => b.Postcodes is not null).SelectMany(b => b.Postcodes!).ToHashSet(StringComparer.Ordinal);
        if (bekendePostcodes.Count > 0 && nu.Postcodes?.Where(p => !bekendePostcodes.Contains(p)).ToList() is { Count: > 0 } nieuw)
        {
            afwijkingen.Add(new(Afwijkingssoort.Adres, $"Ander adres dan bij de eerdere facturen (postcode {string.Join(", ", nieuw.Select(Postcode.Toon))})."));
        }

        return afwijkingen;
    }
}
