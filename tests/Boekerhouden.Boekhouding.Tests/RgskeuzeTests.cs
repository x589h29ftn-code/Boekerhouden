using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De keuzelijst met RGS-codes bij een rekening: uit de meegeleverde schema's, met de naam erbij.</summary>
public sealed class RgskeuzeTests
{
    [Fact]
    public void Hoofdposten_en_de_codes_van_de_meegeleverde_schemas_met_naam_zonder_dubbelen()
    {
        var keuze = Rekeningschemas.Rgskeuze();

        Assert.Contains(keuze, c => c.Code == "BLim");
        Assert.Contains(keuze, c => c.Code == "BLimBanRba");
        Assert.Contains(keuze, c => c.Code == "BEivKapOnd");
        Assert.All(keuze, c => Assert.True(c.Code[0] is 'B' or 'W' && c.Naam.Length > 0, c.Code));
        Assert.Equal(keuze.Count, keuze.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public void Een_eigen_code_uit_het_schema_van_de_administratie_staat_erbij()
    {
        var eigen = new Rekeningschema([.. StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV), new Rekening("0250", "Bedrijfsgebouwen", Rekeningsoort.Balans, "BMvaBegVvp")]);

        Assert.Contains(Rekeningschemas.Rgskeuze(eigen), c => c.Code == "BMvaBegVvp");
    }
}
