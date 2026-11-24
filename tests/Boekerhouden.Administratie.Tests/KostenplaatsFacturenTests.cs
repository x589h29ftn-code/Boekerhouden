using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De kostenplaats op inkoop- en verkoopfacturen en bankregels: per regel te kiezen, met een standaard per relatie, en
/// in de boeking op de kosten- of omzetregel (niet op crediteuren, debiteuren of btw).
/// </summary>
public sealed class KostenplaatsFacturenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Verwerkingsregel Telefoon = new("Belnet is telefoon", "4310", "I21", Automatisch: true, NaamBevat: "Belnet");

    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        Assert.True((await app.Dienst<KantoorDienst>().WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        var id = await app.Administratie();
        var kostenplaatsen = app.Dienst<KostenplaatsDienst>();
        Assert.True((await kostenplaatsen.VoegToe(wie, id, "AUTO1", "Auto")).IsGelukt);
        Assert.True((await kostenplaatsen.VoegToe(wie, id, "WINKEL", "Winkel")).IsGelukt);
        return (wie, id);
    }

    private async Task<Journaalpost> Boeking(Uitvoerder wie, Guid id, string dagboek) =>
        (await app.Dienst<GrootboekDienst>().Posten(wie, id, dagboek)).Single().Post;

    [Fact]
    public async Task Inkoop_met_de_standaard_van_de_leverancier_en_per_regel()
    {
        var (wie, id) = await Administratie();
        var inrichting = app.Dienst<InrichtingDienst>();
        var inkoop = app.Dienst<InkoopDienst>();

        Assert.Equal(["Kostenplaats ONBEKEND bestaat niet."],
            (await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310", Kostenplaats: "ONBEKEND"), "NL63TRIO0212345678")).Fouten);
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310", Kostenplaats: "AUTO1"), "NL63TRIO0212345678")).IsGelukt);
        var belnet = (await inrichting.Relaties(wie, id)).Single();
        Assert.Equal("AUTO1", belnet.Kostenplaats);

        // Automatisch geboekt (regel): de kostenplaats van de leverancier op de kostenregel, niet op crediteuren of btw.
        await inrichting.VoegRegelToe(wie, id, Telefoon);
        var april = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.True(april.IsGeboekt, april.Melding);
        var post = await Boeking(wie, id, "INK");
        Assert.Equal(["AUTO1"], post.Regels.Where(r => r.Kostenplaats is not null).Select(r => r.Rekeningnummer == "4310" ? r.Kostenplaats : "fout"));

        // Zelf boeken: per regel een andere kostenplaats; een onbekende wordt geweigerd.
        var mei = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-2026-0511", "2026-05-01")), "belnet-mei.xml", null);
        Factuurinvoer Invoer(string? tweede) => new(belnet.Code, null, "BN-2026-0511", new(2026, 5, 1), new(2026, 5, 15), "Mei",
            [new Factuurregel("Bellen", "4310", "I21", 10m, Kostenplaats: "AUTO1"), new Factuurregel("Winkel", "4310", "I21", 10.65m, Kostenplaats: tweede)]);
        Assert.Equal(["Kostenplaats ONBEKEND bestaat niet."], (await inkoop.Boek(wie, id, mei.DocumentId!.Value, Invoer("ONBEKEND"))).Fouten);
        var geboekt = await inkoop.Boek(wie, id, mei.DocumentId.Value, Invoer("WINKEL"));
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));

        // Heropenen geeft de kostenplaatsen per regel terug.
        var terug = (await inkoop.Geboekt(wie, id, mei.DocumentId.Value))!;
        Assert.Equal(["AUTO1", "WINKEL"], terug.Regels.Select(r => r.Kostenplaats));
        var overzicht = await app.Dienst<KostenplaatsDienst>().Overzicht(wie, id, new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(20.65m + 10m, overzicht.Single(k => k.Kostenplaats.Code == "AUTO1").Kosten);
        Assert.Equal(10.65m, overzicht.Single(k => k.Kostenplaats.Code == "WINKEL").Kosten);
    }

    [Fact]
    public async Task Verkoop_per_regel_met_de_standaard_van_de_klant()
    {
        var (wie, id) = await Administratie();
        var inrichting = app.Dienst<InrichtingDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@bakkerij.test"),
        })).IsGelukt);
        var nieuw = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Winkelklant B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Omzetrekening: "8000", Verkoopbtwcode: "V21"), Kostenplaats: "WINKEL"), null);
        var klant = (await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code;
        Assert.Equal("WINKEL", (await verkoop.Context(wie, id)).Klanten.Single().Kostenplaats);

        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 3, 1), new(2026, 3, 15), "Maart",
            [new Verkoopregel("Brood", 1m, 100m, "8000", "V9", Kostenplaats: "WINKEL"), new Verkoopregel("Bezorgen", 1m, 20m, "8000", "V21")]));
        Assert.True(concept.IsGelukt, string.Join("; ", concept.Fouten));
        Assert.Equal(["WINKEL", null], (await verkoop.Open(wie, id, concept.Waarde))!.Invoer.Regels.Select(r => r.Kostenplaats));

        var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
        Assert.True(definitief.IsGelukt, string.Join("; ", definitief.Fouten));
        var post = await Boeking(wie, id, "VRK");
        Assert.Equal([("8000", -100m)], post.Regels.Where(r => r.Kostenplaats == "WINKEL").Select(r => (r.Rekeningnummer, r.Bedrag)));
        Assert.Equal(100m, (await app.Dienst<KostenplaatsDienst>().Overzicht(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Single(k => k.Kostenplaats.Code == "WINKEL").Opbrengsten);
    }

    [Fact]
    public async Task Bankregel_met_een_kostenplaats()
    {
        var (wie, id) = await Administratie();
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        var mutatie = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -199.65m);

        Assert.Equal(["Kostenplaats ONBEKEND bestaat niet."],
            (await bank.Verdeel(wie, id, mutatie.Id, [], [new Bankregelinvoer("4300", "I21", 199.65m, "Onderdelen", Kostenplaats: "ONBEKEND")])).Fouten);
        var geboekt = await bank.Verdeel(wie, id, mutatie.Id, [], [new Bankregelinvoer("4300", "I21", 150m, "Onderdelen", Kostenplaats: "AUTO1"),
            new Bankregelinvoer("4310", null, 49.65m, "Telefoon")]);
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));

        var post = (await app.Dienst<GrootboekDienst>().Posten(wie, id, "BNK")).Single().Post;
        var auto = Assert.Single(post.Regels, r => r.Kostenplaats is not null);
        Assert.Equal(("4300", "AUTO1"), (auto.Rekeningnummer, auto.Kostenplaats));
        Assert.Equal(123.97m, (await app.Dienst<KostenplaatsDienst>().Boekingen(wie, id, "AUTO1", new(2026, 1, 1), new(2026, 12, 31))).Single().Bedrag);
    }
}

/// <summary>Afsluiten en heropenen, de export, en de standaard van een relatie als de module uit staat of de kostenplaats dicht is.</summary>
public sealed class KostenplaatsAfsluitenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Afsluiten_heropenen_en_exporteren()
    {
        var wie = await app.Beheerder();
        Assert.True((await app.Dienst<KantoorDienst>().WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        var id = await app.Administratie();
        var dienst = app.Dienst<KostenplaatsDienst>();
        var auto = (await dienst.VoegToe(wie, id, "AUTO1", "Auto")).Waarde;
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 3, 1), "Onderhoud",
            [new Journaalregel("4300", 100m, Kostenplaats: "AUTO1"), new Journaalregel("4310", 20m, Kostenplaats: "AUTO1"), new Journaalregel("1100", -120m)]))).IsGelukt);

        // Afgesloten: niet meer te kiezen en niet meer op te boeken; heropenen kan, in het log.
        Assert.True((await dienst.Wijzig(wie, id, auto, "Auto", afgesloten: true)).IsGelukt);
        Assert.Empty(await dienst.Keuze(wie, id));
        Assert.True((await dienst.Lijst(wie, id)).Single().Afgesloten);
        Assert.True((await dienst.Wijzig(wie, id, auto, "Auto", afgesloten: false)).IsGelukt);
        Assert.Single(await dienst.Keuze(wie, id));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.KostenplaatsGewijzigd && r.Details == "heropend");

        // Export: het totaaloverzicht en per kostenplaats per rekening met subtotalen.
        var (van, tot) = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var totaal = Export.Kostenplaatsexport.Totaal(await dienst.Overzicht(wie, id, van, tot), van, tot);
        Assert.Equal(["Kosten", "Opbrengsten", "Resultaat"], totaal.Koppen);
        Assert.Equal([120m, 0m, -120m], totaal.Regels.Single(r => r.Nummer == "AUTO1").Bedragen);
        var per = Export.Kostenplaatsexport.Kostenplaats((await dienst.Lijst(wie, id)).Single(), await dienst.Boekingen(wie, id, "AUTO1", van, tot), van, tot);
        Assert.Equal(["4300", "MEM 2026-0001", null, "4310", "MEM 2026-0001", null, null], per.Regels.Select(r => r.Nummer));
        Assert.Equal([120m, 0m], per.Regels[^1].Bedragen);
        Assert.NotEmpty(await app.Dienst<OverzichtDienst>().TabelExcel(wie, id, per));
    }

    [Fact]
    public async Task De_standaard_van_de_relatie_alleen_met_de_module_aan_en_open()
    {
        var wie = await app.Beheerder();
        var kantoor = app.Dienst<KantoorDienst>();
        Assert.True((await kantoor.WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        var id = await app.Administratie();
        var dienst = app.Dienst<KostenplaatsDienst>();
        var auto = (await dienst.VoegToe(wie, id, "AUTO1", "Auto")).Waarde;
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310", Kostenplaats: "AUTO1"), "NL63TRIO0212345678")).IsGelukt);
        await inrichting.VoegRegelToe(wie, id, new Verwerkingsregel("Belnet is telefoon", "4310", "I21", Automatisch: true, NaamBevat: "Belnet"));
        var inkoop = app.Dienst<InkoopDienst>();

        // Afgesloten: de factuur wordt gewoon geboekt, zonder kostenplaats.
        Assert.True((await dienst.Wijzig(wie, id, auto, "Auto", afgesloten: true)).IsGelukt);
        var april = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.True(april.IsGeboekt, april.Melding);

        // Module uit: ook zonder kostenplaats.
        Assert.True((await dienst.Wijzig(wie, id, auto, "Auto", afgesloten: false)).IsGelukt);
        Assert.True((await kantoor.WijzigModules(wie, new Kantoormodules(false))).IsGelukt);
        try
        {
            var mei = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-2026-0511", "2026-05-01")), "belnet-mei.xml", null);
            Assert.True(mei.IsGeboekt, mei.Melding);
            Assert.All(await app.Dienst<GrootboekDienst>().Posten(wie, id, "INK"), p => Assert.All(p.Post.Regels, r => Assert.Null(r.Kostenplaats)));
        }
        finally
        {
            await kantoor.WijzigModules(wie, new Kantoormodules(true));
        }
    }
}
