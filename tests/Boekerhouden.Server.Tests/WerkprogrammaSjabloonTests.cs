using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

/// <summary>Het sjabloon geldt voor het hele kantoor; daarom een eigen database, los van de andere werkprogrammatests.</summary>
public sealed class WerkprogrammaSjabloonTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Beheer = new(null, "Bea Beheer");

    private WerkprogrammaSjabloonDienst Sjablonen => server.Dienst<WerkprogrammaSjabloonDienst>();

    private async Task<Guid> NieuwDossier(string naam)
    {
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), naam, Rechtsvorm.BV, null, null), Beheer)).Waarde;
        return (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Beheer)).Waarde;
    }

    [Fact]
    public async Task Wijziging_in_het_sjabloon_geldt_voor_nieuwe_dossiers_niet_voor_lopende()
    {
        var lopend = await NieuwDossier("Lopend BV");
        await server.Dienst<WerkprogrammaDienst>().Overzicht(lopend, Beheer);

        var bv = (await Sjablonen.Lijst()).Single(s => s.Soort == SoortOpdracht.SamenstellenJaarrekening);
        var sjabloon = (await Sjablonen.Haal(bv.Id))!;
        var acceptatie = sjabloon.Onderdelen.Single(o => o.Titel == "Opdracht en acceptatie");
        Assert.True((await Sjablonen.BewaarStap(acceptatie.Id, null, new SjabloonStapInvoer("Onafhankelijkheid medewerkers", "Geen persoonlijke belangen?", true), Beheer)).IsGelukt);
        Assert.True((await Sjablonen.ZetVersie(bv.Id, "2027.1", Beheer)).IsGelukt);

        var nieuw = await NieuwDossier("Nieuw BV");
        var nieuwProgramma = (await server.Dienst<WerkprogrammaDienst>().Overzicht(nieuw, Beheer))!;
        var lopendProgramma = (await server.Dienst<WerkprogrammaDienst>().Overzicht(lopend, Beheer))!;

        Assert.Equal("2027.1", nieuwProgramma.Versie);
        Assert.Contains(nieuwProgramma.Onderdelen.Single(o => o.Titel == "Opdracht en acceptatie").Stappen, s => s.Titel == "Onafhankelijkheid medewerkers" && s.Verplicht);
        Assert.Equal("2026.1", lopendProgramma.Versie);
        Assert.DoesNotContain(lopendProgramma.Onderdelen.SelectMany(o => o.Stappen), s => s.Titel == "Onafhankelijkheid medewerkers");
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.SjabloonGewijzigd), a => a.Details == "versie 2026.1 → 2027.1");
    }

    [Fact]
    public async Task Onderdelen_en_stappen_beheren()
    {
        var bv = (await Sjablonen.Lijst()).Single(s => s.Soort == SoortOpdracht.SamenstellenJaarrekening);

        Assert.False((await Sjablonen.VoegOnderdeelToe(bv.Id, Fase.Uitvoering, "Vastgoedbeleggingen", null, false, Beheer)).IsGelukt);
        Assert.False((await Sjablonen.VoegOnderdeelToe(bv.Id, Fase.Uitvoering, "Nog een keer MVA", "BMva", false, Beheer)).IsGelukt);
        var vastgoed = await Sjablonen.VoegOnderdeelToe(bv.Id, Fase.Uitvoering, "Vastgoedbeleggingen", "BVas", false, Beheer);
        Assert.True(vastgoed.IsGelukt, string.Join("; ", vastgoed.Fouten));
        var eerste = (await Sjablonen.BewaarStap(vastgoed.Waarde, null, new SjabloonStapInvoer("Taxatie", "Is er een recente taxatie?", false), Beheer)).Waarde;
        await Sjablonen.BewaarStap(vastgoed.Waarde, null, new SjabloonStapInvoer("Huurcontracten", null, false), Beheer);
        await Sjablonen.VerplaatsStap(eerste, 1, Beheer);

        var onderdeel = (await Sjablonen.Haal(bv.Id))!.Onderdelen.Single(o => o.HoofdpostCode == "BVas");
        Assert.Equal(["Huurcontracten", "Taxatie"], onderdeel.Stappen.Select(s => s.Titel));
        Assert.Equal(OnderdeelSoort.Post, onderdeel.Soort);

        var planning = (await Sjablonen.Haal(bv.Id))!.Onderdelen.Single(o => o.Soort == OnderdeelSoort.MaterielePosten);
        Assert.False((await Sjablonen.VerwijderOnderdeel(planning.Id, Beheer)).IsGelukt);
        Assert.True((await Sjablonen.VerwijderOnderdeel(onderdeel.Id, Beheer)).IsGelukt);
        Assert.DoesNotContain((await Sjablonen.Haal(bv.Id))!.Onderdelen, o => o.HoofdpostCode == "BVas");
    }
}
