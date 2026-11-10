using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Zoeken in het journaal: elk woord van de zoektekst moet ergens in de post voorkomen (omschrijving, boekstuk, dagboek,
/// en van de regels het rekeningnummer, de naam van de rekening, de omschrijving, de btw-code, de relatie en het
/// factuurnummer); een bedrag vindt de post met dat totaal of met een regel van dat bedrag (het teken telt niet).
/// </summary>
public static class Journaalzoeker
{
    public static bool Past(Journaalpost post, string? zoek, Rekeningschema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (string.IsNullOrWhiteSpace(zoek))
        {
            return true;
        }

        var tekst = zoek.Trim();
        if (BedragParser.TryParse(tekst, out var bedrag) && tekst.Any(char.IsAsciiDigit)
            && (post.TotaalDebet == Math.Abs(bedrag) || post.Regels.Any(r => Math.Abs(r.Bedrag) == Math.Abs(bedrag))))
        {
            return true;
        }

        return tekst.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(woord => Bevat(post, woord, schema));
    }

    private static bool Bevat(Journaalpost post, string woord, Rekeningschema? schema)
    {
        bool In(string? waarde) => waarde?.Contains(woord, StringComparison.CurrentCultureIgnoreCase) == true;
        return In(post.Omschrijving) || In(post.Boekstuk) || In(post.Dagboek)
               || post.Regels.Any(r => r.Rekeningnummer.StartsWith(woord, StringComparison.OrdinalIgnoreCase) || In(schema?.Zoek(r.Rekeningnummer)?.Naam)
                                       || In(r.Omschrijving) || In(r.Relatie) || In(r.Factuurnummer) || string.Equals(r.BtwCode, woord, StringComparison.OrdinalIgnoreCase));
    }
}
