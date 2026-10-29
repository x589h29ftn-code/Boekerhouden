using System.Globalization;

namespace Boekerhouden.Boekhouding.Btw;

public enum BtwRichting
{
    Inkoop,
    Verkoop,
}

/// <summary>Het tarief van een btw-code; het percentage hangt af van de datum (<see cref="Btwtarieven"/>).</summary>
public enum Tarief
{
    Hoog,
    Laag,
    Nul,

    /// <summary>Geen btw: vrijgesteld, buiten de btw, of verlegd naar de afnemer.</summary>
    Geen,
}

/// <summary>Wat een btw-code in de aangifte omzetbelasting doet (zie <see cref="Btwsoorten"/>).</summary>
public enum Btwsoort
{
    VerkoopHoog,
    VerkoopLaag,
    VerkoopNul,
    VerkoopVerlegd,
    VerkoopBuitenEu,
    VerkoopBinnenEu,
    VerkoopAfstand,
    VerkoopVrijgesteld,
    InkoopVoorbelasting,
    InkoopNul,
    InkoopVerlegd,
    InkoopBuitenEu,
    InkoopBinnenEu,
    GeenBtw,
}

/// <summary>
/// De koppeling van een btw-soort met de aangifte: in welke rubriek de grondslag en de btw komen, en of de btw als
/// voorbelasting (5b) wordt afgetrokken.
/// </summary>
/// <param name="Naam">Kort, voor in een keuzelijst, bijv. "Verlegd naar u".</param>
/// <param name="RubriekGrondslag">Waar de grondslag (het bedrag zonder btw) wordt aangegeven, of leeg.</param>
/// <param name="RubriekBtw">Waar de verschuldigde btw wordt aangegeven, of leeg.</param>
/// <param name="Voorbelasting">De btw is aftrekbaar als voorbelasting (rubriek 5b).</param>
/// <param name="Verlegd">De ondernemer berekent de btw zelf (verlegd); er staat geen btw op de factuur.</param>
public sealed record Btwsoortinfo(
    Btwsoort Soort, BtwRichting Richting, string Naam, string Omschrijving, string? RubriekGrondslag, string? RubriekBtw, bool Voorbelasting, bool Verlegd);

/// <summary>De btw-soorten met hun rubrieken in de aangifte omzetbelasting.</summary>
public static class Btwsoorten
{
    public static IReadOnlyList<Btwsoortinfo> Alle { get; } =
    [
        new(Btwsoort.VerkoopHoog, BtwRichting.Verkoop, "Omzet hoog", "Leveringen en diensten belast met het hoge tarief", "1a", "1a", false, false),
        new(Btwsoort.VerkoopLaag, BtwRichting.Verkoop, "Omzet laag", "Leveringen en diensten belast met het lage tarief", "1b", "1b", false, false),
        new(Btwsoort.VerkoopNul, BtwRichting.Verkoop, "Omzet 0%", "Leveringen en diensten belast met 0% of niet bij u belast", "1e", null, false, false),
        new(Btwsoort.VerkoopVerlegd, BtwRichting.Verkoop, "Verlegd door u", "Leveringen en diensten waarbij de btw is verlegd naar de afnemer", "1e", null, false, false),
        new(Btwsoort.VerkoopBuitenEu, BtwRichting.Verkoop, "Export buiten de EU", "Leveringen naar landen buiten de EU (uitvoer)", "3a", null, false, false),
        new(Btwsoort.VerkoopBinnenEu, BtwRichting.Verkoop, "Levering binnen de EU (ICP)", "Leveringen en diensten naar landen binnen de EU", "3b", null, false, false),
        new(Btwsoort.VerkoopAfstand, BtwRichting.Verkoop, "Afstandsverkopen binnen de EU", "Installatie en afstandsverkopen binnen de EU", "3c", null, false, false),
        new(Btwsoort.VerkoopVrijgesteld, BtwRichting.Verkoop, "Vrijgesteld", "Vrijgestelde leveringen en diensten", null, null, false, false),
        new(Btwsoort.InkoopVoorbelasting, BtwRichting.Inkoop, "Voorbelasting", "Inkoop met btw op de factuur: aftrekbaar als voorbelasting", null, null, true, false),
        new(Btwsoort.InkoopNul, BtwRichting.Inkoop, "Inkoop 0% of vrijgesteld", "Inkoop zonder btw op de factuur", null, null, false, false),
        new(Btwsoort.InkoopVerlegd, BtwRichting.Inkoop, "Verlegd naar u", "Binnenlandse inkoop waarbij de btw naar u is verlegd", "2a", "2a", true, true),
        new(Btwsoort.InkoopBuitenEu, BtwRichting.Inkoop, "Import buiten de EU", "Leveringen en diensten uit landen buiten de EU", "4a", "4a", true, true),
        new(Btwsoort.InkoopBinnenEu, BtwRichting.Inkoop, "Inkoop binnen de EU (ICV)", "Leveringen en diensten uit landen binnen de EU", "4b", "4b", true, true),
        new(Btwsoort.GeenBtw, BtwRichting.Inkoop, "Geen btw", "Buiten de btw (bijv. loon, rente, privé)", null, null, false, false),
    ];

    public static Btwsoortinfo Van(Btwsoort soort) => Alle.First(s => s.Soort == soort);
}

/// <summary>
/// Een btw-code: de soort (wat hij in de aangifte doet) en het tarief. <see cref="Percentage"/> is het percentage op
/// een bepaalde datum (<see cref="Btwtabel.Op"/>); de codes van <see cref="BtwCodes.Standaard"/> hebben de huidige tarieven.
/// </summary>
/// <param name="Actief">Een inactieve code staat niet meer in de keuzelijsten, maar oude boekingen houden hem.</param>
public sealed record BtwCode(string Code, string Omschrijving, Btwsoort Soort, Tarief Tarief, decimal Percentage = 0, bool Actief = true)
{
    public Btwsoortinfo Info => Btwsoorten.Van(Soort);

    public BtwRichting Richting => Info.Richting;

    /// <summary>De rubriek van de grondslag in de aangifte, bijv. <c>1a</c> of <c>4b</c>; leeg als die er niet in komt.</summary>
    public string? Rubriek => Info.RubriekGrondslag;

    /// <summary>De rubriek van de verschuldigde btw, of leeg.</summary>
    public string? RubriekBtw => Info.RubriekBtw;

    /// <summary>Aftrekbaar als voorbelasting (5b).</summary>
    public bool Voorbelasting => Info.Voorbelasting;

    /// <summary>
    /// Bij inkoop: de btw is naar de ondernemer verlegd (binnenland, EU, buiten de EU). Hij berekent de btw zelf,
    /// draagt die af en trekt hem tegelijk af als voorbelasting.
    /// </summary>
    public bool Verlegd => Info.Verlegd;

    /// <summary>Komt er een btw-bedrag op de factuur zelf (dus niet bij 0%, vrijgesteld of verlegd)?</summary>
    public bool BtwOpFactuur => Percentage != 0 && !Verlegd;

    /// <summary>Kort, voor in een regel: bijv. "Hoog 21%", "Verlegd 21%", "ICV 21%" of "Geen btw".</summary>
    public string Kort
    {
        get
        {
            var naam = Soort switch
            {
                Btwsoort.InkoopVoorbelasting or Btwsoort.VerkoopHoog or Btwsoort.VerkoopLaag => Tarief == Tarief.Laag ? "Laag" : "Hoog",
                Btwsoort.InkoopVerlegd or Btwsoort.VerkoopVerlegd => "Verlegd",
                Btwsoort.InkoopBinnenEu => "ICV",
                Btwsoort.InkoopBuitenEu => "Import",
                Btwsoort.VerkoopBinnenEu => "ICP",
                Btwsoort.VerkoopBuitenEu => "Export",
                Btwsoort.VerkoopAfstand => "Afstandsverkoop",
                Btwsoort.InkoopNul or Btwsoort.VerkoopNul => "0%",
                Btwsoort.VerkoopVrijgesteld => "Vrijgesteld",
                _ => "Geen btw",
            };
            return Percentage != 0 && naam != "0%" ? $"{naam} {Btwpercentage.Tekst(Percentage)}" : naam;
        }
    }

    /// <summary>De btw over een grondslag, op centen afgerond (half van nul af).</summary>
    public decimal Bereken(decimal grondslag) => BtwBerekening.Btw(grondslag, Percentage);
}

/// <summary>Een btw-percentage zoals het op het scherm staat: <c>21%</c>, <c>9%</c>, <c>21,15%</c>.</summary>
public static class Btwpercentage
{
    public static string Tekst(decimal percentage) => $"{percentage.ToString("0.##", CultureInfo.GetCultureInfo("nl-NL"))}%";
}

/// <summary>Btw berekenen, ook terug uit een bedrag inclusief btw.</summary>
public static class BtwBerekening
{
    public static decimal Btw(decimal grondslag, decimal percentage) =>
        Math.Round(grondslag * percentage / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Splitst een bedrag inclusief btw in grondslag en btw; samen weer precies het bedrag.</summary>
    public static (decimal Grondslag, decimal Btw) UitInclusief(decimal inclusief, decimal percentage)
    {
        var btw = Math.Round(inclusief * percentage / (100m + percentage), 2, MidpointRounding.AwayFromZero);
        return (inclusief - btw, btw);
    }

    /// <summary>
    /// Klopt een btw-bedrag bij deze grondslag en dit percentage? Een cent verschil per regel mag, want leveranciers
    /// ronden per regel af; <paramref name="regels"/> is het aantal factuurregels.
    /// </summary>
    public static bool Klopt(decimal grondslag, decimal btw, decimal percentage, int regels = 1) =>
        Math.Abs(Btw(grondslag, percentage) - btw) <= 0.01m * Math.Max(1, regels);
}

/// <summary>De btw-codes die elke administratie standaard heeft, met de huidige tarieven.</summary>
public static class BtwCodes
{
    /// <summary>De codes zonder percentage; de tarieven komen uit <see cref="Btwtarieven"/>.</summary>
    public static IReadOnlyList<BtwCode> Definities { get; } =
    [
        new("V21", "Verkoop hoog tarief", Btwsoort.VerkoopHoog, Tarief.Hoog),
        new("V9", "Verkoop laag tarief", Btwsoort.VerkoopLaag, Tarief.Laag),
        new("V0", "Verkoop 0%", Btwsoort.VerkoopNul, Tarief.Nul),
        new("VVL", "Verkoop, btw verlegd naar de afnemer", Btwsoort.VerkoopVerlegd, Tarief.Geen),
        new("VEX", "Verkoop buiten de EU (uitvoer)", Btwsoort.VerkoopBuitenEu, Tarief.Nul),
        new("VEU", "Verkoop binnen de EU", Btwsoort.VerkoopBinnenEu, Tarief.Nul),
        new("VVR", "Verkoop vrijgesteld", Btwsoort.VerkoopVrijgesteld, Tarief.Geen),
        new("I21", "Inkoop hoog tarief", Btwsoort.InkoopVoorbelasting, Tarief.Hoog),
        new("I9", "Inkoop laag tarief", Btwsoort.InkoopVoorbelasting, Tarief.Laag),
        new("I0", "Inkoop 0% of vrijgesteld", Btwsoort.InkoopNul, Tarief.Nul),
        new("IVL", "Inkoop, btw naar u verlegd", Btwsoort.InkoopVerlegd, Tarief.Hoog),
        new("IEX", "Inkoop buiten de EU", Btwsoort.InkoopBuitenEu, Tarief.Hoog),
        new("IEU", "Inkoop binnen de EU", Btwsoort.InkoopBinnenEu, Tarief.Hoog),
        new("GB", "Geen btw", Btwsoort.GeenBtw, Tarief.Geen),
    ];

    /// <summary>De standaardcodes met de tarieven van nu (de laatste in <see cref="Btwtarieven.Standaard"/>).</summary>
    public static IReadOnlyList<BtwCode> Standaard { get; } = new Btwtabel(Definities, Btwtarieven.Standaard).Op(DateOnly.MaxValue);

    public static BtwCode? Zoek(IEnumerable<BtwCode> codes, string? code) =>
        code is null ? null : codes.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>De gewone code bij een richting en percentage, bijv. inkoop 21% → <c>I21</c>.</summary>
    public static BtwCode? Bij(IEnumerable<BtwCode> codes, BtwRichting richting, decimal percentage) =>
        codes.FirstOrDefault(c => c.Richting == richting && c.Percentage == percentage && !c.Verlegd);
}
