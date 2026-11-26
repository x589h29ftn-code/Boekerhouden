using Boekerhouden.Administratie.Components.Gedeeld;
using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Overal slepen: Sifer herkent wat een bestand is (afschrift, loonjournaal, verkoop- of inkoopfactuur) en leest het in
/// de juiste werklijst in; alleen voor wie mag invoeren.
/// </summary>
public sealed class InleesDienstTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Eigengegevens Eigen = new("Voorbeeld Handel B.V.", "87654321", "NL123456782B01", ["NL20INGB0001234567"]);

    private static byte[] Fictief(string naam) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", naam));

    [Theory]
    [InlineData("ing-2026-03.mt940", Inleessoort.Bank)]
    [InlineData("abn-2026-03.sta", Inleessoort.Bank)]
    [InlineData("rabo-2026-03.swi", Inleessoort.Bank)]
    [InlineData("camt053-2026-04.xml", Inleessoort.Bank)]
    [InlineData("belnet-2026-0411.xml", Inleessoort.Inkoop)]
    [InlineData("verkoop-ander-pakket.xml", Inleessoort.Verkoop)]
    [InlineData("foto-bon-bouwmarkt.jpg", Inleessoort.Inkoop)]
    [InlineData("scan-kantoorvakhandel.png", Inleessoort.Inkoop)]
    public void Herkent_de_testbestanden(string bestand, Inleessoort soort) =>
        Assert.Equal(soort, InleesDienst.Herken(bestand, Fictief(bestand), Eigen, _ => []));

    [Fact]
    public void Een_pdf_op_zijn_tekst()
    {
        Assert.Equal(Inleessoort.Bank, InleesDienst.HerkenTekst("Rekeningafschrift 12 NL20 INGB 0001 2345 67 Vorig saldo 1.000,00 Nieuw saldo 1.250,00", Eigen));
        Assert.Equal(Inleessoort.Loonjournaal, InleesDienst.HerkenTekst("Loonjournaal periode 2026-3 Werkgever Voorbeeld Handel B.V. Bruto loon", Eigen));
        Assert.Equal(Inleessoort.Verkoop, InleesDienst.HerkenTekst("Factuur 2026-014 Graag betalen op NL20 INGB 0001 2345 67 t.n.v. Voorbeeld Handel", Eigen));
        Assert.Equal(Inleessoort.Verkoop, InleesDienst.HerkenTekst("Factuur 2026-015 KvK 87654321 Btw NL123456782B01", Eigen));
        Assert.Equal(Inleessoort.Inkoop, InleesDienst.HerkenTekst("Factuur BN-2026-0411 Belnet B.V. IBAN NL63 TRIO 0212 3456 78 aan Voorbeeld Handel B.V.", Eigen));

        // Een inkoopfactuur die via incasso wordt betaald, noemt het eigen IBAN als de rekening waarvan wordt afgeschreven:
        // dat is geen verkoopfactuur (de tankpasfactuur kwam anders bij Verkoop terecht en leek daarna dubbel).
        Assert.Equal(Inleessoort.Inkoop, InleesDienst.HerkenTekst("Factuur 7731 Tankstation De Brandstof Totaalbedrag 454,54 Het factuurbedrag zal worden afgeschreven van rekeningnummer NL20 INGB 0001 2345 67", Eigen));
        Assert.Equal(Inleessoort.Inkoop, InleesDienst.HerkenTekst("Factuur 7732 Wij incasseren het bedrag via automatische incasso van NL20INGB0001234567", Eigen));
        Assert.Equal(Inleessoort.Verkoop, InleesDienst.HerkenTekst("Factuur 2026-016 Graag betalen op NL20 INGB 0001 2345 67. Bij machtiging wordt niets afgeschreven.", Eigen));

        // Een PDF die niet geopend mag worden (geen schone virusscan) gaat naar inkoop: die zet hem in quarantaine.
        Assert.Equal(Inleessoort.Inkoop, InleesDienst.Herken("x.pdf", "%PDF-1.7 ..."u8.ToArray(), Eigen, null));
    }

    [Fact]
    public void Samenvatting_en_bestemming()
    {
        IReadOnlyList<Overalingelezen> uitkomsten =
        [
            new("a.pdf", Inleessoort.Inkoop, true, ""), new("b.pdf", Inleessoort.Inkoop, true, ""),
            new("c.mt940", Inleessoort.Bank, true, ""), new("d.pdf", Inleessoort.Inkoop, false, "te groot"),
        ];
        Assert.Equal("2 inkoopfacturen naar inkoop, 1 afschrift naar de bank, 1 niet ingelezen (d.pdf: te groot)", Overalinlezen.Samenvatting(uitkomsten));
        Assert.Equal("/memoriaal", Overalinlezen.Pad(Inleessoort.Loonjournaal));
        Assert.Equal("/verkoop", Overalinlezen.Pad(Inleessoort.Verkoop));
    }

    [Fact]
    public async Task Leest_in_de_juiste_werklijst_in()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Overal Slepen B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with { Naam = "Voorbeeld Handel B.V.", BtwNummer = "NL123456782B01" })).IsGelukt);
        var inlezen = app.Dienst<InleesDienst>();

        var inkoop = await inlezen.Lees(wie, id, Fictief("belnet-2026-0411.xml"), "belnet-2026-0411.xml", "application/xml");
        Assert.Equal((Inleessoort.Inkoop, true), (inkoop.Soort, inkoop.Gelukt));
        var verkoop = await inlezen.Lees(wie, id, Fictief("verkoop-ander-pakket.xml"), "verkoop-ander-pakket.xml", "application/xml");
        Assert.Equal((Inleessoort.Verkoop, true), (verkoop.Soort, verkoop.Gelukt));
        var bank = await inlezen.Lees(wie, id, Fictief("ing-2026-03.mt940"), "ing-2026-03.mt940", null);
        Assert.Equal((Inleessoort.Bank, true), (bank.Soort, bank.Gelukt));

        var aantallen = await app.Dienst<KantooroverzichtDienst>().Aantallen(wie, id);
        Assert.True(aantallen.Bank > 0);
        Assert.Equal(1, aantallen.Verkoop);
        Assert.True(aantallen.Inkoop + (await app.Dienst<InkoopDienst>().Lijst(wie, id, Data.Documentstatus.Geboekt)).Count >= 1);

        var buiten = await app.Gebruiker("Bert Buiten Slepen");
        await Assert.ThrowsAsync<GeenToegangException>(() => inlezen.Lees(buiten, id, Fictief("belnet-2026-0411.xml"), "belnet.xml", null));
    }

    [Fact]
    public void Een_pdf_wordt_ook_zonder_pdf_extensie_herkend_en_dus_gescand()
    {
        Assert.True(InleesDienst.IsPdf("%PDF-1.7\n%âãÏÓ"u8.ToArray()));
        Assert.False(InleesDienst.IsPdf("<?xml version=\"1.0\"?>"u8.ToArray()));
        Assert.False(InleesDienst.IsPdf([]));
    }
}
