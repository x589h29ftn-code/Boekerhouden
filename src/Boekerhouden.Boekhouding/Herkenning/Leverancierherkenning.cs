using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>De herkende relatie en waarop.</summary>
public sealed record Herkendeleverancier(Relatie Relatie, string Op);

/// <summary>
/// Zoekt de leverancier van een herkende factuur tussen de relaties, in volgorde van betrouwbaarheid: btw-nummer,
/// IBAN, KvK-nummer en ten slotte de naam (alleen als die bij precies één relatie past).
/// </summary>
public static class Leverancierherkenning
{
    public static Herkendeleverancier? Zoek(HerkendeFactuur factuur, IEnumerable<Relatie> relaties)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(relaties);
        var lijst = relaties.ToList();
        if (factuur.BtwNummer is { } btw && lijst.FirstOrDefault(r => r.BtwNummer is not null && BtwNummer.Normaliseer(r.BtwNummer) == BtwNummer.Normaliseer(btw)) is { } opBtw)
        {
            return new(opBtw, "btw-nummer");
        }

        if (factuur.Iban is { } iban && lijst.FirstOrDefault(r => r.HeeftIban(iban)) is { } opIban)
        {
            return new(opIban, "IBAN");
        }

        if (factuur.KvkNummer is { } kvk && lijst.FirstOrDefault(r => r.KvkNummer == kvk) is { } opKvk)
        {
            return new(opKvk, "KvK-nummer");
        }

        var opNaam = lijst.Where(r => Namen.Normaliseer(r.Naam) == Namen.Normaliseer(factuur.Naam) && Namen.Normaliseer(r.Naam).Length > 0).ToList();
        if (opNaam.Count == 0)
        {
            opNaam = lijst.Where(r => Namen.LijktOp(r.Naam, factuur.Naam)).ToList();
        }

        return opNaam is [var enige] ? new(enige, "naam") : null;
    }
}
