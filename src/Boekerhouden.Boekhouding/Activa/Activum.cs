using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Activa;

public enum Afschrijvingsmethode
{
    /// <summary>Elke maand hetzelfde bedrag: (aanschafwaarde - restwaarde) gedeeld door de looptijd.</summary>
    Lineair,

    /// <summary>Niet afschrijven (bijv. grond).</summary>
    Geen,
}

/// <summary>Hoe vaak de afschrijvingen geboekt worden: één memoriaalboeking per maand, kwartaal of jaar.</summary>
public enum Afschrijvingsfrequentie
{
    Maand,
    Kwartaal,
    Jaar,
}

/// <summary>
/// Een vast actief (bedrijfsmiddel): wat het kostte, hoe lang het meegaat en op welke rekeningen het staat. De
/// aanschaf zelf staat al in het grootboek (meestal via de inkoopfactuur op de activarekening); de afschrijvingen
/// boekt het pakket per maand in het memoriaal: kosten (W&amp;V) tegen de cumulatieve afschrijving (balans).
/// </summary>
/// <param name="BeginAfschrijving">De eerste maand van afschrijven (meestal de maand van aanschaf of ingebruikname).</param>
/// <param name="Looptijd">In maanden vanaf <paramref name="BeginAfschrijving"/>.</param>
/// <param name="AfgeschrevenVoorBegin">Bij een actief uit een eerdere administratie: wat er vóór de eerste maand al
/// was afgeschreven (staat al op de afschrijvingsrekening); de looptijd is dan de resterende looptijd.</param>
public sealed record Activum(
    string Omschrijving,
    DateOnly Aanschafdatum,
    decimal Aanschafwaarde,
    decimal Restwaarde,
    DateOnly BeginAfschrijving,
    int Looptijd,
    Afschrijvingsmethode Methode,
    string Activarekening,
    string Afschrijvingsrekening,
    string Kostenrekening,
    decimal AfgeschrevenVoorBegin = 0m)
{
    /// <summary>Langste looptijd: vijftig jaar.</summary>
    public const int MaximaleLooptijd = 600;

    /// <summary>Wat er in totaal afgeschreven wordt (ook vóór de eerste maand).</summary>
    public decimal Afschrijfbaar => Methode == Afschrijvingsmethode.Geen ? 0m : Aanschafwaarde - Restwaarde;

    public DateOnly EersteMaand => new(BeginAfschrijving.Year, BeginAfschrijving.Month, 1);

    public IEnumerable<string> Controleer(Rekeningschema? schema = null)
    {
        if (string.IsNullOrWhiteSpace(Omschrijving))
        {
            yield return "Vul een omschrijving in.";
        }

        if (Aanschafwaarde <= 0)
        {
            yield return "De aanschafwaarde moet groter zijn dan 0.";
        }

        if (Restwaarde < 0 || Restwaarde > Aanschafwaarde)
        {
            yield return "De restwaarde ligt tussen 0 en de aanschafwaarde.";
        }

        if (Methode == Afschrijvingsmethode.Lineair && Looptijd is < 1 or > MaximaleLooptijd)
        {
            yield return $"De looptijd is 1 tot {MaximaleLooptijd} maanden.";
        }

        if (EersteMaand < new DateOnly(Aanschafdatum.Year, Aanschafdatum.Month, 1))
        {
            yield return "Afschrijven begint niet vóór de maand van aanschaf.";
        }

        if (AfgeschrevenVoorBegin < 0 || (Aanschafwaarde > 0 && AfgeschrevenVoorBegin > Afschrijfbaar && Methode == Afschrijvingsmethode.Lineair))
        {
            yield return "Wat al is afgeschreven, ligt tussen 0 en de aanschafwaarde min de restwaarde.";
        }

        if (schema is null)
        {
            yield break;
        }

        foreach (var (nummer, wat, balans) in new[] { (Activarekening, "activarekening", true), (Afschrijvingsrekening, "afschrijvingsrekening", true), (Kostenrekening, "kostenrekening", false) })
        {
            if (schema.Zoek(nummer) is not { } rekening)
            {
                yield return $"Kies een {wat}.";
            }
            else if (rekening.IsBalans != balans)
            {
                yield return balans ? $"De {wat} {rekening.Nummer} hoort op de balans." : $"De {wat} {rekening.Nummer} hoort in de winst-en-verliesrekening.";
            }
            else if (rekening.Functie != Rekeningfunctie.Geen)
            {
                yield return $"Rekening {rekening.Nummer} is voor de bank, debiteuren, crediteuren of btw; kies een andere {wat}.";
            }
        }
    }
}

/// <summary>Eén maand in het afschrijvingsplan; <see cref="Geboekt"/> als de maand al in het memoriaal staat.</summary>
/// <param name="Cumulatief">Totaal afgeschreven na deze maand (ook wat vóór de eerste maand al was afgeschreven).</param>
public sealed record Afschrijvingsmaand(DateOnly Maand, decimal Bedrag, decimal Cumulatief, decimal Boekwaarde, bool Geboekt);

/// <summary>
/// Het afschrijvingsplan van een actief: lineair per maand, tot op de cent verdeeld zodat het totaal precies klopt.
/// Al geboekte maanden houden hun bedrag; wijzigt de looptijd of restwaarde later, dan wordt wat er nog over is over de
/// resterende maanden verdeeld (een schattingswijziging werkt alleen vooruit).
/// </summary>
public static class Afschrijvingsplan
{
    public static IReadOnlyList<Afschrijvingsmaand> Maak(Activum activum, IReadOnlyDictionary<DateOnly, decimal>? geboekt = null)
    {
        ArgumentNullException.ThrowIfNull(activum);
        geboekt ??= new Dictionary<DateOnly, decimal>();
        var plan = new List<Afschrijvingsmaand>();
        var cumulatief = activum.AfgeschrevenVoorBegin;
        if (activum.Methode == Afschrijvingsmethode.Geen)
        {
            return [.. geboekt.OrderBy(g => g.Key).Select(g => new Afschrijvingsmaand(g.Key, g.Value, cumulatief += g.Value, activum.Aanschafwaarde - cumulatief, true))];
        }

        var maanden = Enumerable.Range(0, activum.Looptijd).Select(i => activum.EersteMaand.AddMonths(i)).ToList();
        // Geboekte maanden buiten de looptijd (bijv. na het inkorten) tellen ook mee.
        maanden = [.. maanden.Concat(geboekt.Keys.Where(k => !maanden.Contains(k))).Order()];
        var open = maanden.Where(m => !geboekt.ContainsKey(m) && m < activum.EersteMaand.AddMonths(activum.Looptijd)).ToList();
        var rest = activum.Afschrijfbaar - activum.AfgeschrevenVoorBegin - geboekt.Values.Sum();
        var verdeeld = Verdeel(rest, open.Count);
        var volgende = 0;
        foreach (var maand in maanden)
        {
            var isGeboekt = geboekt.TryGetValue(maand, out var bedrag);
            if (!isGeboekt)
            {
                if (volgende >= verdeeld.Count)
                {
                    continue;
                }

                bedrag = verdeeld[volgende++];
            }

            cumulatief += bedrag;
            plan.Add(new Afschrijvingsmaand(maand, bedrag, cumulatief, activum.Aanschafwaarde - cumulatief, isGeboekt));
        }

        return plan;
    }

    /// <summary>Verdeelt een bedrag over een aantal maanden in centen: elke maand het verschil van de afgeronde tussenstanden.</summary>
    public static IReadOnlyList<decimal> Verdeel(decimal totaal, int maanden)
    {
        if (maanden <= 0)
        {
            return [];
        }

        return [.. Enumerable.Range(0, maanden).Select(i =>
            Math.Round(totaal * (i + 1) / maanden, 2, MidpointRounding.AwayFromZero) - Math.Round(totaal * i / maanden, 2, MidpointRounding.AwayFromZero))];
    }

    /// <summary>Het einde van de periode (maand, kwartaal of jaar) waarin een maand valt: de datum van de afschrijvingsboeking.</summary>
    public static DateOnly Periodeeinde(DateOnly maand, Afschrijvingsfrequentie frequentie)
    {
        var laatste = frequentie switch
        {
            Afschrijvingsfrequentie.Kwartaal => ((maand.Month - 1) / 3 * 3) + 3,
            Afschrijvingsfrequentie.Jaar => 12,
            _ => maand.Month,
        };
        return new DateOnly(maand.Year, laatste, DateTime.DaysInMonth(maand.Year, laatste));
    }
}

/// <summary>
/// Een actief buiten gebruik stellen (verkoop, sloop, inruil): de aanschafwaarde en de cumulatieve afschrijving gaan
/// van de balans; de opbrengst (die de verkoopfactuur op een tussenrekening boekte) gaat van die rekening af en het
/// verschil met de boekwaarde is het boekresultaat.
/// </summary>
public static class Desinvestering
{
    /// <param name="Afgeschreven">Totaal afgeschreven tot nu (ook vóór de eerste maand).</param>
    /// <param name="Opbrengst">Opbrengst zonder btw; 0 bij sloop.</param>
    /// <param name="Opbrengstrekening">Waar de opbrengst nu op staat (credit); nodig als er een opbrengst is.</param>
    public static IReadOnlyList<Journaalregel> Regels(Activum activum, decimal afgeschreven, decimal opbrengst, string? opbrengstrekening, string resultaatrekening, string omschrijving)
    {
        ArgumentNullException.ThrowIfNull(activum);
        var boekwaarde = activum.Aanschafwaarde - afgeschreven;
        var resultaat = opbrengst - boekwaarde;
        var regels = new List<Journaalregel>();
        if (afgeschreven != 0)
        {
            regels.Add(new Journaalregel(activum.Afschrijvingsrekening, afgeschreven, omschrijving));
        }

        regels.Add(new Journaalregel(activum.Activarekening, -activum.Aanschafwaarde, omschrijving));
        if (opbrengst != 0)
        {
            regels.Add(new Journaalregel(opbrengstrekening ?? throw new ArgumentException("Kies de rekening waarop de opbrengst staat.", nameof(opbrengstrekening)), opbrengst, omschrijving));
        }

        if (resultaat != 0)
        {
            regels.Add(new Journaalregel(resultaatrekening, -resultaat, omschrijving));
        }

        return regels;
    }
}
