using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>
/// De eisen aan een verkoopfactuur (Wet OB art. 35a) en aan de btw, gecontroleerd vóór het definitief maken: naam en
/// adres van beide partijen, het eigen btw-nummer, een omschrijving per regel, verkoopcodes, en bij verlegde btw of een
/// levering binnen de EU het (geldige) btw-nummer van de afnemer. Plus de vermeldingen die bij zo'n btw-code op de
/// factuur moeten staan.
/// </summary>
public static class Verkoopcontrole
{
    public static IReadOnlyList<string> Controleer(Verkoopfactuur factuur, Partij afzender, IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(afzender);
        ArgumentNullException.ThrowIfNull(codes);
        var fouten = new List<string>();
        if (string.IsNullOrWhiteSpace(afzender.Naam) || afzender.Adres.IsLeeg)
        {
            fouten.Add("Vul naam en adres van de eigen onderneming in (Instellingen).");
        }

        if (string.IsNullOrWhiteSpace(factuur.Relatie) || string.IsNullOrWhiteSpace(factuur.Klant.Naam))
        {
            fouten.Add("Kies een klant.");
        }
        else if (factuur.Klant.Adres.IsLeeg)
        {
            fouten.Add($"Vul het adres van {factuur.Klant.Naam} in.");
        }

        if (factuur.Regels.Count == 0)
        {
            fouten.Add("De factuur heeft geen regels.");
        }

        if (factuur.Vervaldatum < factuur.Datum)
        {
            fouten.Add("De vervaldatum ligt vóór de factuurdatum.");
        }

        var gebruikt = new List<BtwCode>();
        for (var i = 0; i < factuur.Regels.Count; i++)
        {
            var regel = factuur.Regels[i];
            if (string.IsNullOrWhiteSpace(regel.Omschrijving))
            {
                fouten.Add($"Regel {i + 1}: vul de omschrijving in (wat is er geleverd?).");
            }

            if (string.IsNullOrWhiteSpace(regel.Rekeningnummer))
            {
                fouten.Add($"Regel {i + 1}: kies de omzetrekening.");
            }

            if (regel.Korting is < 0 or > 100)
            {
                fouten.Add($"Regel {i + 1}: de korting ligt tussen 0 en 100%.");
            }

            var code = BtwCodes.Zoek(codes, regel.BtwCode);
            if (code is null)
            {
                fouten.Add($"Regel {i + 1}: kies een btw-code.");
            }
            else if (code.Richting != BtwRichting.Verkoop)
            {
                fouten.Add($"Regel {i + 1}: btw-code {code.Code} is voor de inkoop.");
            }
            else
            {
                gebruikt.Add(code);
            }
        }

        var alleenVrijgesteld = gebruikt.Count > 0 && gebruikt.All(c => c.Soort == Btwsoort.VerkoopVrijgesteld);
        if (!alleenVrijgesteld && string.IsNullOrWhiteSpace(afzender.BtwNummer))
        {
            fouten.Add("Vul het btw-nummer van de eigen onderneming in (Instellingen); dat moet op de factuur staan.");
        }

        if (!factuur.IsCredit && afzender.Ibans is not { Count: > 0 })
        {
            fouten.Add("Vul het eigen IBAN in (Instellingen); de klant moet weten waar hij betaalt.");
        }

        var klantnummer = factuur.Klant.BtwNummer is { } k ? BtwNummer.Normaliseer(k) : null;
        if (gebruikt.Any(c => c.Soort == Btwsoort.VerkoopVerlegd) && !BtwNummer.IsGeldig(klantnummer))
        {
            fouten.Add("Bij verlegde btw moet het btw-nummer van de klant op de factuur; vul een geldig btw-nummer in bij de klant.");
        }

        if (gebruikt.Any(c => c.Soort is Btwsoort.VerkoopBinnenEu))
        {
            if (!BtwNummer.IsGeldig(klantnummer))
            {
                fouten.Add("Bij een levering binnen de EU moet het btw-nummer van de klant op de factuur; vul een geldig btw-nummer in bij de klant.");
            }
            else if (klantnummer!.StartsWith("NL", StringComparison.Ordinal) || !BtwNummer.IsEuLand(klantnummer[..2]))
            {
                fouten.Add("Een levering binnen de EU (ICP) is aan een klant met een btw-nummer uit een ander EU-land.");
            }
        }

        return fouten;
    }

    /// <summary>Wat er bij de gebruikte btw-codes op de factuur moet staan, bijv. "Btw verlegd".</summary>
    public static IReadOnlyList<string> Vermeldingen(Verkoopfactuur factuur, IReadOnlyList<BtwCode> codes)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(codes);
        return
        [
            .. factuur.Regels.Select(r => BtwCodes.Zoek(codes, r.BtwCode)?.Soort).OfType<Btwsoort>().Distinct().Select(soort => soort switch
            {
                Btwsoort.VerkoopVerlegd => "Btw verlegd.",
                Btwsoort.VerkoopBinnenEu => "Intracommunautaire levering, btw verlegd (art. 138 Btw-richtlijn).",
                Btwsoort.VerkoopBuitenEu => "Uitvoer buiten de EU, 0% btw.",
                Btwsoort.VerkoopVrijgesteld => "Vrijgesteld van btw.",
                _ => null,
            }).OfType<string>(),
        ];
    }
}
