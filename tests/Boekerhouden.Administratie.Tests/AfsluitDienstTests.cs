using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Periode afsluiten als stappenplan: per stap (bank, inkoop, verkoop, aansluitingen, btw) of hij klaar is en wat er nog
/// ligt; alleen voor leden van de administratie.
/// </summary>
public sealed class AfsluitDienstTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Laat_per_stap_zien_wat_er_nog_ligt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Afsluiten Stappen B.V.");
        var afsluiten = app.Dienst<AfsluitDienst>();
        var tot = new DateOnly(2026, 3, 31);

        // Nog niets ingelezen: de bank is niet bij, de rest heeft niets te doen.
        var stappen = await afsluiten.Stappen(wie, id, tot);
        Assert.Equal(["Bank bij tot en met 31-03-2026", "Inkoop verwerkt", "Verkoop verwerkt", "Aansluitingen sluiten", "Btw-aangifte vastgelegd"], stappen.Select(s => s.Naam));
        Assert.False(stappen[0].Klaar);
        Assert.Equal("nog geen bankafschrift ingelezen", stappen[0].Nog);
        Assert.True(stappen[1].Klaar);
        Assert.True(stappen[2].Klaar);
        Assert.False(stappen[4].Klaar);
        Assert.Contains("1e kwartaal 2026", stappen[4].Nog, StringComparison.Ordinal);

        // Een afschrift met mutaties en een inkoopfactuur: die stappen zeggen wat er nog te verwerken is.
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet("AF-1")), "af1.xml", null);
        stappen = await afsluiten.Stappen(wie, id, tot);
        Assert.False(stappen[0].Klaar);
        Assert.Contains("te verwerken", stappen[0].Nog, StringComparison.Ordinal);
        Assert.False(stappen[1].Klaar);
        Assert.Equal("1 factuur te verwerken", stappen[1].Nog);
        Assert.Equal("/inkoop", stappen[1].Pad);
    }

    [Fact]
    public async Task Alleen_voor_leden()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Afsluiten Toegang B.V.");
        var buiten = await app.Gebruiker("Bert Buiten Afsluiten");
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<AfsluitDienst>().Stappen(buiten, id, new DateOnly(2026, 3, 31)));
        Assert.NotEmpty(await app.Dienst<AfsluitDienst>().Stappen(wie, id, new DateOnly(2026, 3, 31)));
    }
}
