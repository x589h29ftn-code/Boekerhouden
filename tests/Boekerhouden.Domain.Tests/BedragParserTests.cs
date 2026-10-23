using System.Globalization;

namespace Boekerhouden.Domain.Tests;

public class BedragParserTests
{
    [Theory]
    [InlineData("1234,56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1234.56", "1234.56")]
    [InlineData("-1.234,56", "-1234.56")]
    [InlineData("1.234", "1234")]
    [InlineData("1.234.567", "1234567")]
    [InlineData("1,234,567", "1234567")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("€ 1.234,56", "1234.56")]
    [InlineData("€-12,00", "-12.00")]
    [InlineData("1 234,56", "1234.56")]
    [InlineData("1 234,56", "1234.56")]
    [InlineData("(125,40)", "-125.40")]
    [InlineData("125,40-", "-125.40")]
    [InlineData("+5", "5")]
    [InlineData(",50", "0.50")]
    [InlineData("0", "0")]
    [InlineData("12.5", "12.5")]
    [InlineData("0.125", "0.125")]
    [InlineData("−12,00", "-12.00")]
    public void Leest_gangbare_notaties(string invoer, string verwacht)
    {
        Assert.True(BedragParser.TryParse(invoer, out var bedrag));
        Assert.Equal(decimal.Parse(verwacht, CultureInfo.InvariantCulture), bedrag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("12.34.567")]
    [InlineData("1,2,3")]
    [InlineData("1.234,56,7")]
    [InlineData("12-34")]
    [InlineData("-")]
    [InlineData("1234.567")]
    [InlineData("12,")]
    public void Weigert_ongeldige_invoer(string invoer)
    {
        Assert.False(BedragParser.TryParse(invoer, out _));
    }
}
