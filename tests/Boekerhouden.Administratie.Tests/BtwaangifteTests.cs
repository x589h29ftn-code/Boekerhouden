using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De btw-module: aangifte per periode, vastleggen, rondrekening en aansluiting.</summary>
public sealed class BtwaangifteTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>Een verkoop met 21% btw in het memoriaal: omzet en btw met code V21.</summary>
    private static Journaalpost Verkoop(string boekstuk, DateOnly datum, decimal omzet) =>
        new("MEM", boekstuk, datum, $"Verkoop {boekstuk}",
            [new("8000", -omzet, BtwCode: "V21"), new("1510", -omzet * 0.21m, BtwCode: "V21"), new("1300", omzet * 1.21m)]);

    [Fact]
    public async Task Aangifte_per_kwartaal_vastleggen_later_boeken_geeft_verschil_en_de_aansluiting_sluit()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        var dienst = app.Dienst<BtwaangifteDienst>();
        Assert.True((await grootboek.Boek(wie, id, Verkoop("2026-0001", new(2026, 2, 1), 1000m))).IsGelukt);

        var voor = await dienst.Jaar(wie, id, 2026);
        Assert.Equal(Aangiftefrequentie.Kwartaal, voor.Jaar.Frequentie);
        Assert.Equal(210m, voor.Jaar.Perioden[0].Berekend.TeBetalen);
        Assert.Equal(0m, voor.Aansluitingen[0].Verschil);
        Assert.Equal(210m, voor.Aansluitingen[0].Saldo);

        Assert.True((await dienst.Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.False((await dienst.Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Verkoop("2026-0002", new(2026, 3, 15), 100m))).IsGelukt);

        var na = await dienst.Jaar(wie, id, 2026);
        Assert.Equal((231m, 21m), (na.Jaar.Perioden[0].Berekend.TeBetalen, na.Jaar.Perioden[0].Verschil));
        Assert.Equal((210m, 21m, 0m), (na.Jaar.Aangegeven, na.Jaar.Suppletie, na.Jaar.NietAangegeven));

        // De rondrekening: 5c van het 1e kwartaal zoals aangegeven, de administratie met de latere boeking, en de afwijking.
        var vijfC = na.Rondrekening.Regels.Single(r => r.Code == "5c");
        Assert.Equal((210m, 231m, 21m), (vijfC.Perioden[0], vijfC.Administratie, vijfC.Afwijking));
        Assert.Equal(["2026-0001", "2026-0002"], na.Rondrekening.Bronnen(vijfC.Sleutel!, new(2026, 1, 1), new(2026, 3, 31)).Select(b => b.Post.Boekstuk).Distinct());
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BtwaangifteVastgelegd && r.Onderwerp == "1e kwartaal 2026");

        Assert.True((await dienst.Intrekken(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.Null((await dienst.Jaar(wie, id, 2026)).Jaar.Perioden[0].Vastgelegd);
        Assert.False((await dienst.Intrekken(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
    }

    [Fact]
    public async Task Frequentie_per_administratie_en_alleen_het_kantoor_legt_vast()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Maand B.V.");
        var dienst = app.Dienst<BtwaangifteDienst>();

        await dienst.ZetFrequentie(wie, id, Aangiftefrequentie.Maand);

        Assert.Equal(Aangiftefrequentie.Maand, await dienst.Frequentie(wie, id));
        Assert.Equal(12, (await dienst.Jaar(wie, id, 2026)).Jaar.Perioden.Count);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BtwfrequentieGewijzigd && r.Details == "per kwartaal → per maand");

        var ondernemer = await app.Gebruiker("Otto Btw");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        Assert.NotNull(await dienst.Jaar(ondernemer, id, 2026));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Vastleggen(ondernemer, id, new(2026, 1, 1), new(2026, 1, 31)));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.ZetFrequentie(ondernemer, id, Aangiftefrequentie.Jaar));
        var buiten = await app.Gebruiker("Buiten Btw");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Jaar(buiten, id, 2026));
    }
}
