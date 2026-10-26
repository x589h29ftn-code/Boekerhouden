using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class FactuurnummersTests
{
    [Theory]
    [InlineData("2026-001", "Betaling factuur 2026-001", true)]
    [InlineData("2026-001", "factuur:2026/001 dank u", true)]
    [InlineData("2026-001", "FACTUUR2026001", true)]
    [InlineData("2026-001", "factuur 2026-0012", false)]
    [InlineData("2026-001", "factuur 12026-001", false)]
    [InlineData("F-1001", "betaling f1001", true)]
    [InlineData("0001001", "nota 1001", true)]
    [InlineData("12", "factuur 12", true)]
    [InlineData("12", "factuur 2012", false)]
    [InlineData("12", "", false)]
    public void Factuurnummer_in_de_omschrijving(string nummer, string tekst, bool verwacht) =>
        Assert.Equal(verwacht, Factuurnummers.KomtVoor(nummer, tekst));
}

public sealed class AfletteraarTests
{
    private const string IbanBelnet = "NL63TRIO0212345678";
    private const string IbanJansen = "NL36INGB0007654321";
    private const string IbanDeVries = "NL44RABO0123456789";
    private static readonly DateOnly Datum = new(2026, 3, 1);

    private static readonly Relatie[] Relaties =
    [
        new("BELNET", "Belnet B.V.", [IbanBelnet]),
        new("JANSEN", "Kantoorhandel Jansen", [IbanJansen]),
        new("DEVRIES", "J. de Vries", [IbanDeVries]),
        new("PLEIN", "Lunchroom Het Plein", []),
    ];

    [Fact]
    public void Factuurnummer_en_bedrag_is_zeker()
    {
        var post = Crediteur("BELNET", "BN-2026-0311", 25m);
        var mutatie = Betaling(-25m, "Factuur BN-2026-0311 maand maart", IbanBelnet);

        var advies = Afletteraar.Zoek(mutatie, [post, Crediteur("BELNET", "BN-2026-0211", 25m)], Relaties);

        Assert.Equal(post, Assert.Single(advies.Automatisch!.Posten));
        Assert.Equal("Factuurnummer", advies.Kandidaten[0].Reden);
    }

    [Fact]
    public void Iban_en_bedrag_zonder_factuurnummer_is_zeker_als_er_maar_een_post_is()
    {
        var post = Crediteur("JANSEN", "2026-114", 199.65m);
        var mutatie = Betaling(-199.65m, "Betaling inkoop", IbanJansen);

        var advies = Afletteraar.Zoek(mutatie, [post, Crediteur("JANSEN", "2026-090", 50m)], Relaties);

        Assert.Equal("IBAN en bedrag", advies.Automatisch?.Reden);
        Assert.Equal(post, advies.Automatisch!.Posten[0]);
    }

    [Fact]
    public void Iban_en_bedrag_bij_meerdere_posten_is_een_voorstel_oudste_eerst()
    {
        var oud = Crediteur("BELNET", "BN-1", 25m, Datum.AddMonths(-1));
        var nieuw = Crediteur("BELNET", "BN-2", 25m);

        var advies = Afletteraar.Zoek(Betaling(-25m, "Abonnement", IbanBelnet), [nieuw, oud], Relaties);

        Assert.Null(advies.Automatisch);
        Assert.Equal([oud, nieuw], advies.Kandidaten.Select(k => k.Posten[0]));
        Assert.All(advies.Kandidaten, k => Assert.Equal(Zekerheid.Voorstel, k.Zekerheid));
    }

    [Fact]
    public void Betalingskenmerk_is_zeker()
    {
        var post = Debiteur("DEVRIES", "2026-0003", 1210m) with { Betalingskenmerk = "1234 5678 9012 3456" };
        var mutatie = new Bankmutatie(Datum, 1210m, "1234567890123456", IbanDeVries, "J. de Vries", Betalingskenmerk: "1234567890123456");

        var advies = Afletteraar.Zoek(mutatie, [post], Relaties);

        Assert.Equal("Betalingskenmerk", advies.Automatisch?.Reden);
    }

    [Fact]
    public void Deelbetaling_met_factuurnummer_is_een_voorstel_met_het_verschil()
    {
        var post = Debiteur("PLEIN", "2026-0007", 300m);

        var advies = Afletteraar.Zoek(new Bankmutatie(Datum, 255m, "factuur 2026-0007", "NL39RABO0300065264", "Lunchroom Het Plein"), [post], Relaties);

        Assert.Null(advies.Automatisch);
        var kandidaat = Assert.Single(advies.Kandidaten);
        Assert.Equal("Factuurnummer, ander bedrag", kandidaat.Reden);
        Assert.Equal(45m, kandidaat.Verschil);
    }

    [Fact]
    public void Verzamelbetaling_van_meerdere_facturen()
    {
        var a = Crediteur("JANSEN", "A", 100m, Datum.AddDays(-30));
        var b = Crediteur("JANSEN", "B", 49.65m, Datum.AddDays(-20));
        var c = Crediteur("JANSEN", "C", 150m, Datum.AddDays(-10));

        var advies = Afletteraar.Zoek(Betaling(-199.65m, "Betaling", IbanJansen), [a, b, c], Relaties);

        var kandidaat = Assert.Single(advies.Kandidaten);
        Assert.Equal([b, c], kandidaat.Posten);
        Assert.Equal("IBAN en de som van 2 facturen", kandidaat.Reden);
        Assert.Equal(0m, kandidaat.Verschil);
    }

    [Fact]
    public void Naam_en_bedrag_zonder_bekend_iban_is_een_voorstel()
    {
        var post = Debiteur("PLEIN", "2026-0010", 80m);

        var advies = Afletteraar.Zoek(new Bankmutatie(Datum, 80m, "lunch", "NL39RABO0300065264", "LUNCHROOM HET PLEIN"), [post, Debiteur("DEVRIES", "X", 80m)], Relaties);

        var kandidaat = Assert.Single(advies.Kandidaten);
        Assert.Equal("Naam en bedrag", kandidaat.Reden);
        Assert.Null(advies.Automatisch);
    }

    [Fact]
    public void Alleen_een_uniek_bedrag_is_een_voorstel()
    {
        var post = Debiteur("DEVRIES", "2026-0004", 433.12m);

        var advies = Afletteraar.Zoek(new Bankmutatie(Datum, 433.12m, "overboeking"), [post, Debiteur("PLEIN", "Y", 100m)], Relaties);

        Assert.Equal("Alleen bedrag", Assert.Single(advies.Kandidaten).Reden);
    }

    [Fact]
    public void Ontvangst_wordt_nooit_afgeletterd_op_een_crediteur()
    {
        var advies = Afletteraar.Zoek(new Bankmutatie(Datum, 25m, "BN-2026-0311", IbanBelnet), [Crediteur("BELNET", "BN-2026-0311", 25m)], Relaties);

        Assert.Empty(advies.Kandidaten);
    }

    [Fact]
    public void Factuurnummer_van_een_andere_relatie_dan_het_iban_is_niet_zeker()
    {
        var post = Crediteur("BELNET", "2026-114", 50m);

        var advies = Afletteraar.Zoek(Betaling(-50m, "FACTUUR 2026-114", IbanJansen), [post], Relaties);

        Assert.Null(advies.Automatisch);
        Assert.Equal(Zekerheid.Voorstel, Assert.Single(advies.Kandidaten).Zekerheid);
    }

    [Fact]
    public void Twee_zekere_kandidaten_die_elkaar_tegenspreken_worden_niet_automatisch_geboekt()
    {
        var a = Crediteur("JANSEN", "2026-114", 50m);
        var b = Crediteur("JANSEN", "2026-200", 50m, Datum.AddDays(1)) with { Betalingskenmerk = "9999" };

        var advies = Afletteraar.Zoek(new Bankmutatie(Datum, -50m, "factuur 2026-114", IbanJansen, Betalingskenmerk: "9999"), [a, b], Relaties);

        Assert.Equal(2, advies.Kandidaten.Count(k => k.Zekerheid == Zekerheid.Zeker));
        Assert.Null(advies.Automatisch);
    }

    internal static OpenPost Crediteur(string relatie, string nummer, decimal bedrag, DateOnly? datum = null) =>
        new(Guid.NewGuid(), Factuursoort.Inkoop, relatie, nummer, datum ?? Datum, (datum ?? Datum).AddDays(30), -bedrag, -bedrag);

    internal static OpenPost Debiteur(string relatie, string nummer, decimal bedrag, DateOnly? datum = null) =>
        new(Guid.NewGuid(), Factuursoort.Verkoop, relatie, nummer, datum ?? Datum, (datum ?? Datum).AddDays(14), bedrag, bedrag);

    private static Bankmutatie Betaling(decimal bedrag, string omschrijving, string iban) => new(Datum, bedrag, omschrijving, iban);
}

public sealed class BankboekerTests
{
    private static readonly DateOnly Datum = new(2026, 3, 20);

    [Fact]
    public void Afletteren_boekt_bank_tegen_crediteuren_en_maakt_de_post_nihil()
    {
        var journaal = Testadministratie.Journaal();
        var post = AfletteraarTests.Crediteur("JANSEN", "2026-114", 199.65m);

        var (boeking, bijgewerkt) = Bankboeker.Afletteren(new Bankmutatie(Datum, -199.65m, "Betaling inkoop", TegenNaam: "Kantoorhandel Jansen"), [post], Testadministratie.Bank, "2026-0001", journaal.Schema);
        journaal.Boek(boeking);

        Assert.Equal(-199.65m, boeking.Regels.Single(r => r.Rekeningnummer == "1100").Bedrag);
        Assert.Equal(199.65m, boeking.Regels.Single(r => r.Rekeningnummer == "1600").Bedrag);
        Assert.True(Assert.Single(bijgewerkt).IsBetaald);
        Assert.Equal("Kantoorhandel Jansen – Betaling inkoop", boeking.Omschrijving);
    }

    [Fact]
    public void Deelbetaling_laat_een_rest_open_en_te_veel_gaat_naar_vraagposten()
    {
        var schema = Testadministratie.Schema();
        var post = AfletteraarTests.Debiteur("PLEIN", "2026-0007", 300m);

        var (_, deel) = Bankboeker.Afletteren(new Bankmutatie(Datum, 255m, "deel"), [post], Testadministratie.Bank, "1", schema);
        var (teVeel, _) = Bankboeker.Afletteren(new Bankmutatie(Datum, 310m, "te veel"), [post], Testadministratie.Bank, "2", schema);

        Assert.Equal(45m, deel[0].Openstaand);
        Assert.Equal(-300m, teVeel.Regels.Single(r => r.Rekeningnummer == "1300").Bedrag);
        Assert.Equal(-10m, teVeel.Regels.Single(r => r.Rekeningnummer == "2000").Bedrag);
        Assert.Equal(0m, teVeel.Regels.Sum(r => r.Bedrag));
    }

    [Fact]
    public void Op_rekening_met_btw_haalt_de_btw_uit_het_bedrag()
    {
        var journaal = Testadministratie.Journaal();
        var btw = BtwCodes.Zoek(Testadministratie.Btw, "I21");

        var boeking = Bankboeker.OpRekening(new Bankmutatie(Datum, -24.99m, "abonnement", TegenNaam: "Belnet B.V."), "4100", btw, Testadministratie.Bank, "2026-0002", journaal.Schema);
        journaal.Boek(boeking);

        Assert.Equal(20.65m, boeking.Regels.Single(r => r.Rekeningnummer == "4100").Bedrag);
        Assert.Equal(4.34m, boeking.Regels.Single(r => r.Rekeningnummer == "1510").Bedrag);
    }

    [Fact]
    public void Op_rekening_zonder_btw_en_een_verkeerd_dagboek()
    {
        var schema = Testadministratie.Schema();

        var boeking = Bankboeker.OpRekening(new Bankmutatie(Datum, -1.25m, "kosten"), "4300", null, Testadministratie.Bank, "3", schema);

        Assert.Equal(1.25m, boeking.Regels.Single(r => r.Rekeningnummer == "4300").Bedrag);
        Assert.Throws<ArgumentException>(() => Bankboeker.OpRekening(new Bankmutatie(Datum, -1m, "x"), "4300", null, Testadministratie.Memoriaal, "4", schema));
    }
}
