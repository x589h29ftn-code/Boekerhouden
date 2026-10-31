using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Afletteren;

public enum Zekerheid
{
    /// <summary>Waarschijnlijk; de gebruiker bevestigt.</summary>
    Voorstel,

    /// <summary>Zeker genoeg om automatisch te boeken.</summary>
    Zeker,
}

/// <summary>Een of meer openstaande posten die bij een bankmutatie kunnen horen.</summary>
/// <param name="Verschil">Wat er na afletteren openstaat (de som van de posten min de mutatie); 0 = precies betaald.</param>
/// <param name="Korting">De mutatie noemt een (betalings)korting: het verschil is waarschijnlijk korting, af te boeken op
/// een rekening in plaats van open te laten staan.</param>
/// <param name="NummerEnIban">Eén post waarvan de mutatie het factuurnummer of kenmerk noemt, van het IBAN van de relatie.</param>
public sealed record Afletterkandidaat(IReadOnlyList<OpenPost> Posten, Zekerheid Zekerheid, string Reden, decimal Verschil, bool Korting = false, bool NummerEnIban = false)
{
    /// <summary>Eén post waarvan na afletteren nog een deel openstaat (niet te veel betaald).</summary>
    public bool IsDeelbetaling => !Korting && Posten is [var post] && Verschil != 0 && Math.Sign(Verschil) == Math.Sign(post.Openstaand);

    /// <summary>De post was al helemaal betaald: deze betaling is er waarschijnlijk een te veel.</summary>
    public bool IsAlBetaald => Posten is [{ IsBetaald: true }];
}

/// <summary>De kandidaten bij één bankmutatie, de sterkste eerst.</summary>
public sealed record Afletteradvies(Bankmutatie Mutatie, IReadOnlyList<Afletterkandidaat> Kandidaten)
{
    /// <summary>
    /// De kandidaat die automatisch geboekt mag worden: de enige zekere, en dan alleen als hij de mutatie precies
    /// dekt of een deelbetaling is. Twee zekere kandidaten die elkaar tegenspreken, geven geen automatische boeking;
    /// noemt de mutatie een factuur die al betaald is, dan ook niet (mogelijk een dubbele betaling).
    /// </summary>
    public Afletterkandidaat? Automatisch =>
        !Kandidaten.Any(k => k.IsAlBetaald) && Kandidaten.Where(k => k.Zekerheid == Zekerheid.Zeker).ToList() is [var enige] && (enige.Verschil == 0 || enige.IsDeelbetaling)
            ? enige
            : null;

    /// <summary>
    /// De enige kandidaat waarvan nummer en IBAN kloppen, die op een klein verschil na precies betaald is, te veel of te
    /// weinig: het verschil mag worden afgeboekt (instelling betaalverschil). Niet als er ook een zekere kandidaat is
    /// die precies klopt, bij een factuur die al betaald was of bij betalingskorting (die heeft een eigen boeking).
    /// </summary>
    public Afletterkandidaat? KleinVerschil(decimal? grens) =>
        grens is > 0 && !Kandidaten.Any(k => k.IsAlBetaald || (k.Zekerheid == Zekerheid.Zeker && k.Verschil == 0))
        && Kandidaten.Where(k => k.NummerEnIban).ToList() is [{ Korting: false } enige]
        && enige.Verschil != 0 && Math.Abs(enige.Verschil) <= grens
            ? enige
            : null;
}

/// <summary>
/// Zoekt bij een bankmutatie de openstaande posten die erbij horen. Alleen posten met hetzelfde teken doen mee: een
/// ontvangst bij debiteuren, een betaling bij crediteuren (en omgekeerd bij creditfacturen). In volgorde van sterkte:
/// <list type="number">
/// <item>betalingskenmerk gelijk;</item>
/// <item>factuurnummer in de omschrijving;</item>
/// <item>IBAN van de relatie en hetzelfde bedrag (ook zonder factuurnummer);</item>
/// <item>IBAN van de relatie en de som van enkele posten (verzamelbetaling);</item>
/// <item>naam van de tegenpartij en hetzelfde bedrag;</item>
/// <item>alleen hetzelfde bedrag, als dat maar bij één post voorkomt.</item>
/// </list>
/// Vooraf: een verzamelbetaling van enkele posten van de relatie bij het IBAN (ook een factuur min een creditnota) die
/// samen precies het bedrag zijn; zeker als de mutatie elk factuurnummer noemt (of het laatste deel ervan, "1041 1042
/// 1043"). Een deelbetaling met het factuurnummer of betalingskenmerk én het IBAN van de leverancier is ook zeker: het
/// restant blijft openstaan; noemt de mutatie een korting, dan is het een voorstel (het verschil afboeken). Een betaling
/// naar een ander IBAN dan de leverancier bij ons bekend heeft, is nooit zeker. Een betaling met het nummer of kenmerk van een factuur die al betaald is, wordt nooit
/// automatisch geboekt: die staat als mogelijke dubbele betaling bij de mutatie.
/// </summary>
public static partial class Afletteraar
{
    /// <summary>Hoeveel posten van één relatie hoogstens worden gecombineerd bij het zoeken naar een verzamelbetaling.</summary>
    public const int MaximumVoorVerzamelbetaling = 15;

    public static Afletteradvies Zoek(Bankmutatie mutatie, IEnumerable<OpenPost> openPosten, IEnumerable<Relatie> relaties)
    {
        ArgumentNullException.ThrowIfNull(mutatie);
        ArgumentNullException.ThrowIfNull(openPosten);
        ArgumentNullException.ThrowIfNull(relaties);
        if (mutatie.Bedrag == 0)
        {
            return new Afletteradvies(mutatie, []);
        }

        var alle = openPosten.ToList();
        var posten = alle
            .Where(p => !p.IsBetaald && Math.Sign(p.Openstaand) == Math.Sign(mutatie.Bedrag))
            .OrderBy(p => p.Datum).ThenBy(p => p.Factuurnummer, StringComparer.Ordinal)
            .ToList();
        var relatielijst = relaties.ToList();
        var viaIban = relatielijst.Where(r => r.HeeftIban(mutatie.TegenIban)).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var viaNaam = relatielijst.Where(r => Namen.LijktOp(mutatie.TegenNaam, r.Naam)).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var tekst = string.Join(' ', mutatie.Omschrijving, mutatie.EndToEndId, mutatie.Betalingskenmerk);
        var kenmerk = Cijfers(mutatie.Betalingskenmerk);

        var kandidaten = new List<Afletterkandidaat>();
        var gebruikt = new HashSet<Guid>();
        var korting = KortingPatroon().IsMatch(tekst);
        void Voegtoe(IReadOnlyList<OpenPost> lijst, Zekerheid zekerheid, string reden, bool isKorting = false, bool nummerEnIban = false)
        {
            if (lijst.Count > 0 && !lijst.Any(p => gebruikt.Contains(p.Id)))
            {
                gebruikt.UnionWith(lijst.Select(p => p.Id));
                kandidaten.Add(new Afletterkandidaat(lijst, zekerheid, reden, lijst.Sum(p => p.Openstaand) - mutatie.Bedrag, isKorting, nummerEnIban));
            }
        }

        // Een betaling naar een ander IBAN dan wij van de relatie kennen (gewijzigd rekeningnummer, mogelijk fraude).
        var perCode = relatielijst.GroupBy(r => r.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        bool AnderIban(OpenPost p) => mutatie.TegenIban is { Length: > 0 } iban && perCode.TryGetValue(p.Relatie, out var r) && r.Ibans.Count > 0 && !r.HeeftIban(iban);

        // Een post van een andere relatie dan die bij het IBAN hoort, of bij een onbekend IBAN, is nooit zeker.
        bool Past(OpenPost p) => viaIban.Count == 0 ? !AnderIban(p) : viaIban.Contains(p.Relatie);

        // Minder dan wat openstaat, met het IBAN van de leverancier: een deelbetaling (geen korting).
        bool Deel(OpenPost p) => !korting && viaIban.Contains(p.Relatie) && Math.Abs(mutatie.Bedrag) < Math.Abs(p.Openstaand);

        // Iets minder dan wat openstaat (hoogstens 5%) en de mutatie noemt een korting.
        bool Korting(OpenPost p) => korting && Math.Abs(mutatie.Bedrag) < Math.Abs(p.Openstaand) && Math.Abs(p.Openstaand - mutatie.Bedrag) <= Math.Abs(p.Openstaand) * 0.05m;

        string Reden(string bron, OpenPost post) =>
            post.Openstaand == mutatie.Bedrag ? bron
            : Korting(post) ? $"{bron}, betalingskorting {Math.Round(Math.Abs(post.Openstaand - mutatie.Bedrag) / Math.Abs(post.Openstaand) * 100, 1):0.#}%"
            : Deel(post) ? $"{bron}, deelbetaling"
            : $"{bron}, ander bedrag";

        // 0. Verzamelbetaling met de nummers: enkele posten van de relatie bij het IBAN (ook met een creditnota) die
        // samen precies het bedrag zijn.
        var vanIban = alle.Where(p => viaIban.Contains(p.Relatie) && !p.IsBetaald).OrderBy(p => p.Datum).Take(MaximumVoorVerzamelbetaling).ToList();
        if (Combinatie(vanIban, mutatie.Bedrag) is { } genoemd && genoemd.All(p => Factuurnummers.Genoemd(p.Factuurnummer, tekst)))
        {
            Voegtoe(genoemd, Zekerheid.Zeker, $"Factuurnummers en de som van {genoemd.Count} posten");
        }

        // 1. Betalingskenmerk.
        foreach (var post in posten.Where(p => kenmerk.Length > 0 && Cijfers(p.Betalingskenmerk) == kenmerk))
        {
            var zeker = (post.Openstaand == mutatie.Bedrag && Past(post)) || (Deel(post) && Past(post));
            Voegtoe([post], zeker ? Zekerheid.Zeker : Zekerheid.Voorstel, Reden("Betalingskenmerk", post), Korting(post), viaIban.Contains(post.Relatie));
        }

        // 2. Factuurnummer in de omschrijving.
        foreach (var post in posten.Where(p => Factuurnummers.KomtVoor(p.Factuurnummer, tekst)))
        {
            var zeker = (post.Openstaand == mutatie.Bedrag && Past(post)) || (Deel(post) && Past(post));
            Voegtoe([post], zeker ? Zekerheid.Zeker : Zekerheid.Voorstel, Reden("Factuurnummer", post), Korting(post), viaIban.Contains(post.Relatie));
        }

        // 2b. Het nummer of kenmerk van een factuur die al betaald is (of waarop al te veel betaald is), met hetzelfde
        // bedrag: een dubbele betaling. Alleen een voorstel; afletteren zet het bedrag als te vorderen op die factuur.
        foreach (var post in alle.Where(p => Past(p) && p.Bedrag == mutatie.Bedrag && Math.Sign(p.Openstaand) != Math.Sign(mutatie.Bedrag)
                     && ((kenmerk.Length > 0 && Cijfers(p.Betalingskenmerk) == kenmerk) || Factuurnummers.KomtVoor(p.Factuurnummer, tekst))))
        {
            Voegtoe([post], Zekerheid.Voorstel, "Factuur is al betaald: dubbele betaling?");
        }

        // 3. IBAN en bedrag; noemt de mutatie een ander nummer in dezelfde reeks ("9100234603" bij post 9100234601), dan
        // is het waarschijnlijk een andere factuur: alleen een voorstel.
        var vanRelatie = posten.Where(p => viaIban.Contains(p.Relatie)).ToList();
        var gelijk = vanRelatie.Where(p => p.Openstaand == mutatie.Bedrag).ToList();
        foreach (var post in gelijk)
        {
            var ander = Factuurnummers.AnderNummerUitReeks(post.Factuurnummer, tekst);
            Voegtoe([post], gelijk.Count == 1 && !ander ? Zekerheid.Zeker : Zekerheid.Voorstel,
                ander ? "IBAN en bedrag, maar een ander factuurnummer" : gelijk.Count == 1 ? "IBAN en bedrag" : "IBAN en bedrag, meerdere facturen");
        }

        // 4. Verzamelbetaling: de som van enkele posten van de relatie (ook met een creditnota).
        if (Combinatie(vanIban.Where(p => !gebruikt.Contains(p.Id)).ToList(), mutatie.Bedrag) is { } combinatie)
        {
            Voegtoe(combinatie, Zekerheid.Voorstel, $"IBAN en de som van {combinatie.Count} posten");
        }

        // 5. Naam en bedrag.
        foreach (var post in posten.Where(p => viaNaam.Contains(p.Relatie) && p.Openstaand == mutatie.Bedrag && !gebruikt.Contains(p.Id)).ToList())
        {
            Voegtoe([post], Zekerheid.Voorstel, "Naam en bedrag");
        }

        // 6. Alleen het bedrag, als dat uniek is.
        if (posten.Where(p => p.Openstaand == mutatie.Bedrag).ToList() is [var enige] && !gebruikt.Contains(enige.Id))
        {
            Voegtoe([enige], Zekerheid.Voorstel, "Alleen bedrag");
        }

        return new Afletteradvies(mutatie, kandidaten
            .OrderByDescending(k => k.Zekerheid)
            .ToList());
    }

    /// <summary>
    /// De kleinste combinatie (van minstens twee) posten die samen precies het bedrag zijn, de oudste eerst. Posten
    /// van beide tekens doen mee (een factuur min een creditnota), maar minstens één heeft het teken van het bedrag.
    /// </summary>
    private static List<OpenPost>? Combinatie(List<OpenPost> posten, decimal bedrag)
    {
        for (var aantal = 2; aantal <= posten.Count; aantal++)
        {
            if (Zoek(posten, bedrag, bedrag, aantal, 0, []) is { } gevonden)
            {
                return gevonden;
            }
        }

        return null;
    }

    private static List<OpenPost>? Zoek(List<OpenPost> posten, decimal bedrag, decimal rest, int nodig, int vanaf, List<OpenPost> gekozen)
    {
        if (nodig == 0)
        {
            return rest == 0 && gekozen.Any(p => Math.Sign(p.Openstaand) == Math.Sign(bedrag)) ? [.. gekozen] : null;
        }

        for (var i = vanaf; i <= posten.Count - nodig; i++)
        {
            gekozen.Add(posten[i]);
            var gevonden = Zoek(posten, bedrag, rest - posten[i].Openstaand, nodig - 1, i + 1, gekozen);
            gekozen.RemoveAt(gekozen.Count - 1);
            if (gevonden is not null)
            {
                return gevonden;
            }
        }

        return null;
    }

    private static string Cijfers(string? tekst) => tekst is null ? string.Empty : string.Concat(tekst.Where(char.IsAsciiDigit));

    [GeneratedRegex(@"korting|discount|skonto", RegexOptions.IgnoreCase)]
    private static partial Regex KortingPatroon();
}
