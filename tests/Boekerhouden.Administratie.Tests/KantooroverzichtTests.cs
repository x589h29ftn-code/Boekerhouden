using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Het kantooroverzicht op de startpagina: per administratie wat er te doen is (inkoop, bank, verkoopconcepten,
/// vervallen debiteuren) en tot wanneer bank en perioden bij zijn; gelijk aan wat de schermen zelf tonen, en alleen
/// voor administraties waarvan de gebruiker lid is.
/// </summary>
public sealed class KantooroverzichtTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<Guid> MetWerk(Uitvoerder wie)
    {
        var id = await app.Administratie("Overzicht Werk B.V.");
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet("K-1")), "k1.xml", null);
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet("K-2")), "k2.xml", null);

        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@werk.test"),
        })).IsGelukt);
        var relatie = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Klant Laat B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", null, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), null);
        var klant = (await inrichting.Relaties(wie, id)).Single(r => r.Id == relatie.Waarde).Code;

        var verkoop = app.Dienst<VerkoopDienst>();
        var datum = new DateOnly(2026, 1, 5);
        var oud = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, datum, datum.AddDays(14), "Advies", [new Verkoopregel("Advies", 10m, 100m, "8000", "V21")]));
        Assert.True((await verkoop.MaakDefinitief(wie, id, oud.Waarde)).IsGelukt);
        Assert.True((await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, datum, datum.AddDays(14), "Concept", [new Verkoopregel("Advies", 1m, 50m, "8000", "V21")]))).IsGelukt);
        Assert.True((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new DateOnly(2026, 2, 10))).IsGelukt);
        return id;
    }

    [Fact]
    public async Task Telt_hetzelfde_als_de_schermen_van_de_administratie()
    {
        var wie = await app.Beheerder();
        var id = await MetWerk(wie);
        var leeg = await app.Administratie("Overzicht Leeg B.V.");

        var standen = await app.Dienst<KantooroverzichtDienst>().Stand(wie);
        var stand = standen[id];

        Assert.Equal((await app.Dienst<InkoopDienst>().Lijst(wie, id, Documentstatus.TeVerwerken)).Count, stand.Inkoop);
        Assert.Equal(2, stand.Inkoop);
        var mutaties = (await app.Dienst<BankDienst>().Lijst(wie, id)).Where(m => m.Status == Banktransactiestatus.TeVerwerken).ToList();
        Assert.Equal(mutaties.Count, stand.Bank);
        Assert.True(stand.Bank > 0);
        Assert.Equal(mutaties.Min(m => m.Datum), stand.OudsteBank);
        Assert.Equal(Assert.Single(await app.Dienst<BankDienst>().Stand(wie, id)).Tot, stand.AfschriftTot);
        Assert.Equal(1, stand.Concepten);
        Assert.Equal(1, stand.Vervallen);
        Assert.Equal(1210m, stand.VervallenBedrag);
        Assert.Equal(new DateOnly(2026, 2, 28), stand.AfgeslotenTot);
        Assert.False(stand.Leeg);

        Assert.True(standen[leeg].Leeg);
        Assert.Null(standen[leeg].AfschriftTot);
    }

    [Fact]
    public async Task Alleen_administraties_waarvan_je_lid_bent_en_niet_gearchiveerd()
    {
        var wie = await app.Beheerder();
        var id = await MetWerk(wie);
        var buiten = await app.Gebruiker("Bert Buiten");
        Assert.Empty(await app.Dienst<KantooroverzichtDienst>().Stand(buiten));

        var lid = await app.Gebruiker("Lia Lid");
        await app.Dienst<AdministratieDienst>().ZetLid(id, lid.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        Assert.Equal([id], (await app.Dienst<KantooroverzichtDienst>().Stand(lid)).Keys);

        Assert.True((await app.Dienst<AdministratieDienst>().Archiveer(wie, id)).IsGelukt);
        Assert.Empty(await app.Dienst<KantooroverzichtDienst>().Stand(lid));
        Assert.False((await app.Dienst<KantooroverzichtDienst>().Stand(wie)).ContainsKey(id));
    }
}
