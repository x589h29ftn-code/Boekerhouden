using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class JournaalzoekerTests
{
    private static readonly Rekeningschema Schema = new(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

    private static readonly Journaalpost Post = new("INK", "2026-0042", new(2026, 3, 5), "Belnet abonnement maart",
        [new("4310", 20.65m, "Mobiel", "I21", "BELNET", "BN-2026-0411"), new("1500", 4.34m, BtwCode: "I21"), new("1600", -24.99m, Relatie: "BELNET", Factuurnummer: "BN-2026-0411")]);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("belnet")]
    [InlineData("ABONNEMENT maart")]
    [InlineData("2026-0042")]
    [InlineData("ink")]
    [InlineData("4310")]
    [InlineData("43")]
    [InlineData("telefoon")]
    [InlineData("mobiel belnet")]
    [InlineData("BN-2026")]
    [InlineData("i21")]
    [InlineData("24,99")]
    [InlineData("-24,99")]
    [InlineData("20,65")]
    public void Vindt(string? zoek) => Assert.True(Journaalzoeker.Past(Post, zoek, Schema));

    [Theory]
    [InlineData("jansen")]
    [InlineData("belnet april")]
    [InlineData("25,00")]
    [InlineData("310")]
    [InlineData("I2")]
    public void Vindt_niet(string zoek) => Assert.False(Journaalzoeker.Past(Post, zoek, Schema));

    [Fact]
    public void Zonder_schema_zoekt_hij_niet_op_de_naam_van_de_rekening()
    {
        Assert.False(Journaalzoeker.Past(Post, "telefoon"));
        Assert.True(Journaalzoeker.Past(Post, "4310"));
    }
}
