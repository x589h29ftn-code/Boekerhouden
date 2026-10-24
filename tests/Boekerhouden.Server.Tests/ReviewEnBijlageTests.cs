using System.Net;
using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Server.Tests;

public sealed class ReviewEnBijlageTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Medewerker = new(null, "Mo Medewerker");

    private WerkprogrammaDienst Werkprogramma => server.Dienst<WerkprogrammaDienst>();

    private ReviewDienst Review => server.Dienst<ReviewDienst>();

    private BijlageDienst Bijlagen => server.Dienst<BijlageDienst>();

    /// <summary>Een dossier met het onderdeel "Kennis van de cliënt" (drie stappen).</summary>
    private async Task<(Guid DossierId, OnderdeelInfo Kennis)> Dossier()
    {
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Review BV", Rechtsvorm.BV, null, null), Medewerker)).Waarde;
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Medewerker)).Waarde;
        return (dossierId, await Kennis(dossierId));
    }

    private async Task<OnderdeelInfo> Kennis(Guid dossierId) =>
        (await Werkprogramma.Overzicht(dossierId, Medewerker))!.Onderdelen.Single(o => o.Titel == "Kennis van de cliënt");

    private async Task AllesGereed(OnderdeelInfo onderdeel)
    {
        foreach (var stap in onderdeel.Stappen)
        {
            Assert.True((await Werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Medewerker)).IsGelukt);
        }
    }

    [Fact]
    public async Task Reviewen_kan_pas_als_alles_klaar_is_en_alleen_door_een_reviewer()
    {
        var reviewer = await server.Beheerder();
        var (dossierId, kennis) = await Dossier();

        Assert.Contains("stap(pen) open", Assert.Single((await Review.Reviewen(kennis.Id, reviewer)).Fouten), StringComparison.Ordinal);
        await AllesGereed(kennis);
        Assert.Equal(ReviewDienst.AlleenReviewers, Assert.Single((await Review.Reviewen(kennis.Id, Medewerker)).Fouten));

        var punt = await Review.PlaatsPunt(kennis.Id, kennis.Stappen[0].Id, "Bedrijfsbeschrijving is nog van vorig jaar.", reviewer);
        Assert.True(punt.IsGelukt);
        Assert.Contains("reviewpunt(en) open", Assert.Single((await Review.Reviewen(kennis.Id, reviewer)).Fouten), StringComparison.Ordinal);
        Assert.Equal(1, (await Kennis(dossierId)).OpenPunten);

        Assert.True((await Review.LosOp(punt.Waarde, "Bijgewerkt met de nieuwe activiteiten.", Medewerker)).IsGelukt);
        Assert.True((await Review.Reviewen(kennis.Id, reviewer)).IsGelukt);

        var gereviewd = await Kennis(dossierId);
        Assert.Equal("Bea Beheer", gereviewd.GereviewdDoor);
        var opgelost = Assert.Single(gereviewd.Reviewpunten);
        Assert.True(opgelost.Opgelost);
        Assert.Equal("Bijgewerkt met de nieuwe activiteiten.", opgelost.Reactie);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.OnderdeelGereviewd), a => a.Details == "Kennis van de cliënt");
    }

    [Fact]
    public async Task Gereviewd_onderdeel_is_vergrendeld_en_een_nieuw_punt_laat_de_review_vervallen()
    {
        var reviewer = await server.Beheerder();
        var (dossierId, kennis) = await Dossier();
        await AllesGereed(kennis);
        Assert.True((await Review.Reviewen(kennis.Id, reviewer)).IsGelukt);

        Assert.Equal(WerkprogrammaDienst.Gereviewd, Assert.Single((await Werkprogramma.ZetStatus(kennis.Stappen[0].Id, StapStatus.Open, null, Medewerker)).Fouten));
        Assert.Equal(WerkprogrammaDienst.Gereviewd, Assert.Single((await Werkprogramma.BewaarAntwoord(kennis.Stappen[0].Id, "later", Medewerker)).Fouten));
        Assert.False((await Werkprogramma.VoegStapToe(kennis.Id, "Extra", null, Medewerker)).IsGelukt);

        await Review.PlaatsPunt(kennis.Id, null, "Toch nog even naar de stelselkeuze kijken.", reviewer);
        Assert.Null((await Kennis(dossierId)).GereviewdDoor);
        Assert.True((await Werkprogramma.BewaarAntwoord(kennis.Stappen[2].Id, "Titel 9 Boek 2 BW, ongewijzigd.", Medewerker)).IsGelukt);

        // Terugzetten door een reviewer maakt het onderdeel weer bewerkbaar.
        var punt = (await Kennis(dossierId)).Reviewpunten.Single();
        await Review.LosOp(punt.Id, null, Medewerker);
        Assert.True((await Review.Reviewen(kennis.Id, reviewer)).IsGelukt);
        Assert.False((await Review.ReviewTerug(kennis.Id, Medewerker)).IsGelukt);
        Assert.True((await Review.ReviewTerug(kennis.Id, reviewer)).IsGelukt);
        Assert.True((await Werkprogramma.ZetStatus(kennis.Stappen[0].Id, StapStatus.Open, null, Medewerker)).IsGelukt);
    }

    [Fact]
    public async Task Bijlage_toevoegen_downloaden_en_verwijderen()
    {
        await server.Beheerder();
        var (dossierId, kennis) = await Dossier();
        var stap = kennis.Stappen[0];
        var inhoud = Encoding.UTF8.GetBytes("Organisatieschema Review BV");

        var uitkomst = await Bijlagen.VoegToe(stap.Id, "C:\\map\\schema.txt", "text/plain", new MemoryStream(inhoud), Medewerker);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var bijlage = Assert.Single((await Kennis(dossierId)).Stappen[0].Bijlagen);
        Assert.Equal("schema.txt", bijlage.Bestandsnaam);
        Assert.Equal(inhoud.Length, bijlage.Grootte);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.BijlageToegevoegd), a => a.Details!.Contains("SHA-256", StringComparison.Ordinal));

        // Downloaden alleen na inloggen, als bestand.
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var adres = new Uri($"/bijlagen/{bijlage.Id}", UriKind.Relative);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(adres)).StatusCode);
        await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "/");
        var antwoord = await client.GetAsync(adres);
        Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);
        Assert.Equal(inhoud, await antwoord.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", antwoord.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", antwoord.Headers.GetValues("X-Content-Type-Options").Single());

        // Een eigen stap met een bijlage verdwijnt niet zonder reden.
        var eigen = (await Werkprogramma.VoegStapToe(kennis.Id, "Notulen AVA", null, Medewerker)).Waarde;
        await Bijlagen.VoegToe(eigen, "notulen.html", "text/html", new MemoryStream("<p>AVA</p>"u8.ToArray()), Medewerker);
        Assert.False((await Werkprogramma.VerwijderStap(eigen, null, Medewerker)).IsGelukt);
        var html = (await Kennis(dossierId)).Stappen.Single(s => s.Titel == "Notulen AVA").Bijlagen.Single();
        Assert.Equal("application/octet-stream", (await client.GetAsync(new Uri($"/bijlagen/{html.Id}", UriKind.Relative))).Content.Headers.ContentType?.MediaType);

        Assert.True((await Bijlagen.Verwijder(bijlage.Id, Medewerker)).IsGelukt);
        Assert.Empty((await Kennis(dossierId)).Stappen[0].Bijlagen);
        Assert.False((await Bijlagen.VoegToe(stap.Id, "leeg.txt", null, new MemoryStream(), Medewerker)).IsGelukt);
    }

    [Fact]
    public async Task Gereed_voor_review_vervalt_als_er_weer_iets_open_gaat()
    {
        var (dossierId, kennis) = await Dossier();

        Assert.Contains("stap(pen) open", Assert.Single((await Review.GereedVoorReview(kennis.Id, Medewerker)).Fouten), StringComparison.Ordinal);
        await AllesGereed(kennis);
        Assert.True((await Review.GereedVoorReview(kennis.Id, Medewerker)).IsGelukt);
        var gemeld = await Kennis(dossierId);
        Assert.Equal(("Mo Medewerker", true), (gemeld.TerReviewDoor, gemeld.IsTerReview));
        Assert.False((await Review.GereedVoorReview(kennis.Id, Medewerker)).IsGelukt);

        // Een stap weer open: niet meer gereed voor review.
        Assert.True((await Werkprogramma.ZetStatus(kennis.Stappen[0].Id, StapStatus.Open, null, Medewerker)).IsGelukt);
        Assert.False((await Kennis(dossierId)).IsTerReview);

        Assert.True((await Werkprogramma.ZetStatus(kennis.Stappen[0].Id, StapStatus.Gereed, null, Medewerker)).IsGelukt);
        Assert.True((await Review.GereedVoorReview(kennis.Id, Medewerker)).IsGelukt);
        Assert.True((await Review.TrekReviewIn(kennis.Id, Medewerker)).IsGelukt);
        Assert.False((await Kennis(dossierId)).IsTerReview);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.GereedVoorReview), a => a.Details == "Kennis van de cliënt");
    }

    [Fact]
    public async Task Reviewopmerking_toewijzen_aan_een_gebruiker()
    {
        var reviewer = await server.Beheerder();
        var (dossierId, kennis) = await Dossier();
        await AllesGereed(kennis);
        await Review.GereedVoorReview(kennis.Id, Medewerker);

        Assert.False((await Review.PlaatsPunt(kennis.Id, null, "Graag aanvullen.", reviewer, Guid.NewGuid())).IsGelukt);
        Assert.True((await Review.PlaatsPunt(kennis.Id, kennis.Stappen[0].Id, "Graag aanvullen.", reviewer, reviewer.Id)).IsGelukt);

        var na = await Kennis(dossierId);
        var punt = Assert.Single(na.Reviewpunten);
        Assert.Equal(("Bea Beheer", "Graag aanvullen."), (punt.ToegewezenAan, punt.Tekst));
        Assert.False(na.IsTerReview);
        Assert.Contains("reviewopmerking(en) open", Assert.Single((await Review.GereedVoorReview(kennis.Id, Medewerker)).Fouten), StringComparison.Ordinal);
    }
}
