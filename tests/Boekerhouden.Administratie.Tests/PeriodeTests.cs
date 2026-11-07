using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Perioden afsluiten: tot en met een maand ligt alles vast. Boeken, aanpassen, herboeken, de bank en de verkoop
/// weigeren een datum in de afgesloten periode; heropenen kan alleen met een reden, en alleen het kantoor mag het.
/// Het verzonnen bankafschrift van maart 2026 (zie <see cref="BankTests"/>).
/// </summary>
public sealed class PeriodeTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static Journaalpost Memoriaal(string boekstuk, DateOnly datum, decimal bedrag) =>
        new("MEM", boekstuk, datum, "Huur", [new Journaalregel("4100", bedrag), new Journaalregel("1800", -bedrag)]);

    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new Journaalregel("1100", 5000m), new Journaalregel("0500", -5000m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0002", new(2026, 3, 1), 800m))).IsGelukt);
        return (wie, id);
    }

    [Fact]
    public async Task Na_afsluiten_is_in_de_periode_niets_meer_te_boeken_en_daarna_wel()
    {
        var (wie, id) = await Administratie();
        var perioden = app.Dienst<PeriodeDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.True((await perioden.SluitAf(wie, id, new DateOnly(2026, 3, 10))).IsGelukt);

        var stand = await perioden.Stand(wie, id, 2026);
        Assert.Equal(new DateOnly(2026, 3, 31), stand.AfgeslotenTot);
        Assert.Equal([true, true, true, false], stand.Maanden.Take(4).Select(m => m.Afgesloten));
        Assert.Equal(1, stand.Maanden[2].Boekingen);

        var maart = await grootboek.Boek(wie, id, Memoriaal("2026-0003", new(2026, 3, 31), 100m));
        Assert.False(maart.IsGelukt);
        Assert.Contains(maart.Fouten, f => f.Contains("tot en met 31-03-2026 is afgesloten", StringComparison.Ordinal));
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0003", new(2026, 4, 1), 100m))).IsGelukt);

        // Aanpassen: niet in de afgesloten periode, en ook niet ernaartoe of eruit.
        Assert.False((await grootboek.PasAan(wie, id, "MEM", "2026-0002", Memoriaal("2026-0002", new(2026, 3, 1), 900m))).IsGelukt);
        Assert.False((await grootboek.PasAan(wie, id, "MEM", "2026-0002", Memoriaal("2026-0002", new(2026, 4, 1), 800m))).IsGelukt);
        Assert.False((await grootboek.PasAan(wie, id, "MEM", "2026-0003", Memoriaal("2026-0003", new(2026, 3, 15), 100m))).IsGelukt);
        Assert.True((await grootboek.PasAan(wie, id, "MEM", "2026-0003", Memoriaal("2026-0003", new(2026, 4, 2), 150m))).IsGelukt);

        // Herboeken van een regel in maart kan ook niet.
        var herboek = await grootboek.Herboek(wie, id, [new Herboekregel("MEM", "2026-0002", 1)], "4900");
        Assert.False(herboek.IsGelukt);
        Assert.Contains("MEM 2026-0002", herboek.Fouten[0], StringComparison.Ordinal);

        // Herhalen per maand vanuit een afgesloten maand weigert meteen.
        Assert.False((await grootboek.BoekPerMaand(wie, id, Memoriaal("2026-0010", new(2026, 2, 1), 50m), 3)).IsGelukt);
    }

    [Fact]
    public async Task Heropenen_kan_alleen_met_een_reden_en_staat_in_het_log()
    {
        var (wie, id) = await Administratie();
        var perioden = app.Dienst<PeriodeDienst>();
        Assert.True((await perioden.SluitAf(wie, id, new DateOnly(2026, 3, 31))).IsGelukt);

        Assert.False((await perioden.Heropen(wie, id, new DateOnly(2026, 3, 1), " ")).IsGelukt);
        Assert.False((await perioden.Heropen(wie, id, new DateOnly(2026, 5, 1), "Correctie")).IsGelukt);
        Assert.False((await perioden.SluitAf(wie, id, new DateOnly(2026, 2, 28))).IsGelukt);

        Assert.True((await perioden.Heropen(wie, id, new DateOnly(2026, 3, 20), "Factuur vergeten")).IsGelukt);
        Assert.Equal(new DateOnly(2026, 2, 28), (await perioden.Stand(wie, id, 2026)).AfgeslotenTot);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, Memoriaal("2026-0003", new(2026, 3, 31), 100m))).IsGelukt);
        Assert.False((await app.Dienst<GrootboekDienst>().Boek(wie, id, Memoriaal("2026-0004", new(2026, 2, 28), 100m))).IsGelukt);

        Assert.True((await perioden.Heropen(wie, id, null, "Jaarrekening opnieuw")).IsGelukt);
        Assert.Null((await perioden.Stand(wie, id, 2026)).AfgeslotenTot);

        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        var log = await db.AuditRegels.AsNoTracking().Where(a => a.AdministratieId == id && (a.Actie == AuditActies.PeriodeAfgesloten || a.Actie == AuditActies.PeriodeHeropend))
            .OrderBy(a => a.Id).Select(a => new { a.Actie, a.Onderwerp, a.Details }).ToListAsync();
        Assert.Equal([AuditActies.PeriodeAfgesloten, AuditActies.PeriodeHeropend, AuditActies.PeriodeHeropend], log.Select(l => l.Actie));
        Assert.Equal("vanaf 01-03-2026", log[1].Onderwerp);
        Assert.Contains("reden: Factuur vergeten", log[1].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_maand_in_de_toekomst_sluit_je_niet_af()
    {
        var (wie, id) = await Administratie();
        var volgendeMaand = DateOnly.FromDateTime(DateTime.Today).AddMonths(1);

        Assert.False((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, volgendeMaand)).IsGelukt);
    }

    [Fact]
    public async Task Bank_en_verkoop_boeken_niet_in_een_afgesloten_periode()
    {
        var (wie, id) = await Administratie();
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        var mutatie = (await bank.Lijst(wie, id))[0];
        var inrichting = app.Dienst<InrichtingDienst>();
        var klant = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Klant B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", null, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), null);
        var code = (await inrichting.Relaties(wie, id)).Single(r => r.Id == klant.Waarde).Code;
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = (await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(code, new(2026, 3, 15), new(2026, 3, 29), "Advies", [new Verkoopregel("Advies", 1m, 100m, "8000", "V21")]))).Waarde;
        var perioden = app.Dienst<PeriodeDienst>();

        var signalen = await perioden.Openstaand(wie, id, new DateOnly(2026, 3, 31));
        Assert.Contains(signalen, s => s.StartsWith("3 bankmutaties", StringComparison.Ordinal));
        Assert.Contains(signalen, s => s.StartsWith("1 conceptfactuur", StringComparison.Ordinal));
        Assert.True((await perioden.SluitAf(wie, id, new DateOnly(2026, 3, 31))).IsGelukt);

        var opRekening = await bank.OpRekening(wie, id, mutatie.Id, "9000", null, null);
        Assert.False(opRekening.IsGelukt);
        Assert.Contains("afgesloten", opRekening.Fouten[0], StringComparison.Ordinal);
        Assert.Equal(3, (await bank.Lijst(wie, id)).Count);
        Assert.Contains((await verkoop.MaakDefinitief(wie, id, concept)).Fouten, f => f.Contains("is afgesloten", StringComparison.Ordinal));

        Assert.True((await perioden.Heropen(wie, id, new DateOnly(2026, 3, 1), "Bank nog verwerken")).IsGelukt);
        Assert.True((await bank.OpRekening(wie, id, mutatie.Id, "9000", null, null)).IsGelukt);
    }

    [Fact]
    public async Task Het_vangnet_bij_de_keten_weigert_ook_zonder_controle_vooraf()
    {
        var (wie, id) = await Administratie();
        Assert.True((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new DateOnly(2026, 3, 31))).IsGelukt);

        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden);
        var grootboek = app.Dienst<GrootboekDienst>();
        await Assert.ThrowsAsync<PeriodeAfgeslotenException>(() => grootboek.VoegToeAanKeten(db, id, Memoriaal("2026-0099", new(2026, 2, 1), 1m), wie));
        var maart = await db.Boekingen.AsNoTracking().Include(b => b.Regels).SingleAsync(b => b.Boekstuk == "2026-0002");
        await Assert.ThrowsAsync<PeriodeAfgeslotenException>(() => grootboek.NieuweVersie(db, id, maart, Memoriaal("2026-0002", new(2026, 4, 1), 800m), wie));
    }

    [Fact]
    public async Task Alleen_het_kantoor_sluit_af_en_heropent()
    {
        var (beheerder, id) = await Administratie();
        var ondernemer = await app.Gebruiker("Ondernemer Periode");
        var inzien = await app.Gebruiker("Inzien Periode");
        var vreemde = await app.Gebruiker("Vreemde Periode");
        var beheer = app.Dienst<AdministratieDienst>();
        await beheer.ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, beheerder);
        await beheer.ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, beheerder);
        var perioden = app.Dienst<PeriodeDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => perioden.SluitAf(ondernemer, id, new DateOnly(2026, 3, 31)));
        await Assert.ThrowsAsync<GeenToegangException>(() => perioden.SluitAf(inzien, id, new DateOnly(2026, 3, 31)));
        await Assert.ThrowsAsync<GeenToegangException>(() => perioden.Stand(vreemde, id, 2026));
        Assert.Null((await perioden.Stand(inzien, id, 2026)).AfgeslotenTot);

        Assert.True((await perioden.SluitAf(beheerder, id, new DateOnly(2026, 3, 31))).IsGelukt);
        await Assert.ThrowsAsync<GeenToegangException>(() => perioden.Heropen(ondernemer, id, null, "Mag ik niet"));

        // De ondernemer boekt ook niet via de bank in de afgesloten periode.
        Assert.True((await app.Dienst<BankDienst>().Lees(ondernemer, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        var mutatie = (await app.Dienst<BankDienst>().Lijst(ondernemer, id))[0];
        Assert.False((await app.Dienst<BankDienst>().OpRekening(ondernemer, id, mutatie.Id, "9000", null, null)).IsGelukt);
    }
}
