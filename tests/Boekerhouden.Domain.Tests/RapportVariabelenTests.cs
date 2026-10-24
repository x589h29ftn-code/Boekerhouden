using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class RapportVariabelenTests
{
    private static int volgorde;

    private static RgsCode C(string code, string omschrijving, Saldokant kant = Saldokant.Debet) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            omschrijving, omschrijving, kant, null, ++volgorde, null, false, new HashSet<string> { "Basis" }, new HashSet<string>());

    private static readonly RgsSchema Schema = new("3.8",
    [
        C("B", "Balans"),
        C("BMva", "Materiële vaste activa"),
        C("BMvaInv", "Inventaris"),
        C("BLim", "Liquide middelen"),
        C("BLimBan", "Banken"),
        C("BEiv", "Eigen vermogen", Saldokant.Credit),
        C("BEivGok", "Gestort en opgevraagd kapitaal", Saldokant.Credit),
        C("W", "Winst-en-verliesrekening"),
        C("WOmz", "Netto-omzet", Saldokant.Credit),
        C("WOmzNop", "Netto-omzet producten", Saldokant.Credit),
        C("WKpr", "Kostprijs van de omzet"),
        C("WKprInk", "Inkoopwaarde"),
        C("WBel", "Belastingen"),
        C("WBelBel", "Vennootschapsbelasting"),
    ]);

    private static readonly Guid Inventaris = Guid.NewGuid(), Bank = Guid.NewGuid(), Kapitaal = Guid.NewGuid(), Omzet = Guid.NewGuid(), Inkoop = Guid.NewGuid(), Vpb = Guid.NewGuid();

    private static RapportGegevens Gegevens(decimal omzet = -50_000m, IReadOnlyList<Grondslag>? grondslagen = null, Dictionary<string, string>? toelichtingen = null, string? kvk = "12345678")
    {
        RekeningSaldo[] saldi =
        [
            new(Inventaris, "0200", "Inventaris", 10_000m, 12_000m),
            new(Bank, "1100", "Bank", -(omzet + 22_000m), 21_000m),
            new(Kapitaal, "0500", "Kapitaal", -10_000m, -10_000m),
            new(Omzet, "8000", "Omzet", omzet, -40_000m),
            new(Inkoop, "7000", "Inkoop", 20_000m, 16_000m),
            new(Vpb, "9500", "Vennootschapsbelasting", 2_000m, 1_000m),
        ];
        var koppelingen = new Dictionary<Guid, KoppelDoel>
        {
            [Inventaris] = new("BMvaInv"),
            [Bank] = new("BLimBan"),
            [Kapitaal] = new("BEivGok"),
            [Omzet] = new("WOmzNop"),
            [Inkoop] = new("WKprInk"),
            [Vpb] = new("WBelBel"),
        };
        var balans = PostBalans.Bereken(Schema, [], koppelingen, saldi);
        return new RapportGegevens(RapportSoort.Jaarrekening, "Voorbeeld BV", "Ergens", 2025, true, Schema, balans, saldi, [],
            code => Schema.Zoek(code)?.Omschrijving ?? code, new Dictionary<Guid, RekeningWeergave>(), new HashSet<string>(),
            Toelichtingen: toelichtingen, Grondslagen: grondslagen, Kvk: kvk);
    }

    [Fact]
    public void Bedragen_zijn_die_uit_het_rapport()
    {
        var g = Gegevens();
        var waarden = RapportVariabelen.Bepaal(g, RapportOpbouw.Balans(g));

        Assert.Equal("2025", waarden["boekjaar"]);
        Assert.Equal("2024", waarden["boekjaar_vorig"]);
        Assert.Equal("31 december 2025", waarden["einde_boekjaar"]);
        Assert.Equal("28.000", waarden["nettoresultaat"]);
        Assert.Equal("23.000", waarden["nettoresultaat_vorig"]);
        Assert.Equal("winst", waarden["winst_of_verlies"]);
        Assert.Equal("30.000", waarden["resultaat_voor_belastingen"]);
        Assert.Equal("2.000", waarden["belastingen"]);
        Assert.Equal("50.000", waarden["netto_omzet"]);
        Assert.Equal("30.000", waarden["brutomarge"]);
        Assert.Equal("38.000", waarden["eigen_vermogen"]);
        Assert.Equal("38.000", waarden["balanstotaal"]);
        Assert.Equal("100,0%", waarden["solvabiliteit"]);
        Assert.Equal("12345678", waarden["kvk"]);
    }

    [Fact]
    public void Klantenkaart_in_de_variabelen_met_terugval_op_naam_en_plaats()
    {
        var leeg = Gegevens();
        var kaart = leeg with { Kaart = new Klantkaart("Dorpsstraat 12 A", "1234 AB", "123456789", "47.72", "Voorbeeld Holding B.V.", "Amsterdam", new DateOnly(2020, 6, 28)) };

        var zonder = RapportVariabelen.Bepaal(leeg, RapportOpbouw.Balans(leeg));
        var met = RapportVariabelen.Bepaal(kaart, RapportOpbouw.Balans(kaart));

        Assert.Equal(("[vestigingsadres]", "[RSIN]", "Voorbeeld BV", "Ergens", "[datum oprichting]"),
            (zonder["adres"], zonder["rsin"], zonder["statutaire_naam"], zonder["statutaire_zetel"], zonder["oprichtingsdatum"]));
        Assert.Equal(("Dorpsstraat 12 A", "1234 AB", "123456789", "47.72"), (met["adres"], met["postcode"], met["rsin"], met["sbi"]));
        Assert.Equal(("Voorbeeld Holding B.V.", "Amsterdam", "28 juni 2020"), (met["statutaire_naam"], met["statutaire_zetel"], met["oprichtingsdatum"]));
        Assert.All(RapportVariabelen.Lijst.Where(v => !v.Naam.Contains(':', StringComparison.Ordinal)), v => Assert.True(met.ContainsKey(v.Naam), v.Naam));
    }

    [Fact]
    public void Een_verlies_heeft_een_minteken_en_het_woord_verlies()
    {
        var g = Gegevens(omzet: -10_000m);
        var waarden = RapportVariabelen.Bepaal(g, RapportOpbouw.Balans(g));

        Assert.Equal("-12.000", waarden["nettoresultaat"]);
        Assert.Equal("12.000", waarden["nettoresultaat_absoluut"]);
        Assert.Equal("verlies", waarden["winst_of_verlies"]);
    }

    [Fact]
    public void Posten_en_onbekende_variabelen()
    {
        var g = Gegevens(kvk: null);
        var waarden = RapportVariabelen.Bepaal(g, RapportOpbouw.Balans(g));
        var onbekend = new List<string>();

        var tekst = RapportVariabelen.Vul("Inventaris {valuta} {post:BMva} (vorig jaar {post_vorig:BMva}); {kvk}; {foo} en {post:BXyz}.", g, waarden, onbekend);

        Assert.Equal("Inventaris € 10.000 (vorig jaar 12.000); [KvK-nummer]; {foo} en {post:BXyz}.", tekst);
        Assert.Equal(["{foo}", "{post:BXyz}"], onbekend);
    }

    [Fact]
    public void Het_rapport_vult_toelichtingen_en_grondslagen_in_en_onthoudt_de_ingevoerde_tekst()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(
            grondslagen: [new("vestiging", "Algemeen", "Vestiging", "{naam} is ingeschreven onder nummer {kvk}.", GrondslagStandaard.Altijd, [])],
            toelichtingen: new() { ["BEiv"] = "Het ingehouden deel van het resultaat over {boekjaar} bedraagt {valuta} {nettoresultaat}.", ["WOmz"] = "Omzet in {boekjaar}: [omschrijving]. {onbekend}" },
            kvk: null));

        var blokken = rapport.Hoofdstukken.SelectMany(h => h.Blokken).ToList();
        var eigen = blokken.OfType<TekstBlok>().Single(b => b.Toelichting == "BEiv");
        Assert.Equal("Het ingehouden deel van het resultaat over 2025 bedraagt € 28.000.", eigen.Tekst);
        Assert.Equal("Het ingehouden deel van het resultaat over {boekjaar} bedraagt {valuta} {nettoresultaat}.", eigen.Sjabloon);
        var vestiging = blokken.OfType<GrondslagBlok>().Single();
        Assert.Equal("Voorbeeld BV is ingeschreven onder nummer [KvK-nummer].", vestiging.Tekst);
        Assert.Equal("{naam} is ingeschreven onder nummer {kvk}.", vestiging.Sjabloon);

        Assert.Contains("Vestiging: tekst tussen [ ] nog invullen.", rapport.Waarschuwingen);
        Assert.Contains("Toelichting Netto-omzet: tekst tussen [ ] nog invullen.", rapport.Waarschuwingen);
        Assert.Contains("Onbekende variabele {onbekend} in een tekst.", rapport.Waarschuwingen);
        Assert.Equal("28.000", rapport.Variabelen["nettoresultaat"]);
    }

    [Fact]
    public void Een_tekst_zonder_variabelen_heeft_geen_sjabloon()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(toelichtingen: new() { ["BMva"] = "Er is een nieuwe oven gekocht." }));

        var blok = rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TekstBlok>().Single(b => b.Toelichting == "BMva");
        Assert.Null(blok.Sjabloon);
    }

    [Fact]
    public void Elke_variabele_in_de_lijst_heeft_een_waarde_of_is_een_post()
    {
        var g = Gegevens();
        var waarden = RapportVariabelen.Bepaal(g, RapportOpbouw.Balans(g));

        Assert.All(RapportVariabelen.Lijst, v => Assert.True(waarden.ContainsKey(v.Naam) || v.Naam.EndsWith(":CODE", StringComparison.Ordinal), v.Naam));
    }
}
