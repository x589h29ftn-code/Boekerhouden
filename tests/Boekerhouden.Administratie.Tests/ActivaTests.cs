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

    private static Desinvesteringsinvoer Verkoop(DateOnly datum, decimal opbrengst, decimal deel = 100m, string? opbrengstrekening = "1800", string? resultaat = "4900", bool eerst = true) =>
        new(datum, Desinvesteringssoort.Verkoop, deel, opbrengst, opbrengstrekening, resultaat, eerst);

    [Fact]
    public async Task Verkopen_geeft_een_boekresultaat_en_de_activastaat_sluit_op_het_grootboek()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        // De verkoop van de laptop staat op 1800 (bijv. via de verkoopfactuur): 1.000 zonder btw.
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0003", new(2026, 6, 15), "Verkoop laptop",
            [new Journaalregel("1100", 1000m), new Journaalregel("1800", -1000m)]))).IsGelukt);

        // Zonder eerst afschrijven kan het niet; met die keuze boekt het pakket maart tot en met juni eerst (per maand).
        Assert.Contains((await dienst.Desinvesteer(wie, id, laptop, Verkoop(new(2026, 6, 15), 1000m, eerst: false))).Fouten,
            f => f.StartsWith("Boek eerst de afschrijvingen tot en met juni 2026", StringComparison.Ordinal));
        Assert.Contains((await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 6, 15), 1000m, opbrengstrekening: null))).Fouten, f => f.Contains("opbrengst", StringComparison.Ordinal));
        Assert.Contains((await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 6, 15), 1000m, resultaat: "0200"))).Fouten, f => f.Contains("boekresultaat", StringComparison.Ordinal));

        // Het voorstel: 4 maanden eerst, boekwaarde 1.066,67 (1.200 - 133,33), verlies 66,67, en de boeking die erbij hoort.
        var voorstel = await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 6, 15), 1000m));
        Assert.Empty(voorstel.Fouten);
        Assert.Equal(4, voorstel.EerstAfschrijven.Count);
        Assert.Equal((1200m, 133.33m, 1066.67m, -66.67m), (voorstel.Bedragen!.Aanschaf, voorstel.Bedragen.Afschrijving, voorstel.Bedragen.Boekwaarde, voorstel.Bedragen.Resultaat));
        Assert.Equal([("0210", 133.33m), ("0200", -1200m), ("1800", 1000m), ("4900", 66.67m)], voorstel.Boeking.Select(r => (r.Rekening, r.Bedrag)));
        Assert.Equal("Verkoop 1 Laptop", voorstel.Omschrijving);

        var verkocht = await dienst.Desinvesteer(wie, id, laptop, Verkoop(new(2026, 6, 15), 1000m));
        Assert.True(verkocht.IsGelukt, string.Join("; ", verkocht.Fouten));
        Assert.Equal(66.67m, await Saldo(wie, id, "4900"));
        Assert.Equal(0m, await Saldo(wie, id, "0200"));
        Assert.Equal(0m, await Saldo(wie, id, "0210"));
        Assert.Equal(0m, await Saldo(wie, id, "1800") + 24000m + 1200m);
        var regel = (await dienst.Lijst(wie, id)).Single(a => a.Id == laptop);
        Assert.Equal((0m, new DateOnly(2026, 6, 15)), (regel.Boekwaarde, regel.BuitenGebruikOp));
        Assert.Contains("helemaal gedesinvesteerd", (await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 7, 1), 0m))).Fouten[0], StringComparison.Ordinal);

        // Daarna wordt hij niet meer afgeschreven; de auto wel.
        Assert.All(await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 7, 31), Afschrijvingsfrequentie.Maand), v => Assert.Equal(1, v.Activa));

        var staat = await dienst.Activastaat(wie, id, 2026);
        Assert.All(staat, g => Assert.True(g.Sluit, $"{g.Activarekening}: {g.GrootboekAanschaf}/{g.Totaal.AanschafEind}, {g.GrootboekAfschrijving}/{g.Totaal.AfschrijvingEind}"));
        var inventaris = staat[0].Totaal;
        Assert.Equal((1200m, 133.33m, 1200m, 133.33m, 0m), (inventaris.Investering, inventaris.Afschrijving, inventaris.DesinvesteringAanschaf, inventaris.DesinvesteringAfschrijving, inventaris.BoekwaardeEind));

        // Terugdraaien: tegenboeking, weer in gebruik; de eerst geboekte afschrijvingen blijven.
        var detail = (await dienst.Open(wie, id, laptop))!;
        var desinvestering = Assert.Single(detail.Desinvesteringen);
        Assert.True(desinvestering.Laatste);
        Assert.True((await dienst.TerugdraaienDesinvestering(wie, id, desinvestering.Id)).IsGelukt);
        Assert.False((await dienst.TerugdraaienDesinvestering(wie, id, desinvestering.Id)).IsGelukt);
        Assert.Equal(0m, await Saldo(wie, id, "4900"));
        Assert.Equal(1200m, await Saldo(wie, id, "0200"));
        Assert.Equal(-133.33m, await Saldo(wie, id, "0210"));
        detail = (await dienst.Open(wie, id, laptop))!;
        Assert.Null(detail.BuitenGebruikOp);
        Assert.NotNull(detail.Desinvesteringen.Single().Tegenboeking);
        Assert.All(await dienst.Activastaat(wie, id, 2026), g => Assert.True(g.Sluit));
    }

    [Fact]
    public async Task Een_deel_verkopen_en_de_rest_ruilen_we_later_in()
    {
        var (wie, id) = await Administratie();
        var (_, auto) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        // Bestelauto 24.000, rest 4.000, 60 maanden vanaf april: 333,33 per maand. 25% verkocht op 10 juni.
        var deel = await dienst.Desinvesteer(wie, id, auto, Verkoop(new(2026, 6, 10), 4000m, deel: 25m));
        Assert.True(deel.IsGelukt, string.Join("; ", deel.Fouten));

        var detail = (await dienst.Open(wie, id, auto))!;
        var afgeschreven = Afschrijvingsplan.Verdeel(20000m, 60).Take(3).Sum();
        Assert.Equal((18000m, 3000m), (detail.Aanschafwaarde, detail.Restwaarde));
        Assert.Equal(afgeschreven - Math.Round(afgeschreven * 0.25m, 2), detail.Afgeschreven);
        Assert.Null(detail.BuitenGebruikOp);
        // Het plan gaat verder met de rest en eindigt op de nieuwe restwaarde.
        Assert.Equal(3000m, detail.Plan[^1].Boekwaarde);
        Assert.InRange(detail.PerMaand, Math.Round((18000m - 3000m - detail.Afgeschreven) / 57m, 2) - 0.01m, Math.Round((18000m - 3000m - detail.Afgeschreven) / 57m, 2) + 0.01m);
        Assert.True((await dienst.Lijst(wie, id)).Single(a => a.Id == auto).Gedesinvesteerd);
        Assert.Equal(18000m, await Saldo(wie, id, "0300"));
        Assert.Equal(-detail.Afgeschreven, await Saldo(wie, id, "0310"));

        // Aanpassen van de restwaarde mag nog; aanschaf niet meer.
        Assert.False((await dienst.Bewaar(wie, id, auto, detail.Invoer with { Aanschafwaarde = 20000m })).IsGelukt);

        // Afschrijven vanaf de kaart (alleen dit actief), dan de rest inruilen: de inruilwaarde staat op 1800.
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 8, 31), Afschrijvingsfrequentie.Maand, auto)).IsGelukt);
        Assert.Empty(await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 8, 31), Afschrijvingsfrequentie.Maand, auto));
        Assert.NotEmpty(await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 8, 31), Afschrijvingsfrequentie.Maand));
        var inruil = new Desinvesteringsinvoer(new(2026, 9, 1), Desinvesteringssoort.Inruil, 100m, 15000m, "1800", "4900");
        var voorstel = await dienst.VoorstelDesinvestering(wie, id, auto, inruil);
        Assert.Equal("Inruil 2 Bestelauto", voorstel.Omschrijving);
        Assert.Single(voorstel.EerstAfschrijven);
        Assert.True((await dienst.Desinvesteer(wie, id, auto, inruil)).IsGelukt);
        Assert.Equal(0m, await Saldo(wie, id, "0300"));
        Assert.Equal(0m, await Saldo(wie, id, "0310"));

        // Alleen de laatste desinvestering is terug te draaien.
        detail = (await dienst.Open(wie, id, auto))!;
        Assert.Equal([false, true], detail.Desinvesteringen.Select(d => d.Laatste));
        Assert.False((await dienst.TerugdraaienDesinvestering(wie, id, detail.Desinvesteringen[0].Id)).IsGelukt);
        Assert.All(await dienst.Activastaat(wie, id, 2026), g => Assert.True(g.Sluit, $"{g.Activarekening}: {g.GrootboekAanschaf}/{g.Totaal.AanschafEind}, {g.GrootboekAfschrijving}/{g.Totaal.AfschrijvingEind}"));
    }

    [Fact]
    public async Task Sloop_heeft_geen_opbrengst_en_een_deel_boven_100_kan_niet()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();

        Assert.Contains((await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 4, 1), 0m, deel: 120m))).Fouten, f => f.Contains("100%", StringComparison.Ordinal));
        Assert.Contains((await dienst.VoorstelDesinvestering(wie, id, laptop, Verkoop(new(2026, 2, 1), 0m))).Fouten, f => f.Contains("vóór de aanschaf", StringComparison.Ordinal));
        var sloop = new Desinvesteringsinvoer(new(2026, 4, 20), Desinvesteringssoort.Sloop, 100m, 500m, "1800", "4900");
        var voorstel = await dienst.VoorstelDesinvestering(wie, id, laptop, sloop);
        Assert.Equal(0m, voorstel.Bedragen!.Opbrengst);
        Assert.DoesNotContain(voorstel.Boeking, r => r.Rekening == "1800");
        Assert.True((await dienst.Desinvesteer(wie, id, laptop, sloop)).IsGelukt);
        Assert.Equal(1200m - Afschrijvingsplan.Verdeel(1200m, 36).Take(2).Sum(), await Saldo(wie, id, "4900"));
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
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Desinvesteer(ondernemer, id, laptop, Verkoop(new(2026, 4, 1), 0m)));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.TerugdraaienDesinvestering(ondernemer, id, 1));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VoorstelDesinvestering(vreemde, id, laptop, Verkoop(new(2026, 4, 1), 0m)));
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

    [Fact]
    public async Task Direct_activeren_vanuit_de_boeking()
    {
        var (wie, id) = await Administratie();
        var dienst = app.Dienst<ActivaDienst>();

        Assert.Superset(new HashSet<string> { "0200", "0300" }, (HashSet<string>)await dienst.Activarekeningen(wie, id));
        Assert.DoesNotContain("1100", await dienst.Activarekeningen(wie, id));
        Assert.Equal(["MEM 2026-0001", "MEM 2026-0002"], (await dienst.TeActiveren(wie, id)).Order());

        var geactiveerd = await dienst.Activeer(wie, id, "MEM 2026-0001");
        Assert.True(geactiveerd.IsGelukt, string.Join("; ", geactiveerd.Fouten));
        var laptop = (await dienst.Open(wie, id, Assert.Single(geactiveerd.Waarde!)))!;
        Assert.Equal(("Laptop", 1200m, "0210", "4800", 60, "MEM 2026-0001"),
            (laptop.Invoer.Omschrijving, laptop.Invoer.Aanschafwaarde, laptop.Invoer.Afschrijvingsrekening, laptop.Invoer.Kostenrekening, laptop.Invoer.Looptijd, laptop.Invoer.Bron));
        Assert.Equal((laptop.Id, 1), (await dienst.UitBoeking(wie, id, "MEM 2026-0001")).Select(a => (a.Id, a.Nummer)).Single());
        Assert.Equal(["MEM 2026-0002"], await dienst.TeActiveren(wie, id));

        // Een tweede keer, of een boeking zonder activarekening, kan niet.
        Assert.False((await dienst.Activeer(wie, id, "MEM 2026-0001")).IsGelukt);
        Assert.False((await dienst.Activeer(wie, id, "MEM 2099-0001")).IsGelukt);

        // Een volgende aanschaf op dezelfde rekening krijgt de looptijd van het vorige actief.
        Assert.True((await dienst.Bewaar(wie, id, laptop.Id, laptop.Invoer with { Looptijd = 36 })).IsGelukt);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0003", new(2026, 5, 1), "Bureau",
            [new Journaalregel("0200", 800m, "Bureau"), new Journaalregel("1800", -800m)]))).IsGelukt);
        var bureau = (await dienst.Activeer(wie, id, "MEM 2026-0003")).Waarde!.Single();
        Assert.Equal(36, (await dienst.Open(wie, id, bureau))!.Invoer.Looptijd);

        // Alleen het kantoor activeert.
        var ondernemer = await app.Gebruiker("Ondernemer Activeren");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Activeer(ondernemer, id, "MEM 2026-0002"));
    }

    [Fact]
    public async Task Activastaat_naar_Excel_en_PDF()
    {
        var (wie, id) = await Administratie();
        await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 6, 30), Afschrijvingsfrequentie.Maand)).IsGelukt);
        var staat = await dienst.Activastaat(wie, id, 2026);

        using var excel = new ClosedXML.Excel.XLWorkbook(new MemoryStream(Export.OverzichtExport.ActivastaatExcel(staat, 2026, "Bakkerij Voorbeeld")));
        var blad = excel.Worksheet("Activastaat");
        Assert.Equal("Activastaat 2026", blad.Cell(1, 1).GetString());
        var cellen = blad.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("0200 Inventaris", cellen);
        Assert.Contains(cellen, c => c.Trim() == "1 Laptop");
        Assert.Contains("Totaal", cellen);
        var totaal = blad.RowsUsed().Last();
        Assert.Equal(25200m, totaal.Cell(3).GetValue<decimal>());

        var html = Export.OverzichtExport.ActivastaatHtml(staat, 2026, "Bakkerij Voorbeeld");
        Assert.Contains("Activastaat 2026", html, StringComparison.Ordinal);
        Assert.Contains("Bestelauto", html, StringComparison.Ordinal);
        Assert.Contains("Boekwaarde<br>31-12", html, StringComparison.Ordinal);
        Assert.Contains(Domain.Bedragen.Getal(25200m), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aansluiting_van_de_activa_op_het_grootboek()
    {
        var (wie, id) = await Administratie();
        var (_, auto) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 6, 30), Afschrijvingsfrequentie.Maand)).IsGelukt);
        Assert.True((await dienst.Desinvesteer(wie, id, auto, Verkoop(new(2026, 7, 10), 5000m, deel: 25m))).IsGelukt);
        var aansluiting = app.Dienst<AansluitingDienst>();

        var activa = (await aansluiting.Aansluitingen(wie, id)).Where(a => a.Soort == Aansluitingsoort.Activa).ToList();
        Assert.Equal(["0200", "0300", "0210", "0310"], activa.Select(a => a.Rekening));
        Assert.All(activa, a => Assert.True(a.Sluit, $"{a.Rekening}: {a.Grootboek} / {a.Bron}"));
        Assert.Equal(18000m, activa.Single(a => a.Rekening == "0300").Bron);
        Assert.True(activa.Single(a => a.Rekening == "0310").Bron < 0);

        // Een aanschaf die nog geen actief is, en een boeking buiten de activa om op de afschrijvingsrekening, sluiten niet.
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0090", new(2026, 8, 1), "Printer",
            [new Journaalregel("0200", 300m, "Printer"), new Journaalregel("1800", -300m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0091", new(2026, 8, 1), "Correctie",
            [new Journaalregel("4810", 50m), new Journaalregel("0310", -50m)]))).IsGelukt);
        activa = [.. (await aansluiting.Aansluitingen(wie, id)).Where(a => a.Soort == Aansluitingsoort.Activa)];
        var inventaris = activa.Single(a => a.Rekening == "0200");
        Assert.Equal((false, 300m), (inventaris.Sluit, inventaris.Verschil));
        Assert.Contains("1 aanschaf nog niet geactiveerd", inventaris.Toelichting, StringComparison.Ordinal);
        Assert.Equal((false, -50m), (activa.Single(a => a.Rekening == "0310").Sluit, activa.Single(a => a.Rekening == "0310").Verschil));
        Assert.True(activa.Single(a => a.Rekening == "0300").Sluit);

        // Geactiveerd: de inventaris sluit weer.
        Assert.True((await dienst.Activeer(wie, id, "MEM 2026-0090")).IsGelukt);
        Assert.True((await aansluiting.Aansluitingen(wie, id)).Single(a => a.Rekening == "0200").Sluit);
    }

    [Fact]
    public async Task Vaste_activa_sluiten_op_de_kolommenbalans_op_elke_peildatum()
    {
        var (wie, id) = await Administratie();
        var (laptop, auto) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 9, 30), Afschrijvingsfrequentie.Maand)).IsGelukt);
        Assert.True((await dienst.Desinvesteer(wie, id, laptop, Verkoop(new(2026, 7, 20), 800m))).IsGelukt);
        Assert.True((await dienst.Desinvesteer(wie, id, auto, Verkoop(new(2026, 9, 20), 4000m, deel: 25m))).IsGelukt);

        // Op elke peildatum: wat de activa zeggen is gelijk aan het saldo in de kolommenbalans van die periode.
        foreach (var tot in new[] { new DateOnly(2026, 3, 31), new DateOnly(2026, 6, 30), new DateOnly(2026, 9, 30), new DateOnly(2026, 12, 31) })
        {
            var stand = await dienst.Stand(wie, id, tot);
            var koba = await grootboek.Kolommenbalans(wie, id, new DateOnly(2026, 1, 1), tot);
            foreach (var rekening in new[] { "0200", "0210", "0300", "0310" })
            {
                Assert.Equal(koba.Regels.SingleOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m, stand.GetValueOrDefault(rekening));
            }
        }

        // In maart staat de bestelauto (april) er nog niet; op 30 juni wel, nog zonder de verkoop van september.
        Assert.False((await dienst.Stand(wie, id, new DateOnly(2026, 3, 31))).ContainsKey("0300"));
        Assert.Equal(24000m, (await dienst.Stand(wie, id, new DateOnly(2026, 6, 30)))["0300"]);
        Assert.Equal(18000m, (await dienst.Stand(wie, id, new DateOnly(2026, 9, 30)))["0300"]);
    }

    [Fact]
    public async Task Afschrijving_wijzigen_per_datum_werkt_alleen_vooruit_en_staat_in_het_log()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var dienst = app.Dienst<ActivaDienst>();
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 5, 31), Afschrijvingsfrequentie.Maand, laptop)).IsGelukt);

        // Vóór of in een geboekte maand kan niet; daarna wel.
        var tevroeg = await dienst.Herzie(wie, id, laptop, new DateOnly(2026, 5, 1), 10m);
        Assert.Contains(tevroeg.Fouten, f => f.Contains("al afgeschreven", StringComparison.Ordinal));
        Assert.False((await dienst.Herzie(wie, id, laptop, new DateOnly(2026, 6, 1), 0m)).IsGelukt);
        Assert.True((await dienst.Herzie(wie, id, laptop, new DateOnly(2026, 6, 20), 10m)).IsGelukt);

        var detail = (await dienst.Open(wie, id, laptop))!;
        Assert.Equal(new DateOnly(2026, 6, 1), Assert.Single(detail.Herzieningen!).Ingang);
        Assert.Equal(100m, detail.Plan.Where(m => m.Geboekt).Sum(m => m.Bedrag));
        Assert.All(detail.Plan.Where(m => !m.Geboekt), m => Assert.Equal(10m, m.Bedrag));
        Assert.Equal(1200m, detail.Plan.Sum(m => m.Bedrag));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ActivumHerzien && r.AdministratieId == id && r.Details == "vanaf 06-2026 10% per jaar");

        // Opnieuw in dezelfde maand vervangt; verwijderen zolang er nog niets vanaf die maand is geboekt.
        Assert.True((await dienst.Herzie(wie, id, laptop, new DateOnly(2026, 6, 1), 12.5m)).IsGelukt);
        Assert.Equal(12.5m, Assert.Single((await dienst.Open(wie, id, laptop))!.Herzieningen!).PercentagePerJaar);
        Assert.True((await dienst.VerwijderHerziening(wie, id, laptop, new DateOnly(2026, 6, 1))).IsGelukt);
        Assert.Empty((await dienst.Open(wie, id, laptop))!.Herzieningen!);

        Assert.True((await dienst.Herzie(wie, id, laptop, new DateOnly(2026, 6, 1), 10m)).IsGelukt);
        Assert.True((await dienst.Afschrijven(wie, id, new DateOnly(2026, 6, 30), Afschrijvingsfrequentie.Maand, laptop)).IsGelukt);
        Assert.Equal(10m, (await dienst.Open(wie, id, laptop))!.Plan.Single(m => m.Maand == new DateOnly(2026, 6, 1)).Bedrag);
        Assert.False((await dienst.VerwijderHerziening(wie, id, laptop, new DateOnly(2026, 6, 1))).IsGelukt);
    }

    [Fact]
    public async Task Afschrijving_wijzigen_mag_alleen_het_kantoor()
    {
        var (wie, id) = await Administratie();
        var (laptop, _) = await Activa(wie, id);
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var buiten = await app.Gebruiker("Bert Buiten");
        var dienst = app.Dienst<ActivaDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Herzie(ondernemer, id, laptop, new DateOnly(2027, 1, 1), 10m));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Herzie(buiten, id, laptop, new DateOnly(2027, 1, 1), 10m));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VerwijderHerziening(ondernemer, id, laptop, new DateOnly(2027, 1, 1)));
        Assert.Empty((await dienst.Open(wie, id, laptop))!.Herzieningen!);
    }
}
