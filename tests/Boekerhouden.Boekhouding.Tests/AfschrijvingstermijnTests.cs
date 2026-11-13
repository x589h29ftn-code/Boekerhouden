using Boekerhouden.Boekhouding.Activa;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class AfschrijvingstermijnTests
{
    [Theory]
    [InlineData("5", Termijneenheid.Jaren, 60)]
    [InlineData("2,5", Termijneenheid.Jaren, 30)]
    [InlineData("20", Termijneenheid.ProcentPerJaar, 60)]
    [InlineData("10%", Termijneenheid.ProcentPerJaar, 120)]
    [InlineData("3,33", Termijneenheid.ProcentPerJaar, 360)]
    [InlineData("5", Termijneenheid.ProcentPerJaar, 240)]
    [InlineData("33.33", Termijneenheid.ProcentPerJaar, 36)]
    [InlineData("36", Termijneenheid.Maanden, 36)]
    public void Jaren_en_procent_per_jaar_worden_maanden(string tekst, Termijneenheid eenheid, int maanden) =>
        Assert.Equal(maanden, Afschrijvingstermijn.NaarMaanden(tekst, eenheid));

    [Theory]
    [InlineData("")]
    [InlineData("nul")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0,01")]
    public void Geen_of_een_onmogelijke_termijn_is_leeg(string tekst) =>
        Assert.Null(Afschrijvingstermijn.NaarMaanden(tekst, Termijneenheid.ProcentPerJaar));

    [Fact]
    public void Maanden_terug_naar_de_eenheid_en_de_passende_eenheid()
    {
        Assert.Equal(("5", "20", "60"), (Afschrijvingstermijn.Tekst(60, Termijneenheid.Jaren), Afschrijvingstermijn.Tekst(60, Termijneenheid.ProcentPerJaar), Afschrijvingstermijn.Tekst(60, Termijneenheid.Maanden)));
        Assert.Equal(("30", "3,33"), (Afschrijvingstermijn.Tekst(360, Termijneenheid.Jaren), Afschrijvingstermijn.Tekst(360, Termijneenheid.ProcentPerJaar)));
        Assert.Equal((Termijneenheid.Jaren, Termijneenheid.Maanden), (Afschrijvingstermijn.Passend(48), Afschrijvingstermijn.Passend(30)));
    }
}
