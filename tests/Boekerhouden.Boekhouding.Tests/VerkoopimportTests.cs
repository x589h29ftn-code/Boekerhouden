using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class VerkoopimportTests
{
    private static readonly IReadOnlyList<BtwCode> Codes = BtwCodes.Standaard;

    [Fact]
    public void Alleen_een_totaal_zonder_tarief_wordt_niet_vanzelf_geboekt()
    {
        var factuur = Factuurtekst.Analyseer("Bouwbedrijf Voorbeeld B.V.\nFactuurnummer: 2026-17\nFactuurdatum: 01-09-2026\nTotaal te betalen 121,00");

        var regels = Verkoopimport.Regels(factuur, Codes, "8000");
        var verkoop = new Verkoopfactuur("KLANT", new Partij("Klant", new()), null, new(2026, 9, 1), new(2026, 9, 15), "Werk", regels);

        Assert.Equal((100m, "V21"), (Assert.Single(regels).Bedrag, regels[0].BtwCode));
        Assert.Contains(Verkoopimport.Belemmeringen(factuur, verkoop, Codes, klantHerkend: true), r => r.Contains("btw-tarief", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CH", "VEX")]
    [InlineData("US", "VEX")]
    [InlineData("NL", "V0")]
    [InlineData("DE", "V0")]
    [InlineData(null, "V0")]
    public void Zonder_btw_aan_een_klant_buiten_de_EU_is_uitvoer(string? land, string code)
    {
        var factuur = new HerkendeFactuur(Herkenningsbron.EFactuur, Factuurnummer: "VF26-0025", Regels: [new HerkendeRegel("E-bike", 7_500m, 0m)]);

        Assert.Equal(code, Assert.Single(Verkoopimport.Regels(factuur, Codes, "8010", land)).BtwCode);
    }

    [Fact]
    public void Exclusief_en_inclusief_samen_zijn_geen_aanname()
    {
        var factuur = Factuurtekst.Analyseer("Bouwbedrijf Voorbeeld B.V.\nFactuurnummer: 2026-18\nFactuurdatum: 01-09-2026\nTotaal excl. btw 100,00\nTotaal incl. btw 121,00");

        var regels = Verkoopimport.Regels(factuur, Codes, "8000");
        var verkoop = new Verkoopfactuur("KLANT", new Partij("Klant", new()), null, new(2026, 9, 1), new(2026, 9, 15), "Werk", regels);

        Assert.DoesNotContain(Verkoopimport.Belemmeringen(factuur, verkoop, Codes, klantHerkend: true), r => r.Contains("btw-tarief", StringComparison.Ordinal));
    }
}
