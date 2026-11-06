using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Einde van de klantrelatie: archiveren (alleen nog inzien), uit het archief halen, en verwijderen door de beheerder
/// (eerst archiveren, naam typen, bewaarplicht bevestigen). Boekingen blijven verder onverwijderbaar.
/// </summary>
public sealed class AdministratieArchiefTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static Journaalpost Memoriaal(string boekstuk) =>
        new("MEM", boekstuk, new(2026, 3, 1), "Beginbalans", [new Journaalregel("1100", 1000m), new Journaalregel("0500", -1000m)]);

    /// <summary>Een administratie met van alles erin: een memoriaalboeking, een bankafschrift, een inkoop- en een verkoopfactuur.</summary>
    private async Task<(Uitvoerder Wie, Guid Id)> Gevuld(string naam)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie(naam);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, Memoriaal("2026-0001"))).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@voorbeeld.test"),
        })).IsGelukt);
        var klant = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Klant B.V.", null, null, null, new Klantgegevens(true, "Laan 2", "5678 CD", "Elders")), null);
        var code = (await inrichting.Relaties(wie, id)).Single(r => r.Id == klant.Waarde).Code;
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(code, new(2026, 3, 1), new(2026, 3, 31), "Werk",
            [new Boekhouding.Verkoop.Verkoopregel("Advies", 1m, 100m, "8000", "V21")]));
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Waarde)).IsGelukt);
        return (wie, id);
    }

    private async Task<int> Aantal<T>(Guid id)
        where T : class, IVanAdministratie
    {
        await using var db = await app.Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        return await db.Set<T>().IgnoreQueryFilters().CountAsync(e => e.AdministratieId == id);
    }

    [Fact]
    public async Task Gearchiveerd_is_alleen_inzien_en_niet_in_de_gewone_lijst()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Archief Inzien B.V.");
        var dienst = app.Dienst<AdministratieDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0001"))).IsGelukt);

        Assert.True((await dienst.Archiveer(wie, id)).IsGelukt);

        Assert.DoesNotContain(await dienst.Lijst(wie), a => a.Id == id);
        var kop = Assert.Single(await dienst.Lijst(wie, ookGearchiveerd: true), a => a.Id == id);
        Assert.NotNull(kop.Gearchiveerd);
        Assert.False(kop.Mag(Recht.Invoeren));
        Assert.True(kop.Mag(Recht.Lezen));
        Assert.False((await grootboek.Boek(wie, id, Memoriaal("2026-0002"))).IsGelukt);
        var fout = await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<Toegang>().Open(wie, id, Recht.Invoeren));
        Assert.Contains("gearchiveerd", fout.Message, StringComparison.Ordinal);
        Assert.Single((await grootboek.Posten(wie, id)));
        Assert.False((await dienst.Archiveer(wie, id)).IsGelukt);

        Assert.True((await dienst.Herstel(wie, id)).IsGelukt);

        Assert.True((await grootboek.Boek(wie, id, Memoriaal("2026-0002"))).IsGelukt);
        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.AdministratieGearchiveerd && r.Onderwerp == "Archief Inzien B.V.");
        Assert.Contains(log, r => r.Actie == AuditActies.AdministratieHersteld);
    }

    [Fact]
    public async Task Alleen_het_kantoor_archiveert_en_alleen_de_beheerder_verwijdert()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Archief Rechten B.V.");
        var dienst = app.Dienst<AdministratieDienst>();
        var ondernemer = await app.Gebruiker("Olga Ondernemer");
        await dienst.ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var medewerker = await app.Gebruiker("Karel Kantoor");
        await dienst.ZetLid(id, medewerker.Id!.Value, Lidmaatschapsrol.Kantoor, wie);
        var buiten = await app.Gebruiker("Bo Buiten");

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Archiveer(ondernemer, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Archiveer(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(medewerker, id, "Archief Rechten B.V.", true));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(buiten, id, "Archief Rechten B.V.", true));
        Assert.True((await dienst.Archiveer(medewerker, id)).IsGelukt);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Herstel(ondernemer, id));
        Assert.NotNull(await dienst.Open(wie, id));
    }

    [Fact]
    public async Task Verwijderen_eerst_archiveren_naam_typen_en_bewaarplicht_bevestigen_dan_is_alles_weg_behalve_het_log()
    {
        var (wie, id) = await Gevuld("Weg Ermee B.V.");
        var (_, ander) = await Gevuld("Blijft Staan B.V.");
        var dienst = app.Dienst<AdministratieDienst>();
        Assert.True(await Aantal<Boeking>(id) > 1);
        Assert.True(await Aantal<Banktransactie>(id) > 0);
        Assert.True(await Aantal<Document>(id) > 0);

        Assert.Contains("eerst naar het archief", (await dienst.Verwijder(wie, id, "Weg Ermee B.V.", true)).Fouten[0], StringComparison.Ordinal);
        Assert.True((await dienst.Archiveer(wie, id)).IsGelukt);
        Assert.Contains("precies over", (await dienst.Verwijder(wie, id, "Weg ermee", true)).Fouten[0], StringComparison.Ordinal);
        var voorwaarden = await dienst.Voorwaarden(wie, id);
        Assert.Equal(new DateOnly(2033, 12, 31), voorwaarden.Bewaarplicht);
        Assert.Contains("bewaarplicht loopt nog tot 31-12-2033", (await dienst.Verwijder(wie, id, "Weg Ermee B.V.", false)).Fouten[0], StringComparison.Ordinal);

        Assert.True((await dienst.Verwijder(wie, id, "Weg Ermee B.V.", true)).IsGelukt);

        Assert.Null(await dienst.Open(wie, id));
        Assert.DoesNotContain(await dienst.Lijst(wie, ookGearchiveerd: true), a => a.Id == id);
        Assert.Equal((0, 0, 0, 0, 0), (await Aantal<Boeking>(id), await Aantal<Boekingsregel>(id), await Aantal<Banktransactie>(id), await Aantal<Document>(id), await Aantal<Verkoopdocument>(id)));
        Assert.True(await Aantal<Boeking>(ander) > 1);
        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.AdministratieVerwijderd && r.Onderwerp == "Weg Ermee B.V.");
        Assert.Contains(log, r => r.Actie == AuditActies.AdministratieAangemaakt && r.Onderwerp == "Weg Ermee B.V.");

        // Buiten de app om blijven de boekingen van een bestaande administratie onverwijderbaar.
        await Assert.ThrowsAnyAsync<Exception>(() => app.VoerSqlUit($"DELETE FROM \"Boekingsregels\" WHERE \"AdministratieId\" = '{(SqliteAan ? ander.ToString().ToUpperInvariant() : ander.ToString())}'"));
        Assert.True(await Aantal<Boekingsregel>(ander) > 1);
    }

    [Fact]
    public async Task Een_lege_administratie_kan_meteen_weg()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Leeg B.V.");
        var dienst = app.Dienst<AdministratieDienst>();

        var voorwaarden = await dienst.Voorwaarden(wie, id);

        Assert.Equal((0, (DateOnly?)null, false), (voorwaarden.Boekingen, voorwaarden.Bewaarplicht, voorwaarden.Gearchiveerd));
        Assert.True((await dienst.Verwijder(wie, id, "Leeg B.V.", false)).IsGelukt);
        Assert.Null(await dienst.Open(wie, id));
    }

    private static bool SqliteAan => Environment.GetEnvironmentVariable("BOEKERHOUDEN_TEST_SQLITE") is { Length: > 0 };
}
