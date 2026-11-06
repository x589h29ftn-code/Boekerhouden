using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een boekjaar als XAF-auditfile maken en een auditfile inlezen als nieuwe administratie: na heen en terug hebben de
/// balansrekeningen dezelfde saldi, de W&amp;V van het jaar is gelijk, de openstaande posten sluiten aan.
/// </summary>
public sealed class AuditfileDienstTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>
    /// 2025: kapitaal en een omzet (het resultaat van 2025 komt in de beginbalans van 2026 op de overige reserves);
    /// 2026: het bankafschrift, de inkoopfactuur van Belnet (open) en een verkoopfactuur (open).
    /// </summary>
    private async Task<(Uitvoerder Wie, Guid Id)> Gevuld()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Auditfile Bron B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0001", new(2025, 1, 1), "Storting kapitaal",
            [new Journaalregel("1100", 5000m), new Journaalregel("0500", -5000m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0002", new(2025, 6, 1), "Omzet 2025",
            [new Journaalregel("1100", 800m), new Journaalregel("8000", -800m)]))).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        var document = gelezen.DocumentId!.Value;
        var herkend = (await app.Dienst<InkoopDienst>().Verwerking(wie, id, document))!.Herkend!;
        Assert.True((await app.Dienst<InkoopDienst>().Boek(wie, id, document, new Factuurinvoer(null, new NieuweRelatie("Belnet B.V.", herkend.Iban, null, null),
            "BN-2026-0411", new(2026, 4, 1), new(2026, 4, 15), "Telefoon april", [new Boekhouding.Facturen.Factuurregel("Telefoon", "4310", "I21", 20.65m, 4.34m)]))).IsGelukt);
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
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(code, new(2026, 5, 1), new(2026, 5, 31), "Advies mei",
            [new Boekhouding.Verkoop.Verkoopregel("Advies", 4m, 100m, "8000", "V21")]));
        Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Waarde)).IsGelukt);
        return (wie, id);
    }

    private async Task<Dictionary<string, decimal>> Saldi(Guid id, DateOnly van, DateOnly tot)
    {
        await using var db = await app.Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        db.Geopend = id;
        return (await (from r in db.GeldendeRegels join b in db.Boekingen on r.BoekingId equals b.Id where b.Datum >= van && b.Datum <= tot select new { r.Rekeningnummer, r.Bedrag }).ToListAsync())
            .GroupBy(r => r.Rekeningnummer).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag));
    }

    [Fact]
    public async Task Maken_en_weer_inlezen_geeft_dezelfde_saldi_en_openstaande_posten()
    {
        var (wie, id) = await Gevuld();
        var dienst = app.Dienst<AuditfileDienst>();

        var bestand = await dienst.Maak(wie, id, 2026);

        Assert.Equal("Auditfile Auditfile Bron B.V. 2026.xaf", bestand.Bestandsnaam);
        Assert.Contains("<auditfile xmlns=\"http://www.auditfiles.nl/XAF/3.2\">", Encoding.UTF8.GetString(bestand.Inhoud), StringComparison.Ordinal);
        var ingelezen = await dienst.LeesIn(wie, new MemoryStream(bestand.Inhoud), bestand.Bestandsnaam, Ondernemingsvorm.BV);
        Assert.True(ingelezen.IsGelukt, string.Join("; ", ingelezen.Fouten));
        var nieuw = ingelezen.Waarde!;
        Assert.Equal("Auditfile Bron B.V.", nieuw.Naam);
        Assert.Equal(bestand.Boekingen + 1, nieuw.Boekingen);

        // Balans eind 2026: gelijk (het resultaat 2025 staat nu op de overige reserves); W&V 2026: gelijk.
        var bron = await Saldi(id, DateOnly.MinValue, new(2026, 12, 31));
        var doel = await Saldi(nieuw.Administratie, DateOnly.MinValue, new(2026, 12, 31));
        var schema = await app.Dienst<GrootboekDienst>().Schema(wie, id);
        foreach (var r in schema.Alle.Where(r => r.IsBalans && r.Functie != Rekeningfunctie.OnverdeeldResultaat))
        {
            Assert.True(bron.GetValueOrDefault(r.Nummer) == doel.GetValueOrDefault(r.Nummer), $"{r.Nummer}: {bron.GetValueOrDefault(r.Nummer)} <> {doel.GetValueOrDefault(r.Nummer)}");
        }

        Assert.Equal(-800m, doel["0510"]);
        var wv = await Saldi(id, new(2026, 1, 1), new(2026, 12, 31));
        var wvNieuw = await Saldi(nieuw.Administratie, new(2026, 1, 1), new(2026, 12, 31));
        Assert.All(schema.Alle.Where(r => !r.IsBalans), r => Assert.Equal(wv.GetValueOrDefault(r.Nummer), wvNieuw.GetValueOrDefault(r.Nummer)));

        // Rekeningschema met RGS en functies, de relaties, en de openstaande posten die aansluiten.
        var nieuwSchema = await app.Dienst<GrootboekDienst>().Schema(wie, nieuw.Administratie);
        Assert.Equal((Rekeningfunctie.Bank, Rekeningfunctie.Crediteuren, Rekeningfunctie.Debiteuren, "BLimBanRba"),
            (nieuwSchema.Zoek("1100")!.Functie, nieuwSchema.Zoek("1600")!.Functie, nieuwSchema.Zoek("1300")!.Functie, nieuwSchema.Zoek("1100")!.RgsCode));
        Assert.Contains(await app.Dienst<InrichtingDienst>().Relaties(wie, nieuw.Administratie), r => r.Naam == "Belnet B.V.");
        Assert.Equal(2, nieuw.OpenstaandePosten);
        var aansluitingen = await app.Dienst<AansluitingDienst>().Aansluitingen(wie, nieuw.Administratie);
        Assert.All(aansluitingen.Where(a => a.Soort is Aansluitingsoort.Crediteuren or Aansluitingsoort.Debiteuren), a => Assert.True(a.Sluit, $"{a.Onderwerp}: {a.Verschil}"));
        Assert.True(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, nieuw.Administratie) is { Count: 0 });

        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.AuditfileGemaakt);
        Assert.Contains(log, r => r.Actie == AuditActies.AuditfileIngelezen && r.Onderwerp == "Auditfile Bron B.V.");
    }

    [Fact]
    public async Task Een_auditfile_die_niet_klopt_leest_niets_in()
    {
        var wie = await app.Beheerder();
        var dienst = app.Dienst<AuditfileDienst>();
        var voor = (await app.Dienst<AdministratieDienst>().Lijst(wie)).Count;
        const string Fout = """
            <auditfile xmlns="http://www.auditfiles.nl/XAF/3.2">
             <header><fiscalYear>2026</fiscalYear><startDate>2026-01-01</startDate><endDate>2026-12-31</endDate></header>
             <company><companyName>Scheef BV</companyName>
              <generalLedger><ledgerAccount><accID>1100</accID><accDesc>Bank</accDesc><accTp>B</accTp></ledgerAccount></generalLedger>
              <transactions><journal><jrnID>MEM</jrnID><desc>Memoriaal</desc><jrnTp>Z</jrnTp>
               <transaction><nr>1</nr><desc>Scheef</desc><trDt>2026-02-01</trDt>
                <trLine><accID>1100</accID><amnt>100.00</amnt><amntTp>D</amntTp></trLine>
                <trLine><accID>9999</accID><amnt>90.00</amnt><amntTp>C</amntTp></trLine>
               </transaction></journal></transactions>
             </company>
            </auditfile>
            """;

        var uitkomst = await dienst.LeesIn(wie, new MemoryStream(Encoding.UTF8.GetBytes(Fout)), "scheef.xaf", Ondernemingsvorm.BV);
        var geenXml = await dienst.LeesIn(wie, new MemoryStream("geen xml"u8.ToArray()), "kapot.xaf", Ondernemingsvorm.BV);

        Assert.False(uitkomst.IsGelukt);
        Assert.Contains(uitkomst.Fouten, f => f.Contains("Rekening 9999 staat niet in het rekeningschema", StringComparison.Ordinal));
        Assert.Contains(uitkomst.Fouten, f => f.Contains("niet in evenwicht", StringComparison.Ordinal));
        Assert.StartsWith("kapot.xaf: Geen geldige XML", geenXml.Fouten[0], StringComparison.Ordinal);
        Assert.Equal(voor, (await app.Dienst<AdministratieDienst>().Lijst(wie)).Count);
    }

    [Fact]
    public async Task Alleen_leden_maken_een_auditfile()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Auditfile Rechten B.V.");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var buiten = await app.Gebruiker("Bo Buiten");
        var dienst = app.Dienst<AuditfileDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Maak(buiten, id, 2026));
        Assert.NotEmpty((await dienst.Maak(inzien, id, 2026)).Inhoud);
    }
}
