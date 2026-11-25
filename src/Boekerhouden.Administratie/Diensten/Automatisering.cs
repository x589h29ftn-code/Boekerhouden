using Boekerhouden.Administratie.Data;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Hoeveel er vanzelf geboekt is: per boeking de verwerkingswijze, en het percentage over een lijst.</summary>
public static class Automatisering
{
    /// <summary>De naam in de kolom Verwerking; leeg als het niet bekend is.</summary>
    public static string Naam(Verwerkingswijze wijze) => wijze switch
    {
        Verwerkingswijze.Handmatig => "handmatig",
        Verwerkingswijze.Autocodering => "autocodering",
        Verwerkingswijze.Afgeletterd => "afgeletterd",
        Verwerkingswijze.Automatisch => "automatisch",
        _ => string.Empty,
    };

    /// <summary>De uitleg in de tooltip.</summary>
    public static string Uitleg(Verwerkingswijze wijze) => wijze switch
    {
        Verwerkingswijze.Handmatig => "Door de gebruiker geboekt",
        Verwerkingswijze.Autocodering => "Vanzelf geboekt: een regel of wat Sifer geleerd heeft",
        Verwerkingswijze.Afgeletterd => "Vanzelf afgeletterd op een openstaande post",
        Verwerkingswijze.Automatisch => "Vanzelf geboekt",
        _ => "Niet bekend: geboekt voordat dit werd bijgehouden, of ingelezen uit een auditfile",
    };

    public static bool IsAutomatisch(Verwerkingswijze wijze) =>
        wijze is Verwerkingswijze.Autocodering or Verwerkingswijze.Afgeletterd or Verwerkingswijze.Automatisch;

    /// <summary>
    /// Het deel dat vanzelf ging, in hele procenten, over de boekingen waarvan bekend is hoe ze geboekt zijn; leeg als
    /// er geen zijn. Met de aantallen per wijze voor de tooltip.
    /// </summary>
    public static (int? Procent, string Uitleg) Percentage(IEnumerable<Verwerkingswijze> wijzen)
    {
        ArgumentNullException.ThrowIfNull(wijzen);
        var bekend = wijzen.Where(w => w != Verwerkingswijze.Onbekend).ToList();
        if (bekend.Count == 0)
        {
            return (null, string.Empty);
        }

        var vanzelf = bekend.Count(IsAutomatisch);
        var procent = (int)Math.Round(100m * vanzelf / bekend.Count, MidpointRounding.ToZero);
        var uitleg = string.Join(", ", bekend.GroupBy(w => w).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Naam(g.Key)}"));
        return (procent, $"{vanzelf} van {bekend.Count} vanzelf geboekt ({uitleg})");
    }
}
