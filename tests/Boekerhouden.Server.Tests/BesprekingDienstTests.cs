using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;

namespace Boekerhouden.Server.Tests;

public sealed class BesprekingDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private static readonly DateOnly Gisteren = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

    private BesprekingDienst Bespreking => server.Dienst<BesprekingDienst>();

    private DoorleesDienst Doorlezen => server.Dienst<DoorleesDienst>();

    private WerkprogrammaDienst Werkprogramma => server.Dienst<WerkprogrammaDienst>();

    private async Task<Guid> Dossier(SoortOpdracht soort = SoortOpdracht.SamenstellenJaarrekening)
    {
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Bespreking B.V.",
            soort == SoortOpdracht.SamenstellenJaarrekening ? Rechtsvorm.BV : Rechtsvorm.Eenmanszaak, null, null), Tester)).Waarde;
        return (await server.Dienst<DossierDienst>().Maak(klantId, 2025, soort, Tester)).Waarde;
    }

    private async Task<OnderdeelInfo> Onderdeel(Guid dossierId) =>
        (await Werkprogramma.Overzicht(dossierId, Tester))!.Onderdelen.Single(o => o.Soort == OnderdeelSoort.Bespreking);

    [Fact]
    public async Task Bespreekpunten_met_uitkomst_en_de_erkenning_zijn_nodig_voor_het_aftekenen()
    {
        var dossierId = await Dossier();
        var onderdeel = await Onderdeel(dossierId);
        var (besproken, erkenning) = (onderdeel.Stappen.Single(s => s.Controle == StapControle.BespreekpuntenBesproken), onderdeel.Stappen.Single(s => s.Controle == StapControle.ErkenningVastgelegd));
        var dividend = (await Doorlezen.VoegBespreekpuntToe(dossierId, "Dividenduitkering", "Wil de DGA dividend uitkeren?", Tester)).Waarde;
        var voorziening = (await Doorlezen.VoegBespreekpuntToe(dossierId, "Voorziening dubieuze debiteuren", null, Tester)).Waarde;

        Assert.Equal("Vul eerst de datum van de bespreking in.", Assert.Single((await Werkprogramma.ZetStatus(besproken.Id, StapStatus.Gereed, null, Tester)).Fouten));
        Assert.False((await Bespreking.BewaarBespreking(dossierId, DateOnly.FromDateTime(DateTime.Today.AddDays(10)), null, null, Tester)).IsGelukt);
        Assert.True((await Bespreking.BewaarBespreking(dossierId, Gisteren, "J. Jansen (bestuurder), Test Gebruiker", null, Tester)).IsGelukt);
        Assert.Equal("Nog 2 bespreekpunt(en) zonder uitkomst.", Assert.Single((await Werkprogramma.ZetStatus(besproken.Id, StapStatus.Gereed, null, Tester)).Fouten));

        Assert.True((await Bespreking.BewaarUitkomst(dividend, "Geen dividend in 2026; winst blijft in de BV.", false, Tester)).IsGelukt);
        Assert.True((await Bespreking.BewaarUitkomst(voorziening, " Voorziening van 10% op vorderingen ouder dan 90 dagen. ", true, Tester)).IsGelukt);
        Assert.True((await Werkprogramma.ZetStatus(besproken.Id, StapStatus.Gereed, null, Tester)).IsGelukt);

        Assert.StartsWith("Leg eerst vast wie", Assert.Single((await Werkprogramma.ZetStatus(erkenning.Id, StapStatus.Gereed, null, Tester)).Fouten), StringComparison.Ordinal);
        Assert.True((await Bespreking.BewaarErkenning(dossierId, "J. Jansen, bestuurder", Gisteren, Erkenningswijze.Bevestigingsbrief, Tester)).IsGelukt);
        Assert.True((await Werkprogramma.ZetStatus(erkenning.Id, StapStatus.Gereed, null, Tester)).IsGelukt);

        var b = (await Bespreking.Overzicht(dossierId))!;
        Assert.Equal((Gisteren, "J. Jansen (bestuurder), Test Gebruiker", true, 0), (b.Datum!.Value, b.Aanwezigen, b.IsErkend, b.Open));
        Assert.Equal(("Voorziening van 10% op vorderingen ouder dan 90 dagen.", true), (b.Punten[1].Uitkomst, b.Punten[1].Significant));

        // Een besproken punt kan niet meer weg; leeg maken zet het weer open.
        Assert.False((await Doorlezen.VerwijderBespreekpunt(dividend, Tester)).IsGelukt);
        Assert.True((await Bespreking.BewaarUitkomst(dividend, "  ", false, Tester)).IsGelukt);
        Assert.Equal(1, (await Bespreking.Overzicht(dossierId))!.Open);

        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.ErkenningVastgelegd), a => a.Details == $"J. Jansen, bestuurder, {Weergave.Datum(Gisteren)}, bevestigingsbrief");
    }

    [Fact]
    public async Task Na_de_review_ligt_de_bespreking_vast()
    {
        var dossierId = await Dossier();
        var punt = (await Doorlezen.VoegBespreekpuntToe(dossierId, "Continuïteit", null, Tester)).Waarde;
        await Bespreking.BewaarBespreking(dossierId, Gisteren, null, null, Tester);
        await Bespreking.BewaarUitkomst(punt, "Bank verlengt het krediet.", true, Tester);
        await Bespreking.BewaarErkenning(dossierId, "J. Jansen", Gisteren, null, Tester);
        var onderdeel = await Onderdeel(dossierId);
        foreach (var stap in onderdeel.Stappen)
        {
            Assert.True((await Werkprogramma.ZetStatus(stap.Id, StapStatus.Gereed, null, Tester)).IsGelukt);
        }

        Assert.True((await server.Dienst<ReviewDienst>().Reviewen(onderdeel.Id, await server.Beheerder())).IsGelukt);

        Assert.True((await Bespreking.Overzicht(dossierId))!.Vergrendeld);
        Assert.Equal(BesprekingDienst.Vergrendeld, Assert.Single((await Bespreking.BewaarUitkomst(punt, "Anders.", true, Tester)).Fouten));
        Assert.Equal(BesprekingDienst.Vergrendeld, Assert.Single((await Bespreking.BewaarErkenning(dossierId, "X", Gisteren, null, Tester)).Fouten));
    }

    [Fact]
    public async Task Bespreekverslag_met_punten_uitkomst_en_erkenning()
    {
        var dossierId = await Dossier(SoortOpdracht.WinstaangifteOpstellen);
        Assert.Contains((await Werkprogramma.Overzicht(dossierId, Tester))!.Onderdelen, o => o.Titel == "Bespreking met de ondernemer");
        var punt = (await Doorlezen.VoegBespreekpuntToe(dossierId, "Privégebruik auto <script>", "Rit-registratie?", Tester)).Waarde;
        await Doorlezen.VoegBespreekpuntToe(dossierId, "Investeringsaftrek", null, Tester);
        await Bespreking.BewaarBespreking(dossierId, Gisteren, "De ondernemer", "Goed gesprek.\nAlles doorgenomen.", Tester);
        await Bespreking.BewaarUitkomst(punt, "Bijtelling 22%.", true, Tester);

        var verslag = (await Bespreking.Verslag(dossierId, Tester))!;
        var html = BespreekverslagHtml.Html(verslag);

        Assert.Contains("auto &lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("Bijtelling 22%.", html, StringComparison.Ordinal);
        Assert.Contains("significante aangelegenheid", html, StringComparison.Ordinal);
        Assert.Contains("Nog niet besproken.", html, StringComparison.Ordinal);
        Assert.Contains("Goed gesprek.<br>Alles doorgenomen.", html, StringComparison.Ordinal);
        Assert.Contains("De ondernemer bevestigt", html, StringComparison.Ordinal);
        Assert.Contains("Nog niet vastgelegd.", html, StringComparison.Ordinal);
        Assert.Equal("Bespreekverslag 2025 Bespreking B.V..pdf", BespreekverslagHtml.Bestandsnaam(verslag));
    }
}
