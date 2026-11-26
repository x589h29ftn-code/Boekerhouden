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

    /// <summary>Een verkoop waarvan de btw niet op hele euro's uitkomt: 1.002,25 omzet, 210,47 btw.</summary>
    private static Journaalpost VerkoopMetCenten(string boekstuk, DateOnly datum) =>
        new("MEM", boekstuk, datum, $"Verkoop {boekstuk}",
            [new("8000", -1_002.25m, BtwCode: "V21"), new("1510", -210.47m, BtwCode: "V21"), new("1300", 1_212.72m)]);

    private static async Task<decimal> Saldo(GrootboekDienst grootboek, Uitvoerder wie, Guid id, string rekening) =>
        (await grootboek.Kolommenbalans(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;

    [Fact]
    public async Task Vastleggen_boekt_de_afronding_naar_het_resultaat_en_intrekken_laat_hem_vervallen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Afronding B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var dienst = app.Dienst<BtwaangifteDienst>();
        Assert.True((await grootboek.Boek(wie, id, VerkoopMetCenten("2026-0001", new(2026, 2, 1)))).IsGelukt);

        Assert.True((await dienst.Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        // Ingediend: 1.002 omzet en 210 btw; de 0,47 is in het voordeel van de ondernemer.
        var afronding = Assert.Single(await grootboek.Posten(wie, id, "MEM"), p => p.Post.Omschrijving == "Afronding btw-aangifte 1e kwartaal 2026").Post;
        Assert.Equal(new DateOnly(2026, 3, 31), afronding.Datum);
        Assert.Equal([("1510", 0.47m), ("4910", -0.47m)], afronding.Regels.Select(r => (r.Rekeningnummer, r.Bedrag)));
        Assert.Equal(Rekeningfunctie.Afrondingsverschillen, (await grootboek.Schema(wie, id)).Zoek("4910")!.Functie);
        Assert.Equal(-210m, await Saldo(grootboek, wie, id, "1510"));

        // Na de betaling van wat is ingediend staat er niets meer op de btw-rekening, en de aansluiting sluit.
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0099", new(2026, 4, 28), "Omzetbelasting 1e kwartaal 2026",
            [new("1510", 210m), new("1100", -210m)]))).IsGelukt);
        Assert.Equal(0m, await Saldo(grootboek, wie, id, "1510"));
        var stand = await dienst.Jaar(wie, id, 2026);
        Assert.All(stand.Aansluitingen, a => Assert.Equal(0m, a.Verschil));
        Assert.Equal((0m, 0.47m), (stand.Jaar.Suppletie, stand.Jaar.Afronding));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BtwaangifteVastgelegd && r.Details!.Contains("afronding 0.47", StringComparison.Ordinal));

        // Intrekken: de afronding vervalt mee; opnieuw vastleggen boekt hem opnieuw (één keer).
        Assert.True((await dienst.Intrekken(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.Equal(0m, await Saldo(grootboek, wie, id, "4910"));
        Assert.True((await dienst.Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.Equal(-0.47m, await Saldo(grootboek, wie, id, "4910"));
        Assert.Equal(0m, await Saldo(grootboek, wie, id, "1510"));
    }

    [Fact]
    public async Task Op_hele_euros_geen_afrondingsboeking()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Rond B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Verkoop("2026-0001", new(2026, 2, 1), 1000m))).IsGelukt);
        Assert.True((await app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);
        Assert.DoesNotContain(await grootboek.Posten(wie, id, "MEM"), p => p.Post.Omschrijving.StartsWith("Afronding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Zonder_afrondingsrekening_maakt_vastleggen_hem_aan()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Oud B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        // Een oudere administratie: zonder de rekening voor afrondingsverschillen, en 4910 al in gebruik voor iets anders.
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden))
        {
            var oud = db.Rekeningen.Single(r => r.Nummer == "4910");
            (oud.Naam, oud.Functie) = ("Overige kosten", Rekeningfunctie.Geen);
            await db.SaveChangesAsync();
        }

        Assert.True((await grootboek.Boek(wie, id, VerkoopMetCenten("2026-0001", new(2026, 2, 1)))).IsGelukt);
        Assert.True((await app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new(2026, 1, 1), new(2026, 3, 31))).IsGelukt);

        var nieuw = Assert.Single((await grootboek.Schema(wie, id)).MetFunctie(Rekeningfunctie.Afrondingsverschillen));
        Assert.Equal(("4911", "Afrondingsverschillen btw", Rekeningsoort.Resultaat, "WBedAlkOal"), (nieuw.Nummer, nieuw.Naam, nieuw.Soort, nieuw.RgsCode));
        Assert.Equal(-0.47m, await Saldo(grootboek, wie, id, "4911"));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.RekeningToegevoegd && r.Onderwerp == "4911 Afrondingsverschillen btw");
    }

    [Fact]
    public async Task Buiten_het_pakket_aangegeven_en_via_de_bank_betaald_telt_als_aangegeven()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Verkoop("2026-0001", new(2026, 8, 1), 1000m))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0002", new(2026, 10, 28), "Omzetbelasting 3e kwartaal 2026",
            [new("1510", 210m), new("1100", -210m)]))).IsGelukt);

        var stand = await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, 2026);
        var q3 = stand.Jaar.Perioden[2];
        Assert.Null(q3.Vastgelegd);
        Assert.Equal(210m, Assert.Single(stand.Rondrekening.BetaaldZonderVastleggen(q3)).Bedrag);
        var regel = stand.Jaaropstelling.Single(r => r.Omschrijving.StartsWith("3e kwartaal 2026", StringComparison.Ordinal));
        Assert.Equal(("3e kwartaal 2026 (betaald 28-10-2026)", 210m), (regel.Omschrijving, regel.Omzet!.Bedrag));
        Assert.Equal(0m, stand.Jaaropstelling.Single(r => r.Omschrijving.StartsWith("Suppletie omzetbelasting 2026", StringComparison.Ordinal)).Btw!.Bedrag);
        Assert.DoesNotContain(stand.Jaaropstelling, r => r.Omschrijving == "Nog niet aangegeven");
        Assert.Equal((210m, 0m, 0m), stand.Rondrekening.Stand());
        Assert.Equal((0m, 210m, 0m), (stand.Jaar.Aangegeven, stand.Jaar.NietAangegeven, stand.Jaar.Suppletie));
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

    [Fact]
    public async Task Jaaropstelling_met_de_hand_aanpassen_en_terugzetten_alleen_het_kantoor()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Aanpassen B.V.");
        var dienst = app.Dienst<BtwaangifteDienst>();
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, Verkoop("2026-0001", new(2026, 2, 1), 1000m))).IsGelukt);
        static decimal? Btw(Btwstandjaar s, string tekst) => s.Jaaropstelling.First(r => r.Omschrijving.StartsWith(tekst, StringComparison.Ordinal) && r.Soort != Rondrekeningsoort.Kop).Btw?.Bedrag;

        // Het 1e kwartaal is buiten Sifer aangegeven: met de hand 210 als afdracht.
        Assert.True((await dienst.PasAan(wie, id, 2026, "afdracht.2026-01-01", 210m)).IsGelukt);
        var na = await dienst.Jaar(wie, id, 2026);
        Assert.Equal(210m, na.Aanpassingen["afdracht.2026-01-01"]);
        Assert.Equal((210m, 0m), (Btw(na, "Totaal aangegeven"), Btw(na, "Suppletie omzetbelasting 2026")));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BtwjaaropstellingAangepast && r.Details == "berekend → 210.00");

        // Een totaal is niet aan te passen; leeg zet terug.
        Assert.False((await dienst.PasAan(wie, id, 2026, "5c", 1m)).IsGelukt);
        Assert.True((await dienst.PasAan(wie, id, 2026, "afdracht.2026-01-01", null)).IsGelukt);
        Assert.Empty((await dienst.Jaar(wie, id, 2026)).Aanpassingen);

        var ondernemer = await app.Gebruiker("Otto Aanpassen");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.PasAan(ondernemer, id, 2026, "1a.btw", 1m));
        var buiten = await app.Gebruiker("Buiten Aanpassen");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.PasAan(buiten, id, 2026, "1a.btw", 1m));
    }
}
