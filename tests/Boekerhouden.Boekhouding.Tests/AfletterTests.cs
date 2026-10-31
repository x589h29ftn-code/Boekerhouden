using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Regels;
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
    public void Klein_betaalverschil_alleen_met_nummer_en_iban_en_binnen_de_grens()
    {
        var post = Crediteur("BELNET", "BN-2026-0440", 24.99m);

        var teVeel = Afletteraar.Zoek(Betaling(-25m, "Factuur BN-2026-0440", IbanBelnet), [post], Relaties);
        var teWeinig = Afletteraar.Zoek(Betaling(-24.50m, "Factuur BN-2026-0440", IbanBelnet), [post], Relaties);
        var vreemd = Afletteraar.Zoek(Betaling(-25m, "Factuur BN-2026-0440", "NL02ABNA0123456789"), [post], Relaties);
        var precies = Afletteraar.Zoek(Betaling(-24.99m, "Factuur BN-2026-0440", IbanBelnet), [post], Relaties);

        Assert.Equal(0.01m, teVeel.KleinVerschil(1m)!.Verschil);
        Assert.Null(teVeel.KleinVerschil(null));
        Assert.Equal(-0.49m, teWeinig.KleinVerschil(1m)!.Verschil);
        Assert.Null(teWeinig.KleinVerschil(0.25m));
        Assert.Null(vreemd.KleinVerschil(1m));
        Assert.Null(precies.KleinVerschil(1m));
    }

    [Fact]
    public void Klein_betaalverschil_niet_bij_twee_facturen_al_betaald_of_korting_wel_bij_een_ontvangst()
    {
        var eerste = Crediteur("BELNET", "BN-2026-0440", 24.99m);
        var tweede = Crediteur("BELNET", "BN-2026-0441", 24.99m);
        var twee = Afletteraar.Zoek(Betaling(-50m, "Facturen BN-2026-0440 BN-2026-0441", IbanBelnet), [eerste, tweede], Relaties);
        var betaald = eerste with { Openstaand = 0m };
        var dubbel = Afletteraar.Zoek(Betaling(-25m, "Factuur BN-2026-0440", IbanBelnet), [betaald], Relaties);
        var korting = Afletteraar.Zoek(Betaling(-24.50m, "Factuur BN-2026-0440 2% korting", IbanBelnet), [eerste], Relaties);
        var ontvangst = Afletteraar.Zoek(new Bankmutatie(Datum, 120.98m, "Factuur 2026-0017", IbanDeVries), [Debiteur("DEVRIES", "2026-0017", 121m)], Relaties);

        Assert.Null(twee.KleinVerschil(1m));
        Assert.Null(dubbel.KleinVerschil(1m));
        Assert.Null(korting.KleinVerschil(1m));
        Assert.Equal(0.02m, ontvangst.KleinVerschil(0.05m)!.Verschil);
    }

    [Fact]
    public void Deelbetaling_met_factuurnummer_en_iban_is_zeker_zonder_iban_een_voorstel()
    {
        var post = Crediteur("BELNET", "BN-2026-0440", 3000m);

        var deel = Afletteraar.Zoek(Betaling(-1800m, "Factuur BN-2026-0440 deelbetaling 1 van 2", IbanBelnet), [post], Relaties);
        var onbekend = Afletteraar.Zoek(Betaling(-1800m, "Factuur BN-2026-0440 deelbetaling 1 van 2", "NL02ABNA0123456789"), [post], Relaties);
        var teVeel = Afletteraar.Zoek(Betaling(-3500m, "Factuur BN-2026-0440", IbanBelnet), [post], Relaties);

        Assert.Equal(("Factuurnummer, deelbetaling", -1200m, true), (deel.Automatisch!.Reden, deel.Automatisch.Verschil, deel.Automatisch.IsDeelbetaling));
        Assert.Null(onbekend.Automatisch);
        Assert.Equal("Factuurnummer, ander bedrag", Assert.Single(onbekend.Kandidaten).Reden);
        Assert.Null(teVeel.Automatisch);
    }

    [Fact]
    public void Een_betaling_van_een_factuur_die_al_betaald_is_wordt_een_voorstel_en_nooit_automatisch()
    {
        var betaald = Crediteur("BELNET", "BN-2026-0302", 600m) with { Openstaand = 0m };
        var ander = Crediteur("BELNET", "BN-2026-0303", 600m);
        var mutatie = Betaling(-600m, "Factuur BN-2026-0302", IbanBelnet);

        var advies = Afletteraar.Zoek(mutatie, [betaald, ander], Relaties);

        Assert.Null(advies.Automatisch);
        var dubbel = Assert.Single(advies.Kandidaten, k => k.IsAlBetaald);
        Assert.Equal(("Factuur is al betaald: dubbele betaling?", Zekerheid.Voorstel, betaald.Id), (dubbel.Reden, dubbel.Zekerheid, dubbel.Posten[0].Id));
        Assert.Empty(Afletteraar.Zoek(Betaling(-600m, "Factuur BN-2026-0302", IbanJansen), [betaald], Relaties).Kandidaten);
    }

    [Fact]
    public void Verzamelbetaling_met_de_nummers_wordt_zeker_gesplitst()
    {
        var posten = new[] { Crediteur("JANSEN", "GB-2026-1041", 375.10m), Crediteur("JANSEN", "GB-2026-1042", 151.86m), Crediteur("JANSEN", "GB-2026-1043", 106.48m), Crediteur("JANSEN", "GB-2026-1050", 99m) };

        var advies = Afletteraar.Zoek(Betaling(-633.44m, "Fact GB-2026-1041 1042 1043", IbanJansen), posten, Relaties);
        var zonderNummers = Afletteraar.Zoek(Betaling(-633.44m, "Betaling oktober", IbanJansen), posten, Relaties);

        Assert.Equal(["GB-2026-1041", "GB-2026-1042", "GB-2026-1043"], advies.Automatisch!.Posten.Select(p => p.Factuurnummer));
        Assert.Equal("Factuurnummers en de som van 3 posten", advies.Automatisch.Reden);
        Assert.Null(zonderNummers.Automatisch);
        Assert.Equal(3, Assert.Single(zonderNummers.Kandidaten).Posten.Count);
    }

    [Fact]
    public void Factuur_min_creditnota_in_een_betaling()
    {
        var factuur = Crediteur("JANSEN", "KH-2026-2201", 1210m);
        var creditnota = Crediteur("JANSEN", "KH-CN-2026-0033", -242m);

        var advies = Afletteraar.Zoek(Betaling(-968m, "Factuur KH-2026-2201 minus creditnota KH-CN-2026-0033", IbanJansen), [factuur, creditnota], Relaties);

        Assert.Equal([factuur.Id, creditnota.Id], advies.Automatisch!.Posten.Select(p => p.Id));
        var (boeking, bijgewerkt) = Bankboeker.Afletteren(advies.Mutatie, advies.Automatisch.Posten, Testadministratie.Bank, "1", Testadministratie.Schema());
        Assert.Equal([1210m, -242m], boeking.Regels.Where(r => r.Rekeningnummer == "1600").Select(r => r.Bedrag));
        Assert.All(bijgewerkt, p => Assert.True(p.IsBetaald));
    }

    [Fact]
    public void Betalingskorting_is_een_voorstel_en_het_verschil_gaat_met_btw_naar_een_rekening()
    {
        var post = Crediteur("JANSEN", "MS-2026-0780", 2420m);

        var advies = Afletteraar.Zoek(Betaling(-2371.60m, "MS-2026-0780 incl 2% korting", IbanJansen), [post], Relaties);

        Assert.Null(advies.Automatisch);
        var kandidaat = Assert.Single(advies.Kandidaten);
        Assert.Equal(("Factuurnummer, betalingskorting 2%", true, false), (kandidaat.Reden, kandidaat.Korting, kandidaat.IsDeelbetaling));
        var (boeking, bijgewerkt) = Bankboeker.Afletteren(advies.Mutatie, [post], Testadministratie.Bank, "1", Testadministratie.Schema(),
            new Afboeking("4100", BtwCodes.Zoek(Testadministratie.Btw, "I21"), "Betalingskorting"));
        Assert.Equal((2420m, -40m, -8.40m, 0m), (boeking.Regels.Single(r => r.Rekeningnummer == "1600").Bedrag, boeking.Regels.Single(r => r.Rekeningnummer == "4100").Bedrag,
            boeking.Regels.Single(r => r.Rekeningnummer == "1510").Bedrag, boeking.Regels.Sum(r => r.Bedrag)));
        Assert.True(Assert.Single(bijgewerkt).IsBetaald);
    }

    [Fact]
    public void Kosten_met_het_nummer_van_een_betaalde_factuur_zijn_geen_dubbele_betaling()
    {
        var betaald = Crediteur("BELNET", "CT-884201", 462.70m) with { Openstaand = 0m };

        Assert.Empty(Afletteraar.Zoek(Betaling(-12.50m, "Kosten betaling buitenland CT-884201", IbanBelnet), [betaald], Relaties).Kandidaten);
    }

    [Fact]
    public void Een_betaling_naar_een_ander_iban_dan_bekend_is_nooit_zeker()
    {
        var post = Crediteur("JANSEN", "FKF-2026-00455", 400m);

        var advies = Afletteraar.Zoek(Betaling(-400m, "Factuur FKF-2026-00455", "NL02ABNA0123456789"), [post], Relaties);

        Assert.Null(advies.Automatisch);
        Assert.Equal(("Factuurnummer", Zekerheid.Voorstel), (Assert.Single(advies.Kandidaten).Reden, advies.Kandidaten[0].Zekerheid));
    }

    [Fact]
    public void Iban_en_bedrag_met_een_ander_factuurnummer_uit_de_reeks_is_een_voorstel()
    {
        var post = Crediteur("BELNET", "9100234601", 99.99m);

        var ander = Afletteraar.Zoek(Betaling(-99.99m, "Betaling factuur 9100234603", IbanBelnet), [post], Relaties);
        var zonder = Afletteraar.Zoek(Betaling(-99.99m, "Abonnement september", IbanBelnet), [post], Relaties);

        Assert.Null(ander.Automatisch);
        Assert.Equal("IBAN en bedrag, maar een ander factuurnummer", Assert.Single(ander.Kandidaten).Reden);
        Assert.Equal("IBAN en bedrag", zonder.Automatisch!.Reden);
    }

    [Fact]
    public void Postzoeker_geeft_de_beste_vijf_op_nummer_bedrag_of_naam()
    {
        var posten = Enumerable.Range(1, 8).Select(i => Crediteur("BELNET", $"BN-2026-04{i:00}", 20m + i, Datum.AddDays(i)))
            .Append(Crediteur("JANSEN", "2026-114", 199.65m)).Append(Crediteur("JANSEN", "2026-115", 24.99m)).ToList();

        Assert.Equal(5, Postzoeker.Zoek("BN-2026", posten, Relaties).Count);
        Assert.Equal("2026-115", Postzoeker.Zoek("24,99", posten, Relaties)[0].Factuurnummer);
        Assert.Equal("BN-2026-0403", Postzoeker.Zoek("bn20260403", posten, Relaties)[0].Factuurnummer);
        Assert.Equal(["2026-114", "2026-115"], Postzoeker.Zoek("Kantoorhandel", posten, Relaties).Select(p => p.Factuurnummer));
        Assert.Equal("JANSEN", Postzoeker.Zoek("", posten, Relaties, new HashSet<string> { "JANSEN" })[0].Relatie);
        Assert.Empty(Postzoeker.Zoek("XYZ", posten, Relaties));
    }

    [Theory]
    [InlineData("INV-2026-0087", "inv 2026 87 installatie kantoor", true)]
    [InlineData("INV-2026-0087", "inv 2026 88", false)]
    [InlineData("2026-01", "januari 2026 1 stuk", false)]
    public void Factuurnummer_los_geschreven(string nummer, string tekst, bool verwacht) =>
        Assert.Equal(verwacht, Factuurnummers.KomtVoor(nummer, tekst));

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
        Assert.Equal("IBAN en de som van 2 posten", kandidaat.Reden);
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
    public void Deelbetaling_laat_een_rest_open_en_te_veel_blijft_op_de_post()
    {
        var schema = Testadministratie.Schema();
        var post = AfletteraarTests.Debiteur("PLEIN", "2026-0007", 300m);

        var (_, deel) = Bankboeker.Afletteren(new Bankmutatie(Datum, 255m, "deel"), [post], Testadministratie.Bank, "1", schema);
        var (teVeel, bijgewerkt) = Bankboeker.Afletteren(new Bankmutatie(Datum, 310m, "te veel"), [post], Testadministratie.Bank, "2", schema);
        var (dubbel, nogmaals) = Bankboeker.Afletteren(new Bankmutatie(Datum, 300m, "nog eens"), [post with { Openstaand = 0m }], Testadministratie.Bank, "3", schema);

        Assert.Equal(45m, deel[0].Openstaand);
        Assert.Equal([-300m, -10m], teVeel.Regels.Where(r => r.Rekeningnummer == "1300").Select(r => r.Bedrag));
        Assert.All(teVeel.Regels.Where(r => r.Rekeningnummer == "1300"), r => Assert.Equal(("PLEIN", "2026-0007"), (r.Relatie, r.Factuurnummer)));
        Assert.DoesNotContain(teVeel.Regels, r => r.Rekeningnummer == "2000");
        Assert.Equal(-10m, Assert.Single(bijgewerkt).Openstaand);
        Assert.Equal(0m, teVeel.Regels.Sum(r => r.Bedrag));
        Assert.Equal(-300m, dubbel.Regels.Single(r => r.Rekeningnummer == "1300").Bedrag);
        Assert.Equal(-300m, Assert.Single(nogmaals).Openstaand);
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

public sealed class BankverwerkerTests
{
    private const string IbanBank = "NL86INGB0002445588";
    private static readonly Relatie[] Relaties = [new("BELNET", "Belnet B.V.", ["NL63TRIO0212345678"])];
    private static readonly Verwerkingsregel Bankkosten = new("Bankkosten", "9000", Automatisch: true, Soort: Verwerkingssoort.Bankmutatie, TegenIban: IbanBank);

    private static Bankmutatie Kosten(decimal bedrag = -7.5m) => new(new DateOnly(2026, 3, 31), bedrag, "Kosten zakelijk pakket", IbanBank, "ING Bank");

    [Fact]
    public void Zekere_kandidaat_wordt_afgeletterd()
    {
        var post = new OpenPost(Guid.NewGuid(), Factuursoort.Inkoop, "BELNET", "BN-2026-0311", new(2026, 3, 1), new(2026, 3, 15), -25m, -25m);
        var mutatie = new Bankmutatie(new(2026, 3, 2), -25m, "Factuur BN-2026-0311", "NL63TRIO0212345678", "Belnet B.V.");

        var voorstel = Bankverwerker.Verwerk(mutatie, [post], Relaties, [], new Leergeheugen(), BtwCodes.Standaard);

        Assert.Equal(Bankactie.Afletteren, voorstel.Automatisch);
        Assert.Equal("BELNET", voorstel.Relatie?.Code);
        Assert.Equal("Afgeletterd: factuurnummer", voorstel.Reden);
    }

    [Fact]
    public void Automatische_regel_boekt_op_rekening_als_er_geen_openstaande_post_bij_past()
    {
        var voorstel = Bankverwerker.Verwerk(Kosten(), [], Relaties, [Bankkosten], new Leergeheugen(), BtwCodes.Standaard);

        Assert.Equal(Bankactie.OpRekening, voorstel.Automatisch);
        Assert.Equal(("9000", "Regel: Bankkosten"), (voorstel.Advies.Rekeningnummer, voorstel.Reden));
    }

    [Fact]
    public void Een_mogelijke_factuur_gaat_voor_op_een_automatische_regel()
    {
        var post = new OpenPost(Guid.NewGuid(), Factuursoort.Inkoop, "ING", "2026-03", new(2026, 3, 1), new(2026, 3, 31), -7.5m, -7.5m);

        var voorstel = Bankverwerker.Verwerk(Kosten(), [post], Relaties, [Bankkosten], new Leergeheugen(), BtwCodes.Standaard);

        Assert.Equal(Bankactie.Geen, voorstel.Automatisch);
        Assert.Equal("Voorstel: alleen bedrag", voorstel.Reden);
    }

    [Fact]
    public void Geleerd_na_de_drempel_en_weer_ter_controle_bij_een_hoger_bedrag()
    {
        var geheugen = new Leergeheugen(drempel: 2);
        var gegevens = Bankverwerker.Gegevens(Kosten(), null);
        geheugen.Onthoud(gegevens, new Keuze("9000", null));
        Assert.Equal(Bankactie.Geen, Bankverwerker.Verwerk(Kosten(), [], Relaties, [], geheugen, BtwCodes.Standaard).Automatisch);

        geheugen.Onthoud(gegevens, new Keuze("9000", null));
        Assert.Equal(Bankactie.OpRekening, Bankverwerker.Verwerk(Kosten(), [], Relaties, [], geheugen, BtwCodes.Standaard).Automatisch);
        Assert.Equal(Bankactie.Geen, Bankverwerker.Verwerk(Kosten(-50m), [], Relaties, [], geheugen, BtwCodes.Standaard).Automatisch);
    }
}
