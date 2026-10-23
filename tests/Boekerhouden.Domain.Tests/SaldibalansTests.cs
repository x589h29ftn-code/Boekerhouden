using Boekerhouden.Domain;

namespace Boekerhouden.Domain.Tests;

public class SaldibalansTests
{
    [Fact]
    public void Totalen_en_saldo_worden_uit_de_regels_berekend()
    {
        var saldibalans = new Saldibalans(2025, "Test B.V.",
        [
            new SaldibalansRegel("1100", "Bank", 1_000.50m),
            new SaldibalansRegel("1300", "Debiteuren", 250m),
            new SaldibalansRegel("0500", "Geplaatst kapitaal", -1_250.50m),
        ]);

        Assert.Equal(1_250.50m, saldibalans.TotaalDebet);
        Assert.Equal(1_250.50m, saldibalans.TotaalCredit);
        Assert.Equal(0m, saldibalans.Saldo);
        Assert.True(saldibalans.IsInEvenwicht);
    }

    [Fact]
    public void Saldibalans_met_verschil_is_niet_in_evenwicht()
    {
        var saldibalans = new Saldibalans(2025, null,
        [
            new SaldibalansRegel("1100", "Bank", 100m),
            new SaldibalansRegel("0500", "Kapitaal", -99.99m),
        ]);

        Assert.Equal(0.01m, saldibalans.Saldo);
        Assert.False(saldibalans.IsInEvenwicht);
    }

    [Fact]
    public void Dubbel_grootboeknummer_is_niet_toegestaan()
    {
        var fout = Assert.Throws<ArgumentException>(() => new Saldibalans(2025, null,
        [
            new SaldibalansRegel("1100", "Bank", 100m),
            new SaldibalansRegel("1100", "Bank 2", -100m),
        ]));

        Assert.Contains("1100", fout.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Bank")]
    [InlineData(" ", "Bank")]
    [InlineData("1100", "")]
    public void Regel_vereist_grootboeknummer_en_omschrijving(string nummer, string omschrijving)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SaldibalansRegel(nummer, omschrijving, 0m));
    }
}
