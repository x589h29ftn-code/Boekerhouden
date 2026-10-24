using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class VerklaringDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private static readonly DateOnly Gisteren = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

    private VerklaringDienst Verklaring => server.Dienst<VerklaringDienst>();

    private async Task<Guid> Dossier(SoortOpdracht soort = SoortOpdracht.SamenstellenJaarrekening)
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Verklaring B.V.",
            soort == SoortOpdracht.SamenstellenJaarrekening ? Rechtsvorm.BV : Rechtsvorm.Eenmanszaak, null, "Zwolle"), Tester)).Waarde;
        Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, 2025, TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv"), "2025.csv", Tester)).IsGelukt);
        return (await server.Dienst<DossierDienst>().Maak(klantId, 2025, soort, Tester)).Waarde;
    }

    [Fact]
    public async Task Accountant_en_datum_op_of_na_de_erkenning()
    {
        var beheerder = await server.Beheerder();
        var dossierId = await Dossier();
        var werkprogramma = server.Dienst<WerkprogrammaDienst>();
        var stap = (await werkprogramma.Overzicht(dossierId, Tester))!.Onderdelen.Single(o => o.Soort == OnderdeelSoort.Uitbrengen)
            .Stappen.Single(s => s.Controle == StapControle.DatumVerklaring);

        var v = (await Verklaring.Overzicht(dossierId))!;
        var accountant = Assert.Single(v.Accountants, a => a.Id == beheerder.Id);
        Assert.Equal("Bea Beheer AA", accountant.Naam);
        Assert.False((await Verklaring.Bewaar(dossierId, Gisteren, Guid.NewGuid(), null, Tester)).IsGelukt);
        Assert.False((await Verklaring.Bewaar(dossierId, DateOnly.FromDateTime(DateTime.Today.AddDays(5)), beheerder.Id, null, Tester)).IsGelukt);

        Assert.Equal("Samenstellingsverklaring: datum en accountant nog invullen.", Assert.Single((await werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).Fouten));
        Assert.True((await Verklaring.Bewaar(dossierId, Gisteren.AddDays(-1), beheerder.Id, "Eigen alinea.", Tester)).IsGelukt);
        Assert.Equal("De erkenning door het bestuur is nog niet vastgelegd.", Assert.Single((await werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).Fouten));

        await server.Dienst<BesprekingDienst>().BewaarErkenning(dossierId, "J. Jansen", Gisteren, Erkenningswijze.Bevestigingsbrief, Tester);
        Assert.StartsWith("De samenstellingsverklaring is gedateerd vóór de erkenning", Assert.Single((await werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).Fouten), StringComparison.Ordinal);
        Assert.True((await Verklaring.Bewaar(dossierId, Gisteren, beheerder.Id, "Eigen alinea.", Tester)).IsGelukt);
        Assert.True((await werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).IsGelukt);

        var rapport = (await server.Dienst<RapportDienst>().Maak(dossierId))!.Waarde!.Rapport;
        var blok = rapport.Hoofdstukken.First().Blokken.OfType<VerklaringBlok>().Single();
        Assert.Equal("Bea Beheer AA", blok.Accountant);
        Assert.Equal("Eigen alinea.", blok.Alineas[^1].Tekst);
        Assert.EndsWith(Weergave.DatumVoluit(Gisteren), blok.Ondertekening, StringComparison.Ordinal);

        // Het doorlezen komt vóór de erkenning: de verklaring telt daar niet mee.
        Assert.Null((await server.Dienst<RapportDienst>().Maak(dossierId, metVerklaring: false))!.Waarde!.Rapport.Hoofdstukken.First().Blokken.OfType<VerklaringBlok>().SingleOrDefault());
    }

    [Fact]
    public async Task Geen_verklaring_bij_de_winstaangifte()
    {
        var beheerder = await server.Beheerder();
        var dossierId = await Dossier(SoortOpdracht.WinstaangifteOpstellen);

        Assert.False((await Verklaring.Bewaar(dossierId, Gisteren, beheerder.Id, null, Tester)).IsGelukt);
        var rapport = (await server.Dienst<RapportDienst>().Maak(dossierId))!.Waarde!.Rapport;
        Assert.Single(rapport.Delen);
        Assert.DoesNotContain((await server.Dienst<WerkprogrammaDienst>().Overzicht(dossierId, Tester))!.Onderdelen, o => o.Soort == OnderdeelSoort.Uitbrengen);
    }
}
