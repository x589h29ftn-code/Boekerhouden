using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een actief wijzigen per datum: een extra afschrijving (eigen boeking, terug te draaien), een andere restwaarde vanaf
/// een maand, en een ander percentage en een andere restwaarde in dezelfde maand. Wat al geboekt is, blijft; het
/// grootboek, het plan, de activastaat en de aansluiting blijven met elkaar kloppen. De laptop: 1.200, maart 2026, 36
/// maanden (33,33 per maand).
/// </summary>
public sealed class ActivaWijzigenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id, Guid Laptop)> Laptop()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 3, 10), "Aanschaf",
            [new Journaalregel("0200", 1200m, "Laptop"), new Journaalregel("1800", -1200m)]))).IsGelukt);
        var dienst = app.Dienst<ActivaDienst>();
        var voorstel = (await dienst.Kandidaten(wie, id)).Single().Voorstel with { Looptijd = 36 };
        var laptop = await dienst.Bewaar(wie, id, null, voorstel);
        Assert.True(laptop.IsGelukt, string.Join("; ", laptop.Fouten));
        return (wie, id, laptop.Waarde);
    }

    private async Task<decimal> Saldo(Uitvoerder wie, Guid id, string rekening) =>
        (await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026)).Regels.SingleOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;

    [Fact]
    public async Task Extra_afschrijving_boeken_het_plan_past_zich_aan_en_terugdraaien_kan_tot_er_weer_is_afgeschreven()
    {
        var (wie, id, laptop) = await Laptop();
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand, laptop)).IsGelukt);

        // Te veel of niets: geweigerd.
        Assert.Contains((await dienst.ExtraAfschrijven(wie, id, laptop, new Extrainvoer(new(2026, 6, 15), 1200m))).Fouten, f => f.Contains("boekwaarde", StringComparison.Ordinal));
        Assert.False((await dienst.ExtraAfschrijven(wie, id, laptop, new Extrainvoer(new(2026, 6, 15), 0m))).IsGelukt);
        Assert.False((await dienst.ExtraAfschrijven(wie, id, laptop, new Extrainvoer(new(2026, 6, 15), 10m, "0210"))).IsGelukt);

        var uitkomst = await dienst.ExtraAfschrijven(wie, id, laptop, new Extrainvoer(new(2026, 6, 15), 300m, null, "Waardevermindering na waterschade"));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));

        // Grootboek: drie maanden (100) en de extra 300 op de kosten, tegen de cumulatieve afschrijving.
        Assert.Equal((400m, -400m), (await Saldo(wie, id, "4800"), await Saldo(wie, id, "0210")));
        var detail = (await dienst.Open(wie, id, laptop))!;
        Assert.Equal(800m, detail.Boekwaarde);
        var extra = Assert.Single(detail.Extra!);
        Assert.Equal((300m, "4800", "Waardevermindering na waterschade", true), (extra.Bedrag, extra.Kostenrekening, extra.Omschrijving, extra.KanTerug));
        Assert.True(detail.Vast);

        // Plan: de drie geboekte maanden blijven; wat over is (800) gaat over juni 2026 t/m februari 2029.
        Assert.Equal(100m, detail.Plan.Where(m => m.Geboekt).Sum(m => m.Bedrag));
        Assert.Equal(300m, detail.Plan.Single(m => m.Maand == new DateOnly(2026, 6, 1)).Extra);
        Assert.Equal(800m, detail.Plan.Where(m => !m.Geboekt).Sum(m => m.Bedrag));
        Assert.Equal((new DateOnly(2029, 2, 1), 0m), (detail.Plan[^1].Maand, detail.Plan[^1].Boekwaarde));
        Assert.InRange(detail.PerMaand, 24.24m, 24.25m);

        // Activastaat en aansluiting tellen de extra afschrijving mee.
        var staat = (await dienst.Activastaat(wie, id, 2026)).Single().Posten.Single().Regel;
        Assert.Equal(400m, staat.Afschrijving);
        Assert.Equal(-400m, (await dienst.Stand(wie, id, new DateOnly(2026, 12, 31)))["0210"]);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ActivumExtraAfgeschreven && r.AdministratieId == id);

        // Terugdraaien: een tegenboeking, alles weer zoals ervoor.
        Assert.True((await dienst.TerugdraaienExtra(wie, id, extra.Id)).IsGelukt);
        Assert.Equal((100m, -100m), (await Saldo(wie, id, "4800"), await Saldo(wie, id, "0210")));
        detail = (await dienst.Open(wie, id, laptop))!;
        Assert.Equal(("teruggedraaid", 1100m), (Assert.Single(detail.Extra!).Tegenboeking is null ? "open" : "teruggedraaid", detail.Boekwaarde));
        Assert.Equal(1200m, detail.Plan.Sum(m => m.Bedrag + m.Extra));
        Assert.False((await dienst.TerugdraaienExtra(wie, id, extra.Id)).IsGelukt);

        // Opnieuw, en daarna juli afschrijven: dan kan terugdraaien niet meer (juli ging uit van wat er toen over was).
        Assert.True((await dienst.ExtraAfschrijven(wie, id, laptop, new Extrainvoer(new(2026, 6, 15), 300m))).IsGelukt);
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 7, 31), Afschrijvingsfrequentie.Maand, laptop)).IsGelukt);
        detail = (await dienst.Open(wie, id, laptop))!;
        var tweede = detail.Extra!.Single(x => x.Tegenboeking is null);
        Assert.False(tweede.KanTerug);
        Assert.Contains((await dienst.TerugdraaienExtra(wie, id, tweede.Id)).Fouten, f => f.Contains("al afgeschreven", StringComparison.Ordinal));
        Assert.Equal(-(100m + 300m + detail.Plan.Where(m => m.Geboekt && m.Maand >= new DateOnly(2026, 6, 1)).Sum(m => m.Bedrag)), await Saldo(wie, id, "0210"));

        // Een actief met een extra afschrijving is niet meer te verwijderen.
        Assert.False((await dienst.Verwijder(wie, id, laptop)).IsGelukt);
    }

    [Fact]
    public async Task Restwaarde_per_datum_wat_ervoor_ligt_blijft()
    {
        var (wie, id, laptop) = await Laptop();
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand, laptop)).IsGelukt);

        Assert.Contains((await dienst.WijzigRestwaarde(wie, id, laptop, new DateOnly(2026, 5, 1), 200m)).Fouten, f => f.Contains("al afgeschreven", StringComparison.Ordinal));
        Assert.Contains((await dienst.WijzigRestwaarde(wie, id, laptop, new DateOnly(2027, 1, 1), 1000m)).Fouten, f => f.Contains("hoger dan de boekwaarde", StringComparison.Ordinal));
        Assert.True((await dienst.WijzigRestwaarde(wie, id, laptop, new DateOnly(2027, 1, 20), 200m)).IsGelukt);

        var detail = (await dienst.Open(wie, id, laptop))!;
        var wijziging = Assert.Single(detail.Herzieningen!);
        Assert.Equal((new DateOnly(2027, 1, 1), (decimal?)null, (decimal?)200m), (wijziging.Ingang, wijziging.PercentagePerJaar, wijziging.Restwaarde));
        Assert.All(detail.Plan.Where(m => m.Maand < new DateOnly(2027, 1, 1)), m => Assert.InRange(m.Bedrag, 33.33m, 33.34m));
        Assert.Equal((new DateOnly(2029, 2, 1), 200m), (detail.Plan[^1].Maand, detail.Plan[^1].Boekwaarde));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ActivumHerzien && r.AdministratieId == id && r.Details == "vanaf 01-2027 restwaarde 200,00");

        // Een percentage in dezelfde maand vult aan; de restwaarde blijft.
        Assert.True((await dienst.Herzie(wie, id, laptop, new DateOnly(2027, 1, 1), 20m)).IsGelukt);
        wijziging = Assert.Single((await dienst.Open(wie, id, laptop))!.Herzieningen!);
        Assert.Equal(((decimal?)20m, (decimal?)200m), (wijziging.PercentagePerJaar, wijziging.Restwaarde));
        Assert.Equal("20% per jaar, restwaarde 200,00", ActivaDienst.Tekst(wijziging));
        Assert.Equal(200m, (await dienst.Open(wie, id, laptop))!.Plan[^1].Boekwaarde);
    }

    [Fact]
    public async Task Wijzigen_en_extra_afschrijven_mag_alleen_het_kantoor()
    {
        var (wie, id, laptop) = await Laptop();
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var buiten = await app.Gebruiker("Bert Buiten");
        var dienst = app.Dienst<ActivaDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.ExtraAfschrijven(ondernemer, id, laptop, new Extrainvoer(new(2026, 6, 1), 10m)));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.ExtraAfschrijven(buiten, id, laptop, new Extrainvoer(new(2026, 6, 1), 10m)));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.WijzigRestwaarde(ondernemer, id, laptop, new DateOnly(2027, 1, 1), 100m));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.TerugdraaienExtra(ondernemer, id, 1));
        Assert.Empty((await dienst.Open(wie, id, laptop))!.Extra!);
    }
}
