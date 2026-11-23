using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Facturen zoals ze in de praktijk binnenkwamen en niet goed werden gelezen, nagebouwd met verzonnen namen en bedragen
/// in dezelfde opbouw: een tankpasfactuur met het totaal aantal liters boven de bedragen, een leverancier zonder
/// rechtsvorm met "Factuur" naast het adres, en een naam met het btw-nummer (met puntjes) direct erachter.
/// </summary>
public sealed class PraktijkfacturenTests
{
    [Fact]
    public void Tankpasfactuur_het_totaal_is_het_factuurbedrag_niet_het_aantal_liters()
    {
        const string Tekst = """
            HANDELSHUIS VOORBEELD BV
            Postbus 12
            1234 AB ERGENS
            Factuurnummer :    48120
            Debiteurnummer :    040111
            Factuurdatum    :    15-11-26
            Datum/Tijd    Station    Product    Km-stand Adv.Pr    Korting    Pompprijs    Volume    Korting    Bedrag
            31-10-26 23:58 BRANDSTOF NOORD TRANSACTIEKOST    0,0000    0,0000    0,031    150,20    0,00    4,66
            06-11-26 20:43 Pomp Oost    Euro 95    000000    2,0570    0,0000    1,726    80,10    26,47    138,25
            15-11-26 11:39 Pomp West    Euro 95    000000    1,9909    0,0000    1,718    70,10    19,10    120,43
            Totaal volume    Euro 95    150,20    Ltr.
            Totaalbedrag Excl    Bedrag Excl.    BTW Tarief    BTW Bedrag    Factuurbedrag
            €    263,34    €    263,34    21,00    €    55,30
            €    0,00    9,00    €    0,00
            €    318,64
            Het factuurbedrag zal worden afgeschreven van rekeningnummer NL20 INGB 0001 2345 67
            """;

        var factuur = Factuurtekst.Analyseer(Tekst, eigenNamen: ["Handelshuis Voorbeeld BV"], eigenIbans: ["NL20INGB0001234567"]);

        Assert.Equal((263.34m, 318.64m), (factuur.TotaalExclusief, factuur.TotaalInclusief));
        Assert.NotEqual("Factuurdatum", factuur.Naam);
    }

    [Fact]
    public void Leverancier_zonder_rechtsvorm_met_factuur_naast_het_adres()
    {
        const string Tekst = """
            Voorbeeld Systems
            Molenstraat 5 unit 45    Factuur
            1234 AB, Ergens
            Netherlands
            Tel : 012 3456789    Datum
            17/11/2026
            NL001234567B01
            Facturatie Adres
            Factuurnummer: 202610409
            Klant & Partners
            Kerkpad 7    Klanten Nummer:
            1234 CD Ergens,    Inkoop Order Number:
            Vervaldatum: 25/11/2026
            1 30/09/2026 Advies    Labor    0,50    65,00/hour    21,00 6,825    32,50
            Totaal excl. BTW    32,50
            BTW 21%    6,83
            Totaal incl. BTW    39,33
            """;

        Assert.Equal("Voorbeeld Systems", Factuurtekst.Analyseer(Tekst, eigenNamen: ["Klant & Partners"]).Naam);
    }

    [Fact]
    public void Naam_met_btw_label_erachter_en_btw_nummer_met_puntjes()
    {
        const string Tekst = """
            Salarisdienst Voorbeeld B.V. BTW    NL 1234.567.82.B01
            Hoofdstraat 13    KvK    12345678
            1234 AB Ergens    IBAN    NL20 INGB 0001 2345 67
            Klant & Partners
            Postbus 30    Factuurdatum    2-11-2026
            Factuurnummer 20262828
            Verwerking salarisadministratie    5.00    25,00    125,00
            Exclusief    125,00
            BTW 21%    26,25
            Te betalen    € 151,25
            """;

        var factuur = Factuurtekst.Analyseer(Tekst, eigenNamen: ["Klant & Partners"]);

        Assert.Equal("Salarisdienst Voorbeeld B.V.", factuur.Naam);
        Assert.Equal("NL123456782B01", factuur.BtwNummer);
    }
}
