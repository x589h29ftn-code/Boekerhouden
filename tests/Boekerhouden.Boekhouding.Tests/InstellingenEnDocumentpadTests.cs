using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Regels;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class VerwerkingsinstellingenTests
{
    private static readonly Verwerkingsinstellingen Kantoor = new(Drempel: 3, Bandbreedte: 1.5m, Limiet: 500m, Betalingstermijn: 14, Adrescontrole: true, Documentmap: "/data/klanten/#administratie");

    [Fact]
    public void Administratie_volgt_het_kantoor_tenzij_ze_afwijkt()
    {
        Assert.Equal(Kantoor, AfwijkendeInstellingen.Geen.Op(Kantoor));

        var eigen = new AfwijkendeInstellingen(Drempel: 1, Adrescontrole: false, LimietAfwijkend: true, Limiet: null).Op(Kantoor);

        Assert.Equal(Kantoor with { Drempel = 1, Adrescontrole = false, Limiet = null }, eigen);
    }

    [Fact]
    public void De_omschrijving_van_de_administratie_gaat_voor_op_die_van_het_kantoor()
    {
        var kantoor = Kantoor with { Omschrijving = "#leverancier #factuurnummer", OmschrijvingVerkoop = "Verkoop #klant" };

        Assert.Equal(("#leverancier #factuurnummer", "Verkoop #klant"), (AfwijkendeInstellingen.Geen.Op(kantoor).Omschrijving, AfwijkendeInstellingen.Geen.Op(kantoor).OmschrijvingVerkoop));
        var eigen = new AfwijkendeInstellingen(Omschrijving: "#leverancier #maand", OmschrijvingVerkoop: " ").Op(kantoor);
        Assert.Equal(("#leverancier #maand", "Verkoop #klant"), (eigen.Omschrijving, eigen.OmschrijvingVerkoop));
    }

    [Fact]
    public void Volgorde_van_de_omschrijving_regel_relatie_geleerd_standaard()
    {
        var relatie = new Relaties.Relatie("BEL", "Belnet", [], Omschrijving: "Telefoon #maand");
        var zonder = relatie with { Omschrijving = null };
        var regel = new Verwerkingsadvies("4310", "I21", Adviesbron.Regel, true, "Regel", Omschrijving: "Regel #maand");
        var geleerd = new Verwerkingsadvies("4310", "I21", Adviesbron.Geleerd, false, "Geleerd", Omschrijving: "Geleerd #maand");

        Assert.Equal("Regel #maand", Herkenning.Inkoopverwerker.Sjabloon(regel, relatie, "Standaard"));
        Assert.Equal("Telefoon #maand", Herkenning.Inkoopverwerker.Sjabloon(geleerd, relatie, "Standaard"));
        Assert.Equal("Geleerd #maand", Herkenning.Inkoopverwerker.Sjabloon(geleerd, zonder, "Standaard"));
        Assert.Equal("Standaard", Herkenning.Inkoopverwerker.Sjabloon(regel with { Omschrijving = null }, zonder, "Standaard"));
        Assert.Equal("Telefoon #maand", Herkenning.Inkoopverwerker.Sjabloon(null, relatie, null));
        Assert.Null(Herkenning.Inkoopverwerker.Sjabloon(null, null, null));
    }

    [Fact]
    public void Lege_limiet_zonder_afwijken_volgt_het_kantoor() =>
        Assert.Equal(500m, new AfwijkendeInstellingen(Limiet: null).Op(Kantoor).Limiet);

    [Fact]
    public void Ongeldige_instellingen() =>
        Assert.Equal(
            [
                "Kies voor automatisch verwerken 1 tot 10 gelijke keuzes.",
                "Kies een bandbreedte van 1 tot 10 keer het hoogste bedrag.",
                "De limiet kan niet negatief zijn.",
                "Kies een betalingstermijn van 0 tot 365 dagen.",
                "De documentmap mag geen '..' bevatten.",
            ],
            new Verwerkingsinstellingen(0, 0.5m, -1, 400, true, "/data/../geheim").Controleer());

    [Fact]
    public void Bandbreedte_en_adrescontrole_uit_de_instellingen()
    {
        var factuur = new Verwerkingsgegevens(Verwerkingssoort.Inkoopfactuur, "KPN", "KPN B.V.", 40m, Postcodes: ["1234AB"]);
        var geheugen = new Leergeheugen(new Verwerkingsinstellingen(Drempel: 1, Bandbreedte: 2m, Adrescontrole: false));
        geheugen.Onthoud(factuur, new Keuze("4310", "I21"));

        Assert.True(geheugen.Voorstel(factuur with { Bedrag = 80m, Postcodes = ["9999ZZ"] })!.Automatisch);
        Assert.False(geheugen.Voorstel(factuur with { Bedrag = 80.01m })!.Automatisch);
    }
}

public sealed class DocumentpadTests
{
    private static readonly Documentgegevens Factuur = new("Bakkerij Voorbeeld B.V.", "Inkoop", new(2026, 4, 1), "Belnet B.V.", "BN-2026/0411", ".PDF");

    [Fact]
    public void Map_per_klant_boekjaar_en_soort()
    {
        var basis = Path.Combine(Path.GetTempPath(), "Klanten");

        // Windows staat geen punt aan het eind van een mapnaam toe.
        Assert.Equal(Path.Combine(basis, "Bakkerij Voorbeeld B.V", "2026", "Inkoop"), Documentpad.Map(basis + "/#administratie/#jaar/#soort", Factuur));
        Assert.Equal(Path.Combine(basis, "2026-Q2", "04"), Documentpad.Map(basis + "/#jaar-#kwartaal/#maand", Factuur));
        Assert.Equal("2026-04-01 Belnet B.V. BN-2026 0411.pdf", Documentpad.Bestandsnaam(Factuur));
    }

    [Fact]
    public void Een_naam_kan_nooit_buiten_de_map_schrijven()
    {
        var basis = Path.Combine(Path.GetTempPath(), "Klanten");
        var kwaad = Factuur with { Administratie = "../../Windows", Relatie = "..\\..\\system32" };

        var map = Documentpad.Map(basis + "/#administratie/#leverancier", kwaad);

        Assert.StartsWith(basis, map, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(basis, "Windows", "system32"), map);
        Assert.Equal("NUL_", Documentpad.Veilig("NUL"));
        Assert.Equal("onbekend", Documentpad.Veilig(" .. "));
    }

    [Theory]
    [InlineData("C:\\Users\\naam\\OneDrive\\Klanten\\#administratie\\#jaar\\#soort", 0)]
    [InlineData("\\\\server\\klanten\\#administratie", 0)]
    [InlineData("Klanten\\#administratie", 1)]
    [InlineData("#administratie\\#jaar", 1)]
    [InlineData("C:\\Klanten\\#klant", 1)]
    [InlineData("", 0)]
    public void Controle_van_de_map(string map, int fouten) =>
        Assert.Equal(fouten, Documentpad.Controleer(map).Count);
}
