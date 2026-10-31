using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>Zet een bankmutatie om in een journaalpost in het bankdagboek.</summary>
public static class Bankboeker
{
    /// <summary>
    /// Lettert een mutatie af op openstaande posten, de oudste eerst. Elke post wordt zo ver mogelijk betaald; wat
    /// overblijft (te veel betaald, of een dubbele betaling van een factuur die al betaald was) blijft op de laatste
    /// post staan als te vorderen. Geeft ook de bijgewerkte openstaande posten.
    /// </summary>
    public static (Journaalpost Post, IReadOnlyList<OpenPost> Bijgewerkt) Afletteren(
        Bankmutatie mutatie, IReadOnlyList<OpenPost> posten, Dagboek dagboek, string boekstuk, Rekeningschema schema)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        var regels = new List<Journaalregel> { new(BankRekening(dagboek), mutatie.Bedrag, mutatie.Omschrijving) };
        var bijgewerkt = new List<OpenPost>();
        var rest = mutatie.Bedrag;
        string Tegenrekening(OpenPost post) => schema.Enige(post.Soort == Factuursoort.Verkoop ? Rekeningfunctie.Debiteuren : Rekeningfunctie.Crediteuren).Nummer;
        foreach (var post in posten.OrderBy(p => p.Datum))
        {
            if (rest == 0 || Math.Sign(post.Openstaand) != Math.Sign(rest))
            {
                continue;
            }

            var deel = Math.Abs(post.Openstaand) <= Math.Abs(rest) ? post.Openstaand : rest;
            regels.Add(new Journaalregel(Tegenrekening(post), -deel, $"Betaling {post.Factuurnummer}", null, post.Relatie, post.Factuurnummer));
            bijgewerkt.Add(post with { Openstaand = post.Openstaand - deel });
            rest -= deel;
        }

        // Te veel betaald (of een factuur die al betaald was): het verschil blijft op de laatste factuur staan, als te
        // vorderen van (of terug te betalen aan) de relatie. Zonder factuur naar de vraagposten.
        if (rest != 0 && posten.OrderBy(p => p.Datum).LastOrDefault() is { } laatste)
        {
            var huidig = bijgewerkt.FirstOrDefault(p => p.Id == laatste.Id) ?? laatste;
            regels.Add(new Journaalregel(Tegenrekening(laatste), -rest, $"Te veel betaald {laatste.Factuurnummer}", null, laatste.Relatie, laatste.Factuurnummer));
            bijgewerkt.RemoveAll(p => p.Id == laatste.Id);
            bijgewerkt.Add(huidig with { Openstaand = huidig.Openstaand - rest });
        }
        else if (rest != 0)
        {
            regels.Add(new Journaalregel(schema.Enige(Rekeningfunctie.Vraagposten).Nummer, -rest, "Niet afgeletterd deel"));
        }

        return (new Journaalpost(dagboek!.Code, boekstuk, mutatie.Datum, Omschrijving(mutatie), regels), bijgewerkt);
    }

    /// <summary>
    /// Boekt een mutatie rechtstreeks op een grootboekrekening, zoals bankkosten, rente of een betaling zonder
    /// factuur. Met een btw-code wordt de btw uit het bedrag gehaald en op de btw-rekening geboekt.
    /// </summary>
    public static Journaalpost OpRekening(
        Bankmutatie mutatie, string rekeningnummer, BtwCode? btw, Dagboek dagboek, string boekstuk, Rekeningschema schema, string? omschrijving = null)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        ArgumentNullException.ThrowIfNull(schema);
        var tekst = omschrijving ?? Omschrijving(mutatie);
        var regels = new List<Journaalregel> { new(BankRekening(dagboek), mutatie.Bedrag, tekst) };
        if (btw is { BtwOpFactuur: true })
        {
            // Een betaling (negatief) is een uitgave: grondslag en btw komen debet.
            var (grondslag, bedrag) = BtwBerekening.UitInclusief(-mutatie.Bedrag, btw.Percentage);
            var functie = btw.Richting == BtwRichting.Inkoop ? Rekeningfunctie.Voorbelasting : Rekeningfunctie.AfTeDragenBtw;
            regels.Add(new Journaalregel(rekeningnummer, grondslag, tekst, btw.Code, mutatie.TegenNaam));
            regels.Add(new Journaalregel(schema.Enige(functie).Nummer, bedrag, $"Btw {btw.Omschrijving.ToLowerInvariant()}", btw.Code, mutatie.TegenNaam));
        }
        else
        {
            regels.Add(new Journaalregel(rekeningnummer, -mutatie.Bedrag, tekst, btw?.Code, mutatie.TegenNaam));
        }

        return new Journaalpost(dagboek!.Code, boekstuk, mutatie.Datum, tekst, regels);
    }

    private static string BankRekening(Dagboek? dagboek)
    {
        ArgumentNullException.ThrowIfNull(dagboek);
        return dagboek.Soort is Dagboeksoort.Bank or Dagboeksoort.Kas && dagboek.Rekeningnummer is { } nummer
            ? nummer
            : throw new ArgumentException($"Dagboek {dagboek.Code} is geen bank- of kasdagboek met een rekening.", nameof(dagboek));
    }

    private static string Omschrijving(Bankmutatie mutatie)
    {
        var tekst = string.Join(" – ", new[] { mutatie.TegenNaam, mutatie.Omschrijving }.Where(t => !string.IsNullOrWhiteSpace(t)));
        return tekst.Length switch
        {
            0 => "Bankmutatie",
            > 200 => tekst[..200],
            _ => tekst,
        };
    }
}
