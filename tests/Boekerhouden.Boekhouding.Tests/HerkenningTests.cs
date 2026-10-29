using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class FactuurtekstTests
{
    private const string EigenBtw = "NL123456782B01";

    [Fact]
    public void Factuur_uit_pdf_tekst_met_twee_tarieven()
    {
        var factuur = Factuurtekst.Analyseer(Lees("jansen-2026-114.txt"), eigenBtwNummers: [EigenBtw]);

        Assert.Equal("Kantoorhandel Jansen B.V.", factuur.Naam);
        Assert.Equal("NL800000006B01", factuur.BtwNummer);
        Assert.Equal("87654321", factuur.KvkNummer);
        Assert.Equal("NL36INGB0007654321", factuur.Iban);
        Assert.Equal("2026-114", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 3, 3), factuur.Factuurdatum);
        Assert.Equal(new DateOnly(2026, 3, 17), factuur.Vervaldatum);
        Assert.Equal(188.72m, factuur.TotaalExclusief);
        Assert.Equal(226.65m, factuur.TotaalInclusief);
        Assert.Equal([new Btwregel(21m, 174.50m, 36.65m), new Btwregel(9m, 14.22m, 1.28m)], factuur.Btwregels);
        Assert.Equal("1234567890123458", factuur.Betalingskenmerk);
        Assert.Empty(factuur.Controleer());
        Assert.Null(factuur.EnigTarief);
        Assert.Equal("Printpapier A4 80 gr e.a.", factuur.Omschrijving);
        Assert.Null(factuur.OpEigenNaam);
    }

    [Fact]
    public void Op_naam_van_de_onderneming_of_niet()
    {
        var tekst = Lees("jansen-2026-114.txt");

        Assert.True(Factuurtekst.Analyseer(tekst, eigenNamen: ["Bakkerij Voorbeeld B.V."]).OpEigenNaam);
        Assert.False(Factuurtekst.Analyseer(tekst, eigenNamen: ["Schildersbedrijf Voorbeeld"]).OpEigenNaam);
    }

    [Fact]
    public void Eenvoudige_bon_met_alleen_totaal_en_btw()
    {
        const string bon = """
            Belnet B.V.
            Factuur nr. BN-2026-0311
            Datum: 01-03-2026
            Mobiel abonnement maart
            Totaal excl. btw 20,65
            21% btw 4,34
            Totaal 24,99
            Betalen binnen 14 dagen
            """;

        var factuur = Factuurtekst.Analyseer(bon);

        Assert.Equal("BN-2026-0311", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 3, 1), factuur.Factuurdatum);
        Assert.Equal(new DateOnly(2026, 3, 15), factuur.Vervaldatum);
        Assert.Equal(20.65m, factuur.TotaalExclusief);
        Assert.Equal(24.99m, factuur.TotaalInclusief);
        Assert.Equal(21m, factuur.EnigTarief);
        Assert.Empty(factuur.Controleer());
    }

    [Fact]
    public void Ontbrekende_en_niet_sluitende_gegevens_worden_gemeld()
    {
        var factuur = Factuurtekst.Analyseer("Iets B.V.\nTotaal excl. btw 100,00\nBtw 21% 25,00\nTotaal 121,00");

        var punten = factuur.Controleer();

        Assert.Contains("Factuurnummer niet gevonden.", punten);
        Assert.Contains("Factuurdatum niet gevonden.", punten);
        Assert.Contains(punten, p => p.StartsWith("Exclusief plus btw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Totaal € 1.234,56", 1234.56)]
    [InlineData("Total 1,234.56", 1234.56)]
    [InlineData("Te betalen: -24,99", -24.99)]
    public void Bedragen_in_een_regel(string regel, decimal verwacht) => Assert.Equal(verwacht, Factuurtekst.Bedragen(regel)[^1]);

    [Fact]
    public void Datums_en_percentages_zijn_geen_bedragen()
    {
        Assert.Empty(Factuurtekst.Bedragen("Datum 12.04.2026, btw 21,00 %"));
    }

    [Theory]
    [InlineData("12-03-2026", 2026, 3, 12)]
    [InlineData("12/3/26", 2026, 3, 12)]
    [InlineData("2026-03-12", 2026, 3, 12)]
    [InlineData("12 mrt. 2026", 2026, 3, 12)]
    [InlineData("1 September 2026", 2026, 9, 1)]
    public void Datums_in_verschillende_notaties(string tekst, int jaar, int maand, int dag) =>
        Assert.Equal(new DateOnly(jaar, maand, dag), Assert.Single(Factuurtekst.Datums(tekst)));

    [Fact]
    public void Onmogelijke_datum_wordt_overgeslagen() => Assert.Empty(Factuurtekst.Datums("31-02-2026"));

    internal static string Lees(string bestand) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", bestand));
}

public sealed class UblTests
{
    [Fact]
    public void E_factuur_wordt_exact_gelezen()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "belnet-2026-0411.xml"));

        Assert.True(UblFactuurLezer.IsUbl(stream));
        var factuur = UblFactuurLezer.Lees(stream);

        Assert.Equal(Herkenningsbron.EFactuur, factuur.Bron);
        Assert.Equal("Belnet B.V.", factuur.Naam);
        Assert.Equal("NL123456782B01", factuur.BtwNummer);
        Assert.Equal("12345678", factuur.KvkNummer);
        Assert.Equal("NL63TRIO0212345678", factuur.Iban);
        Assert.Equal("BN-2026-0411", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 4, 1), factuur.Factuurdatum);
        Assert.Equal(new DateOnly(2026, 4, 15), factuur.Vervaldatum);
        Assert.Equal(24.99m, factuur.TotaalInclusief);
        Assert.Equal(new Btwregel(21m, 20.65m, 4.34m), Assert.Single(factuur.Btwregels));
        Assert.Equal("Zakelijk mobiel abonnement april", factuur.Omschrijving);
        Assert.Empty(factuur.Controleer());
    }

    [Fact]
    public void Creditnota_heeft_negatieve_bedragen()
    {
        const string xml = """
            <CreditNote xmlns="urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2"
                        xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                        xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>CN-1</cbc:ID>
              <cbc:IssueDate>2026-04-02</cbc:IssueDate>
              <cac:TaxTotal><cac:TaxSubtotal><cbc:TaxableAmount>10.00</cbc:TaxableAmount><cbc:TaxAmount>2.10</cbc:TaxAmount>
                <cac:TaxCategory><cbc:Percent>21</cbc:Percent></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
              <cac:LegalMonetaryTotal><cbc:TaxExclusiveAmount>10.00</cbc:TaxExclusiveAmount><cbc:PayableAmount>12.10</cbc:PayableAmount></cac:LegalMonetaryTotal>
            </CreditNote>
            """;

        var factuur = UblFactuurLezer.Lees(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(-12.10m, factuur.TotaalInclusief);
        Assert.Equal(-2.10m, factuur.TotaalBtw);
    }

    [Fact]
    public void Ander_xml_is_geen_ubl()
    {
        Assert.False(UblFactuurLezer.IsUbl(new MemoryStream("<Document/>"u8.ToArray())));
        Assert.False(UblFactuurLezer.IsUbl(new MemoryStream("geen xml"u8.ToArray())));
    }
}

public sealed class InkoopverwerkerTests
{
    private static readonly Relatie[] Relaties =
    [
        new("BELNET", "Belnet B.V.", ["NL63TRIO0212345678"], "NL123456782B01"),
        new("JANSEN", "Kantoorhandel Jansen", ["NL36INGB0007654321"]),
    ];

    private static readonly Verwerkingsregel Telefoon = new("Belnet is telefoon", "4100", "I21", Automatisch: true,
        Soort: Verwerkingssoort.Inkoopfactuur, NaamBevat: "Belnet", BedragVan: 10m, BedragTot: 30m);

    [Fact]
    public void E_factuur_met_regel_wordt_automatisch_verwerkt()
    {
        var herkend = Ubl();

        var voorstel = Inkoopverwerker.Verwerk(herkend, Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.Equal("btw-nummer", voorstel.Leverancier?.Op);
        Assert.True(voorstel.Automatisch);
        var factuur = voorstel.Factuur!;
        Assert.Equal("BELNET", factuur.Relatie);
        var regel = Assert.Single(factuur.Regels);
        Assert.Equal(("4100", "I21", 20.65m, 4.34m), (regel.Rekeningnummer, regel.BtwCode, regel.Bedrag, regel.Btw));
        Assert.Equal(24.99m, factuur.Totaal(Testadministratie.Btw));
    }

    [Fact]
    public void Dubbele_factuur_en_gesloten_periode_houden_automatisch_verwerken_tegen()
    {
        var voorstel = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw,
            [new BestaandeFactuur("BELNET", "BN2026-0411", new(2026, 4, 1), 24.99m)], periodeOpen: d => d.Month > 4);

        Assert.False(voorstel.Automatisch);
        Assert.Contains("Lijkt al geboekt: factuur BN2026-0411 van 01-04-2026.", voorstel.Belemmeringen);
        Assert.Contains("De periode van 01-04-2026 is afgesloten.", voorstel.Belemmeringen);
    }

    [Fact]
    public void Tekstfactuur_met_twee_tarieven_eerst_kiezen_daarna_leren()
    {
        var herkend = Factuurtekst.Analyseer(FactuurtekstTests.Lees("jansen-2026-114.txt"), eigenBtwNummers: ["NL123456782B01"]);
        var geheugen = new Leergeheugen();

        var eerste = Inkoopverwerker.Verwerk(herkend, Relaties, [], geheugen, Testadministratie.Btw, []);
        Assert.Equal("IBAN", eerste.Leverancier?.Op);
        Assert.Null(eerste.Factuur);
        Assert.Contains("Kies een grootboekrekening.", eerste.Belemmeringen);

        var gegevens = Inkoopverwerker.Gegevens(herkend, eerste.Leverancier!.Relatie);
        geheugen.Onthoud(gegevens, new Keuze("4200", "I21"));
        var tweede = Inkoopverwerker.Verwerk(herkend, Relaties, [], geheugen, Testadministratie.Btw, []);
        Assert.False(tweede.Automatisch);
        Assert.Equal(["I21", "I9"], tweede.Factuur!.Regels.Select(r => r.BtwCode));
        Assert.Equal(226.65m, tweede.Factuur.Totaal(Testadministratie.Btw));

        geheugen.Onthoud(gegevens, new Keuze("4200", "I21"));
        var derde = Inkoopverwerker.Verwerk(herkend, Relaties, [], geheugen, Testadministratie.Btw, []);
        Assert.True(derde.Automatisch);
    }

    [Fact]
    public void Onbekende_leverancier_blijft_in_te_verwerken()
    {
        var herkend = Factuurtekst.Analyseer("Nieuw Bedrijf B.V.\nFactuurnummer 77\nDatum 01-05-2026\nTotaal excl. btw 10,00\nBtw 21% 2,10\nTotaal 12,10");

        var voorstel = Inkoopverwerker.Verwerk(herkend, Relaties, [], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.Null(voorstel.Leverancier);
        Assert.Null(voorstel.Factuur);
        Assert.Contains("Leverancier niet herkend.", voorstel.Belemmeringen);
    }

    [Fact]
    public void Zonder_btw_op_de_factuur_haalt_de_adviescode_de_btw_uit_het_totaal()
    {
        var herkend = new HerkendeFactuur(Herkenningsbron.Ocr, "Belnet", Factuurnummer: "B-1", Factuurdatum: new(2026, 5, 1), TotaalInclusief: 24.99m);

        var voorstel = Inkoopverwerker.Verwerk(herkend, Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        var regel = Assert.Single(voorstel.Factuur!.Regels);
        Assert.Equal((20.65m, 4.34m), (regel.Bedrag, regel.Btw));
        Assert.Equal(new DateOnly(2026, 5, 31), voorstel.Factuur.Vervaldatum);
        Assert.True(voorstel.Automatisch);
    }

    [Fact]
    public void Bekende_leverancier_met_een_ander_iban_wordt_niet_automatisch_verwerkt()
    {
        var herkend = Ubl() with { Iban = "NL44RABO0123456789" };

        var voorstel = Inkoopverwerker.Verwerk(herkend, Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.Equal("btw-nummer", voorstel.Leverancier?.Op);
        Assert.False(voorstel.Automatisch);
        Assert.Contains(voorstel.Belemmeringen, b => b.StartsWith("Het IBAN op de factuur (NL44 RABO 0123 4567 89) is niet het bekende IBAN van Belnet B.V.", StringComparison.Ordinal));
    }

    [Fact]
    public void Factuur_niet_op_naam_van_de_onderneming_wordt_niet_automatisch_verwerkt()
    {
        var voorstel = Inkoopverwerker.Verwerk(Ubl() with { OpEigenNaam = false }, Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.False(voorstel.Automatisch);
        Assert.Contains("De factuur staat niet op naam van de onderneming; zonder dat is de btw niet aftrekbaar.", voorstel.Belemmeringen);
    }

    [Fact]
    public void Boven_de_limiet_wordt_niet_automatisch_verwerkt()
    {
        var onder = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, [], limietAutomatisch: 24.99m);
        var boven = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, [], limietAutomatisch: 20m);

        Assert.True(onder.Automatisch);
        Assert.False(boven.Automatisch);
        Assert.Contains("Het bedrag is hoger dan de limiet voor automatisch verwerken (€ 20,00).", boven.Belemmeringen);
    }

    [Fact]
    public void Veel_hoger_bedrag_dan_gebruikelijk_wordt_niet_automatisch_verwerkt()
    {
        static BestaandeFactuur Eerder(int maand, decimal totaal) => new("BELNET", $"BN2026-{maand:00}", new(2026, maand, 1), totaal);

        var gewoon = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, [Eerder(1, 15m), Eerder(2, 12m), Eerder(3, 15m)]);
        var nieuw = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, [Eerder(1, 8m), Eerder(2, 9m)]);
        var hoog = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, [Eerder(1, 8m), Eerder(2, -9m), Eerder(3, 10m)]);

        Assert.True(gewoon.Automatisch);
        Assert.True(nieuw.Automatisch);
        Assert.False(hoog.Automatisch);
        Assert.Contains("Het bedrag is veel hoger dan gebruikelijk bij Belnet B.V. (tot nu toe hoogstens € 10,00).", hoog.Belemmeringen);
        Assert.Null(Inkoopverwerker.Afwijkend(20m, "BELNET", [Eerder(1, 10m), Eerder(2, 10m), Eerder(3, 10m)]));
        Assert.Null(Inkoopverwerker.Afwijkend(100m, "ANDER", [Eerder(1, 10m), Eerder(2, 10m), Eerder(3, 10m)]));
    }

    private static HerkendeFactuur Ubl()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "belnet-2026-0411.xml"));
        return UblFactuurLezer.Lees(stream);
    }
}

/// <summary>
/// Facturen voor de administratie "Test BV": de eigen naam (het klantblok) is nooit de leverancier, ook niet in kolommen
/// of na "Klant:" of "Bill to:"; een leverancier die alleen in de voettekst staat wordt gevonden.
/// </summary>
public sealed class EigenNaamEnVerleggingTests
{
    private static readonly string[] Eigen = ["Test BV"];

    [Fact]
    public void Klantblok_rechts_naast_de_leverancier()
    {
        var tekst = "Schoonmaakbedrijf Glans B.V.    Test BV\nPoetsweg 12    t.a.v. de administratie\n3511 AA Utrecht    Industrieweg 5\nFactuurnummer: GL-1\nTotaal € 121,00";

        Assert.Equal("Schoonmaakbedrijf Glans B.V.", Factuurtekst.Analyseer(tekst, eigenNamen: Eigen).Naam);
    }

    [Fact]
    public void Klant_bovenaan_en_de_leverancier_alleen_in_de_voettekst()
    {
        var tekst = "Test BV\nIndustrieweg 5\n1234 AB Amersfoort\nFACTUUR\nFactuurnummer: 2026-118\nDatum: 02-05-2026\nOnderhoud website mei    € 200,00\nTotaal incl. btw € 242,00\n"
                    + "Webbureau Pixel V.O.F. · Kanaalstraat 3 · 2011 AA Haarlem · KvK 87654321";

        var factuur = Factuurtekst.Analyseer(tekst, eigenNamen: Eigen);

        Assert.Equal("Webbureau Pixel V.O.F.", factuur.Naam);
        Assert.True(factuur.OpEigenNaam);
    }

    [Theory]
    [InlineData("Klant: Test BV")]
    [InlineData("Bill to: Test B.V.")]
    [InlineData("TEST BV")]
    public void Eigen_naam_met_label_of_andere_schrijfwijze_telt_niet(string klantregel)
    {
        var tekst = $"{klantregel}\nGroothandel Vers B.V.\nFactuurnr. 77812\nTotaal 89,60";

        Assert.Equal("Groothandel Vers B.V.", Factuurtekst.Analyseer(tekst, eigenNamen: Eigen).Naam);
    }

    [Fact]
    public void Een_leverancier_die_de_eigen_naam_alleen_bevat_blijft_herkend()
    {
        // Eigen naam "Bakkerij" (één woord): "Bakkerij Jansen B.V." is een andere onderneming.
        Assert.Equal("Bakkerij Jansen B.V.", Factuurtekst.Analyseer("Bakkerij Jansen B.V.\nTotaal 10,00", eigenNamen: ["Bakkerij"]).Naam);
    }

    [Fact]
    public void Engelse_factuur_uit_de_EU_met_verlegde_btw()
    {
        var tekst = "Cloudsoft Ltd    Bill to: Test BV\n12 Harbour Street, Dublin    VAT NL009876543B01\nVAT IE1234567T\nIBAN IE29AIBK93115212345678\n"
                    + "Invoice number: INV-88123\nInvoice date: May 1, 2026\nDue date: May 31, 2026\nCloud hosting - May 2026    € 120.00\nSubtotal € 120.00\nVAT reverse charged (0%) € 0.00\nTotal € 120.00";

        var factuur = Factuurtekst.Analyseer(tekst, eigenBtwNummers: ["NL009876543B01"], eigenNamen: Eigen);

        Assert.Equal(("Cloudsoft Ltd", "IE1234567T"), (factuur.Naam, factuur.BtwNummer));
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)), (factuur.Factuurdatum, factuur.Vervaldatum));
        Assert.True(factuur.Verlegd);
        Assert.Equal("IEU", Inkoopverwerker.VerlegdeCode(factuur, Btw.BtwCodes.Standaard)?.Code);

        // Met een regel voor deze leverancier: één regel, verlegd, zonder btw op de factuur.
        var relatie = new Relatie("CLOUD", "Cloudsoft Ltd", ["IE29AIBK93115212345678"], "IE1234567T");
        var voorstel = Inkoopverwerker.Verwerk(factuur, [relatie], [new Verwerkingsregel("Hosting", "4330", "I21", Relatie: "CLOUD")], new Leergeheugen(), Btw.BtwCodes.Standaard, []);
        var regel = Assert.Single(voorstel.Factuur!.Regels);
        Assert.Equal(("IEU", 120m, (decimal?)null), (regel.BtwCode, regel.Bedrag, regel.Btw));
    }

    [Theory]
    [InlineData("Btw verlegd", "NL800000006B01", null, "IVL")]
    [InlineData("Reverse charge", null, "US12BANK0000000001", "IEX")]
    [InlineData("Steuerschuldnerschaft des Leistungsempfängers", "DE123456789", null, "IEU")]
    [InlineData("Totaal", null, null, null)]
    public void Verlegde_btw_naar_land_van_de_leverancier(string tekst, string? btw, string? iban, string? code)
    {
        var factuur = new HerkendeFactuur(Herkenningsbron.Tekst, BtwNummer: btw, Iban: iban, Verlegd: Factuurtekst.Analyseer(tekst).Verlegd);

        Assert.Equal(code, Inkoopverwerker.VerlegdeCode(factuur, Btw.BtwCodes.Standaard)?.Code);
    }

    [Fact]
    public void Ubl_met_btw_categorie_verlegd()
    {
        var xml = FactuurtekstTests.Lees("belnet-2026-0411.xml").Replace("<cbc:ID>S</cbc:ID>", "<cbc:ID>AE</cbc:ID>", StringComparison.Ordinal);

        Assert.True(UblFactuurLezer.Lees(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml))).Verlegd);
        Assert.False(UblFactuurLezer.Lees(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(FactuurtekstTests.Lees("belnet-2026-0411.xml")))).Verlegd);
    }
}
