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

public sealed class FactuurkopTests
{
    private const string Kop = """
        Drukkerij Het Anker B.V.                                    FACTUUR
        Havenstraat 8, 8601 CD Sneek
        KVK / registratie: 01234501
        BTW-nummer: NL123456782B01
        IBAN: NL44RABO0123456789
        Factuur aan:                         Nummer:          DA-2026-0101
        Testbedrijf B.V.                     Datum:           02-10-2026
        Voorbeeldstraat 1                    Vervaldatum:     16-10-2026
        8601 AA Sneek                        Betaalkenmerk:   4410 1234 5678 9012
        Omschrijving                                    BTW     Bedrag excl. (EUR)
        Drukwerk: 500 visitekaartjes                    21%                 200,00
        Totaal excl. BTW                                              EUR 200,00
        BTW 21% over 200,00                                            EUR 42,00
        Totaal incl. BTW                                              EUR 242,00
        """;

    [Fact]
    public void Nummer_als_label_betaalkenmerk_en_geen_tabelkop_als_btw_nummer()
    {
        var factuur = Factuurtekst.Analyseer(Kop);

        Assert.Equal(("DA-2026-0101", "4410123456789012", "NL123456782B01"), (factuur.Factuurnummer, factuur.Betalingskenmerk, factuur.BtwNummer));
        Assert.False(factuur.Herinnering);
    }

    [Fact]
    public void Een_factuur_in_vreemde_valuta_gaat_nooit_automatisch()
    {
        var dollars = Factuurtekst.Analyseer(Kop.Replace("EUR", "USD", StringComparison.Ordinal));

        Assert.Equal(("USD", 242m), (dollars.Valuta, dollars.TotaalInclusief));
        Assert.Null(Factuurtekst.Analyseer(Kop).Valuta);
        var voorstel = Inkoopverwerker.Verwerk(dollars, [new Relatie("ANKER", "Drukkerij Het Anker B.V.", ["NL44RABO0123456789"])], [], new Leergeheugen(), Testadministratie.Btw, []);
        Assert.Contains("De factuur is in USD: vul de bedragen in euro's in (koers van de factuurdatum).", voorstel.Belemmeringen);
        Assert.False(voorstel.Automatisch);
    }

    [Theory]
    [InlineData("HERINNERING")]
    [InlineData("AANMANING")]
    public void Een_herinnering_wordt_herkend(string kop)
    {
        var herinnering = Factuurtekst.Analyseer(Kop.Replace("FACTUUR", kop, StringComparison.Ordinal) + "\n2e aanmaning: volgens onze administratie is deze factuur nog niet voldaan.");

        Assert.True(herinnering.Herinnering);
        var voorstel = Inkoopverwerker.Verwerk(herinnering, [new Relatie("ANKER", "Drukkerij Het Anker B.V.", ["NL44RABO0123456789"])], [], new Leergeheugen(), Testadministratie.Btw,
            [new BestaandeFactuur("ANKER", "DA-2026-0101", new(2026, 10, 2), 242m, Betaald: true)]);
        Assert.Contains("Herinnering van factuur DA-2026-0101 van 02-10-2026: die is al geboekt en betaald. Niet nog eens boeken.", voorstel.Belemmeringen);
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

    [Theory]
    [InlineData("2026-03-29", true)]
    [InlineData("2026-04-07", true)]
    [InlineData("2026-03-01", false)]
    [InlineData("2026-04-08", false)]
    public void Zelfde_bedrag_kort_erna_met_een_ander_nummer_is_verdacht(string eerder, bool verdacht)
    {
        var voorstel = Inkoopverwerker.Verwerk(Ubl(), Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw,
            [new BestaandeFactuur("BELNET", "BN2026-0399", DateOnly.Parse(eerder, System.Globalization.CultureInfo.InvariantCulture), 24.99m)]);

        Assert.Equal(verdacht, voorstel.Belemmeringen.Any(b => b.StartsWith("Zelfde bedrag als factuur BN2026-0399", StringComparison.Ordinal)));
        Assert.Equal(!verdacht, voorstel.Automatisch);
    }

    [Theory]
    [InlineData("20", true)]
    [InlineData("24.99", true)]
    [InlineData("25", false)]
    public void Leverancier_met_eigen_bedraggrens_komt_vanaf_dat_bedrag_ter_controle(string tekst, bool verwacht)
    {
        var grens = decimal.Parse(tekst, System.Globalization.CultureInfo.InvariantCulture);
        Relatie[] relaties = [Relaties[0] with { ControleVanaf = grens }, Relaties[1]];

        var voorstel = Inkoopverwerker.Verwerk(Ubl(), relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.Equal(verwacht, voorstel.Belemmeringen.Contains($"Facturen van Belnet B.V. vanaf {Domain.Bedragen.Euro(grens)} controleer je zelf."));
        Assert.Equal(!verwacht, voorstel.Automatisch);
    }

    [Theory]
    [InlineData(20, 50, false)]
    [InlineData(10, 50, true)]
    [InlineData(40, 30, true)]
    [InlineData(30, 30, false)]
    public void Leverancier_met_eigen_afwijking_vergelijkt_met_de_vorige_factuur(int vorigeEuro, int procentGetal, bool afwijkend)
    {
        var (vorige, procent) = ((decimal)vorigeEuro, (decimal)procentGetal);
        Relatie[] relaties = [Relaties[0] with { ControleAfwijking = procent }, Relaties[1]];
        BestaandeFactuur[] bestaand =
        [
            new("BELNET", "BN2026-0100", new(2025, 1, 1), 24.99m),
            new("BELNET", "BN2026-0300", new(2026, 2, 1), vorige),
        ];

        var voorstel = Inkoopverwerker.Verwerk(Ubl(), relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, bestaand);

        Assert.Equal(afwijkend, voorstel.Belemmeringen.Any(b => b.StartsWith($"Het bedrag wijkt meer dan {procent}% af van de vorige factuur van Belnet B.V. ({Domain.Bedragen.Euro(vorige)})", StringComparison.Ordinal)));
        Assert.Equal(!afwijkend, voorstel.Automatisch);
    }

    [Fact]
    public void Zonder_vorige_factuur_geldt_de_afwijking_niet()
    {
        Assert.Empty(Inkoopverwerker.EigenControle(5000m, "X-1", new Relatie("A", "A", [], ControleAfwijking: 10m), []));
        Assert.Empty(Inkoopverwerker.EigenControle(5000m, "X-1", new Relatie("A", "A", [], ControleAfwijking: 10m), [new BestaandeFactuur("A", "X-1", new(2026, 1, 1), 100m)]));
        Assert.Single(Inkoopverwerker.EigenControle(-5000m, "X-2", new Relatie("A", "A", [], ControleVanaf: 1000m), []));
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
    public void Factuur_niet_op_naam_van_de_onderneming_houdt_automatisch_verwerken_niet_tegen()
    {
        var voorstel = Inkoopverwerker.Verwerk(Ubl() with { OpEigenNaam = false }, Relaties, [Telefoon], new Leergeheugen(), Testadministratie.Btw, []);

        Assert.DoesNotContain(voorstel.Belemmeringen, b => b.Contains("op naam", StringComparison.Ordinal));
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

/// <summary>Facturen zonder btw (vrijgesteld, kleineondernemersregeling), met een btw-tabel, en zonder kopregel boven de regels.</summary>
public sealed class BtwVormenTests
{
    [Theory]
    [InlineData("Te betalen € 740,52\nVrijgesteld van btw", 740.52)]
    [InlineData("Totaal €350,00\nGeen btw in rekening gebracht op grond van de kleineondernemersregeling (KOR).", 350.00)]
    [InlineData("Total € 99.00\nVAT exempt", 99.00)]
    public void Geen_btw_op_de_factuur_is_nul_procent_over_het_hele_bedrag(string slot, decimal totaal)
    {
        var factuur = Factuurtekst.Analyseer($"Leverancier B.V.\nFactuurnummer: 1\nDatum: 01-04-2026\n{slot}");

        Assert.Equal([new Btwregel(0m, totaal, 0m)], factuur.Btwregels);
        Assert.Equal((totaal, totaal, 0m), (factuur.TotaalExclusief!.Value, factuur.TotaalInclusief!.Value, factuur.EnigTarief!.Value));
        Assert.Equal("I0", Btw.BtwCodes.Bij(Btw.BtwCodes.Standaard, Btw.BtwRichting.Inkoop, factuur.EnigTarief.Value)?.Code);
    }

    [Fact]
    public void Btw_tabel_met_percentage_grondslag_en_btw()
    {
        var factuur = Factuurtekst.Analyseer("Pen B.V.\nInvoice #12345\nDatum: 2026-04-15\nBureaustoel    1.150,00\nKorting 10%    -115,00\n"
                                            + "Btw-percentage    Grondslag    Btw\n21%    1.035,00    217,35\nTotaalbedrag € 1.252,35");

        Assert.Equal([new Btwregel(21m, 1035m, 217.35m)], factuur.Btwregels);
        Assert.Equal((1035m, 1252.35m), (factuur.TotaalExclusief!.Value, factuur.TotaalInclusief!.Value));
        Assert.Empty(factuur.Controleer());
    }

    [Fact]
    public void Btw_regel_met_over_is_geen_tabelkop()
    {
        var factuur = Factuurtekst.Analyseer("Vers B.V.\nBTW 9% over 60,00: 5,40\nBTW 21% over 20,00: 4,20\nTotaal te betalen 89,60");

        Assert.Equal([new Btwregel(21m, 20m, 4.20m), new Btwregel(9m, 60m, 5.40m)], factuur.Btwregels);
    }

    [Fact]
    public void Omschrijving_zonder_kopregel_is_de_eerste_regel_met_een_bedrag_na_nummer_en_datum()
    {
        var factuur = Factuurtekst.Analyseer("Fotografie Lens\nFactuur nr. 2026/0042\nFactuurdatum: 15-4-26\nProductfoto's website    €350,00\nTotaal €350,00");

        Assert.Equal(("Productfoto's website", "2026/0042", new DateOnly(2026, 4, 15)), (factuur.Omschrijving, factuur.Factuurnummer, factuur.Factuurdatum!.Value));
    }
}

/// <summary>
/// Facturen met een ongewone opmaak (verzonnen): een specificatie met eigen "totalen" op een volgende pagina, een naam
/// die alleen in het logo staat, en een geadresseerde met rechtsvorm boven "t.a.v.".
/// </summary>
public sealed class SpecificatieEnLogoTests
{
    private const string MetSpecificatie = """
        Tel:       +31 30 000 0000
        Kantoor Voorbeeld                                   IBAN:  NL91 ABNA 0417 1643 00
        Dorpsstraat 7                                        BTW:   NL800000006B01
        8601 AB Sneek
        FACTUUR
        Factuurnummer:  202617576
        Factuurdatum:   01-10-2026
        Vervaldatum:    15-10-2026
        Betreft:        Verleende diensten in periode 01-09-2026 t/m 30-09-2026
        Omschrijving                    Aantal     Prijs     Bedrag
        Standaard abonnement               1      € 10,00    € 10,00
        Administraties extra              15       € 3,15    € 47,25
        Gebruikers extra                  23       € 1,65    € 37,95
        Documenten                       256       € 0,18    € 46,08
        Totaal (excl. BTW)                                   €141,28
        BTW 21 %                                              €29,67
        Totaal (incl. BTW)                                   €170,95
        Softwarehuis Delta B.V. · Havenstraat 10 · 3811 AB Amersfoort
        SPECIFICATIE Kantoor Voorbeeld
        Administraties                               Prijs     Totaal
        1      Administraties inbegrepen            € 0,00     € 0,00
        15     Administraties extra                 € 3,15    € 47,25
        Documenten per administratie                 Prijs     Totaal
        22     Documenten                           € 0,18     € 3,96
        """;

    [Fact]
    public void Totalen_van_de_factuur_en_niet_van_de_specificatie_op_de_volgende_pagina()
    {
        var factuur = Factuurtekst.Analyseer(MetSpecificatie, eigenNamen: ["Kantoor Voorbeeld"]);

        Assert.Equal((141.28m, 170.95m, 29.67m), (factuur.TotaalExclusief, factuur.TotaalInclusief, factuur.TotaalBtw));
        Assert.Equal("Softwarehuis Delta B.V.", factuur.Naam);
        Assert.Empty(factuur.Controleer());
    }

    private const string NaamInLogo = """
        Van Sminialaan 2 – 8731 BE Wommels
        Telefoon 0612-345678
        Mail info@q4-adviseurs.nl
        KvK 01180433
        NL22 RABO 0119 9157 15
        BTW NL001145148B88
        Faktuur
        Markt Voorbeeld B.V.
        t.a.v. de heren J. Jansen en P. Pietersen
        jan@marktvoorbeeld.nl
        Faktuur          2026-10-01
        Datum:           28 oktober 2026
        Betreft:         Overeenkomst van opdracht
        Hiermee declareren wij de in de maand oktober gemaakte kosten.
        Zie onderstaande     €     454,79     Excl. BTW
        BTW 21%                     95,51
        Faktuurbedrag        €     550,30     Incl. BTW
        Wij verzoeken u vriendelijk uw betaling binnen 14 dagen te voldoen.
        """;

    [Fact]
    public void Naam_alleen_in_het_logo_komt_uit_het_domein_en_de_geadresseerde_is_geen_leverancier()
    {
        var factuur = Factuurtekst.Analyseer(NaamInLogo);

        Assert.Equal("Q4 Adviseurs", factuur.Naam);
        Assert.Equal("2026-10-01", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 10, 28), factuur.Factuurdatum);
        Assert.Equal((454.79m, 550.30m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
    }

    [Fact]
    public void Naam_uit_het_domein_als_tekst_in_de_voettekst()
    {
        var tekst = NaamInLogo + "\nQ4 Adviseurs   ·   Van Sminialaan 2   ·   www.q4-adviseurs.nl";

        Assert.Equal("Q4 Adviseurs", Factuurtekst.Analyseer(tekst).Naam);
    }

    [Theory]
    [InlineData("Postbus 30, 8600 AD Sneek\nTotaal 10,00", null)]
    [InlineData("Postbus 30\nE-mail facturen@gmail.com\nTotaal 10,00", null)]
    [InlineData("Postbus 30\nE-mail administratie@bouw-en-zo.nl\nTotaal 10,00", "Bouw En Zo")]
    public void Postbus_en_maildienst_zijn_geen_naam(string tekst, string? naam) =>
        Assert.Equal(naam, Factuurtekst.Analyseer(tekst).Naam);

    [Fact]
    public void Exclusief_plus_btw_wordt_teruggezocht_op_de_factuur_als_het_totaal_geen_bekend_label_heeft()
    {
        // "Eindbedrag" is geen bekend label; onderaan staat een specificatie met "Totaal".
        var tekst = "Werkplaats Oost B.V.\nSubtotaal 141,28\nBTW 21% 29,67\nEindbedrag 170,95\nSpecificatie\nDocumenten   Prijs   Totaal\n22   0,18   3,96";

        var factuur = Factuurtekst.Analyseer(tekst);

        Assert.Equal((141.28m, 170.95m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
    }

    [Fact]
    public void Totaal_min_btw_wordt_teruggezocht_als_het_exclusieve_bedrag_geen_bekend_label_heeft()
    {
        // Het onderste "Totaal" (3,96) is van de specificatie; 170,95 min 29,67 staat op de factuur, 3,96 min 29,67 niet.
        var tekst = "Werkplaats Oost B.V.\nNetto 141,28\nBTW 21% 29,67\nTotaal 170,95\nSpecificatie\nDocumenten   Totaal\n22   0,18   3,96";

        var factuur = Factuurtekst.Analyseer(tekst);

        Assert.Equal((141.28m, 170.95m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
    }

    [Fact]
    public void Zonder_enig_bekend_totaal_twee_bedragen_die_met_de_btw_sluiten()
    {
        var tekst = "Werkplaats Oost B.V.\nOnderhoud bus   2 uur   100,00   200,00\nNetto 200,00\nBTW 21% 42,00\nEindbedrag 242,00";

        var factuur = Factuurtekst.Analyseer(tekst);

        Assert.Equal((200m, 242m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
    }

    [Fact]
    public void Btw_zonder_herkend_label_uit_drie_bedragen_die_sluiten()
    {
        var tekst = "Werkplaats Oost B.V.\nOnderhoud bus   200,00\nBelasting   42,00\nEindbedrag   242,00";

        var factuur = Factuurtekst.Analyseer(tekst);

        Assert.Equal((200m, 242m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
        Assert.Equal(new Btwregel(21m, 200m, 42m), Assert.Single(factuur.Btwregels));
    }

    [Theory]
    [InlineData("Lunch B.V.\nBroodjes   10,00\nSoep   5,00\nTotaal   15,00")]
    [InlineData("Fotografie Lens\nVrijgesteld van btw (KOR)\nShoot   200,00\nReiskosten   42,00\nTotaal   242,00")]
    [InlineData("Lunch B.V.\nBroodjes   200,00\nKoffie   42,00\nTaart   18,00\nTotaal   260,00")]
    public void Geen_btw_uit_een_toevallig_drietal(string tekst) =>
        Assert.DoesNotContain(Factuurtekst.Analyseer(tekst).Btwregels, b => b.Percentage > 0);

    [Fact]
    public void Eigen_domein_is_geen_leverancier()
    {
        var tekst = "Mail jan@marktvoorbeeld.nl\nTotaal 10,00";

        Assert.Null(Factuurtekst.Analyseer(tekst, eigenNamen: ["Markt Voorbeeld B.V."]).Naam);
    }
}
