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
public sealed record Verwerkingsadvies(string? Rekeningnummer, string? BtwCode, Adviesbron Bron, bool Automatisch, string Reden)
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
            return Tarief(new Verwerkingsadvies(regel.Rekeningnummer, regel.BtwCode, Adviesbron.Regel, regel.Automatisch, $"Regel: {regel.Naam}"), gegevens, btwCodes);
        }

        if (geheugen.Voorstel(gegevens) is { } geleerd)
        {
            var reden = geleerd.Automatisch ? $"Geleerd: {geleerd.OpRij} keer op rij dezelfde keuze" : "Geleerd: vorige keuze";
            return Tarief(new Verwerkingsadvies(geleerd.Keuze.Rekeningnummer, geleerd.Keuze.BtwCode, Adviesbron.Geleerd, geleerd.Automatisch, reden), gegevens, btwCodes);
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
