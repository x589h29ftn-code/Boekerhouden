using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Facturen;

/// <summary>De bedragen zoals ze op de factuur staan, om de boeking mee te vergelijken.</summary>
/// <param name="Regels">Het aantal regels op de factuur; per regel mag de btw een cent afwijken door afronden.</param>
public sealed record Factuurbedragen(decimal? Totaal, decimal? Btw = null, int Regels = 1);

/// <summary>
/// Controleert of de bedragen van een boeking kloppen: de btw per regel bij het tarief (100,00 en 21% is 21,00), en
/// de boeking samen tegen het totaal en de btw van de factuur (100,00 + 21,00 = 121,00). Het levert signalen op;
/// de gebruiker kan bewust toch boeken (bijv. bij een afwijkende afronding van de leverancier).
/// </summary>
public static class Bedragcontrole
{
    public static IReadOnlyList<string> Controleer(IReadOnlyList<Factuurregel> regels, IReadOnlyList<BtwCode> codes, Factuurbedragen? factuur = null)
    {
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(codes);
        var signalen = new List<string>();
        var marge = 0.01m * Math.Max(1, factuur?.Regels ?? 1);
        for (var i = 0; i < regels.Count; i++)
        {
            var regel = regels[i];
            if (BtwCodes.Zoek(codes, regel.BtwCode) is not { } code || regel.Btw is not { } btw)
            {
                continue;
            }

            if (!code.BtwOpFactuur && btw != 0)
            {
                signalen.Add($"Regel {i + 1}: bij {code.Omschrijving.ToLowerInvariant()} staat geen btw op de factuur, maar wel {Bedragen.Euro(btw)} in de boeking.");
            }
            else if (code.BtwOpFactuur && Math.Abs(code.Bereken(regel.Bedrag) - btw) > marge)
            {
                signalen.Add($"Regel {i + 1}: {code.Percentage:0.##}% btw over {Bedragen.Euro(regel.Bedrag)} is {Bedragen.Euro(code.Bereken(regel.Bedrag))}, niet {Bedragen.Euro(btw)}.");
            }
        }

        if (factuur is null || regels.Any(r => BtwCodes.Zoek(codes, r.BtwCode) is null))
        {
            return signalen;
        }

        var boeking = new Factuur(Factuursoort.Inkoop, "-", "-", DateOnly.MinValue, DateOnly.MinValue, "-", regels);
        var totaal = boeking.Totaal(codes);
        if (factuur.Totaal is { } verwacht && totaal != verwacht)
        {
            signalen.Add($"De boeking ({Bedragen.Euro(totaal)}) sluit niet op het totaal van de factuur ({Bedragen.Euro(verwacht)}); verschil {Bedragen.Euro(totaal - verwacht)}.");
        }

        var btwBoeking = boeking.TotaalBtw(codes);
        if (factuur.Btw is { } btwFactuur && Math.Abs(btwBoeking - btwFactuur) > marge)
        {
            signalen.Add($"De btw in de boeking ({Bedragen.Euro(btwBoeking)}) is niet de btw op de factuur ({Bedragen.Euro(btwFactuur)}).");
        }

        return signalen;
    }
}
