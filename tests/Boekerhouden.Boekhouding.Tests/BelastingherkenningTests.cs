using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Tests.Gedeeld;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class BelastingherkenningTests
{
    private static readonly string[] Vier = ["btw", "loonheffingen", "vpb", "inkomstenbelasting"];

    private static Bankmutatie Betaling(string omschrijving, string? iban = Belastingherkenning.BelastingdienstIban, string? naam = "Belastingdienst", string? kenmerk = null) =>
        new(new DateOnly(2026, 4, 30), -840m, omschrijving, iban, naam, kenmerk);

    [Theory]
    [InlineData("8123.45.678.B.01 tijdvak 1e kwartaal", Belastingsoort.Omzetbelasting)]
    [InlineData("812345678L0126", Belastingsoort.Loonheffingen)]
    [InlineData("Aanslag 8123.45.678.V.56", Belastingsoort.Vennootschapsbelasting)]
    [InlineData("Aanslagnummer 1234.56.782.H.65", Belastingsoort.Inkomstenbelasting)]
    [InlineData("Aangifte omzetbelasting 2026-1", Belastingsoort.Omzetbelasting)]
    [InlineData("/REMI//CDTRREFTP//CD/SCOR/ISSR/CUR/CDTRREF/1123456781601270/ISDT/2026-10-30", Belastingsoort.Omzetbelasting)]
    [InlineData("LOONHEFFINGEN MAART", Belastingsoort.Loonheffingen)]
    [InlineData("VPB voorlopige aanslag", Belastingsoort.Vennootschapsbelasting)]
    [InlineData("Inkomstenbelasting 2025", Belastingsoort.Inkomstenbelasting)]
    public void Soort_uit_aanslagnummer_of_omschrijving(string omschrijving, Belastingsoort soort)
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

        Assert.Equal(soort, Belastingherkenning.Herken(Betaling(omschrijving), schema, Ondernemingsvorm.BV)?.Soort);
    }

    [Theory]
    [InlineData("1123456781601270", "1234.56.782.B.01.6270", 2026, "3e kwartaal")]
    [InlineData("0123456786601080", "1234.56.782.L.01.6080", 2026, "augustus")]
    [InlineData("9123456786501080", "1234.56.782.L.01.5080", 2025, "augustus")]
    [InlineData("2123456785602400", "1234.56.782.F.02.6400", 2026, "heel jaar")]
    [InlineData("1123456780607750", "1234.56.782.A.07.6750", 2026, "vierwekenperiode 5")]
    public void Betalingskenmerk_terug_naar_aanslagnummer(string kenmerk, string aanslag, int jaar, string tijdvak)
    {
        var gelezen = Belastingherkenning.Lees(kenmerk, new DateOnly(2026, 10, 30));

        Assert.Equal((aanslag, jaar, tijdvak), (gelezen?.Aanslagnummer, gelezen?.Jaar, gelezen?.Tijdvak));
    }

    [Theory]
    [InlineData("3123456787012345", 'H', Belastingsoort.Inkomstenbelasting)]
    [InlineData("2123456787512345", 'W', Belastingsoort.Inkomstenbelasting)]
    [InlineData("6123456789312345", 'V', Belastingsoort.Vennootschapsbelasting)]
    [InlineData("0123456787412345", 'V', Belastingsoort.Vennootschapsbelasting)]
    [InlineData("6123456788512345", 'Z', null)]
    public void Soort_uit_de_code_van_twee_cijfers(string kenmerk, char letter, Belastingsoort? soort)
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

        Assert.Equal(letter, Belastingherkenning.Lees(kenmerk, new DateOnly(2026, 1, 1))?.Letter);
        Assert.Equal(soort, Belastingherkenning.Herken(Betaling($"/CDTRREF/{kenmerk}"), schema, Ondernemingsvorm.BV)?.Soort);
    }

    [Theory]
    [InlineData("1000000547165445", true)]
    [InlineData("1123456781601270", true)]
    [InlineData("2123456781601270", false)]
    [InlineData("1123456781601273", false)]
    [InlineData("112345678160127", false)]
    public void Controlecijfer_modulus_11(string kenmerk, bool klopt)
    {
        Assert.Equal(klopt, Belastingherkenning.ControlecijferKlopt(kenmerk));
        if (!klopt)
        {
            Assert.Null(Belastingherkenning.Lees(kenmerk, new DateOnly(2026, 1, 1)));
        }
    }

    [Fact]
    public void Kenmerk_met_onbekende_letter_of_zonder_geldig_bsn_wordt_niet_gelezen_en_de_omschrijving_beslist()
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

        Assert.Null(Belastingherkenning.Lees("4123456783301080", new DateOnly(2026, 1, 1)));
        Assert.Null(Belastingherkenning.Lees("412345678601080", new DateOnly(2026, 1, 1)));
        var loon = Belastingherkenning.Herken(Betaling("/CDTRREF/0123456786601080/ISDT/2026-09-30"), schema, Ondernemingsvorm.BV)!;
        Assert.Equal((Belastingsoort.Loonheffingen, "1520", "Belastingdienst: loonheffingen augustus 2026, aanslag 1234.56.782.L.01.6080"), (loon.Soort, loon.Rekening?.Nummer, loon.Reden));
        Assert.Equal(Belastingsoort.Vennootschapsbelasting, Belastingherkenning.Herken(Betaling("Vpb 4123456783301080"), schema, Ondernemingsvorm.BV)?.Soort);
    }

    [Fact]
    public void Rekening_per_belasting_en_rechtsvorm()
    {
        var bv = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));
        var ez = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.Eenmanszaak));
        var ovpa = new Rekeningschema(Rekeningschemas.Rekeningen(Schemakeuze.Ovpa, Ondernemingsvorm.BV));
        string? Rekening(string omschrijving, Rekeningschema schema, Ondernemingsvorm vorm = Ondernemingsvorm.BV) =>
            Belastingherkenning.Herken(Betaling(omschrijving), schema, vorm)?.Rekening?.Nummer;

        Assert.Equal(["1510", "1520", "1550", "1560"], Vier.Select(o => Rekening(o, bv)));
        Assert.Equal("0520", Rekening("inkomstenbelasting", ez, Ondernemingsvorm.Eenmanszaak));
        // In het OVPA-schema de rekeningen "afgedragen …".
        Assert.Equal(["1820", "1735", "1780"], Vier.Take(3).Select(o => Rekening(o, ovpa)));
    }

    [Fact]
    public void Alleen_de_belastingdienst_en_alleen_met_een_soort()
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

        Assert.Null(Belastingherkenning.Herken(Betaling("btw", "NL36INGB0007654321", "Kantoorhandel Jansen"), schema, Ondernemingsvorm.BV));
        Assert.Null(Belastingherkenning.Herken(Betaling("Teruggaaf", naam: null), schema, Ondernemingsvorm.BV));
        Assert.NotNull(Belastingherkenning.Herken(Betaling("Omzetbelasting", iban: null, naam: "BELASTINGDIENST APELDOORN"), schema, Ondernemingsvorm.BV));
    }
}

public sealed class RekeningschemasTests
{
    [Fact]
    public void Het_ovpa_schema_heeft_bestaande_btw_codes_en_rgs_codes_en_elke_functie_een_keer()
    {
        var rekeningen = Rekeningschemas.Rekeningen(Schemakeuze.Ovpa, Ondernemingsvorm.BV);
        var rgs = RgsTestWerkboek.Regels.Select(r => r[0] as string).ToHashSet();

        Assert.Equal(377, rekeningen.Count);
        Assert.Equal(rekeningen.Count, rekeningen.Select(r => r.Nummer).Distinct().Count());
        Assert.All(rekeningen.Where(r => r.BtwCode is not null), r => Assert.NotNull(BtwCodes.Zoek(BtwCodes.Definities, r.BtwCode)));
        Assert.All(rekeningen.Where(r => r.RgsCode is not null), r => Assert.Contains(r.RgsCode, rgs));
        Assert.All(rekeningen, r => Assert.Equal(string.CompareOrdinal(r.Nummer, "4000") < 0, r.IsBalans));
        foreach (var functie in new[] { Rekeningfunctie.Bank, Rekeningfunctie.Kas, Rekeningfunctie.Debiteuren, Rekeningfunctie.Crediteuren, Rekeningfunctie.Voorbelasting, Rekeningfunctie.AfTeDragenBtw, Rekeningfunctie.OnverdeeldResultaat, Rekeningfunctie.Vraagposten })
        {
            Assert.Single(rekeningen, r => r.Functie == functie);
        }

        Assert.Equal(("I21", "V21", "IVL", "VEU"), (Btw("4100"), Btw("8000"), Btw("6021"), Btw("8300")));
        Assert.Equal(("1010", "1000"), (Dagboek(Dagboeksoort.Bank), Dagboek(Dagboeksoort.Kas)));

        string? Btw(string nummer) => rekeningen.Single(r => r.Nummer == nummer).BtwCode;
        string? Dagboek(Dagboeksoort soort) => Rekeningschemas.Dagboeken(Schemakeuze.Ovpa).Single(d => d.Soort == soort).Rekeningnummer;
    }

    [Fact]
    public void Het_schema_van_sifer_is_het_standaardschema() =>
        Assert.Equal(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.Eenmanszaak), Rekeningschemas.Rekeningen(Schemakeuze.Sifer, Ondernemingsvorm.Eenmanszaak));
}
