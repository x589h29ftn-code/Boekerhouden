using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Kostenplaatsen in de randgevallen: alles wat een boeking kopieert, terugdraait, crediteert, herhaalt, verdeelt of
/// omboekt, moet de kostenplaats meenemen (en precies één keer laten tellen), en een kostenplaats die nog ergens in
/// gebruik is, verdwijnt niet.
/// </summary>
public sealed class KostenplaatsRandgevallenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly (DateOnly Van, DateOnly Tot) Jaar = (new(2026, 1, 1), new(2026, 12, 31));

    private async Task<(Uitvoerder Wie, Guid Id, string Leverancier)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await app.Kostenplaatsen(wie, id, true);
        var kostenplaatsen = app.Dienst<KostenplaatsDienst>();
        Assert.True((await kostenplaatsen.VoegToe(wie, id, "AUTO1", "Auto")).IsGelukt);
        Assert.True((await kostenplaatsen.VoegToe(wie, id, "WINKEL", "Winkel")).IsGelukt);
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678")).IsGelukt);
        return (wie, id, (await inrichting.Relaties(wie, id)).Single().Code);
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.WijzigInstellingen(wie, id, await inrichting.Instellingen(wie, id) with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@bakkerij.test"),
        })).IsGelukt);
        var nieuw = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Winkelklant B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Omzetrekening: "8000", Verkoopbtwcode: "V21")), null);
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code;
    }

    private async Task<Kostenplaatstotaal> Totaal(Uitvoerder wie, Guid id, string code) =>
        (await app.Dienst<KostenplaatsDienst>().Overzicht(wie, id, Jaar.Van, Jaar.Tot)).Single(k => k.Kostenplaats.Code == code);

    private async Task<Guid> Inkoop(Uitvoerder wie, Guid id, string leverancier, string nummer, DateOnly datum, decimal bedrag, string? kostenplaats,
        bool creditnota = false, Periodeverdeling? verdeling = null)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(nummer, datum.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))), $"{nummer}.xml", null);
        var geboekt = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(leverancier, null, nummer, datum, datum.AddDays(14), "Telefoon",
            [new Factuurregel("Telefoon", "4310", "I21", bedrag, Kostenplaats: kostenplaats)], Creditnota: creditnota, Verdeling: verdeling));
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        return gelezen.DocumentId.Value;
    }

    [Fact]
    public async Task Een_creditnota_op_de_kosten_verlaagt_de_kosten()
    {
        var (wie, id, leverancier) = await Administratie();
        await Inkoop(wie, id, leverancier, "BN-1", new(2026, 3, 1), 100m, "AUTO1");
        await Inkoop(wie, id, leverancier, "BN-2", new(2026, 3, 5), 30m, "AUTO1", creditnota: true);
        await Inkoop(wie, id, leverancier, "BN-3", new(2026, 3, 9), 20m, "WINKEL", creditnota: true);

        Assert.Equal((70m, 0m, -70m), (await Totaal(wie, id, "AUTO1")) is var a ? (a.Kosten, a.Opbrengsten, a.Resultaat) : default);
        // Alleen een creditnota: minder kosten, geen opbrengst.
        Assert.Equal((-20m, 0m, 20m), (await Totaal(wie, id, "WINKEL")) is var w ? (w.Kosten, w.Opbrengsten, w.Resultaat) : default);
    }

    [Fact]
    public async Task Terugdraaien_en_verwijderen_van_een_inkoopfactuur()
    {
        var (wie, id, leverancier) = await Administratie();
        var document = await Inkoop(wie, id, leverancier, "BN-1", new(2026, 3, 1), 100m, "AUTO1");
        Assert.Equal(100m, (await Totaal(wie, id, "AUTO1")).Kosten);

        Assert.True((await app.Dienst<InkoopDienst>().Terugdraaien(wie, id, document)).IsGelukt);
        Assert.Equal(0m, (await Totaal(wie, id, "AUTO1")).Kosten);
    }

    [Fact]
    public async Task Crediteren_kopieren_en_per_maand_nemen_de_kostenplaats_mee()
    {
        var (wie, id, _) = await Administratie();
        var klant = await Klant(wie, id);
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 3, 1), new(2026, 3, 15), "Maart",
            [new Verkoopregel("Brood", 1m, 100m, "8000", "V9", Kostenplaats: "WINKEL")]));
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Waarde)).IsGelukt);
        Assert.Equal(100m, (await Totaal(wie, id, "WINKEL")).Opbrengsten);

        var credit = await verkoop.Crediteer(wie, id, concept.Waarde);
        Assert.True(credit.IsGelukt, string.Join("; ", credit.Fouten));
        Assert.Equal("WINKEL", (await verkoop.Open(wie, id, credit.Waarde))!.Invoer.Regels.Single().Kostenplaats);
        var definitief = await verkoop.MaakDefinitief(wie, id, credit.Waarde);
        Assert.True(definitief.IsGelukt, string.Join("; ", definitief.Fouten));
        Assert.Equal(0m, (await Totaal(wie, id, "WINKEL")).Resultaat);

        var kopie = await verkoop.Kopieer(wie, id, concept.Waarde);
        Assert.True(kopie.IsGelukt, string.Join("; ", kopie.Fouten));
        Assert.Equal("WINKEL", (await verkoop.Open(wie, id, kopie.Waarde))!.Invoer.Regels.Single().Kostenplaats);

        var perMaand = await verkoop.MaakPerMaand(wie, id, concept.Waarde, 2026, 4, 2);
        Assert.True(perMaand.IsGelukt, string.Join("; ", perMaand.Fouten));
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.All(await db.Verkoopdocumentregels.AsNoTracking().ToListAsync(), r => Assert.Equal("WINKEL", r.Kostenplaats));
    }

    [Fact]
    public async Task Terugkerende_facturen_houden_de_kostenplaats()
    {
        var (wie, id, _) = await Administratie();
        var klant = await Klant(wie, id);
        var abonnementen = app.Dienst<AbonnementDienst>();
        var bewaard = await abonnementen.Bewaar(wie, id, null, new Abonnementinvoer("Huur", klant, Herhaling.Maand, new(2026, 1, 1), null, "Huur #periode",
            [new Verkoopregel("Huur", 1m, 500m, "8000", "V21", Kostenplaats: "WINKEL")], Automatisch: true));
        Assert.True(bewaard.IsGelukt, string.Join("; ", bewaard.Fouten));
        var gemaakt = await abonnementen.Maak(wie, id, new DateOnly(2026, 3, 15));
        Assert.True(gemaakt.IsGelukt, string.Join("; ", gemaakt.Fouten));
        Assert.Equal(1500m, (await Totaal(wie, id, "WINKEL")).Opbrengsten);
    }

    [Fact]
    public async Task Memoriaal_herhalen_en_herboeken_houden_de_kostenplaats()
    {
        var (wie, id, _) = await Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 1, 15), "Lease",
            [new Journaalregel("4500", 300m, Kostenplaats: "AUTO1"), new Journaalregel("1100", -300m)]))).IsGelukt);
        var herhaald = await grootboek.KopieerNaarMaand(wie, id, "MEM", "2026-0001", 2026, 2);
        Assert.True(herhaald.IsGelukt, string.Join("; ", herhaald.Fouten));
        Assert.Equal((600m, 2), (await Totaal(wie, id, "AUTO1")) is var t ? (t.Kosten, t.Regels) : default);

        var herboekt = await grootboek.Herboek(wie, id, [new Herboekregel("MEM", "2026-0001", 0)], "4300");
        Assert.True(herboekt.IsGelukt, string.Join("; ", herboekt.Fouten));
        var regels = await app.Dienst<KostenplaatsDienst>().Boekingen(wie, id, "AUTO1", Jaar.Van, Jaar.Tot);
        Assert.Equal(["4300", "4500"], regels.Select(r => r.Rekeningnummer));
        Assert.Equal(600m, (await Totaal(wie, id, "AUTO1")).Kosten);
    }

    [Fact]
    public async Task Een_kostenplaats_in_gebruik_bij_een_relatie_concept_of_abonnement_verdwijnt_niet()
    {
        var (wie, id, _) = await Administratie();
        var dienst = app.Dienst<KostenplaatsDienst>();
        var lijst = await dienst.Lijst(wie, id);
        Guid Code(string code) => lijst.Single(k => k.Code == code).Id;
        var inrichting = app.Dienst<InrichtingDienst>();
        var relatie = (await inrichting.Relaties(wie, id)).Single();
        Assert.True((await inrichting.WijzigRelatie(wie, id, relatie.Id, new RelatieInvoer(relatie.Naam, relatie.BtwNummer, relatie.KvkNummer, relatie.Tegenrekening, Kostenplaats: "AUTO1"))).IsGelukt);
        Assert.Contains("Belnet", Assert.Single((await dienst.Verwijder(wie, id, Code("AUTO1"))).Fouten), StringComparison.Ordinal);

        var klant = await Klant(wie, id);
        var concept = await app.Dienst<VerkoopDienst>().Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 3, 1), new(2026, 3, 15), "Maart",
            [new Verkoopregel("Brood", 1m, 100m, "8000", "V9", Kostenplaats: "WINKEL")]));
        Assert.True(concept.IsGelukt);
        Assert.Contains("concept", Assert.Single((await dienst.Verwijder(wie, id, Code("WINKEL"))).Fouten), StringComparison.Ordinal);
        Assert.Equal(2, (await dienst.Lijst(wie, id)).Count);
    }

    [Fact]
    public async Task Bij_een_verdeling_wordt_de_kostenplaats_nog_steeds_gecontroleerd()
    {
        var (wie, id, leverancier) = await Administratie();
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-9", "2026-04-01")), "bn9.xml", null);
        var invoer = new Factuurinvoer(leverancier, null, "BN-9", new(2026, 4, 1), new(2026, 4, 15), "Jaar",
            [new Factuurregel("Abonnement", "4310", "I21", 1200m, Kostenplaats: "ONBEKEND")], Verdeling: new Periodeverdeling(new(2026, 4, 1), new(2027, 3, 31), "1410"));
        Assert.Contains("Kostenplaats ONBEKEND bestaat niet.", (await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, invoer)).Fouten);

        var auto = (await app.Dienst<KostenplaatsDienst>().Lijst(wie, id)).Single(k => k.Code == "AUTO1");
        Assert.True((await app.Dienst<KostenplaatsDienst>().Wijzig(wie, id, auto.Id, auto.Naam, afgesloten: true)).IsGelukt);
        Assert.Contains("Kostenplaats AUTO1 is afgesloten.",
            (await inkoop.Boek(wie, id, gelezen.DocumentId.Value, invoer with { Regels = [invoer.Regels[0] with { Kostenplaats = "AUTO1" }] })).Fouten);

        var klant = await Klant(wie, id);
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 1, 5), new(2026, 1, 19), "Jaar",
            [new Verkoopregel("Abonnement", 1m, 1200m, "8000", "V21", Kostenplaats: "AUTO1")])
        { Verdeling = new Periodeverdeling(new(2026, 1, 1), new(2026, 12, 31), "1710") });
        var definitief = concept.IsGelukt ? await verkoop.MaakDefinitief(wie, id, concept.Waarde) : null;
        Assert.False(concept.IsGelukt && definitief!.IsGelukt, "een afgesloten kostenplaats op een verdeelde verkoopfactuur werd geboekt");
    }

    [Fact]
    public async Task Een_betaalde_verdeelde_factuur_kan_achteraf_een_kostenplaats_krijgen()
    {
        var (wie, id, leverancier) = await Administratie();
        var document = await Inkoop(wie, id, leverancier, "BN-5", new(2026, 4, 1), 1200m, null, verdeling: new Periodeverdeling(new(2026, 4, 1), new(2027, 3, 31), "1410"));
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden))
        {
            (await db.OpenstaandePosten.SingleAsync()).Openstaand = 0m;
            await db.SaveChangesAsync();
        }

        var inkoop = app.Dienst<InkoopDienst>();
        var geboekt = (await inkoop.Geboekt(wie, id, document))!;
        var uitkomst = await inkoop.Boek(wie, id, document, new Factuurinvoer(geboekt.Relatie, null, geboekt.Factuurnummer, geboekt.Datum, geboekt.Vervaldatum,
            geboekt.Omschrijving, [.. geboekt.Regels.Select(r => r with { Kostenplaats = "AUTO1" })], geboekt.Betalingskenmerk, Verdeling: geboekt.Verdeling));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal((1200m, 12), (await app.Dienst<KostenplaatsDienst>().Overzicht(wie, id, new(2026, 1, 1), new(2027, 12, 31))).Single(k => k.Kostenplaats.Code == "AUTO1") is var t ? (t.Kosten, t.Regels) : default);
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen))
        {
            Assert.Equal(0m, (await db.OpenstaandePosten.SingleAsync()).Openstaand);
        }

        Assert.Empty(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, id));
    }
    [Fact]
    public async Task Een_verdeling_met_twee_kostenplaatsen_verdeelt_elke_regel_naar_zijn_eigen()
    {
        var (wie, id, leverancier) = await Administratie();
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-7", "2026-01-01")), "bn7.xml", null);
        var geboekt = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(leverancier, null, "BN-7", new(2026, 1, 1), new(2026, 1, 15), "Jaar",
            [new Factuurregel("Auto", "4500", "I21", 600m, Kostenplaats: "AUTO1"), new Factuurregel("Winkel", "4310", "I21", 300m, Kostenplaats: "WINKEL"), new Factuurregel("Algemeen", "4300", "I21", 120m)],
            Verdeling: new Periodeverdeling(new(2026, 1, 1), new(2026, 6, 30), "1410")));
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        Assert.Equal((600m, 6), (await Totaal(wie, id, "AUTO1")) is var a ? (a.Kosten, a.Regels) : default);
        Assert.Equal((300m, 6), (await Totaal(wie, id, "WINKEL")) is var w ? (w.Kosten, w.Regels) : default);
        Assert.All(await app.Dienst<KostenplaatsDienst>().Boekingen(wie, id, "AUTO1", Jaar.Van, Jaar.Tot), r => Assert.Equal(("4500", 100m), (r.Rekeningnummer, r.Bedrag)));
        Assert.Equal(["AUTO1", "WINKEL", null], (await inkoop.Geboekt(wie, id, gelezen.DocumentId.Value))!.Regels.Select(r => r.Kostenplaats));
    }

    [Fact]
    public async Task In_een_afgesloten_periode_verandert_de_kostenplaats_niet()
    {
        var (wie, id, leverancier) = await Administratie();
        var document = await Inkoop(wie, id, leverancier, "BN-1", new(2026, 3, 1), 100m, null);
        Assert.True((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new(2026, 3, 31))).IsGelukt);
        var inkoop = app.Dienst<InkoopDienst>();
        var g = (await inkoop.Geboekt(wie, id, document))!;
        var uitkomst = await inkoop.Boek(wie, id, document, new Factuurinvoer(g.Relatie, null, g.Factuurnummer, g.Datum, g.Vervaldatum, g.Omschrijving,
            [.. g.Regels.Select(r => r with { Kostenplaats = "AUTO1" })], g.Betalingskenmerk));
        Assert.False(uitkomst.IsGelukt);
        Assert.Contains("afgesloten", Assert.Single(uitkomst.Fouten), StringComparison.Ordinal);
        Assert.Equal(0, (await Totaal(wie, id, "AUTO1")).Regels);
    }
}
