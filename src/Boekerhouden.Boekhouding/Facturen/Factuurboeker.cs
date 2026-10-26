using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Facturen;

/// <summary>
/// Zet een factuur om in een journaalpost en een openstaande post.
/// <list type="bullet">
/// <item>Inkoop: kosten en voorbelasting debet, crediteuren credit. Bij verlegde btw komt de btw zowel debet op de
/// voorbelasting als credit op de af te dragen btw.</item>
/// <item>Verkoop: debiteuren debet, omzet en af te dragen btw credit.</item>
/// </list>
/// Btw wordt per tarief opgeteld en per tarief één keer geboekt, met de btw-code erbij voor de aangifte.
/// </summary>
public static class Factuurboeker
{
    public static (Journaalpost Post, OpenPost OpenPost) Boek(Factuur factuur, Dagboek dagboek, string boekstuk, Rekeningschema schema, IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(dagboek);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(codes);
        var fouten = Controleer(factuur, dagboek, codes);
        if (fouten.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", fouten), nameof(factuur));
        }

        var inkoop = factuur.Soort == Factuursoort.Inkoop;
        var teken = inkoop ? 1 : -1;
        var regels = new List<Journaalregel>();
        foreach (var regel in factuur.Regels)
        {
            regels.Add(new Journaalregel(regel.Rekeningnummer, teken * regel.Bedrag, regel.Omschrijving, regel.BtwCode, factuur.Relatie, factuur.Factuurnummer));
        }

        foreach (var perCode in factuur.Regels.GroupBy(r => BtwCodes.Zoek(codes, r.BtwCode)!))
        {
            var code = perCode.Key;
            var btw = perCode.Sum(r => Factuur.BtwVan(r, codes));
            if (btw == 0)
            {
                continue;
            }

            var omschrijving = $"Btw {code.Omschrijving.ToLowerInvariant()}";
            if (inkoop)
            {
                regels.Add(new Journaalregel(schema.Enige(Rekeningfunctie.Voorbelasting).Nummer, btw, omschrijving, code.Code, factuur.Relatie, factuur.Factuurnummer));
                if (code.Verlegd)
                {
                    regels.Add(new Journaalregel(schema.Enige(Rekeningfunctie.AfTeDragenBtw).Nummer, -btw, omschrijving, code.Code, factuur.Relatie, factuur.Factuurnummer));
                }
            }
            else
            {
                regels.Add(new Journaalregel(schema.Enige(Rekeningfunctie.AfTeDragenBtw).Nummer, -btw, omschrijving, code.Code, factuur.Relatie, factuur.Factuurnummer));
            }
        }

        var totaal = factuur.Totaal(codes);
        var tegenrekening = schema.Enige(inkoop ? Rekeningfunctie.Crediteuren : Rekeningfunctie.Debiteuren);
        regels.Insert(0, new Journaalregel(tegenrekening.Nummer, -teken * totaal, factuur.Omschrijving, null, factuur.Relatie, factuur.Factuurnummer));

        var post = new Journaalpost(dagboek.Code, boekstuk, factuur.Datum, factuur.Omschrijving, regels);
        var openPost = new OpenPost(Guid.NewGuid(), factuur.Soort, factuur.Relatie, factuur.Factuurnummer, factuur.Datum, factuur.Vervaldatum, -teken * totaal, -teken * totaal, factuur.Betalingskenmerk);
        return (post, openPost);
    }

    public static IReadOnlyList<string> Controleer(Factuur factuur, Dagboek dagboek, IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(dagboek);
        ArgumentNullException.ThrowIfNull(codes);
        var fouten = new List<string>();
        var verwacht = factuur.Soort == Factuursoort.Inkoop ? Dagboeksoort.Inkoop : Dagboeksoort.Verkoop;
        if (dagboek.Soort != verwacht)
        {
            fouten.Add($"Een {factuur.Soort.ToString().ToLowerInvariant()}factuur hoort in een {verwacht.ToString().ToLowerInvariant()}dagboek.");
        }

        if (string.IsNullOrWhiteSpace(factuur.Relatie))
        {
            fouten.Add("Kies een relatie.");
        }

        if (string.IsNullOrWhiteSpace(factuur.Factuurnummer))
        {
            fouten.Add("Vul het factuurnummer in.");
        }

        if (factuur.Regels.Count == 0)
        {
            fouten.Add("De factuur heeft geen regels.");
        }

        var richting = factuur.Soort == Factuursoort.Inkoop ? BtwRichting.Inkoop : BtwRichting.Verkoop;
        for (var i = 0; i < factuur.Regels.Count; i++)
        {
            var regel = factuur.Regels[i];
            var code = BtwCodes.Zoek(codes, regel.BtwCode);
            if (code is null)
            {
                fouten.Add($"Regel {i + 1}: btw-code {regel.BtwCode} bestaat niet.");
            }
            else if (code.Richting != richting)
            {
                fouten.Add($"Regel {i + 1}: btw-code {code.Code} is voor de {code.Richting.ToString().ToLowerInvariant()}.");
            }
        }

        if (factuur.Vervaldatum < factuur.Datum)
        {
            fouten.Add("De vervaldatum ligt vóór de factuurdatum.");
        }

        return fouten;
    }
}
