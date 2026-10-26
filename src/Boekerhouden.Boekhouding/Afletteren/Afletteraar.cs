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
public sealed record Afletterkandidaat(IReadOnlyList<OpenPost> Posten, Zekerheid Zekerheid, string Reden, decimal Verschil);

/// <summary>De kandidaten bij één bankmutatie, de sterkste eerst.</summary>
public sealed record Afletteradvies(Bankmutatie Mutatie, IReadOnlyList<Afletterkandidaat> Kandidaten)
{
    /// <summary>
    /// De kandidaat die automatisch geboekt mag worden: de enige zekere, en dan alleen als hij de mutatie precies
    /// dekt. Twee zekere kandidaten die elkaar tegenspreken, geven geen automatische boeking.
    /// </summary>
    public Afletterkandidaat? Automatisch =>
        Kandidaten.Where(k => k.Zekerheid == Zekerheid.Zeker).ToList() is [{ Verschil: 0 } enige] ? enige : null;
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
/// </summary>
public static class Afletteraar
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

        var posten = openPosten
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
        void Voegtoe(IReadOnlyList<OpenPost> lijst, Zekerheid zekerheid, string reden)
        {
            if (lijst.Count > 0 && !lijst.Any(p => gebruikt.Contains(p.Id)))
            {
                gebruikt.UnionWith(lijst.Select(p => p.Id));
                kandidaten.Add(new Afletterkandidaat(lijst, zekerheid, reden, lijst.Sum(p => p.Openstaand) - mutatie.Bedrag));
            }
        }

        // Een post van een andere relatie dan die bij het IBAN hoort, is nooit zeker.
        bool Past(OpenPost p) => viaIban.Count == 0 || viaIban.Contains(p.Relatie);

        // 1. Betalingskenmerk.
        foreach (var post in posten.Where(p => kenmerk.Length > 0 && Cijfers(p.Betalingskenmerk) == kenmerk))
        {
            var zeker = post.Openstaand == mutatie.Bedrag && Past(post);
            Voegtoe([post], zeker ? Zekerheid.Zeker : Zekerheid.Voorstel, zeker ? "Betalingskenmerk" : "Betalingskenmerk, ander bedrag");
        }

        // 2. Factuurnummer in de omschrijving.
        foreach (var post in posten.Where(p => Factuurnummers.KomtVoor(p.Factuurnummer, tekst)))
        {
            var zeker = post.Openstaand == mutatie.Bedrag && Past(post);
            Voegtoe([post], zeker ? Zekerheid.Zeker : Zekerheid.Voorstel, post.Openstaand == mutatie.Bedrag ? "Factuurnummer" : "Factuurnummer, ander bedrag");
        }

        // 3. IBAN en bedrag.
        var vanRelatie = posten.Where(p => viaIban.Contains(p.Relatie)).ToList();
        var gelijk = vanRelatie.Where(p => p.Openstaand == mutatie.Bedrag).ToList();
        foreach (var post in gelijk)
        {
            Voegtoe([post], gelijk.Count == 1 ? Zekerheid.Zeker : Zekerheid.Voorstel, gelijk.Count == 1 ? "IBAN en bedrag" : "IBAN en bedrag, meerdere facturen");
        }

        // 4. Verzamelbetaling: de som van enkele posten van de relatie.
        if (Combinatie(vanRelatie.Where(p => !gebruikt.Contains(p.Id)).Take(MaximumVoorVerzamelbetaling).ToList(), mutatie.Bedrag) is { } combinatie)
        {
            Voegtoe(combinatie, Zekerheid.Voorstel, $"IBAN en de som van {combinatie.Count} facturen");
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

    /// <summary>De kleinste combinatie (van minstens twee) posten die samen precies het bedrag zijn, de oudste eerst.</summary>
    private static List<OpenPost>? Combinatie(List<OpenPost> posten, decimal bedrag)
    {
        for (var aantal = 2; aantal <= posten.Count; aantal++)
        {
            if (Zoek(posten, bedrag, aantal, 0, []) is { } gevonden)
            {
                return gevonden;
            }
        }

        return null;
    }

    private static List<OpenPost>? Zoek(List<OpenPost> posten, decimal rest, int nodig, int vanaf, List<OpenPost> gekozen)
    {
        if (nodig == 0)
        {
            return rest == 0 ? [.. gekozen] : null;
        }

        for (var i = vanaf; i <= posten.Count - nodig; i++)
        {
            // Alle posten hebben hetzelfde teken als het bedrag: voorbij het bedrag schieten heeft geen zin.
            if (Math.Abs(posten[i].Openstaand) > Math.Abs(rest))
            {
                continue;
            }

            gekozen.Add(posten[i]);
            var gevonden = Zoek(posten, rest - posten[i].Openstaand, nodig - 1, i + 1, gekozen);
            gekozen.RemoveAt(gekozen.Count - 1);
            if (gevonden is not null)
            {
                return gevonden;
            }
        }

        return null;
    }

    private static string Cijfers(string? tekst) => tekst is null ? string.Empty : string.Concat(tekst.Where(char.IsAsciiDigit));
}
