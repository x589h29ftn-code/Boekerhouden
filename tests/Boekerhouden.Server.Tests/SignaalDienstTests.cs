using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class SignaalDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    [Fact]
    public async Task Signalen_volgen_de_stand_van_het_dossier()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Signaal BV", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var signalen = server.Dienst<SignaalDienst>();

        var leeg = (await signalen.VoorDossiers([dossierId]))[dossierId];
        Assert.Equal(["Nog geen saldibalans 2025 ingelezen"], leeg.Signalen);
        Assert.Equal(100, leeg.Percentage);
        Assert.Equal(0, leeg.StappenTotaal);

        await server.Dienst<ImportDienst>().Importeer(klantId, 2025, TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv"), "2025.csv", Tester);
        var rubriceer = server.Dienst<RubriceerDienst>();
        var r = (await rubriceer.Overzicht(dossierId, metVoorstellen: false))!.Rekeningen.ToDictionary(x => x.Nummer, x => x.Id);
        await rubriceer.Koppel(klantId, [r["1000"]], "BLimKasKas", false, Tester);
        var werkprogramma = (await server.Dienst<WerkprogrammaDienst>().Overzicht(dossierId, Tester))!;
        await server.Dienst<ReviewDienst>().PlaatsPunt(werkprogramma.Onderdelen[0].Id, null, "Opdrachtbevestiging ontbreekt.", Tester);

        var stand = (await signalen.VoorDossiers([dossierId]))[dossierId];
        var overzicht = (await rubriceer.Overzicht(dossierId, metVoorstellen: false))!;

        Assert.Equal(overzicht.Relevant, stand.Relevant);
        Assert.Equal(overzicht.Percentage, stand.Percentage);
        Assert.Equal(1, stand.OpenPunten);
        Assert.Equal(werkprogramma.Onderdelen.Sum(o => o.Aantal), stand.StappenTotaal);
        Assert.Equal(
            [$"{overzicht.Relevant - 1} rekening(en) met saldo niet gekoppeld", "1 open reviewpunt(en)", "Beoordeling posten nog niet afgetekend"],
            stand.Signalen);
        Assert.Empty(await signalen.VoorDossiers([]));
    }
}
