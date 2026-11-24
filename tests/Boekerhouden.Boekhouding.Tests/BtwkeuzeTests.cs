using Boekerhouden.Boekhouding.Btw;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// De btw-codes in een keuzelijst met inkoop en verkoop door elkaar (bank, regels, rekeningschema): elke code een eigen
/// tekst met de richting voluit, zodat "Hoog 21%" bij inkoop en verkoop niet meer twee keer hetzelfde lijkt; de tooltip
/// zegt of de btw af te dragen of te vorderen is.
/// </summary>
public sealed class BtwkeuzeTests
{
    [Fact]
    public void Elke_standaardcode_heeft_een_eigen_duidelijke_tekst()
    {
        var codes = BtwCodes.Standaard;
        Assert.Equal(codes.Count, codes.Select(c => c.Keuze).Distinct().Count());
        Assert.Equal("Verkoop 21%", BtwCodes.Zoek(codes, "V21")!.Keuze);
        Assert.Equal("Inkoop 21%", BtwCodes.Zoek(codes, "I21")!.Keuze);
        Assert.Equal("Inkoop 9%", BtwCodes.Zoek(codes, "I9")!.Keuze);
        Assert.Equal("Inkoop verlegd 21%", BtwCodes.Zoek(codes, "IVL")!.Keuze);
        Assert.Equal("Inkoop EU (ICV) 21%", BtwCodes.Zoek(codes, "IEU")!.Keuze);
        Assert.Equal("Verkoop EU (ICP)", BtwCodes.Zoek(codes, "VEU")!.Keuze);
        Assert.Equal("Geen btw", BtwCodes.Zoek(codes, "GB")!.Keuze);
    }

    [Fact]
    public void De_tooltip_zegt_af_te_dragen_of_te_vorderen()
    {
        var codes = BtwCodes.Standaard;
        Assert.EndsWith("af te dragen", BtwCodes.Zoek(codes, "V21")!.Uitleg, StringComparison.Ordinal);
        Assert.EndsWith("te vorderen (voorbelasting)", BtwCodes.Zoek(codes, "I21")!.Uitleg, StringComparison.Ordinal);
        Assert.Contains("zelf af te dragen en tegelijk te vorderen", BtwCodes.Zoek(codes, "IVL")!.Uitleg, StringComparison.Ordinal);
        Assert.StartsWith("V21 · ", BtwCodes.Zoek(codes, "V21")!.Uitleg, StringComparison.Ordinal);
    }
}
