using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Regels;

public enum Regelrichting
{
    Beide,
    Betaling,
    Ontvangst,
}

/// <summary>
/// Een regel van de gebruiker: als alle ingevulde voorwaarden kloppen, dan deze rekening en btw-code, bijvoorbeeld
/// "naam bevat KPN, bedrag tussen € 10 en € 30 → telefoonkosten, 21%, automatisch verwerken". Lege voorwaarden
/// tellen niet mee; een regel zonder voorwaarden past nooit.
/// </summary>
/// <param name="Soort">Waar de regel voor geldt; leeg = facturen en bankmutaties.</param>
/// <param name="BedragVan">Ondergrens van het totaal (inclusief btw, zonder teken), inclusief.</param>
/// <param name="BedragTot">Bovengrens van het totaal (inclusief btw, zonder teken), inclusief.</param>
public sealed record Verwerkingsregel(
    string Naam,
    string Rekeningnummer,
    string? BtwCode = null,
    bool Automatisch = false,
    Verwerkingssoort? Soort = null,
    string? Relatie = null,
    string? NaamBevat = null,
    decimal? BedragVan = null,
    decimal? BedragTot = null,
    string? OmschrijvingBevat = null,
    string? TegenIban = null,
    Regelrichting Richting = Regelrichting.Beide,
    string? Omschrijving = null)
{
    public bool HeeftVoorwaarden =>
        Relatie is not null || NaamBevat is not null || BedragVan is not null || BedragTot is not null
        || OmschrijvingBevat is not null || TegenIban is not null || Richting != Regelrichting.Beide;

    public bool Past(Verwerkingsgegevens gegevens)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        var bedrag = Math.Abs(gegevens.Bedrag);
        return HeeftVoorwaarden
               && (Soort is null || Soort == gegevens.Soort)
               && (Relatie is null || string.Equals(Relatie, gegevens.Relatie, StringComparison.OrdinalIgnoreCase))
               && (NaamBevat is null || Bevat(gegevens.Naam, NaamBevat))
               && (BedragVan is null || bedrag >= BedragVan)
               && (BedragTot is null || bedrag <= BedragTot)
               && (OmschrijvingBevat is null || Bevat(gegevens.Omschrijving, OmschrijvingBevat))
               && (TegenIban is null || (gegevens.TegenIban is not null && Iban.Normaliseer(TegenIban) == Iban.Normaliseer(gegevens.TegenIban)))
               && Richting switch
               {
                   Regelrichting.Betaling => gegevens.Soort == Verwerkingssoort.Inkoopfactuur ? gegevens.Bedrag >= 0 : gegevens.Bedrag < 0,
                   Regelrichting.Ontvangst => gegevens.Soort == Verwerkingssoort.Bankmutatie && gegevens.Bedrag > 0,
                   _ => true,
               };
    }

    /// <summary>Bevat de tekst het zoekwoord, ongeacht hoofdletters, accenten en rechtsvorm ("KPN" in "K.P.N. B.V.")?</summary>
    private static bool Bevat(string? tekst, string zoek)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return false;
        }

        var compact = Compact(zoek);
        return tekst.Contains(zoek, StringComparison.OrdinalIgnoreCase) || (compact.Length > 0 && Compact(tekst).Contains(compact, StringComparison.Ordinal));
    }

    private static string Compact(string tekst) => string.Concat(Namen.Woorden(tekst));
}
