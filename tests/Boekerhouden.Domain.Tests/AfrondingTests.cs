namespace Boekerhouden.Domain.Tests;

public sealed class AfrondingTests
{
    [Theory]
    [InlineData("1234.49", "1234")]
    [InlineData("1234.50", "1235")]
    [InlineData("-1234.50", "-1235")]
    [InlineData("-0.40", "0")]
    public void Op_hele_euro(string bedrag, string verwacht) =>
        Assert.Equal(decimal.Parse(verwacht, System.Globalization.CultureInfo.InvariantCulture),
            Afronding.OpHeleEuro(decimal.Parse(bedrag, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Saldo_nihil_blijft_nihil_na_afronden()
    {
        // Drie keer ,50 naar boven geeft +1,50 te veel: de som moet 0 blijven.
        var bedragen = new Dictionary<string, decimal> { ["a"] = 100.50m, ["b"] = 200.50m, ["c"] = 300.50m, ["d"] = -601.50m };

        var afgerond = Afronding.OpHeleEuros(bedragen);

        Assert.Equal(0m, afgerond.Values.Sum());
        Assert.All(afgerond, a => Assert.True(Math.Abs(a.Value - bedragen[a.Key]) < 1m));
    }

    [Fact]
    public void Verschil_gaat_naar_het_bedrag_met_de_grootste_rest()
    {
        var bedragen = new Dictionary<string, decimal> { ["a"] = 10.40m, ["b"] = 10.45m, ["c"] = -20.85m };

        var afgerond = Afronding.OpHeleEuros(bedragen);

        // 10 + 10 - 21 = -1; de som moet 0 zijn: "b" (rest 0,45) gaat naar 11.
        Assert.Equal(new Dictionary<string, decimal> { ["a"] = 10m, ["b"] = 11m, ["c"] = -21m }, afgerond);
    }

    [Fact]
    public void Fictieve_saldibalans_sluit_in_hele_euros()
    {
        var regels = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fictief", "bv-voorbeeld-bakkerij", "saldibalans-2025.csv")).Skip(4)
            .Select(r => r.Split(';'))
            .ToDictionary(d => d[0], d => decimal.Parse(d[2].Replace(".", "", StringComparison.Ordinal).Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture));

        var afgerond = Afronding.OpHeleEuros(regels);

        Assert.Equal(0m, regels.Values.Sum());
        Assert.Equal(0m, afgerond.Values.Sum());
        Assert.All(afgerond.Values, b => Assert.Equal(decimal.Truncate(b), b));
    }

    [Theory]
    [InlineData("1234567.89", "1.234.568")]
    [InlineData("-81300", "-81.300")]
    [InlineData("0.49", "0")]
    public void Weergave_in_hele_euros(string bedrag, string verwacht) =>
        Assert.Equal(verwacht, Bedragen.Heel(decimal.Parse(bedrag, System.Globalization.CultureInfo.InvariantCulture)));
}
