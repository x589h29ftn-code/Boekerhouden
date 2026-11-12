using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Verkoop;
using Boekerhouden.Documenten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een ingelezen verkoopfactuur verwerken zoals inkoop: de standaardomschrijving van het kantoor met variabelen
/// (#klant, #maandnr), en het origineel met de woorden op hun plek om een gebied aan te wijzen.
/// </summary>
public sealed class VerkoopAanwijzenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Partij Eigen = new("Bakkerij Import B.V.", new("Dorpsstraat 1", "1234 AB", "Ergens"), "NL123456782B01", "12345678", "info@bakkerij.test", ["NL20INGB0001234567"]);

    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Bakkerij Import B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with { BtwNummer = "NL123456782B01", Ibans = ["NL20INGB0001234567"] })).IsGelukt);
        return (wie, id);
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id, string naam = "Hotel De Klant B.V.", string? btw = "NL800000006B01")
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(naam, btw, null, null, new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Omzetrekening: "8010")), null);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    private static MemoryStream Ubl(string nummer, DateOnly datum, string klant = "Hotel De Klant B.V.", string? btw = "NL800000006B01") =>
        new(Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(
            new Verkoopfactuur("X", new Partij(klant, new("Laan 2", "5678 CD", "Elders"), btw), nummer, datum, datum.AddDays(14), "Brood en banket",
                [new Verkoopregel("Brood en banket", 1m, 400m, "8000", "V9")], Betalingskenmerk.Maak(nummer)),
            Eigen, BtwCodes.Standaard)));

    [Fact]
    public async Task De_standaardomschrijving_met_klant_en_maandnummer_komt_in_de_boeking()
    {
        var (wie, id) = await Administratie();
        await Klant(wie, id);
        var kantoor = app.Dienst<KantoorDienst>();
        Assert.Equal(["De standaardomschrijving voor verkoop kent #maant niet."],
            (await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard with { OmschrijvingVerkoop = "Verkoop #klant #maant" })).Fouten);
        Assert.True((await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard with { OmschrijvingVerkoop = "Verkoop #klant #maandnr-#jaar" })).IsGelukt);
        var import = app.Dienst<VerkoopimportDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();

        // Bekende klant: vanzelf geboekt, de variabelen gevuld (maart is 3).
        var bekend = await import.Lees(wie, id, Ubl("A-100", new(2026, 3, 9)), "a-100.xml", null);
        Assert.True(bekend.Geboekt, bekend.Melding);
        Assert.Equal("Verkoop Hotel De Klant B.V. 3-2026", (await verkoop.Open(wie, id, bekend.Id!.Value))!.Factuur.Omschrijving);
        Assert.Equal("Verkoop Hotel De Klant B.V. 3-2026", (await app.Dienst<GrootboekDienst>().Posten(wie, id, "VRK")).Single().Post.Omschrijving);

        // Onbekende klant: de variabelen blijven staan tot de klant gekozen is en de factuur definitief wordt.
        var onbekend = await import.Lees(wie, id, Ubl("A-101", new(2026, 11, 2), "Nieuwe Klant B.V.", "BE0123456789"), "a-101.xml", null);
        var concept = (await verkoop.Open(wie, id, onbekend.Id!.Value))!;
        Assert.Equal("Verkoop #klant #maandnr-#jaar", concept.Invoer.Omschrijving);
        var klant = await Klant(wie, id, "Nieuwe Klant B.V.", "BE0123456789");
        Assert.True((await verkoop.Bewaar(wie, id, concept.Id, concept.Invoer with { Relatie = klant, Omschrijving = "#klant #factuurnummer #maandnr-#jaar" })).IsGelukt);
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Id)).IsGelukt);
        Assert.Equal("Nieuwe Klant B.V. A-101 11-2026", (await verkoop.Open(wie, id, concept.Id))!.Factuur.Omschrijving);

        Assert.True((await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard)).IsGelukt);
    }

    [Fact]
    public async Task Het_betalingskenmerk_van_een_ingelezen_factuur_is_aan_te_passen()
    {
        var (wie, id) = await Administratie();
        var verkoop = app.Dienst<VerkoopDienst>();
        var gelezen = await app.Dienst<VerkoopimportDienst>().Lees(wie, id, Ubl("D-400", new(2026, 6, 2), "Nog Onbekend B.V.", "BE0123456789"), "d-400.xml", null);
        var concept = (await verkoop.Open(wie, id, gelezen.Id!.Value))!;
        Assert.Equal(Betalingskenmerk.Maak("D-400"), concept.Invoer.Betalingskenmerk);
        var klant = await Klant(wie, id, "Nog Onbekend B.V.", "BE0123456789");

        Assert.True((await verkoop.Bewaar(wie, id, concept.Id, concept.Invoer with { Relatie = klant, Betalingskenmerk = "PO-7781" })).IsGelukt);
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Id)).IsGelukt);

        Assert.Equal("PO-7781", (await verkoop.Open(wie, id, concept.Id))!.Factuur.Betalingskenmerk);
    }

    [Fact]
    public async Task Zonder_standaard_blijft_de_omschrijving_van_de_factuur()
    {
        var (wie, id) = await Administratie();
        await Klant(wie, id);

        var uitkomst = await app.Dienst<VerkoopimportDienst>().Lees(wie, id, Ubl("B-200", new(2026, 3, 9)), "b-200.xml", null);

        Assert.Equal("Brood en banket", (await app.Dienst<VerkoopDienst>().Open(wie, id, uitkomst.Id!.Value))!.Factuur.Omschrijving);
    }

    [PdfBrowserFact]
    public async Task Het_origineel_geeft_de_woorden_om_een_gebied_aan_te_wijzen()
    {
        var (wie, id) = await Administratie();
        var beeld = new Verkoopbeeld(new Verkoopfactuur("", new Partij("Onbekende Afnemer B.V.", new("Laan 2", "5678 CD", "Elders")), "PDF-2026-31",
            new(2026, 8, 3), new(2026, 8, 17), "Brood en banket", [new Verkoopregel("Brood en banket augustus", 1m, 1000m, "8010", "V9")]), Eigen, BtwCodes.Standaard);
        var pdf = await app.Dienst<PdfDienst>().Maak(FactuurHtml.Document(beeld, Factuurontwerp.Standaard, null));
        Assert.True(pdf.IsGelukt, string.Join("; ", pdf.Fouten));
        var import = app.Dienst<VerkoopimportDienst>();
        var gelezen = await import.Lees(wie, id, new MemoryStream(pdf.Waarde!), "factuur.pdf", "application/pdf");

        var origineel = (await import.Beeld(wie, id, gelezen.Id!.Value))!;

        Assert.NotNull(origineel.Pdf);
        Assert.Null(origineel.Afbeelding);
        // Het gebied rond het factuurnummer geeft precies dat nummer; de hele pagina bevat ook de klant.
        var nummer = origineel.Woorden.First(w => w.Tekst == "PDF-2026-31");
        Assert.Equal(["PDF-2026-31"], PdfTekst.InGebied(origineel.Woorden, nummer.Pagina, nummer.X0 - 1, nummer.Y0 - 1, nummer.X1 + 1, nummer.Y1 + 1));
        Assert.Contains("Afnemer", string.Join(" ", PdfTekst.InGebied(origineel.Woorden, 1, 0, 0, 600, 850)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_e_factuur_heeft_geen_beeld_en_zonder_toegang_geen_origineel()
    {
        var (wie, id) = await Administratie();
        await Klant(wie, id);
        var import = app.Dienst<VerkoopimportDienst>();
        var gelezen = await import.Lees(wie, id, Ubl("C-300", new(2026, 5, 1)), "c.xml", null);
        var buiten = await app.Gebruiker("Buiten Staander");

        Assert.Null(await import.Beeld(wie, id, gelezen.Id!.Value));
        await Assert.ThrowsAsync<GeenToegangException>(() => import.Beeld(buiten, id, gelezen.Id!.Value));
        Assert.Null(await import.Beeld(wie, await app.Administratie("Andere Zaak"), gelezen.Id.Value));
    }
}
