using System.Text;
using Boekerhouden.Boekhouding.Auditfiles;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class AuditfileTests
{
    private static Auditfile Voorbeeld() => new(
        "3.2",
        new AuditfileKop(2026, new(2026, 1, 1), new(2026, 12, 31), "EUR", new(2027, 2, 1), "Boekerhouden", "1.0"),
        new AuditfileBedrijf("Bakkerij Voorbeeld B.V.", "12345678", "NL123456782B01", "Dorpsstraat 1", "1234 AB", "Ergens", "NL"),
        [new AuditfileRelatie("BELNET", "Belnet B.V.", null, "NL123456782B01", false, true, ["NL63TRIO0212345678"]),
         new AuditfileRelatie("JVRIES", "J. de Vries", null, null, true, false, [])],
        [new AuditfileRekening("0500", "Aandelenkapitaal", Rekeningsoort.Balans, "BEivGokGea"),
         new AuditfileRekening("1100", "Bank", Rekeningsoort.Balans, "BLimBanRba"),
         new AuditfileRekening("1600", "Crediteuren", Rekeningsoort.Balans, "BSchCreHac"),
         new AuditfileRekening("4310", "Telefoon", Rekeningsoort.Resultaat, null)],
        [new AuditfileBtwcode("I21", "Inkoop hoog", null, "1500")],
        new DateOnly(2026, 1, 1),
        [new AuditfileRegel("1100", 5000m), new AuditfileRegel("0500", -5000m)],
        [new AuditfileDagboek("INK", "Inkoop", Dagboeksoort.Inkoop, null,
            [new AuditfileBoeking("2026-0001", "Belnet maart", new(2026, 3, 1), 3,
                [new AuditfileRegel("4310", 24.99m, "Telefoon maart"), new AuditfileRegel("1600", -24.99m, "Belnet maart", "BELNET", "BN-0311")])]),
         new AuditfileDagboek("BNK", "Bank", Dagboeksoort.Bank, "1100",
            [new AuditfileBoeking("2026-0001", "Betaling Belnet", new(2026, 3, 20), 3,
                [new AuditfileRegel("1600", 24.99m, null, "BELNET", "BN-0311"), new AuditfileRegel("1100", -24.99m)])])]);

    private static (Auditfile A, IReadOnlyList<string> Fouten) Lees(string xml) => AuditfileLezer.Lees(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Schrijven_en_teruglezen_geeft_dezelfde_administratie()
    {
        var bron = Voorbeeld();

        var (terug, fouten) = AuditfileLezer.Lees(new MemoryStream(AuditfileSchrijver.Schrijf(bron)));

        Assert.Empty(fouten);
        Assert.Equal("3.2", terug.Versie);
        Assert.Equal(bron.Kop, terug.Kop);
        Assert.Equal(bron.Bedrijf, terug.Bedrijf);
        Assert.Equal(bron.Rekeningen, terug.Rekeningen);
        Assert.Equal(bron.Btwcodes, terug.Btwcodes);
        Assert.Equal(bron.Beginbalans, terug.Beginbalans);
        Assert.Equal(bron.Relaties.Select(r => (r.Code, r.Naam, r.Klant, r.Leverancier, string.Join(",", r.Ibans), r.BtwNummer)),
            terug.Relaties.Select(r => (r.Code, r.Naam, r.Klant, r.Leverancier, string.Join(",", r.Ibans), r.BtwNummer)));
        Assert.Equal(2, terug.Dagboeken.Count);
        var bank = terug.Dagboeken.Single(d => d.Code == "BNK");
        Assert.Equal((Dagboeksoort.Bank, "1100"), (bank.Soort, bank.Tegenrekening));
        var inkoop = terug.Dagboeken.Single(d => d.Code == "INK").Boekingen.Single();
        Assert.Equal(("2026-0001", "Belnet maart", new DateOnly(2026, 3, 1), 3), (inkoop.Boekstuk, inkoop.Omschrijving, inkoop.Datum, inkoop.Periode));
        Assert.Equal(new AuditfileRegel("1600", -24.99m, "Belnet maart", "BELNET", "BN-0311"), inkoop.Regels[1]);
        Assert.Equal("Belnet maart", terug.Dagboeken.Single(d => d.Code == "INK").Boekingen.Single().Regels[1].Omschrijving);
    }

    [Fact]
    public void Het_voorbeeld_uit_het_onderzoek_wordt_gelezen()
    {
        var (a, fouten) = Lees("""
            <auditfile xmlns="http://www.auditfiles.nl/XAF/3.2">
             <header><fiscalYear>2025</fiscalYear><startDate>2025-01-01</startDate><endDate>2025-12-31</endDate><curCode>EUR</curCode>
              <dateCreated>2026-02-15</dateCreated><softwareDesc>PakketX</softwareDesc><softwareVersion>1.0</softwareVersion></header>
             <company><companyIdent>12345678</companyIdent><companyName>Voorbeeld BV</companyName>
              <taxRegistrationCountry>NL</taxRegistrationCountry><taxRegIdent>NL001234567B01</taxRegIdent>
              <generalLedger>
               <ledgerAccount><accID>0500</accID><accDesc>Geplaatst kapitaal</accDesc><accTp>B</accTp><leadReference>BEivGokGea</leadReference></ledgerAccount>
               <ledgerAccount><accID>1100</accID><accDesc>ING Bank</accDesc><accTp>B</accTp><leadReference>BLimBanRba</leadReference></ledgerAccount>
               <ledgerAccount><accID>8000</accID><accDesc>Omzet</accDesc><accTp>P</accTp><leadReference>WOmzNop</leadReference></ledgerAccount>
              </generalLedger>
              <openingBalance><opBalDate>2025-01-01</opBalDate><linesCount>2</linesCount><totalDebit>5000.00</totalDebit><totalCredit>5000.00</totalCredit>
               <obLine><nr>1</nr><accID>1100</accID><amnt>5000.00</amnt><amntTp>D</amntTp></obLine>
               <obLine><nr>2</nr><accID>0500</accID><amnt>5000.00</amnt><amntTp>C</amntTp></obLine>
              </openingBalance>
              <transactions><linesCount>2</linesCount><totalDebit>1000.00</totalDebit><totalCredit>1000.00</totalCredit>
               <journal><jrnID>BNK</jrnID><desc>Bank</desc><jrnTp>B</jrnTp>
                <transaction><nr>25-0001</nr><desc>Ontvangst</desc><periodNumber>1</periodNumber><trDt>2025-01-15</trDt>
                 <trLine><nr>1</nr><accID>1100</accID><docRef>F25-001</docRef><effDate>2025-01-15</effDate><desc>Ontvangst</desc><amnt>1000.00</amnt><amntTp>D</amntTp></trLine>
                 <trLine><nr>2</nr><accID>8000</accID><docRef>F25-001</docRef><effDate>2025-01-15</effDate><desc>Omzet</desc><amnt>1000.00</amnt><amntTp>C</amntTp></trLine>
                </transaction></journal></transactions>
             </company>
            </auditfile>
            """);

        Assert.Empty(fouten);
        Assert.Equal(("3.2", 2025, "Voorbeeld BV", "12345678"), (a.Versie, a.Kop.Boekjaar, a.Bedrijf.Naam, a.Bedrijf.KvkNummer));
        Assert.Equal(["BEivGokGea", "BLimBanRba", "WOmzNop"], a.Rekeningen.Select(r => r.RgsCode));
        Assert.Equal(Rekeningsoort.Resultaat, a.Rekeningen.Single(r => r.Nummer == "8000").Soort);
        Assert.Equal([5000m, -5000m], a.Beginbalans.Select(r => r.Bedrag));
        Assert.Equal([1000m, -1000m], a.Dagboeken.Single().Boekingen.Single().Regels.Select(r => r.Bedrag));
    }

    [Fact]
    public void Verkeerde_totalen_een_onevenwichtige_boeking_en_een_onbekende_rekening_zijn_fouten()
    {
        var doc = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(AuditfileSchrijver.Schrijf(Voorbeeld())));
        System.Xml.Linq.XElement Eerste(System.Xml.Linq.XContainer c, string naam) => c.Descendants().First(e => e.Name.LocalName == naam);
        var transacties = Eerste(doc, "transactions");
        Eerste(transacties, "totalDebit").Value = "50.00";
        var journalen = transacties.Elements().Where(e => e.Name.LocalName == "journal").ToList();
        Eerste(Eerste(journalen[0], "trLine"), "accID").Value = "9999";
        Eerste(Eerste(journalen[1], "trLine"), "amnt").Value = "25.00";
        var xml = doc.ToString();

        var (_, fouten) = Lees(xml);

        Assert.Contains(fouten, f => f.StartsWith("Het totaal debet van de boekingen klopt niet", StringComparison.Ordinal));
        Assert.Contains("Rekening 9999 staat niet in het rekeningschema van de auditfile.", fouten);
        Assert.Contains(fouten, f => f.StartsWith("Boeking BNK 2026-0001 is niet in evenwicht", StringComparison.Ordinal));
    }

    [Fact]
    public void Versie_4_met_rgscode_en_bedrag_met_teken()
    {
        var (a, fouten) = Lees("""
            <auditfile xmlns="http://www.odb.belastingdienst.nl/Belastingdienst/BCPP/1.1/structures/XmlauditfileXAF_4.0">
             <header><fiscalYear>2027</fiscalYear><startDate>2027-01-01</startDate><endDate>2027-12-31</endDate></header>
             <company><companyName>Nieuw BV</companyName>
              <generalLedger>
               <ledgerAccount><accID>1100</accID><accDesc>Bank</accDesc><accTp>B</accTp><RGScode>BLimBanRba</RGScode></ledgerAccount>
               <ledgerAccount><accID>8000</accID><accDesc>Omzet</accDesc><accTp>P</accTp><RGScode>WOmzNop</RGScode></ledgerAccount>
              </generalLedger>
              <transactions>
               <journal><jrnID>MEM</jrnID><desc>Memoriaal</desc><jrnTp>Z</jrnTp>
                <transaction><nr>1</nr><desc>Omzet</desc><trDt>2027-02-01</trDt>
                 <trLine><accID>1100</accID><amnt>100.00</amnt></trLine><trLine><accID>8000</accID><amnt>-100.00</amnt></trLine>
                </transaction></journal></transactions>
             </company>
            </auditfile>
            """);

        Assert.Empty(fouten);
        Assert.Equal("4.0", a.Versie);
        Assert.Equal(["BLimBanRba", "WOmzNop"], a.Rekeningen.Select(r => r.RgsCode));
        Assert.Equal([100m, -100m], a.Dagboeken.Single().Boekingen.Single().Regels.Select(r => r.Bedrag));
        Assert.Equal(Dagboeksoort.Memoriaal, a.Dagboeken.Single().Soort);
    }

    [Fact]
    public void Een_dtd_of_iets_anders_dan_een_auditfile_wordt_geweigerd()
    {
        Assert.Throws<AuditfileException>(() => Lees("""<?xml version="1.0"?><!DOCTYPE auditfile [<!ENTITY x "y">]><auditfile><header/></auditfile>"""));
        Assert.Throws<AuditfileException>(() => Lees("<Invoice/>"));
        Assert.Throws<AuditfileException>(() => Lees("geen xml"));
    }
}
