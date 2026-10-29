using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Regels;

public enum Adviesbron
{
    Geen,
    Regel,
    Geleerd,
    Relatie,
}

/// <summary>Het voorstel voor een factuur of bankmutatie: rekening, btw-code, en of het pakket het zelf mag boeken.</summary>
/// <param name="Afwijkingen">Waarom een geleerde keuze nu niet automatisch gaat (ander bedrag, IBAN of adres).</param>
/// <param name="Omschrijving">Het sjabloon voor de omschrijving van de boeking, met variabelen zoals <c>#factuurnummer</c>.</param>
public sealed record Verwerkingsadvies(
    string? Rekeningnummer,
    string? BtwCode,
    Adviesbron Bron,
    bool Automatisch,
    string Reden,
    IReadOnlyList<Afwijking>? Afwijkingen = null,
    string? Omschrijving = null)
{
    public static Verwerkingsadvies Geen { get; } = new(null, null, Adviesbron.Geen, false, "Eerste keer: kies de rekening");
}

/// <summary>
/// Bepaalt het voorstel in vaste volgorde: een regel van de gebruiker gaat voor, dan wat geleerd is, dan de
/// standaardrekening van de relatie. Herkent de factuur een ander btw-tarief dan de keuze, dan wordt de btw-code
/// aangepast aan het tarief en volgt geen automatische verwerking.
/// </summary>
public static class Verwerkingsadviseur
{
    public static Verwerkingsadvies Advies(
        Verwerkingsgegevens gegevens, IEnumerable<Verwerkingsregel> regels, Leergeheugen geheugen, Relatie? relatie, IReadOnlyList<BtwCode> btwCodes)
    {
        ArgumentNullException.ThrowIfNull(gegevens);
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(geheugen);
        ArgumentNullException.ThrowIfNull(btwCodes);

        if (regels.FirstOrDefault(r => r.Past(gegevens)) is { } regel)
        {
            return Tarief(new Verwerkingsadvies(regel.Rekeningnummer, regel.BtwCode, Adviesbron.Regel, regel.Automatisch, $"Regel: {regel.Naam}", Omschrijving: regel.Omschrijving), gegevens, btwCodes);
        }

        if (geheugen.Voorstel(gegevens) is { } geleerd)
        {
            var reden = geleerd switch
            {
                { Automatisch: true } => $"Geleerd: {geleerd.OpRij} keer op rij dezelfde keuze",
                { Afwijkingen.Count: > 0 } when geleerd.OpRij >= geheugen.Drempel => $"Geleerd: {geleerd.OpRij} keer dezelfde keuze, nu ter controle",
                _ => $"Geleerd: vorige keuze ({geleerd.OpRij} van {geheugen.Drempel} voor automatisch)",
            };
            return Tarief(new Verwerkingsadvies(geleerd.Keuze.Rekeningnummer, geleerd.Keuze.BtwCode, Adviesbron.Geleerd, geleerd.Automatisch, reden,
                geleerd.Afwijkingen, geleerd.Omschrijving), gegevens, btwCodes);
        }

        if (relatie?.Tegenrekening is { } tegenrekening)
        {
            var btw = gegevens.Btwpercentage is { } pct ? BtwCodes.Bij(btwCodes, BtwRichting.Inkoop, pct)?.Code : null;
            return new Verwerkingsadvies(tegenrekening, btw, Adviesbron.Relatie, false, "Standaardrekening van de relatie");
        }

        return Verwerkingsadvies.Geen;
    }

    /// <summary>Past de btw-code aan het herkende tarief aan; bij een afwijking geen automatische verwerking.</summary>
    private static Verwerkingsadvies Tarief(Verwerkingsadvies advies, Verwerkingsgegevens gegevens, IReadOnlyList<BtwCode> btwCodes)
    {
        if (gegevens.Btwpercentage is not { } herkend || BtwCodes.Zoek(btwCodes, advies.BtwCode) is not { } code || code.Verlegd || code.Percentage == herkend)
        {
            return advies;
        }

        var passend = BtwCodes.Bij(btwCodes, code.Richting, herkend);
        return advies with
        {
            BtwCode = passend?.Code ?? advies.BtwCode,
            Automatisch = false,
            Reden = $"{advies.Reden}; op de factuur staat {herkend:0.##}% btw in plaats van {code.Percentage:0.##}%",
        };
    }
}
