using Boekerhouden.Domain;

namespace Boekerhouden.Domain.Tests;

public class BedragenTests
{
    [Theory]
    [InlineData("1234.56", "€ 1.234,56")]
    [InlineData("-1234.56", "€ -1.234,56")]
    [InlineData("0", "€ 0,00")]
    [InlineData("0.5", "€ 0,50")]
    [InlineData("1000000", "€ 1.000.000,00")]
    public void Euro_gebruikt_Nederlandse_notatie(string bedrag, string verwacht)
    {
        Assert.Equal(verwacht, Bedragen.Euro(decimal.Parse(bedrag, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("125.40", "€ 125,40 debet")]
    [InlineData("-125.40", "€ 125,40 credit")]
    [InlineData("0", "€ 0,00")]
    public void DebetCredit_noemt_de_kant(string bedrag, string verwacht)
    {
        Assert.Equal(verwacht, Bedragen.DebetCredit(decimal.Parse(bedrag, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
