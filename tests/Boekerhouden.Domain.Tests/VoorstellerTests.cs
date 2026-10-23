using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class VoorstellerTests
{
    private static int volgorde;

    private static RgsCode C(string code, string omschrijving) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            omschrijving, omschrijving, Saldokant.Debet, null, ++volgorde, null, false, new HashSet<string>(), new HashSet<string>());

    private static readonly RgsSchema Schema = new("test",
    [
        C("B", "Balans"), C("BMva", "Materiële vaste activa"),
        C("BMvaBei", "Inventaris"), C("BMvaBeiVvp", "Verkrijgings- of vervaardigingsprijs"), C("BMvaBeiCae", "Cumulatieve afschrijvingen en waardeverminderingen"),
        C("W", "W&V"), C("WBed", "Overige bedrijfskosten"), C("WBedKan", "Kantoorkosten"),
        C("WBedKanKan", "Kantoorbenodigdheden"), C("WBedKanTef", "Telefoonkosten"), C("WBedKanCea", "Contributies en abonnementen"),
    ]);

    private static Voorsteller Maak() => new(Schema, Schema.Alle);

    [Theory]
    [InlineData("Kantoorbenodigdheden", "WBedKanKan")]
    [InlineData("Telefoon", "WBedKanTef")]
    [InlineData("Abonnementen", "WBedKanCea")]
    [InlineData("Inventaris aanschafwaarde", "BMvaBeiVvp")]
    [InlineData("Inventaris cum. afschrijvingen", "BMvaBeiCae")]
    public void Herkent_gangbare_omschrijvingen(string omschrijving, string verwacht) =>
        Assert.Equal(verwacht, Maak().StelVoor(omschrijving)?.Code.Code);

    [Theory]
    [InlineData("Xyz qwerty")]
    [InlineData("1234")]
    [InlineData("")]
    public void Geen_voorstel_als_niets_lijkt(string omschrijving) => Assert.Null(Maak().StelVoor(omschrijving));

    [Theory]
    [InlineData("Kantoorkosten", "kantoorkosten", true)]
    [InlineData("Telefoon en internet", "Telefoonkosten", true)]
    [InlineData("Kantoorkosten", "Huur bedrijfsruimte", false)]
    public void Omschrijvingen_vergelijken(string a, string b, bool lijkt) => Assert.Equal(lijkt, Voorsteller.LijktOp(a, b));
}
