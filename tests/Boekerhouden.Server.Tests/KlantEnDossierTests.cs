using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class KlantEnDossierTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private KlantDienst Klanten => server.Dienst<KlantDienst>();

    private DossierDienst Dossiers => server.Dienst<DossierDienst>();

    [Fact]
    public async Task Klant_aanmaken_en_terugvinden()
    {
        var nummer = TestGegevens.Klantnummer();
        var uitkomst = await Klanten.Maak(new KlantInvoer($" {nummer} ", " Fictieve Kapsalon B.V. ", Rechtsvorm.BV, "12345678", "Zwolle"), Tester);

        Assert.True(uitkomst.IsGelukt);
        var klant = await Klanten.Haal(uitkomst.Waarde);
        Assert.NotNull(klant);
        Assert.Equal(nummer, klant.Klantnummer);
        Assert.Equal("Fictieve Kapsalon B.V.", klant.Naam);
        Assert.Equal("Test Gebruiker", klant.GewijzigdDoor);

        var gevonden = await Klanten.Lijst("kapsalon");
        Assert.Contains(gevonden, k => k.Id == uitkomst.Waarde);
    }

    [Fact]
    public async Task Klantnummer_moet_uniek_zijn()
    {
        var nummer = TestGegevens.Klantnummer();
        Assert.True((await Klanten.Maak(new KlantInvoer(nummer, "Eerste", Rechtsvorm.BV, null, null), Tester)).IsGelukt);

        var tweede = await Klanten.Maak(new KlantInvoer(nummer, "Tweede", Rechtsvorm.Eenmanszaak, null, null), Tester);

        Assert.False(tweede.IsGelukt);
        Assert.Contains("bestaat al", Assert.Single(tweede.Fouten), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Naam", "Vul een klantnummer in.")]
    [InlineData("K1", " ", "Vul de naam van de klant in.")]
    public async Task Verplichte_velden(string nummer, string naam, string fout)
    {
        var uitkomst = await Klanten.Maak(new KlantInvoer(nummer, naam, Rechtsvorm.BV, null, null), Tester);
        Assert.Equal([fout], uitkomst.Fouten);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("1234567A")]
    [InlineData("123456789")]
    public async Task KvK_nummer_is_8_cijfers(string kvk)
    {
        var uitkomst = await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant", Rechtsvorm.BV, kvk, null), Tester);
        Assert.Equal(["Een KvK-nummer bestaat uit 8 cijfers."], uitkomst.Fouten);
    }

    [Fact]
    public async Task Klant_wijzigen_wordt_gelogd()
    {
        var id = (await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Oude Naam", Rechtsvorm.Eenmanszaak, null, null), Tester)).Waarde;

        var uitkomst = await Klanten.Wijzig(id, new KlantInvoer("W-" + TestGegevens.Klantnummer(), "Nieuwe Naam", Rechtsvorm.Eenmanszaak, null, "Kampen"), Tester);

        Assert.True(uitkomst.IsGelukt);
        Assert.Equal("Nieuwe Naam", (await Klanten.Haal(id))!.Naam);
        var log = await server.Dienst<AuditDienst>().Recent("Nieuwe Naam");
        Assert.Contains(log, r => r.Actie == AuditActies.KlantGewijzigd && r.Gebruiker == "Test Gebruiker");
    }

    [Theory]
    [InlineData(Rechtsvorm.BV, SoortOpdracht.SamenstellenJaarrekening, "Jaarrekening 2025")]
    [InlineData(Rechtsvorm.Eenmanszaak, SoortOpdracht.WinstaangifteOpstellen, "Winstaangifte 2025")]
    public async Task Dossier_krijgt_naam_naar_soort_opdracht(Rechtsvorm vorm, SoortOpdracht soort, string naam)
    {
        Assert.Equal(soort, DossierDienst.StandaardSoort(vorm));
        var klantId = (await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant", vorm, null, null), Tester)).Waarde;

        var uitkomst = await Dossiers.Maak(klantId, 2025, soort, Tester);

        Assert.True(uitkomst.IsGelukt);
        var dossier = await Dossiers.Haal(uitkomst.Waarde);
        Assert.NotNull(dossier);
        Assert.Equal(naam, dossier.Naam);
        Assert.Equal(DossierStatus.Nieuw, dossier.Status);
        Assert.Equal(vorm, dossier.Rechtsvorm);
    }

    [Fact]
    public async Task Per_jaar_en_soort_een_dossier()
    {
        var klantId = (await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant", Rechtsvorm.BV, null, null), Tester)).Waarde;
        Assert.True((await Dossiers.Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).IsGelukt);

        var dubbel = await Dossiers.Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester);
        var anderJaar = await Dossiers.Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester);
        var andereSoort = await Dossiers.Maak(klantId, 2025, SoortOpdracht.WinstaangifteOpstellen, Tester);

        Assert.False(dubbel.IsGelukt);
        Assert.True(anderJaar.IsGelukt);
        Assert.True(andereSoort.IsGelukt);
        Assert.Equal([2025, 2025, 2024], (await Dossiers.VoorKlant(klantId)).Select(d => d.Boekjaar));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Boekjaar_moet_geldig_zijn(int boekjaar)
    {
        var klantId = (await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant", Rechtsvorm.BV, null, null), Tester)).Waarde;
        Assert.False((await Dossiers.Maak(klantId, boekjaar, SoortOpdracht.SamenstellenJaarrekening, Tester)).IsGelukt);
    }

    [Fact]
    public async Task Klantenlijst_toont_laatste_dossier()
    {
        var klantId = (await Klanten.Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Lijstklant Uniek", Rechtsvorm.BV, null, null), Tester)).Waarde;
        await Dossiers.Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester);
        await Dossiers.Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester);

        var regel = Assert.Single(await Klanten.Lijst("Lijstklant Uniek"));

        Assert.Equal(2025, regel.LaatsteDossier?.Boekjaar);
        Assert.Null(regel.LaatsteImport);
    }
}
