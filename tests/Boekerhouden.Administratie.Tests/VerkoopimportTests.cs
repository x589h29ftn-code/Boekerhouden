using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Verkoopfacturen uit een ander pakket inlezen: e-factuur en PDF, klant herkennen, vanzelf boeken of concept.</summary>
public sealed class VerkoopimportTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Partij Eigen = new("Bakkerij Import B.V.", new("Dorpsstraat 1", "1234 AB", "Ergens"), "NL123456782B01", "12345678", "info@bakkerij.test", ["NL20INGB0001234567"]);

    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Bakkerij Import B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens"),
        })).IsGelukt);
        return (wie, id);
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id, string naam = "Hotel De Klant B.V.", string? btw = "NL800000006B01")
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(naam, btw, null, null, new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Omzetrekening: "8010")), null);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    /// <summary>Een e-factuur zoals een ander pakket hem maakt: de eigen onderneming als leverancier.</summary>
    private static MemoryStream Ubl(string nummer, DateOnly datum, string klant = "Hotel De Klant B.V.", string? btw = "NL800000006B01", decimal bedrag = 400m) =>
        new(Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(
            new Verkoopfactuur("X", new Partij(klant, new("Laan 2", "5678 CD", "Elders"), btw), nummer, datum, datum.AddDays(14), "Brood en banket",
                [new Verkoopregel("Brood en banket", 1m, bedrag, "8000", "V9"), new Verkoopregel("Bezorging", 1m, 25m, "8000", "V21")], Betalingskenmerk.Maak(nummer)),
            Eigen, BtwCodes.Standaard)));

    [Fact]
    public async Task Een_e_factuur_van_een_bekende_klant_wordt_meteen_geboekt_met_het_nummer_van_het_andere_pakket()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id);
        var import = app.Dienst<VerkoopimportDienst>();

        var uitkomst = await import.Lees(wie, id, Ubl("OUD-2026-077", new(2026, 3, 1)), "oud-077.xml", "application/xml");

        Assert.True(uitkomst.Geboekt, uitkomst.Melding);
        var factuur = (await app.Dienst<VerkoopDienst>().Open(wie, id, uitkomst.Id!.Value))!;
        Assert.Equal(("OUD-2026-077", klant, true), (factuur.Factuur.Nummer, factuur.Factuur.Relatie, factuur.Ingelezen));
        Assert.Equal(("VRK", "2026-0001"), factuur.Boeking);
        Assert.Equal(Betalingskenmerk.Maak("OUD-2026-077"), factuur.Factuur.Betalingskenmerk);
        Assert.Equal(400m + 36m + 25m + 5.25m, factuur.Totaal);
        Assert.All(factuur.Factuur.Regels, r => Assert.Equal("8010", r.Rekeningnummer));
        Assert.Equal(["V9", "V21"], factuur.Factuur.Regels.Select(r => r.BtwCode));
        var boek = Assert.Single(await app.Dienst<VerkoopDienst>().Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31)));
        Assert.Equal(("OUD-2026-077", 466.25m, 466.25m), (boek.Factuurnummer, boek.Totaal, boek.Openstaand));
        var bestand = (await import.Bestand(wie, id, uitkomst.Id.Value))!.Value;
        Assert.Equal(("application/xml", "oud-077.xml"), (bestand.Inhoudstype, bestand.Bestandsnaam));
    }

    [Fact]
    public async Task Een_onbekende_klant_geeft_een_concept_dat_na_het_kiezen_van_de_klant_geboekt_wordt()
    {
        var (wie, id) = await Administratie();
        var import = app.Dienst<VerkoopimportDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();

        var uitkomst = await import.Lees(wie, id, Ubl("2026-500", new(2026, 4, 1), "Nieuwe Klant B.V.", "BE0123456789"), "nieuw.xml", null);

        Assert.False(uitkomst.Geboekt);
        Assert.Contains("Nieuwe Klant B.V. is (nog) niet bekend", uitkomst.Melding, StringComparison.Ordinal);
        var concept = (await verkoop.Open(wie, id, uitkomst.Id!.Value))!;
        Assert.Equal((Verkoopstatus.Concept, "Nieuwe Klant B.V.", "BE0123456789", "2026-500"), (concept.Status, concept.Factuur.Klant.Naam, concept.Factuur.Klant.BtwNummer, concept.Invoer.Factuurnummer));
        Assert.Contains("Kies een klant.", (await verkoop.MaakDefinitief(wie, id, concept.Id)).Fouten);

        var klant = await Klant(wie, id, "Nieuwe Klant B.V.", "BE0123456789");
        Assert.True((await verkoop.Bewaar(wie, id, concept.Id, concept.Invoer with { Relatie = klant })).IsGelukt);
        Assert.Equal("2026-500", (await verkoop.MaakDefinitief(wie, id, concept.Id)).Waarde);
        Assert.Null((await verkoop.Open(wie, id, concept.Id))!.Melding);
    }

    [Fact]
    public async Task Hetzelfde_bestand_of_hetzelfde_nummer_nog_eens_wordt_niet_dubbel_geboekt()
    {
        var (wie, id) = await Administratie();
        await Klant(wie, id);
        var import = app.Dienst<VerkoopimportDienst>();
        Assert.True((await import.Lees(wie, id, Ubl("2026-010", new(2026, 5, 1)), "a.xml", null)).Geboekt);

        Assert.Contains("al eerder ingelezen", (await import.Lees(wie, id, Ubl("2026-010", new(2026, 5, 1)), "a-kopie.xml", null)).Melding, StringComparison.Ordinal);
        var ander = await import.Lees(wie, id, Ubl("2026-010", new(2026, 5, 1), bedrag: 500m), "b.xml", null);
        Assert.False(ander.Geboekt);
        Assert.Contains("staat al in de administratie", ander.Melding, StringComparison.Ordinal);
        Assert.Contains("staat al in de administratie", string.Join(" ", (await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, ander.Id!.Value)).Fouten), StringComparison.Ordinal);
        Assert.Single(await app.Dienst<VerkoopDienst>().Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31)));
    }

    [Fact]
    public async Task Na_een_overstap_halverwege_het_jaar_loopt_de_eigen_nummering_door()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id);
        Assert.True((await app.Dienst<VerkoopimportDienst>().Lees(wie, id, Ubl("20260041", new(2026, 6, 30)), "laatste-oude.xml", null)).Geboekt);
        var verkoop = app.Dienst<VerkoopDienst>();

        var concept = (await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 7, 1), new(2026, 7, 15), "Juli", [new Verkoopregel("Brood", 1m, 100m, "8010", "V9")]))).Waarde;

        Assert.Equal("20260042", (await verkoop.MaakDefinitief(wie, id, concept)).Waarde);
    }

    [PdfBrowserFact]
    public async Task Een_pdf_wordt_gelezen_en_de_klant_herkend_aan_het_btw_nummer()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id);
        var beeld = new Verkoopbeeld(new Verkoopfactuur(klant, new Partij("Hotel De Klant B.V.", new("Laan 2", "5678 CD", "Elders"), "NL800000006B01"), "PDF-2026-9",
            new(2026, 8, 3), new(2026, 8, 17), "Brood en banket", [new Verkoopregel("Brood en banket augustus", 1m, 1000m, "8010", "V9")], Betalingskenmerk.Maak("20269")), Eigen, BtwCodes.Standaard);
        var pdf = await app.Dienst<PdfDienst>().Maak(FactuurHtml.Document(beeld, Factuurontwerp.Standaard, null));
        Assert.True(pdf.IsGelukt, string.Join("; ", pdf.Fouten));

        var uitkomst = await app.Dienst<VerkoopimportDienst>().Lees(wie, id, new MemoryStream(pdf.Waarde!), "factuur.pdf", "application/pdf");

        var factuur = (await app.Dienst<VerkoopDienst>().Open(wie, id, uitkomst.Id!.Value))!;
        Assert.True(uitkomst.Geboekt, uitkomst.Melding);
        Assert.Equal(("PDF-2026-9", klant, new DateOnly(2026, 8, 3)), (factuur.Factuur.Nummer, factuur.Factuur.Relatie, factuur.Factuur.Datum));
        Assert.Equal(1090m, factuur.Totaal);
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_niets_inlezen()
    {
        var (wie, id) = await Administratie();
        await Klant(wie, id);
        var import = app.Dienst<VerkoopimportDienst>();
        var gelezen = await import.Lees(wie, id, Ubl("2026-020", new(2026, 5, 1)), "a.xml", null);
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        await Assert.ThrowsAsync<GeenToegangException>(() => import.Lees(inzien, id, Ubl("2026-021", new(2026, 5, 1)), "b.xml", null));
        await Assert.ThrowsAsync<GeenToegangException>(() => import.Lees(buiten, id, Ubl("2026-022", new(2026, 5, 1)), "c.xml", null));
        await Assert.ThrowsAsync<GeenToegangException>(() => import.Bestand(buiten, id, gelezen.Id!.Value));
        Assert.NotNull(await import.Bestand(inzien, id, gelezen.Id!.Value));
        Assert.Null(await import.Bestand(wie, await app.Administratie("Andere Zaak"), gelezen.Id.Value));
    }
}
