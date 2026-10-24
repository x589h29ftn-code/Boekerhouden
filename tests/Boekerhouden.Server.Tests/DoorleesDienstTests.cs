using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Identiteit;

namespace Boekerhouden.Server.Tests;

public sealed class DoorleesDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private DoorleesDienst Doorlezen => server.Dienst<DoorleesDienst>();

    private WerkprogrammaDienst Werkprogramma => server.Dienst<WerkprogrammaDienst>();

    /// <summary>De fictieve bakkerij met een dossier 2025 en de voorstellen voor de koppelingen overgenomen.</summary>
    private async Task<Guid> Bakkerij()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv"), $"{jaar}.csv", Tester)).IsGelukt);
        }

        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await server.Dienst<RubriceerDienst>().AccepteerVoorstellen(dossierId, null, false, Tester);
        return dossierId;
    }

    private async Task<DoorleesOverzicht> Overzicht(Guid dossierId) => (await Doorlezen.Overzicht(dossierId))!;

    private async Task<OnderdeelInfo> Onderdeel(Guid dossierId) =>
        (await Werkprogramma.Overzicht(dossierId, Tester))!.Onderdelen.Single(o => o.Soort == OnderdeelSoort.Doorlezen);

    [Fact]
    public async Task Signaal_in_orde_vraagt_een_toelichting_en_bespreekpunt_maakt_een_punt_voor_de_klant()
    {
        var dossierId = await Bakkerij();
        var o = await Overzicht(dossierId);
        Assert.True(o.Signalen.Count >= 2, string.Join("; ", o.Signalen.Select(s => s.Signaal.Titel)));
        Assert.Equal(o.Signalen.Count, o.Open);
        var (eerste, tweede) = (o.Signalen[0].Signaal, o.Signalen[1].Signaal);

        Assert.False((await Doorlezen.InOrde(dossierId, eerste.Sleutel, " ", Tester)).IsGelukt);
        Assert.True((await Doorlezen.InOrde(dossierId, eerste.Sleutel, "Verklaard: nieuwe oven in maart.", Tester)).IsGelukt);
        Assert.True((await Doorlezen.NaarBespreking(dossierId, tweede.Sleutel, Tester)).IsGelukt);
        Assert.False((await Doorlezen.InOrde(dossierId, "bestaat-niet", "x", Tester)).IsGelukt);

        o = await Overzicht(dossierId);
        Assert.Equal(o.Signalen.Count - 2, o.Open);
        Assert.Equal((LeesAfhandeling.InOrde, "Verklaard: nieuwe oven in maart.", "Test Gebruiker"), (o.Signalen[0].Afhandeling!.Value, o.Signalen[0].Toelichting, o.Signalen[0].Door));
        var punt = Assert.Single(o.Bespreekpunten);
        Assert.Equal((tweede.Titel, tweede.Tekst, tweede.Sleutel, false), (punt.Onderwerp, punt.Toelichting, punt.Signaal, punt.IsBesproken));

        // Toch in orde: het bespreekpunt vervalt. Heropenen zet het signaal weer open.
        Assert.True((await Doorlezen.InOrde(dossierId, tweede.Sleutel, "Toch geen vraag.", Tester)).IsGelukt);
        Assert.Empty((await Overzicht(dossierId)).Bespreekpunten);
        Assert.True((await Doorlezen.Heropen(dossierId, tweede.Sleutel, Tester)).IsGelukt);
        Assert.Null((await Overzicht(dossierId)).Signalen[1].Afhandeling);

        // Een bespreekpunt uit een signaal verwijderen: het signaal staat weer open.
        await Doorlezen.NaarBespreking(dossierId, tweede.Sleutel, Tester);
        Assert.True((await Doorlezen.VerwijderBespreekpunt((await Overzicht(dossierId)).Bespreekpunten[0].Id, Tester)).IsGelukt);
        Assert.Null((await Overzicht(dossierId)).Signalen[1].Afhandeling);

        var acties = (await server.Dienst<AuditDienst>().Recent(AuditActies.SignaalInOrde)).Select(a => a.Details).ToList();
        Assert.Contains($"{eerste.Titel}: Verklaard: nieuwe oven in maart.", acties);
    }

    [Fact]
    public async Task Eigen_bespreekpunten()
    {
        var dossierId = await Bakkerij();

        Assert.False((await Doorlezen.VoegBespreekpuntToe(dossierId, "  ", null, Tester)).IsGelukt);
        var id = await Doorlezen.VoegBespreekpuntToe(dossierId, "Dividenduitkering 2026", "Wil de DGA dividend uitkeren?", Tester);
        Assert.True(id.IsGelukt);
        await Doorlezen.VoegBespreekpuntToe(dossierId, "Nieuwe bankfinanciering", null, Tester);

        var punten = (await Overzicht(dossierId)).Bespreekpunten;
        Assert.Equal(["Dividenduitkering 2026", "Nieuwe bankfinanciering"], punten.Select(p => p.Onderwerp));
        Assert.Null(punten[0].Signaal);

        Assert.True((await Doorlezen.VerwijderBespreekpunt(id.Waarde, Tester)).IsGelukt);
        Assert.Equal(["Nieuwe bankfinanciering"], (await Overzicht(dossierId)).Bespreekpunten.Select(p => p.Onderwerp));
    }

    [Fact]
    public async Task Aftekenen_na_alle_signalen_en_paraaf_alleen_door_de_accountant()
    {
        var dossierId = await Bakkerij();
        var onderdeel = await Onderdeel(dossierId);
        var signalenStap = onderdeel.Stappen.Single(s => s.Controle == StapControle.SignalenAfgehandeld);

        var fout = await Werkprogramma.ZetStatus(signalenStap.Id, StapStatus.Gereed, null, Tester);
        Assert.Contains("signaal/signalen bij het doorlezen open", Assert.Single(fout.Fouten), StringComparison.Ordinal);

        foreach (var l in (await Overzicht(dossierId)).Signalen)
        {
            Assert.True((await Doorlezen.InOrde(dossierId, l.Signaal.Sleutel, "Bekeken.", Tester)).IsGelukt);
        }

        foreach (var stap in onderdeel.Stappen)
        {
            Assert.True((await Werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).IsGelukt);
        }

        // Een reviewer mag reviewen, maar niet paraferen.
        var beheerder = await server.Beheerder();
        var gebruikers = server.Dienst<GebruikerDienst>();
        var email = $"reviewer{Guid.NewGuid():N}@kantoor.test";
        Assert.True((await gebruikers.Maak(new GebruikerInvoer(email, "Rik Reviewer", "RR", null, [Rollen.Reviewer]), beheerder)).IsGelukt);
        var reviewer = new Uitvoerder((await gebruikers.Lijst()).Single(g => g.Email == email).Id, "Rik Reviewer");
        var review = server.Dienst<ReviewDienst>();
        Assert.Equal(ReviewDienst.AlleenAccountant, Assert.Single((await review.Reviewen(onderdeel.Id, reviewer)).Fouten));

        Assert.True((await review.Reviewen(onderdeel.Id, beheerder)).IsGelukt);
        Assert.True((await Overzicht(dossierId)).Geparafeerd);
        var eerste = (await Overzicht(dossierId)).Signalen[0].Signaal.Sleutel;
        Assert.Equal(DoorleesDienst.Geparafeerd, Assert.Single((await Doorlezen.Heropen(dossierId, eerste, Tester)).Fouten));
        Assert.Equal(ReviewDienst.AlleenAccountant, Assert.Single((await review.ReviewTerug(onderdeel.Id, reviewer)).Fouten));
        Assert.True((await review.ReviewTerug(onderdeel.Id, beheerder)).IsGelukt);
        Assert.True((await Doorlezen.Heropen(dossierId, eerste, Tester)).IsGelukt);
    }
}
