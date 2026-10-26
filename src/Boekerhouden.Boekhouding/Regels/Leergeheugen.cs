using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Regels;

/// <summary>Een keuze van de gebruiker: grootboekrekening en btw-code.</summary>
public sealed record Keuze(string Rekeningnummer, string? BtwCode);

/// <summary>Wat het geheugen voorstelt, en hoe vaak die keuze al achter elkaar is gemaakt.</summary>
public sealed record Geleerd(Keuze Keuze, int OpRij, bool Automatisch);

/// <summary>
/// Onthoudt per relatie (of tegenrekening of naam) de keuzes van de gebruiker, en leert daarvan:
/// <list type="number">
/// <item>de eerste keer kiest de gebruiker zelf (geen voorstel);</item>
/// <item>daarna volgt een voorstel: de laatste keuze;</item>
/// <item>is dezelfde keuze <see cref="Drempel"/> keer op rij gemaakt, dan wordt de volgende automatisch verwerkt.</item>
/// </list>
/// Een afwijkende keuze begint de telling opnieuw. Facturen en bankmutaties worden apart onthouden.
/// </summary>
public sealed class Leergeheugen
{
    private readonly Dictionary<string, List<Keuze>> keuzes = new(StringComparer.Ordinal);

    public Leergeheugen(int drempel = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(drempel, 1);
        Drempel = drempel;
    }

    /// <summary>Na hoeveel gelijke keuzes op rij het pakket zelf verwerkt.</summary>
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

    public void Onthoud(Verwerkingsgegevens gegevens, Keuze keuze)
    {
        ArgumentNullException.ThrowIfNull(keuze);
        if (Sleutel(gegevens) is not { } sleutel)
        {
            return;
        }

        if (!keuzes.TryGetValue(sleutel, out var lijst))
        {
            keuzes[sleutel] = lijst = [];
        }

        lijst.Add(keuze);
    }

    public Geleerd? Voorstel(Verwerkingsgegevens gegevens)
    {
        if (Sleutel(gegevens) is not { } sleutel || !keuzes.TryGetValue(sleutel, out var lijst) || lijst.Count == 0)
        {
            return null;
        }

        var laatste = lijst[^1];
        var opRij = 0;
        for (var i = lijst.Count - 1; i >= 0 && lijst[i] == laatste; i--)
        {
            opRij++;
        }

        return new Geleerd(laatste, opRij, opRij >= Drempel);
    }
}
