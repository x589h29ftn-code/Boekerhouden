using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class BtwTests
{
    [Theory]
    [InlineData(100, 21, 21)]
    [InlineData(10.05, 21, 2.11)]
    [InlineData(-10.05, 21, -2.11)]
    [InlineData(33.33, 9, 3)]
    public void Btw_wordt_op_centen_afgerond(decimal grondslag, decimal percentage, decimal btw) =>
        Assert.Equal(btw, BtwBerekening.Btw(grondslag, percentage));

    [Fact]
    public void Uit_inclusief_geeft_samen_weer_het_bedrag()
    {
        var (grondslag, btw) = BtwBerekening.UitInclusief(24.99m, 21m);

        Assert.Equal(4.34m, btw);
        Assert.Equal(20.65m, grondslag);
        Assert.Equal(24.99m, grondslag + btw);
    }

    [Fact]
    public void Een_cent_verschil_per_regel_mag()
    {
        Assert.True(BtwBerekening.Klopt(100m, 21.01m, 21m));
        Assert.False(BtwBerekening.Klopt(100m, 21.02m, 21m));
        Assert.True(BtwBerekening.Klopt(100m, 21.02m, 21m, regels: 2));
    }

    [Fact]
    public void Standaardcode_bij_richting_en_tarief()
    {
        Assert.Equal("I21", BtwCodes.Bij(BtwCodes.Standaard, BtwRichting.Inkoop, 21m)?.Code);
        Assert.Equal("V9", BtwCodes.Bij(BtwCodes.Standaard, BtwRichting.Verkoop, 9m)?.Code);
        Assert.Equal("I0", BtwCodes.Bij(BtwCodes.Standaard, BtwRichting.Inkoop, 0m)?.Code);
    }
}

public sealed class FactuurboekerTests
{
    private static readonly DateOnly Datum = new(2026, 3, 10);

    [Fact]
    public void Inkoopfactuur_kosten_en_voorbelasting_debet_crediteuren_credit()
    {
        var factuur = Inkoop("F-1001", new Factuurregel("Abonnement maart", "4100", "I21", 20.66m, 4.34m));

        var (post, open) = Factuurboeker.Boek(factuur, Testadministratie.Inkoop, "2026-0001", Testadministratie.Schema(), Testadministratie.Btw);

        Assert.Equal(0m, post.Regels.Sum(r => r.Bedrag));
        Assert.Equal(-25m, Bedrag(post, "1600"));
        Assert.Equal(20.66m, Bedrag(post, "4100"));
        Assert.Equal(4.34m, Bedrag(post, "1510"));
        Assert.Equal("I21", post.Regels.Single(r => r.Rekeningnummer == "1510").BtwCode);
        Assert.Equal(-25m, open.Openstaand);
        Assert.Equal("F-1001", open.Factuurnummer);
    }

    [Fact]
    public void Verlegde_btw_staat_debet_en_credit_en_niet_op_de_factuur()
    {
        var factuur = Inkoop("EU-77", new Factuurregel("Software", "4200", "IEU", 100m));

        var (post, open) = Factuurboeker.Boek(factuur, Testadministratie.Inkoop, "2026-0002", Testadministratie.Schema(), Testadministratie.Btw);

        Assert.Equal(21m, Bedrag(post, "1510"));
        Assert.Equal(-21m, Bedrag(post, "1520"));
        Assert.Equal(-100m, open.Bedrag);
    }

    [Fact]
    public void Verkoopfactuur_debiteuren_debet_omzet_en_btw_credit_per_tarief()
    {
        var factuur = new Factuur(Factuursoort.Verkoop, "KLANT1", "2026-0001", Datum, Datum.AddDays(14), "Advies maart",
        [
            new Factuurregel("Advies", "8000", "V21", 500m),
            new Factuurregel("Advies extra", "8000", "V21", 250.50m),
            new Factuurregel("Boek", "8100", "V9", 20m),
        ], Betalingskenmerk: "1234567890123456");

        var (post, open) = Factuurboeker.Boek(factuur, Testadministratie.Verkoop, "2026-0001", Testadministratie.Schema(), Testadministratie.Btw);

        Assert.Equal(0m, post.Regels.Sum(r => r.Bedrag));
        Assert.Equal(-750.50m, Bedrag(post, "8000"));
        Assert.Equal(-(105m + 52.61m + 1.80m), Bedrag(post, "1520"));
        Assert.Equal(2, post.Regels.Count(r => r.Rekeningnummer == "1520"));
        Assert.Equal(929.91m, open.Openstaand);
        Assert.Equal("1234567890123456", open.Betalingskenmerk);
    }

    [Fact]
    public void Creditfactuur_draait_alles_om()
    {
        var factuur = Inkoop("CR-5", new Factuurregel("Retour", "4200", "I21", -50m));

        var (post, open) = Factuurboeker.Boek(factuur, Testadministratie.Inkoop, "2026-0003", Testadministratie.Schema(), Testadministratie.Btw);

        Assert.Equal(60.50m, Bedrag(post, "1600"));
        Assert.Equal(60.50m, open.Openstaand);
    }

    [Fact]
    public void Fouten_in_de_factuur_worden_gemeld()
    {
        var factuur = new Factuur(Factuursoort.Inkoop, "", "", Datum, Datum.AddDays(-1), "x", [new("x", "4100", "V21", 1m), new("x", "4100", "ZZZ", 1m)]);

        var fouten = Factuurboeker.Controleer(factuur, Testadministratie.Verkoop, Testadministratie.Btw);

        Assert.Contains("Een inkoopfactuur hoort in een inkoopdagboek.", fouten);
        Assert.Contains("Kies een relatie.", fouten);
        Assert.Contains("Vul het factuurnummer in.", fouten);
        Assert.Contains("Regel 1: btw-code V21 is voor de verkoop.", fouten);
        Assert.Contains("Regel 2: btw-code ZZZ bestaat niet.", fouten);
        Assert.Contains("De vervaldatum ligt vóór de factuurdatum.", fouten);
    }

    [Fact]
    public void Geboekte_factuur_komt_door_de_journaalcontrole()
    {
        var journaal = Testadministratie.Journaal();
        var (post, _) = Factuurboeker.Boek(Inkoop("F-1", new Factuurregel("Papier", "4200", "I21", 10m)), Testadministratie.Inkoop, "2026-0001", journaal.Schema, Testadministratie.Btw);

        journaal.Boek(post);

        Assert.Equal(-12.10m, journaal.Proefbalans(2026).Regels.Single(r => r.Rekening.Nummer == "1600").Saldo);
    }

    private static Factuur Inkoop(string nummer, params Factuurregel[] regels) =>
        new(Factuursoort.Inkoop, "KPN", nummer, Datum, Datum.AddDays(30), "Factuur " + nummer, regels);

    private static decimal Bedrag(Boekerhouden.Boekhouding.Grootboek.Journaalpost post, string rekening) =>
        post.Regels.Where(r => r.Rekeningnummer == rekening).Sum(r => r.Bedrag);
}

public sealed class RelatieTests
{
    [Theory]
    [InlineData("NL91ABNA0417164300", true)]
    [InlineData("nl91 abna 0417 1643 00", true)]
    [InlineData("NL91ABNA0417164301", false)]
    [InlineData("NL91ABNA041716430", false)]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("geen iban", false)]
    public void Iban_wordt_gecontroleerd(string iban, bool geldig) => Assert.Equal(geldig, Iban.IsGeldig(iban));

    [Fact]
    public void Iban_in_groepen_van_vier() => Assert.Equal("NL91 ABNA 0417 1643 00", Iban.Toon("nl91abna0417164300"));

    [Theory]
    [InlineData("NL001234567B01", false)]
    [InlineData("NL123456782B01", true)]
    [InlineData("NL 1234.56782 B01", true)]
    [InlineData("NL123456789B01", false)]
    [InlineData("BE0123456789", true)]
    public void Btw_nummer_wordt_gecontroleerd(string nummer, bool geldig) => Assert.Equal(geldig, BtwNummer.IsGeldig(nummer));

    [Theory]
    [InlineData("KPN B.V.", "kpn")]
    [InlineData("Bakkerij Él Pan V.O.F.", "bakkerij el pan")]
    [InlineData("  ", "")]
    public void Namen_zonder_rechtsvorm_en_leestekens(string naam, string verwacht) => Assert.Equal(verwacht, Namen.Normaliseer(naam));

    [Theory]
    [InlineData("KPN", "KPN Zakelijk B.V.", true)]
    [InlineData("kpn b.v.", "KPN", true)]
    [InlineData("Vodafone", "KPN", false)]
    [InlineData("AB", "AB Bouw", false)]
    public void Namen_lijken_op_elkaar(string a, string b, bool verwacht) => Assert.Equal(verwacht, Namen.LijktOp(a, b));
}
