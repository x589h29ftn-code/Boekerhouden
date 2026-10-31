using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class VerkoopTests
{
    private static readonly IReadOnlyList<BtwCode> Codes = BtwCodes.Standaard;

    private static readonly Partij Eigen = new("Bakkerij Voorbeeld B.V.", new("Dorpsstraat 1", "1234 AB", "Ergens"), "NL001234567B01", "12345678");

    private static Verkoopfactuur Factuur(params Verkoopregel[] regels) =>
        new("KLANT", new Partij("Klant B.V.", new("Laan 2", "5678 CD", "Elders")), "20260001", new(2026, 3, 1), new(2026, 3, 31), "Maart", regels);

    [Fact]
    public void Nummer_is_jaar_en_volgnummer_van_vier_cijfers()
    {
        Assert.Equal("20260001", Factuurnummering.Nummer(2026, 1));
        Assert.Equal("20261234", Factuurnummering.Nummer(2026, 1234));
        Assert.Equal("202610000", Factuurnummering.Nummer(2026, 10000));
        Assert.Equal("F20260007", Factuurnummering.Nummer(2026, 7, "F"));
    }

    [Fact]
    public void Volgende_nummer_telt_door_binnen_het_jaar_en_begint_opnieuw_in_een_nieuw_jaar()
    {
        string[] uitgegeven = ["20250041", "20260001", "20260002", "20260003"];

        Assert.Equal("20260004", Factuurnummering.Volgende(2026, uitgegeven));
        Assert.Equal("20270001", Factuurnummering.Volgende(2027, uitgegeven));
        Assert.Equal("20250042", Factuurnummering.Volgende(2025, uitgegeven));
        Assert.Equal("20260001", Factuurnummering.Volgende(2026, []));
        Assert.Equal("202610000", Factuurnummering.Volgende(2026, ["20269999"]));
    }

    [Fact]
    public void Nummers_van_buiten_de_reeks_tellen_niet_mee()
    {
        Assert.Null(Factuurnummering.Volgnummer("2026-0001", 2026));
        Assert.Null(Factuurnummering.Volgnummer("F20260001", 2026));
        Assert.Equal(1, Factuurnummering.Volgnummer("F20260001", 2026, "F"));
        Assert.Equal("20260001", Factuurnummering.Volgende(2026, ["INV-99", "2026ABC"]));
    }

    [Fact]
    public void Gaten_in_de_reeks_worden_gevonden()
    {
        Assert.Equal([2, 4], Factuurnummering.Gaten(2026, ["20260001", "20260003", "20260005"]));
        Assert.Empty(Factuurnummering.Gaten(2026, ["20260001", "20260002"]));
    }

    [Fact]
    public void Betalingskenmerk_heeft_zestien_cijfers_met_een_kloppend_controlecijfer()
    {
        var kenmerk = Betalingskenmerk.Maak("20260001");

        Assert.Equal(16, kenmerk.Length);
        Assert.EndsWith("000000020260001", kenmerk, StringComparison.Ordinal);
        Assert.True(Betalingskenmerk.IsGeldig(kenmerk));
        Assert.True(Betalingskenmerk.IsGeldig(Betalingskenmerk.Toon(kenmerk)));
        Assert.Equal(19, Betalingskenmerk.Toon(kenmerk).Length);
    }

    [Fact]
    public void Controlecijfer_volgt_de_gewichten_van_rechts()
    {
        // 000000000000001: 1 × 2 = 2; 11 − 2 = 9.
        Assert.Equal("9000000000000001", Betalingskenmerk.Maak("1"));
        // 000000000000005: 5 × 2 = 10; 11 − 10 = 1.
        Assert.Equal("1000000000000005", Betalingskenmerk.Maak("5"));
        // 000000000000010: 1 × 4 = 4; 11 − 4 = 7.
        Assert.Equal("7000000000000010", Betalingskenmerk.Maak("10"));
    }

    [Fact]
    public void Een_veranderd_cijfer_maakt_het_kenmerk_ongeldig()
    {
        var kenmerk = Betalingskenmerk.Maak("20260042");
        var fout = kenmerk[..10] + (kenmerk[10] == '9' ? '0' : (char)(kenmerk[10] + 1)) + kenmerk[11..];

        Assert.False(Betalingskenmerk.IsGeldig(fout));
        Assert.False(Betalingskenmerk.IsGeldig("123"));
        Assert.False(Betalingskenmerk.IsGeldig(null));
    }

    [Fact]
    public void Regelbedrag_is_aantal_maal_prijs_min_korting()
    {
        Assert.Equal(255m, new Verkoopregel("Uren", 3m, 85m, "8000", "V21").Bedrag);
        Assert.Equal(229.5m, new Verkoopregel("Uren", 3m, 85m, "8000", "V21", Korting: 10m).Bedrag);
        Assert.Equal(4.17m, new Verkoopregel("Broodjes", 1.5m, 2.777m, "8010", "V9").Bedrag);
        Assert.Equal(-255m, new Verkoopregel("Credit", -3m, 85m, "8000", "V21").Bedrag);
    }

    [Fact]
    public void Btw_wordt_per_code_over_de_som_van_de_regels_berekend()
    {
        // Drie regels van 0,05: per regel 0,01 btw (samen 0,03), over de som 0,15 × 21% = 0,03; bij 0,07 per regel
        // wordt per regel 0,01 (0,0147) maar over 0,21 is het 0,04.
        var factuur = Factuur(
            new Verkoopregel("A", 1m, 0.07m, "8000", "V21"),
            new Verkoopregel("B", 1m, 0.07m, "8000", "V21"),
            new Verkoopregel("C", 1m, 0.07m, "8000", "V21"),
            new Verkoopregel("Brood", 2m, 10m, "8010", "V9"));

        var btw = factuur.Btw(Codes);

        Assert.Equal(2, btw.Count);
        Assert.Equal((0.21m, 0.04m), (btw[0].Grondslag, btw[0].Btw));
        Assert.Equal((20m, 1.8m), (btw[1].Grondslag, btw[1].Btw));
        Assert.Equal(22.05m, factuur.Totaal(Codes));
    }

    [Fact]
    public void De_boeking_volgt_de_btw_van_de_factuur_tot_op_de_cent()
    {
        var factuur = Factuur(
            new Verkoopregel("A", 1m, 0.07m, "8000", "V21"),
            new Verkoopregel("B", 1m, 0.07m, "8000", "V21"),
            new Verkoopregel("C", 1m, 0.07m, "8000", "V21"));

        var (post, open) = Factuurboeker.Boek(factuur.NaarFactuur(Codes), StandaardRekeningschema.Dagboeken.First(d => d.Soort == Dagboeksoort.Verkoop), "20260001",
            new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV)), Codes);

        Assert.Equal(0m, post.Regels.Sum(r => r.Bedrag));
        Assert.Equal(0.25m, post.Regels.Single(r => r.Rekeningnummer == "1300").Bedrag);
        Assert.Equal(-0.04m, post.Regels.Single(r => r.Rekeningnummer == "1510").Bedrag);
        Assert.Equal(-0.21m, post.Regels.Where(r => r.Rekeningnummer == "8000").Sum(r => r.Bedrag));
        Assert.Equal(0.25m, open.Bedrag);
        Assert.Equal(Factuursoort.Verkoop, open.Soort);
    }

    [Fact]
    public void Verlegd_en_icp_geven_geen_btw_en_een_vermelding()
    {
        var factuur = Factuur(new Verkoopregel("Montage", 10m, 50m, "8000", "VVL"), new Verkoopregel("Levering", 1m, 1000m, "8010", "VEU"));

        Assert.Equal(0m, factuur.TotaalBtw(Codes));
        Assert.Equal(1500m, factuur.Totaal(Codes));
        var vermeldingen = Verkoopcontrole.Vermeldingen(factuur, Codes);
        Assert.Contains("Btw verlegd.", vermeldingen);
        Assert.Contains(vermeldingen, v => v.Contains("Intracommunautaire levering", StringComparison.Ordinal));
    }

    [Fact]
    public void Een_complete_factuur_is_goed()
    {
        Assert.Empty(Verkoopcontrole.Controleer(Factuur(new Verkoopregel("Uren", 3m, 85m, "8000", "V21")), Eigen, Codes));
    }

    [Fact]
    public void Zonder_eigen_adres_of_btw_nummer_kan_het_niet()
    {
        var factuur = Factuur(new Verkoopregel("Uren", 3m, 85m, "8000", "V21"));

        Assert.Contains(Verkoopcontrole.Controleer(factuur, Eigen with { Adres = new() }, Codes), f => f.Contains("adres van de eigen onderneming", StringComparison.Ordinal));
        Assert.Contains(Verkoopcontrole.Controleer(factuur, Eigen with { BtwNummer = null }, Codes), f => f.Contains("btw-nummer van de eigen onderneming", StringComparison.Ordinal));
        // Alleen vrijgestelde prestaties: geen btw-nummer nodig.
        Assert.Empty(Verkoopcontrole.Controleer(Factuur(new Verkoopregel("Les", 1m, 50m, "8000", "VVR")), Eigen with { BtwNummer = null }, Codes));
    }

    [Fact]
    public void Klant_regels_en_codes_worden_gecontroleerd()
    {
        var fouten = Verkoopcontrole.Controleer(
            Factuur(new Verkoopregel("", 1m, 10m, "8000", "V21"), new Verkoopregel("Iets", 1m, 10m, "8000", "I21"), new Verkoopregel("Korting", 1m, 10m, "8000", "V21", Korting: 120m))
                with { Klant = new Partij("Klant B.V.", new()), Vervaldatum = new(2026, 2, 1) },
            Eigen, Codes);

        Assert.Contains(fouten, f => f.Contains("adres van Klant B.V.", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.StartsWith("Regel 1: vul de omschrijving", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.StartsWith("Regel 2: btw-code I21 is voor de inkoop", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.StartsWith("Regel 3: de korting", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("vervaldatum", StringComparison.Ordinal));
        Assert.Contains(Verkoopcontrole.Controleer(Factuur(), Eigen, Codes), f => f.Contains("geen regels", StringComparison.Ordinal));
    }

    [Fact]
    public void Verlegd_vraagt_een_geldig_btw_nummer_van_de_klant()
    {
        var factuur = Factuur(new Verkoopregel("Montage", 10m, 50m, "8000", "VVL"));

        Assert.Contains(Verkoopcontrole.Controleer(factuur, Eigen, Codes), f => f.Contains("verlegde btw", StringComparison.Ordinal));
        Assert.Empty(Verkoopcontrole.Controleer(factuur with { Klant = factuur.Klant with { BtwNummer = "NL123456782B01" } }, Eigen, Codes));
    }

    [Fact]
    public void Icp_vraagt_een_btw_nummer_uit_een_ander_eu_land()
    {
        var factuur = Factuur(new Verkoopregel("Levering", 1m, 1000m, "8010", "VEU"));

        Assert.Contains(Verkoopcontrole.Controleer(factuur, Eigen, Codes), f => f.Contains("binnen de EU", StringComparison.Ordinal));
        Assert.Contains(Verkoopcontrole.Controleer(factuur with { Klant = factuur.Klant with { BtwNummer = "NL123456782B01" } }, Eigen, Codes), f => f.Contains("ander EU-land", StringComparison.Ordinal));
        Assert.Contains(Verkoopcontrole.Controleer(factuur with { Klant = factuur.Klant with { BtwNummer = "GB123456789" } }, Eigen, Codes), f => f.Contains("ander EU-land", StringComparison.Ordinal));
        Assert.Empty(Verkoopcontrole.Controleer(factuur with { Klant = factuur.Klant with { BtwNummer = "BE0123456789" } }, Eigen, Codes));
    }

    [Fact]
    public void Adres_toont_het_land_alleen_buiten_nederland()
    {
        Assert.Equal(["Laan 2", "5678 CD  Elders"], new Adres("Laan 2", "5678 CD", "Elders").Regels);
        Assert.Equal(["Rue 1", "1000  Brussel", "België"], new Adres("Rue 1", "1000", "Brussel", "be").Regels);
    }
}
