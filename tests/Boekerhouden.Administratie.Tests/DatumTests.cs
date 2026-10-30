namespace Boekerhouden.Administratie.Tests;

public sealed class DatumTests
{
    [Theory]
    [InlineData("30-10-2026", "2026-10-30")]
    [InlineData("30/10/2026", "2026-10-30")]
    [InlineData("30.10.26", "2026-10-30")]
    [InlineData(" 1-2-2026 ", "2026-02-01")]
    [InlineData("30-10", "2026-10-30")]
    [InlineData("3010", "2026-10-30")]
    [InlineData("301026", "2026-10-30")]
    [InlineData("30102026", "2026-10-30")]
    [InlineData("2026-10-30", "2026-10-30")]
    [InlineData("29-2-2028", "2028-02-29")]
    public void Datum_in_Nederlandse_notatie_dag_maand_jaar(string tekst, string verwacht) =>
        Assert.Equal(DateOnly.Parse(verwacht, System.Globalization.CultureInfo.InvariantCulture), Weergave.LeesDatum(tekst, 2026));

    [Theory]
    [InlineData("")]
    [InlineData("10/30/2026")]
    [InlineData("31-11-2026")]
    [InlineData("29-2-2027")]
    [InlineData("30-10-202")]
    [InlineData("morgen")]
    [InlineData("1-1-1800")]
    public void Geen_datum(string tekst) => Assert.Null(Weergave.LeesDatum(tekst, 2026));
}
