using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Hoe een factuur geboekt is (handmatig of vanzelf), in het inkoop- en verkoopboek, en het percentage dat vanzelf ging.
/// De bank staat in de banktests.
/// </summary>
public sealed class VerwerkingswijzeTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public void Het_percentage_telt_alleen_wat_bekend_is()
    {
        Assert.Equal((null, ""), Automatisering.Percentage([]));
        Assert.Equal((null, ""), Automatisering.Percentage([Verwerkingswijze.Onbekend]));
        var (procent, uitleg) = Automatisering.Percentage([Verwerkingswijze.Handmatig, Verwerkingswijze.Autocodering, Verwerkingswijze.Afgeletterd, Verwerkingswijze.Onbekend]);
        Assert.Equal(66, procent);
        Assert.Equal("2 van 3 vanzelf geboekt (1 handmatig, 1 autocodering, 1 afgeletterd)", uitleg);
        Assert.Equal(("handmatig", ""), (Automatisering.Naam(Verwerkingswijze.Handmatig), Automatisering.Naam(Verwerkingswijze.Onbekend)));
    }

    [Fact]
    public async Task Inkoop_vanzelf_handmatig_aangepast_en_teruggedraaid()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678")).IsGelukt);
        var leverancier = (await inrichting.Relaties(wie, id)).Single().Code;
        await inrichting.VoegRegelToe(wie, id, new Verwerkingsregel("Belnet is telefoon", "4310", "I21", Automatisch: true, NaamBevat: "Belnet"));
        var inkoop = app.Dienst<InkoopDienst>();

        // Vanzelf geboekt door de regel: autocodering.
        var april = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.True(april.IsGeboekt, april.Melding);
        Assert.Equal(Verwerkingswijze.Autocodering, Assert.Single(await inkoop.Inkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Verwerking);

        // Aangepast door de gebruiker: handmatig.
        var geboekt = (await inkoop.Geboekt(wie, id, april.DocumentId!.Value))!;
        Factuurinvoer Invoer(GeboekteFactuur g, string omschrijving) => new(g.Relatie, null, g.Factuurnummer, g.Datum, g.Vervaldatum, omschrijving, [.. g.Regels], g.Betalingskenmerk);
        Assert.True((await inkoop.Boek(wie, id, april.DocumentId.Value, Invoer(geboekt, "Telefoon april"))).IsGelukt);
        Assert.Equal(Verwerkingswijze.Handmatig, Assert.Single(await inkoop.Inkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Verwerking);

        // Teruggedraaid: weer onbekend, en niet meer in het boek.
        Assert.True((await inkoop.Terugdraaien(wie, id, april.DocumentId.Value)).IsGelukt);
        Assert.Empty(await inkoop.Inkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31)));
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(Verwerkingswijze.Onbekend, (await db.Documenten.FindAsync(april.DocumentId.Value))!.Verwerking);
    }

    [Fact]
    public async Task Een_alleen_aangepaste_kostenplaats_houdt_de_autocodering()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await app.Kostenplaatsen(wie, id, true);
        Assert.True((await app.Dienst<KostenplaatsDienst>().VoegToe(wie, id, "AUTO1", "Auto")).IsGelukt);
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678")).IsGelukt);
        await inrichting.VoegRegelToe(wie, id, new Verwerkingsregel("Belnet is telefoon", "4310", "I21", Automatisch: true, NaamBevat: "Belnet"));
        var inkoop = app.Dienst<InkoopDienst>();
        var april = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.True(april.IsGeboekt, april.Melding);

        var g = (await inkoop.Geboekt(wie, id, april.DocumentId!.Value))!;
        Assert.True((await inkoop.Boek(wie, id, april.DocumentId.Value, new Factuurinvoer(g.Relatie, null, g.Factuurnummer, g.Datum, g.Vervaldatum, g.Omschrijving,
            [.. g.Regels.Select(r => r with { Kostenplaats = "AUTO1" })], g.Betalingskenmerk))).IsGelukt);
        Assert.Equal(Verwerkingswijze.Autocodering, Assert.Single(await inkoop.Inkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Verwerking);
    }

    [Fact]
    public async Task Een_zelf_gemaakte_verkoopfactuur_is_handmatig()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
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
        var klant = (await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code;
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 3, 1), new(2026, 3, 15), "Maart", [new Verkoopregel("Brood", 1m, 100m, "8000", "V9")]));
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Waarde)).IsGelukt);
        Assert.Equal(Verwerkingswijze.Handmatig, Assert.Single(await verkoop.Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Verwerking);
    }
}
