using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Vpb;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De fiscale positie van een BV: uit het grootboek, met de hand aanpassen, de fiscale indeling en het dashboard.</summary>
public sealed class VpbTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static Journaalpost Memoriaal(string boekstuk, params Journaalregel[] regels) => new("MEM", boekstuk, new(2026, 3, 1), $"Boeking {boekstuk}", regels);

    private async Task<(Uitvoerder Wie, Guid Id)> Bv(string naam)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie(naam);
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0001", new("8000", -100_000m), new("1300", 100_000m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0002", new("4410", 10_000m), new("1300", -10_000m)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0003", new("1550", 5_000m), new("1300", -5_000m)))).IsGelukt);
        return (wie, id);
    }

    [Fact]
    public async Task Fiscale_positie_uit_het_grootboek_met_voorlopige_aanslag()
    {
        var (wie, id) = await Bv("Vpb Grootboek B.V.");

        var stand = (await app.Dienst<VpbDienst>().Positie(wie, id, 2026))!;

        var p = stand.Positie;
        Assert.Equal(90_000m, p.Regels.First(r => r.Omschrijving == "Resultaat voor belastingen").Totaal!.Bedrag);
        Assert.Equal(["4410"], p.Beperkt.Select(r => r.Nummer));
        Assert.Equal(92_650m, p.Belastbaar);
        Assert.Equal(17_603m, p.Vennootschapsbelasting);
        Assert.Equal(5_000m, p.Regels.First(r => r.Omschrijving == "Betaalde voorlopige aanslagen").Totaal!.Bedrag);
        Assert.Equal(12_603m, p.TeBetalen);

        // Een eenmanszaak heeft geen fiscale positie.
        var ez = await app.Administratie("Vpb Eenmanszaak", Ondernemingsvorm.Eenmanszaak);
        Assert.Null(await app.Dienst<VpbDienst>().Positie(wie, ez, 2026));
        Assert.False((await app.Dienst<VpbDienst>().PasAan(wie, ez, 2026, "vpb.betaald", 1m)).IsGelukt);
    }

    [Fact]
    public async Task Met_de_hand_aanpassen_en_fiscale_indeling_wijzigen_alleen_het_kantoor()
    {
        var (wie, id) = await Bv("Vpb Aanpassen B.V.");
        var dienst = app.Dienst<VpbDienst>();

        // Voorlopige aanslag met de hand, en een ander tarief.
        Assert.True((await dienst.PasAan(wie, id, 2026, "vpb.betaald", 17_603m)).IsGelukt);
        Assert.True((await dienst.PasAan(wie, id, 2026, "vpb.tarief.laag", 20m)).IsGelukt);
        Assert.False((await dienst.PasAan(wie, id, 2026, "vpb.fiscaal", 1m)).IsGelukt);
        var p = (await dienst.Positie(wie, id, 2026))!.Positie;
        Assert.Equal((18_530m, 927m), (p.Vennootschapsbelasting, p.TeBetalen));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.FiscalepositieAangepast && r.Onderwerp == "2026 vpb.betaald");

        // De btw-aangifte ziet de aanpassingen van de fiscale positie niet.
        Assert.Empty((await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, 2026)).Aanpassingen);

        // Reis- en verblijfkosten gewoon aftrekbaar: geen beperkt aftrekbare kosten meer.
        Assert.True((await dienst.ZetFiscaal(wie, id, "4410", Fiscaalsoort.Gewoon)).IsGelukt);
        Assert.Empty((await dienst.Positie(wie, id, 2026))!.Positie.Beperkt);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.FiscaleindelingGewijzigd && r.Details == "beperkt aftrekbaar → gewoon");
        Assert.False((await dienst.ZetFiscaal(wie, id, "9999", Fiscaalsoort.NietAftrekbaar)).IsGelukt);

        var ondernemer = await app.Gebruiker("Otto Vpb");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        Assert.NotNull(await dienst.Positie(ondernemer, id, 2026));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.PasAan(ondernemer, id, 2026, "vpb.betaald", 1m));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.ZetFiscaal(ondernemer, id, "4410", Fiscaalsoort.NietAftrekbaar));
        var buiten = await app.Gebruiker("Buiten Vpb");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Positie(buiten, id, 2026));
    }

    [Fact]
    public async Task Dashboard_van_een_bv_toont_de_verwachte_vennootschapsbelasting()
    {
        var (wie, id) = await Bv("Vpb Dashboard B.V.");
        var dashboards = app.Dienst<DashboardDienst>();

        var indeling = await dashboards.Indeling(wie, id);
        Assert.Contains(indeling, k => k.Soort == Widget.Vennootschapsbelasting);
        var dashboard = await dashboards.Laad(wie, id, [new(Widget.Vennootschapsbelasting)]);
        Assert.NotNull(dashboard.Vennootschapsbelasting);

        var ez = await app.Administratie("Vpb Dashboard Eenmanszaak", Ondernemingsvorm.Eenmanszaak);
        Assert.DoesNotContain(await dashboards.Indeling(wie, ez), k => k.Soort == Widget.Vennootschapsbelasting);
    }
}
