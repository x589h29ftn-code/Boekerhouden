namespace Boekerhouden.Boekhouding.Btw;

/// <summary>Hoe vaak er btw-aangifte wordt gedaan.</summary>
public enum Aangiftefrequentie
{
    Kwartaal,
    Maand,
    Jaar,
}

/// <summary>Een aangifteperiode: een maand, een kwartaal of een jaar.</summary>
public sealed record Aangifteperiode(DateOnly Van, DateOnly Tot, Aangiftefrequentie Frequentie)
{
    private static readonly string[] Maandnamen =
        ["januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december"];

    /// <summary>De periode waarin een datum valt.</summary>
    public static Aangifteperiode Waarin(DateOnly datum, Aangiftefrequentie frequentie)
    {
        var maanden = frequentie switch
        {
            Aangiftefrequentie.Maand => 1,
            Aangiftefrequentie.Kwartaal => 3,
            _ => 12,
        };
        var begin = new DateOnly(datum.Year, (((datum.Month - 1) / maanden) * maanden) + 1, 1);
        return new Aangifteperiode(begin, begin.AddMonths(maanden).AddDays(-1), frequentie);
    }

    /// <summary>De laatste periode die vóór de datum helemaal voorbij is (op 15 oktober: het 3e kwartaal).</summary>
    public static Aangifteperiode Afgelopen(DateOnly datum, Aangiftefrequentie frequentie) => Waarin(Waarin(datum, frequentie).Van.AddDays(-1), frequentie);

    /// <summary>De periode erna.</summary>
    public Aangifteperiode Volgende => Waarin(Tot.AddDays(1), Frequentie);

    /// <summary>Kort, als kolomkop: "3e kw", "sep" of "2026".</summary>
    public string Kort => Frequentie switch
    {
        Aangiftefrequentie.Maand => Maandnamen[Van.Month - 1][..3],
        Aangiftefrequentie.Kwartaal => $"{((Van.Month - 1) / 3) + 1}e kw",
        _ => $"{Van.Year}",
    };

    /// <summary>Bijv. "3e kwartaal 2026", "september 2026" of "2026".</summary>
    public string Naam => Frequentie switch
    {
        Aangiftefrequentie.Maand => $"{Maandnamen[Van.Month - 1]} {Van.Year}",
        Aangiftefrequentie.Kwartaal => $"{((Van.Month - 1) / 3) + 1}e kwartaal {Van.Year}",
        _ => $"{Van.Year}",
    };
}
