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
    /// <param name="geboekt">De geboekte afschrijvingen per maand (eerste dag).</param>
    /// <param name="desinvesteringen">Wat er (deels) van het actief af is gegaan; na de maand van een desinvestering
    /// rekent het plan met de rest: lagere aanschafwaarde, cumulatieve afschrijving en restwaarde.</param>
    public static IReadOnlyList<Afschrijvingsmaand> Maak(Activum activum, IReadOnlyDictionary<DateOnly, decimal>? geboekt = null, IReadOnlyList<Desinvesteringsstap>? desinvesteringen = null)
    {
        ArgumentNullException.ThrowIfNull(activum);
        geboekt ??= new Dictionary<DateOnly, decimal>();
        var stappen = (desinvesteringen ?? []).OrderBy(d => d.Datum).ToList();
        var aanschaf = activum.Aanschafwaarde;
        var cumulatief = activum.AfgeschrevenVoorBegin;
        var volgendeStap = 0;
        void PasToe(DateOnly maand)
        {
            // Een desinvestering telt na de maand waarin hij valt (de afschrijving van die maand gaat er nog aan vooraf).
            while (volgendeStap < stappen.Count && new DateOnly(stappen[volgendeStap].Datum.Year, stappen[volgendeStap].Datum.Month, 1) < maand)
            {
                aanschaf -= stappen[volgendeStap].Aanschaf;
                cumulatief -= stappen[volgendeStap].Afschrijving;
                volgendeStap++;
            }
        }

        var plan = new List<Afschrijvingsmaand>();
        if (activum.Methode == Afschrijvingsmethode.Geen)
        {
            foreach (var (maand, bedrag) in geboekt.OrderBy(g => g.Key))
            {
                PasToe(maand);
                cumulatief += bedrag;
                plan.Add(new Afschrijvingsmaand(maand, bedrag, cumulatief, aanschaf - cumulatief, true));
            }

            return plan;
        }

        var maanden = Enumerable.Range(0, activum.Looptijd).Select(i => activum.EersteMaand.AddMonths(i)).ToList();
        // Geboekte maanden buiten de looptijd (bijv. na het inkorten) tellen ook mee.
        maanden = [.. maanden.Concat(geboekt.Keys.Where(k => !maanden.Contains(k))).Order()];
        var open = maanden.Where(m => !geboekt.ContainsKey(m) && m < activum.EersteMaand.AddMonths(activum.Looptijd)).ToList();

        // Wat er nog af moet: de stand na alle desinvesteringen min wat al geboekt is.
        var eindAanschaf = activum.Aanschafwaarde - stappen.Sum(d => d.Aanschaf);
        var eindRest = activum.Restwaarde - stappen.Sum(d => d.Restwaarde);
        var eindCumulatief = activum.AfgeschrevenVoorBegin + geboekt.Values.Sum() - stappen.Sum(d => d.Afschrijving);
        var rest = eindAanschaf - eindRest - eindCumulatief;
        var verdeeld = rest == 0 ? [] : Verdeel(rest, open.Count);
        var volgende = 0;
        foreach (var maand in maanden)
        {
            PasToe(maand);
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
            plan.Add(new Afschrijvingsmaand(maand, bedrag, cumulatief, aanschaf - cumulatief, isGeboekt));
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

public enum Desinvesteringssoort
{
    /// <summary>Verkocht; de opbrengst staat op een tussenrekening (bijv. via de verkoopfactuur).</summary>
    Verkoop,

    /// <summary>Ingeruild bij de aankoop van een nieuw actief; de inruilwaarde is de opbrengst.</summary>
    Inruil,

    /// <summary>Gesloopt, weggegooid, gestolen of afgestoten zonder opbrengst.</summary>
    Sloop,
}

/// <summary>Wat er bij een desinvestering van een actief af ging: voor het plan en de activastaat.</summary>
public sealed record Desinvesteringsstap(DateOnly Datum, decimal Aanschaf, decimal Afschrijving, decimal Restwaarde);

/// <summary>De bedragen van een (gedeeltelijke) desinvestering.</summary>
/// <param name="Aanschaf">Het deel van de aanschafwaarde dat van de balans gaat.</param>
/// <param name="Afschrijving">Het deel van de cumulatieve afschrijving dat van de balans gaat.</param>
/// <param name="Restwaarde">Het deel van de restwaarde dat meegaat (voor het plan van wat overblijft).</param>
public sealed record Desinvesteringsbedragen(decimal Aanschaf, decimal Afschrijving, decimal Restwaarde, decimal Opbrengst)
{
    public decimal Boekwaarde => Aanschaf - Afschrijving;

    /// <summary>Boekwinst (positief) of boekverlies (negatief).</summary>
    public decimal Resultaat => Opbrengst - Boekwaarde;
}

/// <summary>
/// Een actief (geheel of voor een deel) desinvesteren: verkoop, inruil of sloop. Het deel van de aanschafwaarde en van
/// de cumulatieve afschrijving gaat van de balans; de opbrengst (die de verkoopfactuur of de inkoopfactuur van het
/// nieuwe actief op een tussenrekening boekte) gaat van die rekening af en het verschil met de boekwaarde is het
/// boekresultaat.
/// </summary>
public static class Desinvestering
{
    /// <summary>De bedragen voor een deel (0 &lt; deel ≤ 1) van de huidige stand, in centen; bij het hele actief precies de stand.</summary>
    public static Desinvesteringsbedragen Bereken(decimal aanschaf, decimal afgeschreven, decimal restwaarde, decimal deel, decimal opbrengst)
    {
        if (deel is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deel), "Het deel ligt tussen 0 en 100%.");
        }

        static decimal Cent(decimal bedrag) => Math.Round(bedrag, 2, MidpointRounding.AwayFromZero);
        return deel == 1
            ? new(aanschaf, afgeschreven, restwaarde, opbrengst)
            : new(Cent(aanschaf * deel), Cent(afgeschreven * deel), Cent(restwaarde * deel), opbrengst);
    }

    /// <summary>De memoriaalregels: cumulatieve afschrijving debet, aanschafwaarde credit, opbrengst debet, resultaat.</summary>
    /// <param name="opbrengstrekening">Waar de opbrengst nu op staat (credit); nodig als er een opbrengst is.</param>
    public static IReadOnlyList<Journaalregel> Regels(Activum activum, Desinvesteringsbedragen bedragen, string? opbrengstrekening, string resultaatrekening, string omschrijving)
    {
        ArgumentNullException.ThrowIfNull(activum);
        ArgumentNullException.ThrowIfNull(bedragen);
        var regels = new List<Journaalregel>();
        if (bedragen.Afschrijving != 0)
        {
            regels.Add(new Journaalregel(activum.Afschrijvingsrekening, bedragen.Afschrijving, omschrijving));
        }

        regels.Add(new Journaalregel(activum.Activarekening, -bedragen.Aanschaf, omschrijving));
        if (bedragen.Opbrengst != 0)
        {
            regels.Add(new Journaalregel(opbrengstrekening ?? throw new ArgumentException("Kies de rekening waarop de opbrengst staat.", nameof(opbrengstrekening)), bedragen.Opbrengst, omschrijving));
        }

        if (bedragen.Resultaat != 0)
        {
            regels.Add(new Journaalregel(resultaatrekening, -bedragen.Resultaat, omschrijving));
        }

        return regels;
    }

    /// <summary>Het hele actief in één keer (zonder restwaarde-deel).</summary>
    public static IReadOnlyList<Journaalregel> Regels(Activum activum, decimal afgeschreven, decimal opbrengst, string? opbrengstrekening, string resultaatrekening, string omschrijving)
    {
        ArgumentNullException.ThrowIfNull(activum);
        return Regels(activum, Bereken(activum.Aanschafwaarde, afgeschreven, activum.Restwaarde, 1m, opbrengst), opbrengstrekening, resultaatrekening, omschrijving);
    }

    /// <summary>De omschrijving van de boeking, bijv. "Verkoop 3 Bestelauto" of "Verkoop 40% 3 Bestelauto".</summary>
    public static string Omschrijving(Desinvesteringssoort soort, decimal deel, int nummer, string actief) =>
        $"{soort switch { Desinvesteringssoort.Inruil => "Inruil", Desinvesteringssoort.Sloop => "Sloop", _ => "Verkoop" }}{(deel == 1 ? "" : $" {deel * 100:0.##}%")} {nummer} {actief}";
}
