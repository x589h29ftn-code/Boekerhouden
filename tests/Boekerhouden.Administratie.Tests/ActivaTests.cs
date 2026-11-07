using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Vaste activa in de administratie: een laptop van 1.200 (maart 2026, 36 maanden) en een bestelauto van 24.000
/// (april 2026, 60 maanden, rest 4.000), gekocht via het memoriaal op de activarekeningen van het standaardschema.
/// </summary>
public sealed class ActivaTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 3, 10), "Aanschaf",
            [new Journaalregel("0200", 1200m, "Laptop"), new Journaalregel("1800", -1200m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0002", new(2026, 4, 2), "Bestelauto",
            [new Journaalregel("0300", 24000m), new Journaalregel("1800", -24000m)]))).IsGelukt);
        return (wie, id);
    }

    /// <summary>Beide aanschaffingen als actief, uit de voorstellen; de bestelauto met restwaarde.</summary>
    private async Task<(Guid Laptop, Guid Auto)> Activa(Uitvoerder wie, Guid id)
    {
        var dienst = app.Dienst<ActivaDienst>();
        var kandidaten = await dienst.Kandidaten(wie, id);
        var laptop = kandidaten.Single(k => k.Rekening == "0200").Voorstel with { Looptijd = 36 };
        var auto = kandidaten.Single(k => k.Rekening == "0300").Voorstel with { Restwaarde = 4000m };
        var l = await dienst.Bewaar(wie, id, null, laptop);
        var a = await dienst.Bewaar(wie, id, null, auto);
        Assert.True(l.IsGelukt && a.IsGelukt, string.Join("; ", l.Fouten.Concat(a.Fouten)));
        return (l.Waarde, a.Waarde);
    }

    private async Task<decimal> Saldo(Uitvoerder wie, Guid id, string rekening) =>
        (await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026)).Regels.SingleOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;

    [Fact]
    public async Task Aanschaf_op_een_activarekening_wordt_een_voorstel()
    {
        var (wie, id) = await Administratie();

        var kandidaten = await app.Dienst<ActivaDienst>().Kandidaten(wie, id);

        Assert.Equal(["MEM 2026-0002", "MEM 2026-0001"], kandidaten.Select(k => k.Boekstuk));
        var laptop = kandidaten.Single(k => k.Rekening == "0200").Voorstel;
        Assert.Equal(("Laptop", new DateOnly(2026, 3, 10), 1200m, new DateOnly(2026, 3, 1), 60, "0210", "4800", "MEM 2026-0001"),
            (laptop.Omschrijving, laptop.Aanschafdatum, laptop.Aanschafwaarde, laptop.BeginAfschrijving, laptop.Looptijd, laptop.Afschrijvingsrekening, laptop.Kostenrekening, laptop.Bron));
        Assert.Equal(("0310", "4810"), (kandidaten.Single(k => k.Rekening == "0300").Voorstel.Afschrijvingsrekening, kandidaten.Single(k => k.Rekening == "0300").Voorstel.Kostenrekening));

        await Activa(wie, id);
        Assert.Empty(await app.Dienst<ActivaDienst>().Kandidaten(wie, id));
        var lijst = await app.Dienst<ActivaDienst>().Lijst(wie, id);
        Assert.Equal([1, 2], lijst.Select(a => a.Nummer));
        Assert.Equal(33.33m, lijst[0].PerMaand);
        Assert.Equal(333.33m, lijst[1].PerMaand);
    }

    [Fact]
    public async Task Afschrijven_per_maand_en_per_kwartaal()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();

        // Per maand tot en met mei: maart (laptop), april en mei (beide).
        var voorstel = await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand);
        Assert.Equal([(new DateOnly(2026, 3, 31), 1), (new DateOnly(2026, 4, 30), 2), (new DateOnly(2026, 5, 31), 2)], voorstel.Select(v => (v.Datum, v.Activa)));
        Assert.Equal("Afschrijvingen maart 2026", voorstel[0].Omschrijving);
        var geboekt = await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand);
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        Assert.Equal(3, geboekt.Waarde);
        Assert.False((await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand)).IsGelukt);

        // Daarna per kwartaal: juni valt in het tweede kwartaal, juli tot en met september in het derde.
        var kwartaal = await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 9, 30), Afschrijvingsfrequentie.Kwartaal);
        Assert.Equal(["Afschrijvingen 2e kwartaal 2026", "Afschrijvingen 3e kwartaal 2026"], kwartaal.Select(v => v.Omschrijving));
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 9, 30), Afschrijvingsfrequentie.Kwartaal)).IsGelukt);

        // Laptop 7 maanden (1.200 / 36), auto 6 maanden (20.000 / 60).
        var laptopAf = Afschrijvingsplan.Verdeel(1200m, 36).Take(7).Sum();
        var autoAf = Afschrijvingsplan.Verdeel(20000m, 60).Take(6).Sum();
        Assert.Equal(laptopAf, await Saldo(wie, id, "4800"));
        Assert.Equal(-laptopAf, await Saldo(wie, id, "0210"));
        Assert.Equal(autoAf, await Saldo(wie, id, "4810"));
        var detail = (await dienst.Open(wie, id, laptop))!;
        Assert.Equal(7, detail.Plan.Count(m => m.Geboekt));
        Assert.True(detail.Vast);
        Assert.StartsWith("MEM 2026-", detail.Boekstukken[new DateOnly(2026, 3, 1)], StringComparison.Ordinal);

        // Een afschrijvingsboeking pas je niet in het memoriaal aan en herboek je niet.
        var stuk = detail.Boekstukken[new DateOnly(2026, 3, 1)].Split(' ');
        var grootboek = app.Dienst<GrootboekDienst>();
        var boeking = (await grootboek.Boeking(wie, id, stuk[0], stuk[1]))!;
        Assert.Contains("vaste activa", boeking.Vast, StringComparison.Ordinal);
        Assert.False((await grootboek.PasAan(wie, id, stuk[0], stuk[1], boeking.Boeking.Post)).IsGelukt);
        Assert.False((await grootboek.Herboek(wie, id, [new Herboekregel(stuk[0], stuk[1], 1)], "4900")).IsGelukt);
    }

    [Fact]
    public async Task Na_de_eerste_afschrijving_liggen_aanschaf_en_rekeningen_vast_maar_de_looptijd_niet()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 4, 30), Afschrijvingsfrequentie.Maand)).IsGelukt);
        var invoer = (await dienst.Open(wie, id, laptop))!.Invoer;

        Assert.False((await dienst.Bewaar(wie, id, laptop, invoer with { Aanschafwaarde = 1500m })).IsGelukt);
        Assert.False((await dienst.Bewaar(wie, id, laptop, invoer with { Kostenrekening = "4900" })).IsGelukt);
        Assert.False((await dienst.Verwijder(wie, id, laptop)).IsGelukt);

        // Toch maar 24 maanden: wat nog af moet (1.200 - 33,33 - 33,34) gaat over de resterende 22 maanden.
        Assert.True((await dienst.Bewaar(wie, id, laptop, invoer with { Looptijd = 24, Omschrijving = "Laptop directie" })).IsGelukt);
        var plan = (await dienst.Open(wie, id, laptop))!.Plan;
        Assert.Equal(24, plan.Count);
        Assert.Equal(Afschrijvingsplan.Verdeel(1200m, 36).Take(2), plan.Take(2).Select(m => m.Bedrag));
        Assert.Equal(1200m, plan.Sum(m => m.Bedrag));
        Assert.Equal(0m, plan[^1].Boekwaarde);

        // Zonder boekingen mag alles en kan hij weg.
        var los = await dienst.Bewaar(wie, id, null, invoer with { Omschrijving = "Printer", Aanschafwaarde = 300m, BeginAfschrijving = new DateOnly(2026, 12, 1), Bron = null });
        Assert.True((await dienst.Bewaar(wie, id, los.Waarde, invoer with { Omschrijving = "Printer", Aanschafwaarde = 350m, BeginAfschrijving = new DateOnly(2026, 12, 1) })).IsGelukt);
        Assert.True((await dienst.Verwijder(wie, id, los.Waarde)).IsGelukt);
    }

    [Fact]
    public async Task Verkopen_geeft_een_boekresultaat_en_de_activastaat_sluit_op_het_grootboek()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        // De verkoop van de laptop staat op 1800 (bijv. via de verkoopfactuur): 1.000 zonder btw.
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0003", new(2026, 6, 15), "Verkoop laptop",
            [new Journaalregel("1100", 1000m), new Journaalregel("1800", -1000m)]))).IsGelukt);

        var teVroeg = await dienst.BuitenGebruik(wie, id, laptop, new DateOnly(2026, 6, 15), 1000m, "1800", "4900");
        Assert.Contains(teVroeg.Fouten, f => f.StartsWith("Boek eerst de afschrijvingen tot en met juni 2026", StringComparison.Ordinal));
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 6, 30), Afschrijvingsfrequentie.Maand)).IsGelukt);
        Assert.Contains((await dienst.BuitenGebruik(wie, id, laptop, new DateOnly(2026, 6, 15), 1000m, null, "4900")).Fouten, f => f.Contains("opbrengst", StringComparison.Ordinal));
        Assert.Contains((await dienst.BuitenGebruik(wie, id, laptop, new DateOnly(2026, 6, 15), 1000m, "1800", "0200")).Fouten, f => f.Contains("boekresultaat", StringComparison.Ordinal));

        var verkocht = await dienst.BuitenGebruik(wie, id, laptop, new DateOnly(2026, 6, 15), 1000m, "1800", "4900");
        Assert.True(verkocht.IsGelukt, string.Join("; ", verkocht.Fouten));

        // Afgeschreven 4 maanden = 133,33; boekwaarde 1.066,67; opbrengst 1.000: verlies 66,67.
        Assert.Equal(66.67m, await Saldo(wie, id, "4900"));
        Assert.Equal(24000m, await Saldo(wie, id, "0300"));
        Assert.Equal(0m, await Saldo(wie, id, "0200"));
        Assert.Equal(0m, await Saldo(wie, id, "0210"));
        Assert.Equal(0m, (await dienst.Lijst(wie, id)).Single(a => a.Id == laptop).Boekwaarde);

        // Na buiten gebruik wordt hij niet meer afgeschreven; de auto wel.
        Assert.Equal(1, (await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 7, 31), Afschrijvingsfrequentie.Maand)).Single().Activa);

        var staat = await dienst.Activastaat(wie, id, 2026);
        Assert.Equal(["0200", "0300"], staat.Select(g => g.Activarekening));
        Assert.All(staat, g => Assert.True(g.Sluit, $"{g.Activarekening}: {g.GrootboekAanschaf}/{g.Totaal.AanschafEind}, {g.GrootboekAfschrijving}/{g.Totaal.AfschrijvingEind}"));
        var inventaris = staat[0].Totaal;
        Assert.Equal((1200m, 133.33m, 1200m, 133.33m, 0m), (inventaris.Investering, inventaris.Afschrijving, inventaris.DesinvesteringAanschaf, inventaris.DesinvesteringAfschrijving, inventaris.BoekwaardeEind));

        // Ongedaan maken: tegenboeking, weer in gebruik.
        Assert.True((await dienst.WeerInGebruik(wie, id, laptop)).IsGelukt);
        Assert.Equal(0m, await Saldo(wie, id, "4900"));
        Assert.Equal(1200m, await Saldo(wie, id, "0200"));
        Assert.Null((await dienst.Open(wie, id, laptop))!.BuitenGebruikOp);
    }

    [Fact]
    public async Task Afgesloten_periode_en_signaal_bij_afsluiten()
    {
        var (wie, id) = await Administratie();
        await Activa(wie, id);
        var perioden = app.Dienst<PeriodeDienst>();

        Assert.Contains(await perioden.Openstaand(wie, id, new DateOnly(2026, 4, 30)), s => s.StartsWith("Afschrijvingen tot en met 30-04-2026 nog niet geboekt (2 activa)", StringComparison.Ordinal));
        Assert.True((await perioden.SluitAf(wie, id, new DateOnly(2026, 4, 30))).IsGelukt);

        var dienst = app.Dienst<ActivaDienst>();
        var mislukt = await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand);
        Assert.False(mislukt.IsGelukt);
        Assert.Contains("is afgesloten", mislukt.Fouten[0], StringComparison.Ordinal);
        Assert.Equal(0m, await Saldo(wie, id, "4800"));
    }

    [Fact]
    public async Task Alleen_het_kantoor_beheert_activa_en_buitenstaanders_zien_niets()
    {
        var (beheerder, id) = await Administratie();
        var (laptop, _) = await Activa(beheerder, id);
        var ondernemer = await app.Gebruiker("Ondernemer Activa");
        var vreemde = await app.Gebruiker("Vreemde Activa");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, beheerder);
        var dienst = app.Dienst<ActivaDienst>();
        var invoer = (await dienst.Open(beheerder, id, laptop))!.Invoer;

        Assert.Equal(2, (await dienst.Lijst(ondernemer, id)).Count);
        Assert.NotEmpty(await dienst.Activastaat(ondernemer, id, 2026));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Bewaar(ondernemer, id, null, invoer));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Afschrijven(ondernemer, id, new DateOnly(2026, 4, 30), Afschrijvingsfrequentie.Maand));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.BuitenGebruik(ondernemer, id, laptop, new DateOnly(2026, 4, 1), 0m, null, "4900"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(ondernemer, id, laptop));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lijst(vreemde, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Kandidaten(vreemde, id));

        // Een actief van een andere administratie is niet te openen.
        var andere = await app.Administratie("Andere B.V.");
        Assert.Null(await dienst.Open(beheerder, andere, laptop));
        Assert.False((await dienst.Verwijder(beheerder, andere, laptop)).IsGelukt);

        await using var db = await app.Dienst<Toegang>().Open(beheerder, id, Recht.Lezen);
        Assert.Equal(2, await db.AuditRegels.CountAsync(a => a.AdministratieId == id && a.Actie == AuditActies.ActivumToegevoegd));
    }
}
