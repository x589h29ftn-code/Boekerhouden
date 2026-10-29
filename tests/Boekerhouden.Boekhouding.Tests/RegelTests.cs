using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class VerwerkingsregelTests
{
    private static readonly Verwerkingsregel Telefoon = new("KPN is telefoon", "4100", "I21", Automatisch: true,
        Soort: Verwerkingssoort.Inkoopfactuur, NaamBevat: "KPN", BedragVan: 10m, BedragTot: 30m);

    [Theory]
    [InlineData("KPN B.V.", 24.99, true)]
    [InlineData("K.P.N. B.V.", 10.00, true)]
    [InlineData("kpn zakelijk", 30.00, true)]
    [InlineData("KPN B.V.", 30.01, false)]
    [InlineData("KPN B.V.", 9.99, false)]
    [InlineData("Belnet B.V.", 24.99, false)]
    public void Naam_en_bedrag_tussen_grenzen(string naam, decimal bedrag, bool past) =>
        Assert.Equal(past, Telefoon.Past(new(Verwerkingssoort.Inkoopfactuur, null, naam, bedrag)));

    [Fact]
    public void Regel_voor_facturen_past_niet_op_de_bank()
    {
        Assert.False(Telefoon.Past(new(Verwerkingssoort.Bankmutatie, null, "KPN B.V.", -24.99m)));
    }

    [Fact]
    public void Bankregel_op_omschrijving_tegenrekening_en_richting()
    {
        var kosten = new Verwerkingsregel("Bankkosten", "4300", Automatisch: true, Soort: Verwerkingssoort.Bankmutatie, OmschrijvingBevat: "kosten betaalrekening", Richting: Regelrichting.Betaling);
        var belasting = new Verwerkingsregel("Belastingdienst", "1520", TegenIban: "NL86 INGB 0002 4455 88");

        Assert.True(kosten.Past(new(Verwerkingssoort.Bankmutatie, null, null, -1.25m, "ABN AMRO Bank N.V. KOSTEN BETAALREKENING")));
        Assert.False(kosten.Past(new(Verwerkingssoort.Bankmutatie, null, null, 1.25m, "Kosten betaalrekening, terugbetaling")));
        Assert.True(belasting.Past(new(Verwerkingssoort.Bankmutatie, null, null, -840m, "btw", "NL86INGB0002445588")));
    }

    [Fact]
    public void Regel_zonder_voorwaarden_past_nooit() =>
        Assert.False(new Verwerkingsregel("Leeg", "4100").Past(new(Verwerkingssoort.Inkoopfactuur, "KPN", "KPN", 10m)));
}

public sealed class LeergeheugenTests
{
    private static readonly Verwerkingsgegevens Factuur = new(Verwerkingssoort.Inkoopfactuur, "JANSEN", "Kantoorhandel Jansen", 121m, Btwpercentage: 21m);
    private static readonly Keuze Kantoor = new("4200", "I21");

    [Fact]
    public void Eerste_keer_kiezen_tweede_keer_voorstel_derde_keer_automatisch()
    {
        var geheugen = new Leergeheugen();
        Assert.Null(geheugen.Voorstel(Factuur));

        geheugen.Onthoud(Factuur, Kantoor);
        var tweede = geheugen.Voorstel(Factuur);
        Assert.Equal(Kantoor, tweede?.Keuze);
        Assert.False(tweede!.Automatisch);

        geheugen.Onthoud(Factuur, Kantoor);
        var derde = geheugen.Voorstel(Factuur);
        Assert.True(derde!.Automatisch);
        Assert.Equal(2, derde.OpRij);
    }

    [Fact]
    public void Een_afwijkende_keuze_begint_opnieuw()
    {
        var geheugen = new Leergeheugen();
        geheugen.Onthoud(Factuur, Kantoor);
        geheugen.Onthoud(Factuur, Kantoor);
        geheugen.Onthoud(Factuur, new Keuze("0400", "I21"));

        var voorstel = geheugen.Voorstel(Factuur)!;

        Assert.Equal("0400", voorstel.Keuze.Rekeningnummer);
        Assert.False(voorstel.Automatisch);
    }

    [Fact]
    public void Facturen_en_bank_worden_apart_onthouden_en_zonder_relatie_op_iban_of_naam()
    {
        var geheugen = new Leergeheugen();
        var bank = new Verwerkingsgegevens(Verwerkingssoort.Bankmutatie, null, "Gemeente Ergens", -312m, "aanslag", "NL39RABO0300065264");
        geheugen.Onthoud(Factuur, Kantoor);
        geheugen.Onthoud(bank, new Keuze("4500", null));

        Assert.Equal("4200", geheugen.Voorstel(Factuur)!.Keuze.Rekeningnummer);
        Assert.Equal("4500", geheugen.Voorstel(bank with { Naam = "GEMEENTE ERGENS" })!.Keuze.Rekeningnummer);
        Assert.Null(geheugen.Voorstel(new(Verwerkingssoort.Bankmutatie, null, null, -1m)));
        Assert.Equal("B|naam|gemeente ergens", Leergeheugen.Sleutel(bank with { TegenIban = null }));
    }

    [Fact]
    public void Zolang_het_pakket_leert_telt_elk_bedrag_mee_daarna_alleen_binnen_de_bandbreedte()
    {
        var geheugen = new Leergeheugen();
        geheugen.Onthoud(Factuur with { Bedrag = 30m }, Kantoor);
        geheugen.Onthoud(Factuur with { Bedrag = 45m }, Kantoor);

        var binnen = geheugen.Voorstel(Factuur with { Bedrag = 56.25m })!;
        var boven = geheugen.Voorstel(Factuur with { Bedrag = 56.26m })!;

        Assert.True(binnen.Automatisch);
        Assert.Equal(56.25m, binnen.Bovengrens);
        Assert.False(boven.Automatisch);
        Assert.Equal(Afwijkingssoort.Bedrag, Assert.Single(boven.Afwijkingen).Soort);
    }

    [Fact]
    public void Na_een_afwijking_opnieuw_leren_tot_de_drempel()
    {
        var geheugen = new Leergeheugen();
        var oud = Factuur with { TegenIban = "NL91ABNA0417164300", Postcodes = ["1234AB"] };
        var nieuwIban = oud with { TegenIban = "NL20INGB0001234567" };
        geheugen.Onthoud(oud, Kantoor);
        geheugen.Onthoud(oud, Kantoor);
        Assert.True(geheugen.Voorstel(oud)!.Automatisch);

        var gewijzigd = geheugen.Voorstel(nieuwIban)!;
        Assert.False(gewijzigd.Automatisch);
        Assert.Equal("Ander IBAN dan bij de eerdere facturen (NL20 INGB 0001 2345 67 in plaats van NL91 ABNA 0417 1643 00).", Assert.Single(gewijzigd.Afwijkingen).Melding);

        geheugen.Onthoud(nieuwIban, Kantoor);
        Assert.Equal((1, false), (geheugen.Voorstel(nieuwIban)!.OpRij, geheugen.Voorstel(nieuwIban)!.Automatisch));
        geheugen.Onthoud(nieuwIban, Kantoor);
        Assert.True(geheugen.Voorstel(nieuwIban)!.Automatisch);

        var verhuisd = geheugen.Voorstel(nieuwIban with { Postcodes = ["1234AB", "9999ZZ"] })!;
        Assert.Equal("Ander adres dan bij de eerdere facturen (postcode 9999 ZZ).", Assert.Single(verhuisd.Afwijkingen).Melding);
        Assert.True(geheugen.Voorstel(nieuwIban with { Postcodes = [] })!.Automatisch);
    }

    [Fact]
    public void Het_laatste_sjabloon_voor_de_omschrijving_wordt_onthouden()
    {
        var geheugen = new Leergeheugen();
        geheugen.Onthoud(Factuur, Kantoor, "Kantoorartikelen #maand");
        geheugen.Onthoud(Factuur, Kantoor);

        Assert.Equal("Kantoorartikelen #maand", geheugen.Voorstel(Factuur)!.Omschrijving);
    }

    [Fact]
    public void Drempel_is_instelbaar()
    {
        var geheugen = new Leergeheugen(drempel: 3);
        geheugen.Onthoud(Factuur, Kantoor);
        geheugen.Onthoud(Factuur, Kantoor);

        Assert.False(geheugen.Voorstel(Factuur)!.Automatisch);
    }
}

public sealed class VerwerkingsadviseurTests
{
    private static readonly Verwerkingsgegevens Factuur = new(Verwerkingssoort.Inkoopfactuur, "KPN", "KPN B.V.", 24.99m, Btwpercentage: 21m);

    [Fact]
    public void Regel_gaat_voor_geleerd()
    {
        var geheugen = new Leergeheugen();
        geheugen.Onthoud(Factuur, new Keuze("4200", "I21"));
        var regel = new Verwerkingsregel("KPN is telefoon", "4100", "I21", Automatisch: true, NaamBevat: "KPN", BedragVan: 10m, BedragTot: 30m);

        var advies = Verwerkingsadviseur.Advies(Factuur, [regel], geheugen, null, Testadministratie.Btw);

        Assert.Equal(("4100", "I21", Adviesbron.Regel, true), (advies.Rekeningnummer, advies.BtwCode, advies.Bron, advies.Automatisch));
        Assert.Equal("Regel: KPN is telefoon", advies.Reden);
    }

    [Fact]
    public void Zonder_regel_geleerd_en_daarna_de_standaardrekening_van_de_relatie()
    {
        var relatie = new Relatie("KPN", "KPN B.V.", [], Tegenrekening: "4100");

        var eerste = Verwerkingsadviseur.Advies(Factuur, [], new Leergeheugen(), relatie, Testadministratie.Btw);
        var zonderRelatie = Verwerkingsadviseur.Advies(Factuur, [], new Leergeheugen(), null, Testadministratie.Btw);

        Assert.Equal((Adviesbron.Relatie, "4100", "I21", false), (eerste.Bron, eerste.Rekeningnummer, eerste.BtwCode, eerste.Automatisch));
        Assert.Same(Verwerkingsadvies.Geen, zonderRelatie);
    }

    [Fact]
    public void Ander_tarief_op_de_factuur_past_de_btw_code_aan_en_verwerkt_niet_automatisch()
    {
        var geheugen = new Leergeheugen();
        geheugen.Onthoud(Factuur, new Keuze("4200", "I21"));
        geheugen.Onthoud(Factuur, new Keuze("4200", "I21"));

        var advies = Verwerkingsadviseur.Advies(Factuur with { Btwpercentage = 9m }, [], geheugen, null, Testadministratie.Btw);

        Assert.Equal("I9", advies.BtwCode);
        Assert.False(advies.Automatisch);
        Assert.Contains("9% btw in plaats van 21%", advies.Reden, StringComparison.Ordinal);
    }
}
