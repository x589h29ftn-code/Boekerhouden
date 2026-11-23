namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Eén boeking in het overzicht van een dagboek. Bij inkoop en verkoop het factuurnummer, de relatie en de factuur in
/// exclusief, btw en totaal (een creditnota negatief); bij bank en kas het bedrag op de bank- of kasrekening (bij = positief,
/// af = negatief); bij memoriaal het totaal van de boeking.
/// </summary>
public sealed record Dagboekregel(Journaalpost Post, string? Factuur, string? Relatie, decimal Exclusief, decimal Btw, decimal Bedrag);

/// <summary>Het overzicht van wat er in een dagboek geboekt is (Overzichten › Dagboeken).</summary>
public static class Dagboekoverzicht
{
    /// <summary>De volgorde van de tabbladen: inkoop, verkoop, bank, kas, memoriaal; binnen een soort op code.</summary>
    public static IReadOnlyList<Dagboek> Volgorde(IEnumerable<Dagboek> dagboeken) =>
        [.. dagboeken.OrderBy(d => d.Soort).ThenBy(d => d.Code, StringComparer.OrdinalIgnoreCase)];

    public static Dagboekregel Regel(Journaalpost post, Dagboek dagboek, Rekeningschema schema)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(dagboek);
        ArgumentNullException.ThrowIfNull(schema);
        Rekeningfunctie Functie(Journaalregel r) => schema.Zoek(r.Rekeningnummer)?.Functie ?? Rekeningfunctie.Geen;
        var factuur = post.Regels.Select(r => r.Factuurnummer).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
        var relatie = post.Regels.Select(r => r.Relatie).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));

        switch (dagboek.Soort)
        {
            case Dagboeksoort.Inkoop or Dagboeksoort.Verkoop:
                var verkoop = dagboek.Soort == Dagboeksoort.Verkoop;
                var open = post.Regels.Where(r => Functie(r) is Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren).ToList();
                if (open.Count == 0)
                {
                    return new(post, factuur, relatie, post.TotaalDebet, 0m, post.TotaalDebet);
                }

                // Verkoop: de debiteur staat debet (positief); inkoop: de crediteur staat credit (negatief).
                var teken = verkoop ? 1m : -1m;
                var totaal = teken * open.Sum(r => r.Bedrag);
                var btw = -teken * post.Regels.Where(r => Functie(r) is Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw).Sum(r => r.Bedrag);
                return new(post, factuur, relatie, totaal - btw, btw, totaal);
            case Dagboeksoort.Bank or Dagboeksoort.Kas:
                var eigen = dagboek.Rekeningnummer is { } nummer
                    ? post.Regels.Where(r => r.Rekeningnummer == nummer).ToList()
                    : post.Regels.Where(r => Functie(r) is Rekeningfunctie.Bank or Rekeningfunctie.Kas).ToList();
                return new(post, factuur, relatie, 0m, 0m, eigen.Count > 0 ? eigen.Sum(r => r.Bedrag) : post.TotaalDebet);
            default:
                return new(post, factuur, relatie, post.TotaalDebet, 0m, post.TotaalDebet);
        }
    }
}
