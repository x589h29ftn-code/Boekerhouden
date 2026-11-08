using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Acties op meerdere regels tegelijk (concepten definitief maken of verwijderen, documenten verwijderen): regel voor
/// regel via de gewone dienst; een mislukte regel houdt de rest niet tegen, en zonder recht lukt er niets.
/// </summary>
public sealed class BulkTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id, string Klant)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Bulk Voorbeeld B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@bulk.test"),
        })).IsGelukt);
        var relatie = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Klant Bulk B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", null, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), null);
        return (wie, id, (await inrichting.Relaties(wie, id)).Single(r => r.Id == relatie.Waarde).Code);
    }

    private async Task<Guid> Concept(Uitvoerder wie, Guid id, string klant, string omschrijving, decimal prijs = 100m)
    {
        var datum = new DateOnly(2026, 3, 1);
        var uitkomst = await app.Dienst<VerkoopDienst>().Bewaar(wie, id, null,
            new Verkoopinvoer(klant, datum, datum.AddDays(14), omschrijving, [new Verkoopregel("Advies", 1m, prijs, "8000", "V21")]));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return uitkomst.Waarde;
    }

    [Fact]
    public async Task Concepten_tegelijk_definitief_met_doorlopende_nummers_en_een_fout_houdt_de_rest_niet_tegen()
    {
        var (wie, id, klant) = await Administratie();
        var verkoop = app.Dienst<VerkoopDienst>();
        var eerste = await Concept(wie, id, klant, "Maart");
        var tweede = await Concept(wie, id, klant, "April");
        var weg = Guid.NewGuid();

        var uitkomst = await Bulk.Voer([eerste, weg, tweede], async v => await verkoop.MaakDefinitief(wie, id, v), v => v == weg ? "onbekend" : "concept");

        Assert.Equal(2, uitkomst.Gelukt);
        Assert.StartsWith("onbekend: ", Assert.Single(uitkomst.Fouten), StringComparison.Ordinal);
        var lijst = await verkoop.Lijst(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(["20260001", "20260002"], lijst.Where(r => r.Status == Verkoopstatus.Definitief).Select(r => r.Nummer).Order(StringComparer.Ordinal));
        Assert.Equal(2, (await app.Dienst<AuditDienst>().Recent()).Count(r => r.Actie == AuditActies.VerkoopfactuurDefinitief && r.AdministratieId == id));
    }

    [Fact]
    public async Task Zonder_recht_lukt_niets_en_blijven_de_concepten_staan()
    {
        var (wie, id, klant) = await Administratie();
        var verkoop = app.Dienst<VerkoopDienst>();
        var concepten = new[] { await Concept(wie, id, klant, "Mei"), await Concept(wie, id, klant, "Juni") };
        var inzien = await app.Gebruiker("Ina Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        var uitkomst = await Bulk.Voer(concepten, v => verkoop.VerwijderConcept(inzien, id, v), _ => "concept");
        Assert.Equal(0, uitkomst.Gelukt);
        Assert.Equal(2, uitkomst.Fouten.Count);

        var verwijderd = await Bulk.Voer(concepten, v => verkoop.VerwijderConcept(wie, id, v), _ => "concept");
        Assert.Equal(2, verwijderd.Gelukt);
        Assert.Empty(await verkoop.Lijst(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Documenten_tegelijk_verwijderen_uit_te_verwerken()
    {
        var (wie, id, _) = await Administratie();
        var inkoop = app.Dienst<InkoopDienst>();
        var a = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("B-1")), "b1.xml", null);
        var b = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("B-2")), "b2.xml", null);

        var uitkomst = await Bulk.Voer([a.DocumentId!.Value, b.DocumentId!.Value], d => inkoop.Verwijder(wie, id, d), _ => "document");

        Assert.Equal(2, uitkomst.Gelukt);
        Assert.Empty(await inkoop.Lijst(wie, id, Documentstatus.TeVerwerken));
    }
}
