using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class JournaalkopieTests
{
    private static readonly Journaalpost Lonen = new("MLON", "2026-0010", new(2026, 10, 31), "Lonen oktober 2026",
    [
        new("4000", 3000m, "Brutoloon okt 2026"),
        new("1520", -800m, "Loonheffing 10-2026"),
        new("1800", -2200m),
    ]);

    [Fact]
    public void Kopie_naar_een_andere_maand_met_de_maand_in_de_omschrijving_mee()
    {
        var november = Journaalkopie.NaarMaand(Lonen, 2026, 11);

        Assert.Equal(new DateOnly(2026, 11, 30), november.Datum);
        Assert.Equal("Lonen november 2026", november.Omschrijving);
        Assert.Equal(["Brutoloon nov 2026", "Loonheffing 11-2026", null], november.Regels.Select(r => r.Omschrijving));
        Assert.Equal(Lonen.Regels.Select(r => (r.Rekeningnummer, r.Bedrag)), november.Regels.Select(r => (r.Rekeningnummer, r.Bedrag)));
    }

    [Fact]
    public void Over_de_jaargrens_en_een_kortere_maand()
    {
        var januari = Journaalkopie.NaarMaand(Lonen with { Omschrijving = "Lonen december 2026", Datum = new(2026, 12, 31) }, 2027, 2);

        Assert.Equal(new DateOnly(2027, 2, 28), januari.Datum);
        Assert.Equal("Lonen februari 2027", januari.Omschrijving);
        Assert.Equal("Huur", Journaalkopie.NaarMaand(Lonen with { Omschrijving = "Huur" }, 2026, 11).Omschrijving);
    }
}
