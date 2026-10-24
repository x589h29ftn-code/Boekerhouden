using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class KantoorDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Beheer = new(null, "Bea Beheer");

    private KantoorDienst Kantoor => server.Dienst<KantoorDienst>();

    [Fact]
    public async Task Gegevens_en_logo_bewaren_met_controles()
    {
        Assert.Equal("#1f4e79", (await Kantoor.Haal()).Kleur);

        var fout = await Kantoor.Bewaar(new KantoorInvoer(" ", null, null, null, "blauw"), Beheer);
        Assert.Equal(2, fout.Fouten.Count);

        Assert.True((await Kantoor.Bewaar(new KantoorInvoer("Voorbeeld Accountants", "Kerkstraat 1", "1234 AB", "Zwolle", "#2E5D8A"), Beheer)).IsGelukt);
        var kantoor = await Kantoor.Haal();
        Assert.Equal("Voorbeeld Accountants", kantoor.Naam);
        Assert.Equal("#2e5d8a", kantoor.Kleur);

        Assert.False((await Kantoor.ZetLogo([1, 2, 3], "image/svg+xml", Beheer)).IsGelukt);
        Assert.True((await Kantoor.ZetLogo([0x89, 0x50, 0x4E, 0x47], "image/png", Beheer)).IsGelukt);
        Assert.Equal("image/png", (await Kantoor.Haal()).LogoType);
        Assert.True((await Kantoor.VerwijderLogo(Beheer)).IsGelukt);
        Assert.Null((await Kantoor.Haal()).Logo);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.KantoorGewijzigd), a => a.Details == "logo verwijderd");
    }
}
