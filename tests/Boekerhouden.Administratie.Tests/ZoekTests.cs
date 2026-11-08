using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Het zoekvenster (Ctrl+K): pagina's, relaties, rekeningen, boekingen (boekstuk, omschrijving, factuurnummer of
/// bedrag), vaste activa en administraties; alleen in administraties waarvan de gebruiker lid is.
/// </summary>
public sealed class ZoekTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Zoekwinkel Voorbeeld B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 3, 10), "Aanschaf laptop",
            [new Journaalregel("0200", 1234.56m, "Laptop"), new Journaalregel("1800", -1234.56m, Factuurnummer: "LT-778")]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0002", new(2026, 4, 2), "Huur april",
            [new Journaalregel("4100", 950m), new Journaalregel("1800", -950m)]))).IsGelukt);
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden))
        {
            db.Relaties.Add(new Handelsrelatie { Id = Guid.NewGuid(), AdministratieId = id, Code = "KANTJ", Naam = "Kantoorhandel Jansen", Plaats = "Zwolle" });
            await db.SaveChangesAsync();
        }

        var activa = app.Dienst<ActivaDienst>();
        var voorstel = (await activa.Kandidaten(wie, id)).Single(k => k.Rekening == "0200").Voorstel;
        Assert.True((await activa.Bewaar(wie, id, null, voorstel with { Omschrijving = "Laptop Zoekwinkel" })).IsGelukt);
        return (wie, id);
    }

    private Task<IReadOnlyList<Zoekresultaat>> Zoek(Uitvoerder wie, string tekst, Guid? id) => app.Dienst<ZoekDienst>().Zoek(wie, tekst, id);

    [Fact]
    public async Task Vindt_relaties_rekeningen_boekingen_activa_en_paginas()
    {
        var (wie, id) = await Administratie();

        var jansen = Assert.Single(await Zoek(wie, "jansen", id), r => r.Soort == Zoeksoort.Relatie);
        Assert.Equal($"/a/{id}/relaties/KANTJ", jansen.Url);
        Assert.Equal("KANTJ · Zwolle", jansen.Detail);

        var rekening = Assert.Single(await Zoek(wie, "1100", id), r => r.Soort == Zoeksoort.Rekening);
        Assert.Equal($"/a/{id}/grootboek?rekening=1100", rekening.Url);

        // Op boekstuk, omschrijving, factuurnummer en bedrag (ook als 1.234,56 of negatief).
        Assert.Contains(await Zoek(wie, "2026-0002", id), r => r.Soort == Zoeksoort.Boeking && r.Url == $"/a/{id}/boeking/MEM/2026-0002");
        Assert.Contains(await Zoek(wie, "huur", id), r => r.Soort == Zoeksoort.Boeking && r.Titel == "MEM 2026-0002 Huur april");
        Assert.Contains(await Zoek(wie, "lt-778", id), r => r.Soort == Zoeksoort.Boeking && r.Url.EndsWith("/2026-0001", StringComparison.Ordinal));
        foreach (var bedrag in new[] { "1.234,56", "1234,56", "-1234,56" })
        {
            var boeking = Assert.Single(await Zoek(wie, bedrag, id), r => r.Soort == Zoeksoort.Boeking);
            Assert.Equal("10-03-2026 · 1.234,56", boeking.Detail);
        }

        Assert.DoesNotContain(await Zoek(wie, "1234,57", id), r => r.Soort == Zoeksoort.Boeking);

        Assert.Contains(await Zoek(wie, "zoekwinkel", id), r => r.Soort == Zoeksoort.Activum && r.Titel == "Laptop Zoekwinkel");
        Assert.Contains(await Zoek(wie, "zoekwinkel", id), r => r.Soort == Zoeksoort.Administratie && r.Url == $"/a/{id}");

        var pagina = Assert.Single(await Zoek(wie, "kolommen", id), r => r.Soort == Zoeksoort.Pagina);
        Assert.Equal($"/a/{id}/kolommenbalans", pagina.Url);
        Assert.Equal(3, (await Zoek(wie, "dagboeken", id)).Count(r => r.Soort == Zoeksoort.Pagina && r.Detail == "Dagboeken"));

        // De volgorde van de groepen: pagina's eerst, administraties laatst.
        var alles = await Zoek(wie, "laptop", id);
        Assert.Equal([.. alles.OrderBy(r => r.Soort)], alles);
        Assert.Empty(await Zoek(wie, "l", id));
    }

    [Fact]
    public async Task Zonder_lidmaatschap_vindt_niemand_iets_in_de_administratie()
    {
        var (_, id) = await Administratie();
        var buiten = await app.Gebruiker("Bert Buiten");

        Assert.Empty(await Zoek(buiten, "jansen", id));
        Assert.Empty(await Zoek(buiten, "1.234,56", id));
        Assert.Empty(await Zoek(buiten, "kolommen", id));
        Assert.Empty(await Zoek(buiten, "zoekwinkel", id));
    }

    [Fact]
    public async Task Een_lid_dat_alleen_inziet_ziet_geen_paginas_die_het_kantoor_vraagt()
    {
        var (wie, id) = await Administratie();
        var inzien = await app.Gebruiker("Ina Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        Assert.DoesNotContain(await Zoek(inzien, "memoriaal", id), r => r.Soort == Zoeksoort.Pagina);
        Assert.Contains(await Zoek(wie, "memoriaal", id), r => r.Soort == Zoeksoort.Pagina);
        Assert.Contains(await Zoek(inzien, "jansen", id), r => r.Soort == Zoeksoort.Relatie);
        Assert.Contains(await Zoek(inzien, "zoekwinkel", id), r => r.Soort == Zoeksoort.Administratie);
    }
}
