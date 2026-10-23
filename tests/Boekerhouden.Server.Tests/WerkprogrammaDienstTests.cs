using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

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

        Assert.Equal("2026.1", o.Versie);
        Assert.Equal(nogmaals.Onderdelen.Select(x => x.Id), o.Onderdelen.Select(x => x.Id));
        Assert.DoesNotContain(o.Onderdelen, x => x.Fase == Fase.Uitvoering);
        Assert.Equal(["Opdracht en acceptatie", "Kennis van de cliënt", "Planning"], o.Onderdelen.Where(x => x.Fase == Fase.Voorbereiding).Select(x => x.Titel));
        Assert.Contains(o.Onderdelen, x => x.Titel == "Bevestiging klant" && x.Verplicht);
        Assert.All(o.Onderdelen.SelectMany(x => x.Stappen), s => Assert.False(s.Eigen));
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.WerkprogrammaGestart), a => a.Details == "Samenstellen jaarrekening 2026.1");
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
        Assert.True((await Overzicht(dossierId)).Keuzes["BMva"]);

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
        Assert.False((await Overzicht(dossierId)).Keuzes["BMva"]);

        // Weer materieel: de stappen gaan weer open.
        await Werkprogramma.KiesMaterieel(dossierId, "BMva", true, null, Tester);
        Assert.All((await Overzicht(dossierId)).Post("BMva")!.Stappen, s => Assert.Equal(StapStatus.Open, s.Status));
    }

    [Fact]
    public async Task Aftekenen_niet_van_toepassing_en_controles()
    {
        var (_, dossierId) = await Bakkerij();
        var o = await Overzicht(dossierId);
        var opdracht = Stap(o, "Opdracht en acceptatie", "Opdrachtbevestiging");
        var keuze = Stap(o, "Planning", "Keuze materiële posten");
        var gekoppeld = Stap(o, "Planning", "Alle rekeningen gerubriceerd");

        Assert.True((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.Gereed, null, Tester)).IsGelukt);
        var afgetekend = Stap(await Overzicht(dossierId), "Opdracht en acceptatie", "Opdrachtbevestiging");
        Assert.Equal("Test Gebruiker", afgetekend.UitgevoerdDoor);
        Assert.NotNull(afgetekend.UitgevoerdOp);

        Assert.Contains("reden", Assert.Single((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.NietVanToepassing, " ", Tester)).Fouten), StringComparison.Ordinal);
        Assert.True((await Werkprogramma.ZetStatus(opdracht.Id, StapStatus.Open, null, Tester)).IsGelukt);
        Assert.Null(Stap(await Overzicht(dossierId), "Opdracht en acceptatie", "Opdrachtbevestiging").UitgevoerdDoor);

        // Keuze materiële posten: pas gereed als elke hoofdpost met saldo een keuze heeft.
        var nogNiet = await Werkprogramma.ZetStatus(keuze.Id, StapStatus.Gereed, null, Tester);
        Assert.Contains("Materiële vaste activa", Assert.Single(nogNiet.Fouten), StringComparison.Ordinal);
        await Werkprogramma.KiesMaterieel(dossierId, "BMva", true, null, Tester);
        var hoofdposten = (await Werkprogramma.HoofdpostenMetSaldo(dossierId)).Select(p => p.Code).ToList();
        Assert.Equal(["BMva", "BLim", "WOmz"], hoofdposten);
        Assert.Equal(2, (await Werkprogramma.BevestigNietMaterieel(dossierId, hoofdposten, Tester)).Waarde);
        Assert.True((await Werkprogramma.ZetStatus(keuze.Id, StapStatus.Gereed, null, Tester)).IsGelukt);

        // Alles gerubriceerd: lukt niet zolang er rekeningen met saldo ongekoppeld zijn.
        Assert.Contains("Rubriceer", Assert.Single((await Werkprogramma.ZetStatus(gekoppeld.Id, StapStatus.Gereed, null, Tester)).Fouten), StringComparison.Ordinal);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.StapAfgetekend), a => a.Details!.Contains("Keuze materiële posten: gereed", StringComparison.Ordinal));
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
        Assert.Equal(2, (await Overzicht(dossierId)).Onderdelen.Single(x => x.Titel == "Kennis van de cliënt").Aantal);
        Assert.True((await Werkprogramma.HerstelStap(standaard.Id, Tester)).IsGelukt);
        Assert.False(Stap(await Overzicht(dossierId), "Kennis van de cliënt", "Bijzonderheden in het boekjaar").Verwijderd);

        // Verplichte stap: niet te verwijderen.
        var verplicht = Stap(o, "Opdracht en acceptatie", "Opdrachtbevestiging");
        Assert.Contains("verplichte", Assert.Single((await Werkprogramma.VerwijderStap(verplicht.Id, "reden", Tester)).Fouten), StringComparison.Ordinal);

        // Verplaatsen: de tweede stap gaat naar boven.
        await Werkprogramma.Verplaats(standaard.Id, -1, Tester);
        Assert.Equal("Bijzonderheden in het boekjaar", (await Overzicht(dossierId)).Onderdelen.Single(x => x.Titel == "Kennis van de cliënt").Stappen[0].Titel);
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
        Assert.DoesNotContain(o.Onderdelen, x => x.Titel == "Bevestiging klant");
    }
}
