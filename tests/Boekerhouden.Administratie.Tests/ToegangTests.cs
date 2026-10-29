using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Scheiding van administraties (beveiliging §4.2): wie geen lid is, ziet niets en kan niets, ook niet als hij het
/// ID kent. Deze negatieve tests horen bij elke dienst.
/// </summary>
public sealed class ToegangTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Journaalpost Begin = new("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new("1100", 1000m), new("0500", -1000m)]);

    [Fact]
    public async Task Nieuwe_administratie_heeft_het_standaardschema_en_de_maker_is_kantoor()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie("Schilder Voorbeeld", Ondernemingsvorm.Eenmanszaak);

        var kop = await app.Dienst<AdministratieDienst>().Open(beheerder, id);
        var schema = await app.Dienst<GrootboekDienst>().Schema(beheerder, id);

        Assert.Equal(Lidmaatschapsrol.Kantoor, kop?.Rol);
        Assert.Equal("BEivKapOnd", schema.Enige(Rekeningfunctie.OnverdeeldResultaat).RgsCode);
        Assert.Equal(5, (await app.Dienst<GrootboekDienst>().Dagboeken(beheerder, id)).Count);
        Assert.Contains(await app.Dienst<AdministratieDienst>().Lijst(beheerder), a => a.Id == id);
    }

    [Fact]
    public async Task Geen_lid_ziet_de_administratie_niet_en_kan_er_niets_mee()
    {
        var id = await app.Administratie();
        var buitenstaander = await app.Gebruiker("Bart Buiten");
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.Null(await app.Dienst<AdministratieDienst>().Open(buitenstaander, id));
        Assert.DoesNotContain(await app.Dienst<AdministratieDienst>().Lijst(buitenstaander), a => a.Id == id);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Schema(buitenstaander, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Proefbalans(buitenstaander, id, 2026));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Kaart(buitenstaander, id, "1100", new(2026, 1, 1), new(2026, 12, 31)));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Posten(buitenstaander, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.Boek(buitenstaander, id, Begin));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.VoegRekeningToe(buitenstaander, id, new("4999", "Iets", Rekeningsoort.Resultaat, null)));
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.ControleerKeten(buitenstaander, id));
    }

    [Fact]
    public async Task Lid_van_de_ene_administratie_ziet_niets_van_de_andere()
    {
        var beheerder = await app.Beheerder();
        var eigen = await app.Administratie("Eigen B.V.");
        var ander = await app.Administratie("Ander B.V.");
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(eigen, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, beheerder);
        await app.Dienst<GrootboekDienst>().Boek(beheerder, ander, Begin);

        Assert.NotNull(await app.Dienst<AdministratieDienst>().Open(ondernemer, eigen));
        Assert.Null(await app.Dienst<AdministratieDienst>().Open(ondernemer, ander));
        Assert.Equal(["Eigen B.V."], (await app.Dienst<AdministratieDienst>().Lijst(ondernemer)).Select(a => a.Naam));
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<GrootboekDienst>().Posten(ondernemer, ander));
    }

    [Fact]
    public async Task Database_laat_alleen_de_geopende_administratie_zien_en_opslaan()
    {
        var beheerder = await app.Beheerder();
        var a = await app.Administratie("A B.V.");
        var b = await app.Administratie("B B.V.");
        var toegang = app.Dienst<Toegang>();

        await using (var db = await toegang.Open(beheerder, a, Recht.Lezen))
        {
            Assert.All(await db.Rekeningen.ToListAsync(), r => Assert.Equal(a, r.AdministratieId));
            Assert.Empty(await db.Rekeningen.Where(r => r.AdministratieId == b).ToListAsync());

            db.Rekeningen.Add(new Grootboekrekening { Id = Guid.NewGuid(), AdministratieId = b, Nummer = "9999", Naam = "Ingeschoven" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using var zonder = await app.Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        Assert.Empty(await zonder.Rekeningen.ToListAsync());
    }

    [Fact]
    public async Task Rechten_per_rol_inzien_ondernemer_kantoor()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var inzien = await app.Gebruiker("Ilse Inzien");
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        var beheer = app.Dienst<AdministratieDienst>();
        await beheer.ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, beheerder);
        await beheer.ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, beheerder);
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.NotNull(await grootboek.Proefbalans(inzien, id, 2026));
        Assert.False((await grootboek.Boek(inzien, id, Begin)).IsGelukt);
        Assert.Equal("Je hebt in deze administratie niet het recht om hier te boeken.", (await grootboek.Boek(ondernemer, id, Begin)).Fouten[0]);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.VoegRekeningToe(ondernemer, id, new("4999", "Iets", Rekeningsoort.Resultaat, null)));
        Assert.True((await grootboek.Boek(beheerder, id, Begin)).IsGelukt);
    }

    [Fact]
    public async Task Gedeactiveerde_gebruiker_verliest_de_toegang()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var lid = await app.Gebruiker("Dirk Deactief");
        await app.Dienst<AdministratieDienst>().ZetLid(id, lid.Id!.Value, Lidmaatschapsrol.Kantoor, beheerder);
        await app.VoerSqlUit($"UPDATE \"AspNetUsers\" SET \"Actief\" = false WHERE \"Id\" = '{AdministratieFixture.SqlId(lid.Id.Value)}'");

        Assert.Null(await app.Dienst<AdministratieDienst>().Open(lid, id));
    }
}
