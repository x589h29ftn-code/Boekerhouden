using Boekerhouden.Boekhouding.Beginbalans;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class JaarrekeningbalansTests
{
    private static readonly string[] Regels =
    [
        "Inhoudsopgave",
        "Balans per 31 december 2025    3",
        "Toelichting    5",
        "BALANS PER 31 DECEMBER 2025",
        "ACTIVA",
        "(Na resultaatbestemming)    31 dec 2025    31 dec 2024",
        "€    €    €    €",
        "Vlottende activa",
        "Te vorderen omzetbelasting    1.234,56    -",
        "Liquide middelen    (100)    2.000",
        "Totaal vlottende activa    1.134,56    2.000",
        "Totaal    1.134,56    2.000",
        "PASSIVA",
        "Eigen vermogen",
        "Ondernemingsvermogen    1.134,56    2.000",
        "Totaal    1.134,56    2.000",
        "Dit staat na de balans    999",
    ];

    [Fact]
    public void Posten_uit_tekstregels_met_rubriek_negatief_bedrag_en_zonder_tussentelling()
    {
        var balans = Jaarrekeninglezer.Lees(Regels)!;

        Assert.Equal(new DateOnly(2025, 12, 31), balans.Balansdatum);
        Assert.Equal(
            [(Balanszijde.Activa, "Vlottende activa", "Te vorderen omzetbelasting", 1234.56m), (Balanszijde.Activa, "Vlottende activa", "Liquide middelen", -100m),
             (Balanszijde.Passiva, "Eigen vermogen", "Ondernemingsvermogen", 1134.56m)],
            balans.Posten.Select(p => (p.Zijde, p.Rubriek, p.Naam, p.Bedrag)));
        Assert.Empty(balans.Controle());
        Assert.Equal([1234.56m, -100m, -1134.56m], balans.Posten.Select(p => p.Boekbedrag));
    }

    [Theory]
    [InlineData("1.234", 1234)]
    [InlineData("-1.234", -1234)]
    [InlineData("(1.234)", -1234)]
    [InlineData("12,50", 12.5)]
    [InlineData("-", 0)]
    public void Bedragen_zoals_in_een_jaarrekening(string tekst, decimal bedrag) =>
        Assert.Equal(bedrag, Jaarrekeninglezer.Bedrag(tekst));

    [Fact]
    public void Zonder_balanskop_met_activa_geen_balans() =>
        Assert.Null(Jaarrekeninglezer.Lees(["Balans per 31 december 2025    3", "Winst-en-verliesrekening    4"]));

    [Fact]
    public void Rekening_op_naam_trefwoord_of_rubriek_en_langlopend_blijft_langlopend()
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.Eenmanszaak));
        string? Kies(string naam, string rubriek = "", Balanszijde zijde = Balanszijde.Passiva) =>
            Balansrekeningkeuze.Kies(new Balanspost(zijde, rubriek, naam, 1m), schema)?.Nummer;

        Assert.Equal("1500", Kies("Te vorderen omzetbelasting", "Vorderingen", Balanszijde.Activa));
        Assert.Equal("1100", Kies("Banktegoeden", "Liquide middelen", Balanszijde.Activa));
        Assert.Equal("0500", Kies("Ondernemingsvermogen", "Eigen vermogen"));
        Assert.Equal("0700", Kies("Overige schulden", "Langlopende schulden"));
        Assert.Equal("1800", Kies("Overige schulden", "Kortlopende schulden"));
        Assert.Equal("1800", Kies("Aflossingsverplichtingen", "Kortlopende schulden"));
        Assert.Null(Kies("Andere deelnemingen", "Financiële vaste activa", Balanszijde.Activa));
    }
}
