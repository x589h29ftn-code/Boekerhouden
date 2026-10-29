using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>Wat het pakket zelf met een bankmutatie mag doen.</summary>
public enum Bankactie
{
    /// <summary>Niets: de mutatie wacht op de gebruiker.</summary>
    Geen,

    /// <summary>Afletteren op de enige zekere kandidaat.</summary>
    Afletteren,

    /// <summary>Op de rekening van een regel of van wat geleerd is.</summary>
    OpRekening,
}

/// <summary>Het voorstel bij één bankmutatie: de afletterkandidaten en het advies voor een boeking op een rekening.</summary>
/// <param name="Relatie">De relatie bij het IBAN van de tegenpartij, of leeg.</param>
public sealed record Bankvoorstel(Bankmutatie Mutatie, Relatie? Relatie, Afletteradvies Afletteren, Verwerkingsadvies Advies)
{
    /// <summary>
    /// Een zekere kandidaat gaat voor. Een regel of geleerde keuze boekt alleen zelf als er geen openstaande post bij
    /// de mutatie kan horen: dan is het waarschijnlijk toch de betaling van een factuur.
    /// </summary>
    public Bankactie Automatisch => Afletteren.Automatisch is not null
        ? Bankactie.Afletteren
        : Afletteren.Kandidaten.Count == 0 && Advies is { Automatisch: true, Rekeningnummer: not null } ? Bankactie.OpRekening : Bankactie.Geen;

    /// <summary>Waarom (niet) automatisch, in één zin.</summary>
    public string Reden => Automatisch switch
    {
        Bankactie.Afletteren => $"Afgeletterd: {Afletteren.Automatisch!.Reden.ToLowerInvariant()}",
        Bankactie.OpRekening => Advies.Reden,
        _ when Afletteren.Kandidaten.Count > 0 => $"Voorstel: {Afletteren.Kandidaten[0].Reden.ToLowerInvariant()}",
        _ when Advies.Rekeningnummer is not null => Advies.Reden,
        _ => "Geen factuur of regel gevonden",
    };
}

/// <summary>
/// Bepaalt per bankmutatie wat erbij hoort: eerst de openstaande posten (<see cref="Afletteraar"/>), dan de regels en
/// het leergeheugen (<see cref="Verwerkingsadviseur"/>), zoals bij een inkoopfactuur.
/// </summary>
public static class Bankverwerker
{
    public static Bankvoorstel Verwerk(
        Bankmutatie mutatie,
        IEnumerable<OpenPost> openPosten,
        IReadOnlyList<Relatie> relaties,
        IEnumerable<Verwerkingsregel> regels,
        Leergeheugen geheugen,
        IReadOnlyList<BtwCode> btwCodes)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        ArgumentNullException.ThrowIfNull(relaties);
        var relatie = relaties.FirstOrDefault(r => r.HeeftIban(mutatie.TegenIban));
        var afletteren = Afletteraar.Zoek(mutatie, openPosten, relaties);
        var advies = Verwerkingsadviseur.Advies(Gegevens(mutatie, relatie), regels, geheugen, relatie, btwCodes);
        return new Bankvoorstel(mutatie, relatie, afletteren, advies);
    }

    /// <summary>Wat de regels en het leergeheugen van een bankmutatie weten.</summary>
    public static Verwerkingsgegevens Gegevens(Bankmutatie mutatie, Relatie? relatie)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        return new(Verwerkingssoort.Bankmutatie, relatie?.Code, mutatie.TegenNaam, mutatie.Bedrag, mutatie.Omschrijving, mutatie.TegenIban);
    }
}
