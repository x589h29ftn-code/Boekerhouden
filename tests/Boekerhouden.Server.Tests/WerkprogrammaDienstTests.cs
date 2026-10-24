using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using StandaardWerkprogramma = Boekerhouden.Server.Werkprogramma.StandaardWerkprogramma;

namespace Boekerhouden.Server.Tests;

public sealed class WerkprogrammaDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private WerkprogrammaDienst Werkprogramma => server.Dienst<WerkprogrammaDienst>();

    /// <summary>De fictieve bakkerij (BV) met een dossier 2025; inventaris, kas en omzet zijn gekoppeld, de rest niet.</summary>
    private async Task<(Guid KlantId, Guid DossierId)> Bakkerij()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, 2025, TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv"), "2025.csv", Tester)).IsGelukt);
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var rubriceer = server.Dienst<RubriceerDienst>();
        var r = (await rubriceer.Overzicht(dossierId, metVoorstellen: false))!.Rekeningen.ToDictionary(x => x.Nummer, x => x.Id);
        await rubriceer.Koppel(klantId, [r["0200"], r["0210"]], "BMvaBeiCae", false, Tester);
        await rubriceer.Koppel(klantId, [r["1000"]], "BLimKasKas", false, Tester);
        await rubriceer.Koppel(klantId, [r["8000"]], "WOmzNooNdl", false, Tester);
        return (klantId, dossierId);
    }

    private async Task<WerkprogrammaOverzicht> Overzicht(Guid dossierId) => (await Werkprogramma.Overzicht(dossierId, Tester))!;

    private static StapInfo Stap(WerkprogrammaOverzicht o, string onderdeel, string stap) =>
        o.Onderdelen.Single(x => x.Titel == onderdeel).Stappen.Single(s => s.Titel == stap);

    [Fact]
    public async Task Dossier_start_met_een_kopie_van_voorbereiding_en_afronding()
    {
        var (_, dossierId) = await Bakkerij();

        var o = await Overzicht(dossierId);
        var nogmaals = await Overzicht(dossierId);

        Assert.Equal(StandaardWerkprogramma.Versie, o.Versie);
        Assert.Equal(nogmaals.Onderdelen.Select(x => x.Id), o.Onderdelen.Select(x => x.Id));
        Assert.DoesNotContain(o.Onderdelen, x => x.Fase == Fase.Uitvoering);
        Assert.Equal(["Opdracht en acceptatie", "Kennis van de cliënt", "Planning"], o.Onderdelen.Where(x => x.Fase == Fase.Voorbereiding).Select(x => x.Titel));
        Assert.Contains(o.Onderdelen, x => x.Titel == "Bespreking met de klant" && x.Verplicht && x.Soort == OnderdeelSoort.Bespreking);
        Assert.All(o.Onderdelen.SelectMany(x => x.Stappen), s => Assert.False(s.Eigen));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.WerkprogrammaGestart), a => a.Details == $"Samenstellen jaarrekening {StandaardWerkprogramma.Versie}");
    }

    [Fact]
    public async Task Materiele_post_krijgt_het_postprogramma_en_ingevulde_stappen_verdwijnen_niet_ongemerkt()
    {
        var (_, dossierId) = await Bakkerij();
        await Overzicht(dossierId);

        Assert.True((await Werkprogramma.KiesMaterieel(dossierId, "BMva", true, null, Tester)).IsGelukt);
        var post = (await Overzicht(dossierId)).Post("BMva")!;
        Assert.Equal(Fase.Uitvoering, post.Fase);
        Assert.Equal(["Verloopoverzicht", "Investeringen", "Afschrijvingen", "Desinvesteringen"], post.Stappen.Select(s => s.Titel));
        Assert.Equal(PostBeoordeling.Materieel, (await Overzicht(dossierId)).Keuzes["BMva"].Beoordeling);

        // Niets ingevuld: de post verdwijnt gewoon.
        Assert.True((await Werkprogramma.KiesMaterieel(dossierId, "BMva", false, null, Tester)).IsGelukt);
        Assert.Null((await Overzicht(dossierId)).Post("BMva"));

        // Ingevuld: eerst kiezen tussen bewaren en verwijderen.
        await Werkprogramma.KiesMaterieel(dossierId, "BMva", true, null, Tester);
        var stap = (await Overzicht(dossierId)).Post("BMva")!.Stappen[0];
        await Werkprogramma.BewaarAntwoord(stap.Id, "Verloop aangesloten op het grootboek.", Tester);
        var vraag = await Werkprogramma.KiesMaterieel(dossierId, "BMva", false, null, Tester);
        Assert.Equal(WerkprogrammaDienst.BewarenOfVerwijderen, Assert.Single(vraag.Fouten));

        Assert.True((await Werkprogramma.KiesMaterieel(dossierId, "BMva", false, ingevuldBewaren: true, Tester)).IsGelukt);
        var bewaard = (await Overzicht(dossierId)).Post("BMva")!;
        Assert.All(bewaard.Stappen, s => Assert.Equal(StapStatus.NietVanToepassing, s.Status));
        Assert.Equal("Verloop aangesloten op het grootboek.", bewaard.Stappen[0].Antwoord);
        Assert.False((await Overzicht(dossierId)).Keuzes["BMva"].MetWerk);

        // Weer materieel: de stappen gaan weer open.
        await Werkprogramma.KiesMaterieel(dossierId, "BMva", true, null, Tester);
        Assert.All((await Overzicht(dossierId)).Post("BMva")!.Stappen, s => Assert.Equal(StapStatus.Open, s.Status));
    }

    [Fact]
    public async Task Risicopost_krijgt_het_postprogramma_en_een_risico()
    {
        var (_, dossierId) = await Bakkerij();
        await Overzicht(dossierId);

        Assert.False((await Werkprogramma.ZetRisico(dossierId, "BMva", "Te hoge waardering", Tester)).IsGelukt);
        Assert.True((await Werkprogramma.Beoordeel(dossierId, "BMva", PostBeoordeling.Risicopost, null, Tester)).IsGelukt);
        Assert.True((await Werkprogramma.ZetRisico(dossierId, "BMva", " Te hoge waardering van de oven ", Tester)).IsGelukt);

        var o = await Overzicht(dossierId);
        Assert.Equal(new PostKeuzeInfo(PostBeoordeling.Risicopost, "Te hoge waardering van de oven"), o.Keuzes["BMva"]);
        Assert.NotNull(o.Post("BMva"));
        Assert.False(o.Keuzes.ContainsKey("BLim"));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.PostRisicopost), a => a.Details == "BMva");
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.RisicoVastgelegd), a => a.Details == "BMva: Te hoge waardering van de oven");

        // Van risicopost naar materieel: het programma blijft, het risico ook.
        Assert.True((await Werkprogramma.Beoordeel(dossierId, "BMva", PostBeoordeling.Materieel, null, Tester)).IsGelukt);
        Assert.Equal(PostBeoordeling.Materieel, (await Overzicht(dossierId)).Keuzes["BMva"].Beoordeling);
        Assert.NotNull((await Overzicht(dossierId)).Post("BMva"));
    }

    [Fact]
    public async Task Bedrijfsverkenning_en_beoordeling_rollen_door_naar_volgend_jaar()
    {
        var (klantId, dossierId) = await Bakkerij();
        var o = await Overzicht(dossierId);
        await Werkprogramma.BewaarAntwoord(Stap(o, "Kennis van de cliënt", "Activiteiten en afhankelijkheden").Id, "Bakkerij met drie winkels; geen grote afnemers.", Tester);
        await Werkprogramma.Beoordeel(dossierId, "BMva", PostBeoordeling.Risicopost, null, Tester);
        await Werkprogramma.ZetRisico(dossierId, "BMva", "Ovens te hoog gewaardeerd", Tester);
        var bestand = Encoding.UTF8.GetString(TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv")).Replace("Boekjaar;2025", "Boekjaar;2026", StringComparison.Ordinal);
        Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, 2026, Encoding.UTF8.GetBytes(bestand), "2026.csv", Tester)).IsGelukt);
        var volgend = (await server.Dienst<DossierDienst>().Maak(klantId, 2026, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;

        var n = await Overzicht(volgend);

        var stap = Stap(n, "Kennis van de cliënt", "Activiteiten en afhankelijkheden");
        Assert.Equal(("Bakkerij met drie winkels; geen grote afnemers.", 2025, StapStatus.Open), (stap.Antwoord, stap.OvergenomenVan, stap.Status));
        Assert.Equal(new PostKeuzeInfo(PostBeoordeling.Risicopost, "Ovens te hoog gewaardeerd"), n.Keuzes["BMva"]);
        Assert.NotNull(n.Post("BMva"));

        // Bijwerken: dan is het antwoord van dit jaar.
        await Werkprogramma.BewaarAntwoord(stap.Id, "Bakkerij met vier winkels.", Tester);
        Assert.Null(Stap(await Overzicht(volgend), "Kennis van de cliënt", "Activiteiten en afhankelijkheden").OvergenomenVan);
    }

    [Fact]
    public async Task Aftekenen_niet_van_toepassing_en_controles()
    {
        var (_, dossierId) = await Bakkerij();
        var o = await Overzicht(dossierId);
        var opdracht = Stap(o, "Opdracht en acceptatie", "Opdrachtbevestiging");
        var keuze = Stap(o, "Planning", "Beoordeling posten");
        var gekoppeld = Stap(o, "Planning", "Alle rekeningen gerubriceerd");

        Assert.True((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.Gereed, null, Tester)).IsGelukt);
        var afgetekend = Stap(await Overzicht(dossierId), "Opdracht en acceptatie", "Opdrachtbevestiging");
        Assert.Equal("Test Gebruiker", afgetekend.UitgevoerdDoor);
        Assert.NotNull(afgetekend.UitgevoerdOp);

        Assert.Contains("reden", Assert.Single((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.NietVanToepassing, " ", Tester)).Fouten), StringComparison.Ordinal);
        Assert.True((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.Open, null, Tester)).IsGelukt);
        Assert.Null(Stap(await Overzicht(dossierId), "Opdracht en acceptatie", "Opdrachtbevestiging").UitgevoerdDoor);

        // Beoordeling posten: standaard is alles n.v.t., dus aftekenen kan meteen.
        Assert.True((await Werkprogramma.ZetStatus(keuze.Id, StapStatus.Gereed, null, Tester)).IsGelukt);

        // Alles gerubriceerd: lukt niet zolang er rekeningen met saldo ongekoppeld zijn.
        Assert.Contains("Rubriceer", Assert.Single((await Werkprogramma.ZetStatus(gekoppeld.Id, StapStatus.Gereed, null, Tester)).Fouten), StringComparison.Ordinal);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.StapAfgetekend), a => a.Details!.Contains("Beoordeling posten: gereed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stappen_toevoegen_verwijderen_met_reden_herstellen_en_verplaatsen()
    {
        var (_, dossierId) = await Bakkerij();
        var o = await Overzicht(dossierId);
        var kennis = o.Onderdelen.Single(x => x.Titel == "Kennis van de cliënt");

        var eigen = (await Werkprogramma.VoegStapToe(kennis.Id, "Organisatieschema", "Vraag het organisatieschema op.", Tester)).Waarde;
        Assert.True(Stap(await Overzicht(dossierId), "Kennis van de cliënt", "Organisatieschema").Eigen);

        // Eigen stap zonder invulling: echt weg, zonder reden.
        Assert.True((await Werkprogramma.VerwijderStap(eigen, null, Tester)).IsGelukt);
        Assert.DoesNotContain((await Overzicht(dossierId)).Onderdelen.SelectMany(x => x.Stappen), s => s.Titel == "Organisatieschema");

        // Standaardstap: alleen met reden, en blijft zichtbaar als verwijderd.
        var standaard = Stap(o, "Kennis van de cliënt", "Bijzonderheden in het boekjaar");
        Assert.False((await Werkprogramma.VerwijderStap(standaard.Id, null, Tester)).IsGelukt);
        Assert.True((await Werkprogramma.VerwijderStap(standaard.Id, "Geen bijzonderheden bij deze klant", Tester)).IsGelukt);
        var verwijderd = Stap(await Overzicht(dossierId), "Kennis van de cliënt", "Bijzonderheden in het boekjaar");
        Assert.True(verwijderd.Verwijderd);
        Assert.Equal("Geen bijzonderheden bij deze klant", verwijderd.Reden);
        Assert.Equal(9, (await Overzicht(dossierId)).Onderdelen.Single(x => x.Titel == "Kennis van de cliënt").Aantal);
        Assert.True((await Werkprogramma.HerstelStap(standaard.Id, Tester)).IsGelukt);
        Assert.False(Stap(await Overzicht(dossierId), "Kennis van de cliënt", "Bijzonderheden in het boekjaar").Verwijderd);

        // Verplichte stap: niet te verwijderen.
        var verplicht = Stap(o, "Opdracht en acceptatie", "Opdrachtbevestiging");
        Assert.Contains("verplichte", Assert.Single((await Werkprogramma.VerwijderStap(verplicht.Id, "reden", Tester)).Fouten), StringComparison.Ordinal);

        // Verplaatsen: de stap gaat één plaats omhoog.
        var voor = (await Overzicht(dossierId)).Onderdelen.Single(x => x.Titel == "Kennis van de cliënt").Stappen.Select(x => x.Titel).ToList();
        await Werkprogramma.Verplaats(standaard.Id, -1, Tester);
        var na = (await Overzicht(dossierId)).Onderdelen.Single(x => x.Titel == "Kennis van de cliënt").Stappen.Select(x => x.Titel).ToList();
        Assert.Equal(voor.IndexOf("Bijzonderheden in het boekjaar") - 1, na.IndexOf("Bijzonderheden in het boekjaar"));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.StapVerwijderd), a => a.Details!.Contains("Geen bijzonderheden", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Eenmanszaak_krijgt_het_werkprogramma_voor_de_winstaangifte()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Kapsalon", Rechtsvorm.Eenmanszaak, null, null), Tester)).Waarde;
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.WinstaangifteOpstellen, Tester)).Waarde;

        var o = await Overzicht(dossierId);

        Assert.Contains(o.Onderdelen, x => x.Titel == "Kennis van de ondernemer");
        Assert.Contains(o.Onderdelen, x => x.Titel == "Fiscale winst en aangifte");
        Assert.Contains(o.Onderdelen, x => x.Titel == "Bespreking met de ondernemer" && x.Soort == OnderdeelSoort.Bespreking);
        Assert.DoesNotContain(o.Onderdelen, x => x.Titel == "Bespreking met de klant");
    }
}
