using System.Diagnostics;
using System.Globalization;
using System.Text;
using Boekerhouden.Boekhouding.Bank;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Bankbestanden zoals verschillende banken ze schrijven (verzonnen gegevens, de vorm van de bank): MT940 met
/// gestructureerde codes, met labels en zonder labels, met een kop voor het eerste :20:, rekeningnummers met en zonder
/// valuta, bedragen met voorloopnullen, een storno (RD/RC), omschrijvingen over meerdere regels, meerdere afschriften en
/// rekeningen in één bestand; CAMT.053 met een afschrift over meerdere dagen (meer slotsaldi), een batch, een
/// transactie in vreemde valuta, een negatief saldo, een leeg afschrift; en een groot bestand. Elk afschrift moet
/// sluiten (begin + mutaties = eind) en de mutaties moeten kloppen.
/// </summary>
public sealed class BankformatenTests
{
    private static IReadOnlyList<Bankafschrift> Lees(string inhoud) => Bankbestand.Lees(new MemoryStream(Encoding.Latin1.GetBytes(inhoud)));

    [Fact]
    public void Mt940_gestructureerd_met_cntp_remi_en_eref()
    {
        var a = Assert.Single(Lees("""
            :20:P250301000000001
            :25:NL69INGB0123456789EUR
            :28C:00012
            :60F:C250228EUR1662,23
            :61:2503010301C1,56NTRFEREF//00000000001005
            /TRCD/00100/
            :86:/EREF/EV12341REP1231456T1234//CNTP/NL32INGB0000012345/INGBNL2A/
            ING BANK NV INZAKE WEB///REMI/USTD//Factuur 2025-0042/
            :61:2503030303D1100,00NTRFNONREF//00000000001006
            :86:/CNTP/NL91ABNA0417164300/ABNANL2A/Verhuur De Hoek BV///REMI/STRD/C
            UR/1234567890123456/
            :62F:C250303EUR563,79
            """));

        Assert.True(a.SluitAan, $"verschil {a.Verschil}");
        Assert.Equal("NL69INGB0123456789", a.Iban);
        Assert.Equal(("NL32INGB0000012345", "ING BANK NV INZAKE WEB"), (a.Mutaties[0].TegenIban, a.Mutaties[0].TegenNaam));
        Assert.Contains("2025-0042", a.Mutaties[0].Omschrijving, StringComparison.Ordinal);
        Assert.Equal(-1_100m, a.Mutaties[1].Bedrag);
        Assert.Equal("Verhuur De Hoek BV", a.Mutaties[1].TegenNaam);
    }

    [Fact]
    public void Mt940_met_een_kop_rekening_zonder_valuta_en_voorloopnullen()
    {
        var a = Assert.Single(Lees("""
            :940:
            :20:940S250301
            :25:NL50RABO0123456789
            :28C:0
            :60F:C250228EUR000000001000,00
            :61:250301C000000000500,00N541EREF
            0301NL04ASNB0123456789
            :86:/EREF/F2025-0001//BENM//NAME/J. de Vries//REMI/Factuur F2025-0001//IBAN/NL04ASNB0123456789/
            :61:250302D000000000012,50N093NONREF
            :86:/REMI/Kosten betalingsverkeer februari 2025/
            :62F:C250302EUR000000001487,50
            """));

        Assert.True(a.SluitAan, $"verschil {a.Verschil}");
        Assert.Equal("NL50RABO0123456789", a.Iban);
        Assert.Equal((500m, -12.50m), (a.Mutaties[0].Bedrag, a.Mutaties[1].Bedrag));
        Assert.Equal("J. de Vries", a.Mutaties[0].TegenNaam);
        Assert.Equal("NL04ASNB0123456789", a.Mutaties[0].TegenIban);
    }

    [Fact]
    public void Mt940_vrije_tekst_met_labels_en_een_kop_voor_het_eerste_afschrift()
    {
        var a = Assert.Single(Lees("""
            ABNANL2A
            940
            ABNANL2A
            :20:ABN AMRO BANK NV
            :25:NL12ABNA0123456789
            :28:25060/1
            :60F:C250301EUR3236,28
            :61:2503040304D9,NONREF
            :86:SEPA INCASSO ALGEMEEN DOORLOPEND INCASSANT: NL12ZZZ123456780000
            NAAM: Telecom Noord BV MACHTIGING: 12345 OMSCHRIJVING: Abo
            maart 2025 IBAN: NL98INGB0001234567 KENMERK: 9100234601
            :61:2503050305C1210,NONREF
            :86:SEPA OVERBOEKING IBAN: NL44RABO0123456789 BIC: RABONL2U NAAM: Fiet
            splan Zakelijk BV OMSCHRIJVING: Factuur 20250012
            :62F:C250305EUR4437,28
            """));

        Assert.True(a.SluitAan, $"verschil {a.Verschil}");
        Assert.Equal(("NL98INGB0001234567", -9m), (a.Mutaties[0].TegenIban, a.Mutaties[0].Bedrag));
        Assert.Contains("Telecom Noord", a.Mutaties[0].TegenNaam ?? "", StringComparison.Ordinal);
        Assert.Equal("NL44RABO0123456789", a.Mutaties[1].TegenIban);
        Assert.Contains("20250012", a.Mutaties[1].Omschrijving, StringComparison.Ordinal);
    }

    [Fact]
    public void Mt940_storno_als_rd_en_rc_en_meerdere_rekeningen_in_een_bestand()
    {
        var afschriften = Lees("""
            :20:STMT1
            :25:NL20INGB0001234567EUR
            :28C:1
            :60F:C250301EUR100,00
            :61:2503020302D50,00NDDTNONREF
            :86:/CNTP/NL98INGB0001234567/INGBNL2A/Energie Oost BV///REMI/USTD//Termijn maart/
            :61:2503040304RD50,00NDDTNONREF
            :86:/RTRN/MS03/CNTP/NL98INGB0001234567/INGBNL2A/Energie Oost BV///REMI/USTD//Storno termijn maart/
            :62F:C250304EUR100,00
            :20:STMT2
            :25:NL13INGB0009876543EUR
            :28C:1
            :60F:D250301EUR20,00
            :61:2503030303C30,00NTRFNONREF
            :86:/REMI/USTD//Rente/
            :62F:C250303EUR10,00
            """);

        Assert.Equal(2, afschriften.Count);
        Assert.All(afschriften, a => Assert.True(a.SluitAan, $"{a.Iban}: verschil {a.Verschil}"));
        // RD = storno van een afschrijving: het geld komt terug.
        Assert.Equal([-50m, 50m], afschriften[0].Mutaties.Select(m => m.Bedrag));
        Assert.Equal(-20m, afschriften[1].Beginsaldo);
    }

    private const string CamtKop = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02"><BkToCstmrStmt><GrpHdr><MsgId>1</MsgId><CreDtTm>2025-03-31T00:00:00</CreDtTm></GrpHdr>
        """;

    private static string Bal(string code, decimal bedrag, string datum) =>
        $"<Bal><Tp><CdOrPrtry><Cd>{code}</Cd></CdOrPrtry></Tp><Amt Ccy=\"EUR\">{Math.Abs(bedrag).ToString("0.00", CultureInfo.InvariantCulture)}</Amt><CdtDbtInd>{(bedrag < 0 ? "DBIT" : "CRDT")}</CdtDbtInd><Dt><Dt>{datum}</Dt></Dt></Bal>";

    private static string Ntry(decimal bedrag, string datum, string tx = "") =>
        $"<Ntry><Amt Ccy=\"EUR\">{Math.Abs(bedrag).ToString("0.00", CultureInfo.InvariantCulture)}</Amt><CdtDbtInd>{(bedrag < 0 ? "DBIT" : "CRDT")}</CdtDbtInd><Sts>BOOK</Sts><BookgDt><Dt>{datum}</Dt></BookgDt><NtryDtls>{tx}</NtryDtls></Ntry>";

    [Fact]
    public void Camt_over_meerdere_dagen_met_een_slotsaldo_per_dag_neemt_het_laatste()
    {
        var a = Assert.Single(Lees(CamtKop + $"""
            <Stmt><Id>1</Id><Acct><Id><IBAN>NL20INGB0001234567</IBAN></Id></Acct>
            {Bal("OPBD", 100m, "2025-03-03")}{Bal("CLBD", 150m, "2025-03-03")}{Bal("OPBD", 150m, "2025-03-04")}{Bal("CLBD", 120m, "2025-03-04")}
            {Ntry(50m, "2025-03-03")}{Ntry(-30m, "2025-03-04")}
            </Stmt></BkToCstmrStmt></Document>
            """));

        Assert.True(a.SluitAan, $"begin {a.Beginsaldo}, eind {a.Eindsaldo}, verschil {a.Verschil}");
        Assert.Equal((100m, 120m, new DateOnly(2025, 3, 4)), (a.Beginsaldo, a.Eindsaldo, a.Einddatum!.Value));
    }

    [Fact]
    public void Camt_batch_vreemde_valuta_negatief_saldo_en_een_leeg_afschrift()
    {
        const string usd = """<TxDtls><AmtDtls><InstdAmt><Amt Ccy="USD">110.00</Amt></InstdAmt><TxAmt><Amt Ccy="EUR">100.00</Amt></TxAmt></AmtDtls><RltdPties><Cdtr><Nm>Cloud Inc</Nm></Cdtr></RltdPties><RmtInf><Ustrd>Invoice 4400</Ustrd></RmtInf></TxDtls>""";
        const string batch = """<TxDtls><Amt Ccy="EUR">30.00</Amt><RmtInf><Ustrd>Factuur A</Ustrd></RmtInf></TxDtls><TxDtls><Amt Ccy="EUR">20.00</Amt><RmtInf><Ustrd>Factuur B</Ustrd></RmtInf></TxDtls>""";
        var afschriften = Lees(CamtKop + $"""
            <Stmt><Id>1</Id><Acct><Id><IBAN>NL20INGB0001234567</IBAN></Id></Acct>
            {Bal("OPBD", -10m, "2025-03-03")}{Bal("CLBD", -160m, "2025-03-03")}
            {Ntry(-100m, "2025-03-03", usd)}{Ntry(-50m, "2025-03-03", batch)}
            </Stmt>
            <Stmt><Id>2</Id><Acct><Id><IBAN>NL13INGB0009876543</IBAN></Id></Acct>{Bal("OPBD", 5m, "2025-03-03")}{Bal("CLBD", 5m, "2025-03-03")}</Stmt>
            </BkToCstmrStmt></Document>
            """);

        Assert.Equal(2, afschriften.Count);
        Assert.All(afschriften, a => Assert.True(a.SluitAan, $"{a.Iban}: verschil {a.Verschil}"));
        Assert.Equal([-100m, -30m, -20m], afschriften[0].Mutaties.Select(m => m.Bedrag));
        Assert.Equal("Cloud Inc", afschriften[0].Mutaties[0].TegenNaam);
        Assert.Empty(afschriften[1].Mutaties);
    }

    [Fact]
    public void Een_groot_bestand_van_twintigduizend_mutaties()
    {
        var mt940 = new StringBuilder(":20:GROOT\n:25:NL20INGB0001234567EUR\n:28C:1\n:60F:C250101EUR0,00\n");
        var saldo = 0m;
        for (var i = 0; i < 20_000; i++)
        {
            var bedrag = (i % 7 - 3) * 1.37m + 0.01m;
            saldo += bedrag;
            var dag = new DateOnly(2025, 1, 1).AddDays(i / 60);
            mt940.Append(CultureInfo.InvariantCulture, $":61:{dag:yyMMdd}{dag:MMdd}{(bedrag < 0 ? "D" : "C")}{Math.Abs(bedrag).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}NTRFNONREF\n");
            mt940.Append(CultureInfo.InvariantCulture, $":86:/CNTP/NL91ABNA0417164300/ABNANL2A/Klant {i % 300}///REMI/USTD//Factuur {i:00000}/\n");
        }

        var eind = new DateOnly(2025, 1, 1).AddDays(19_999 / 60);
        mt940.Append(CultureInfo.InvariantCulture, $":62F:{(saldo < 0 ? "D" : "C")}{eind:yyMMdd}EUR{Math.Abs(saldo).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}\n");
        var klok = Stopwatch.StartNew();

        var a = Assert.Single(Lees(mt940.ToString()));

        Assert.True(a.SluitAan, $"verschil {a.Verschil}");
        Assert.Equal(20_000, a.Mutaties.Count);
        Assert.True(klok.Elapsed < TimeSpan.FromSeconds(10), $"{klok.Elapsed.TotalSeconds:0.0} s");
    }
}
