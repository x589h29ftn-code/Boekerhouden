using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class RekeningschemaTests
{
    [Fact]
    public void Rekeningen_staan_numeriek_op_volgorde()
    {
        var schema = new Rekeningschema(
        [
            new("1000", "Bank", Rekeningsoort.Balans),
            new("800", "Inventaris", Rekeningsoort.Balans),
            new("80", "Gebouwen", Rekeningsoort.Balans),
            new("A1", "Tekst", Rekeningsoort.Balans),
        ]);

        Assert.Equal(["80", "800", "1000", "A1"], schema.Alle.Select(r => r.Nummer));
    }

    [Fact]
    public void Dubbel_nummer_en_rgs_code_van_de_verkeerde_soort_worden_geweigerd()
    {
        var schema = Testadministratie.Schema();

        Assert.Throws<ArgumentException>(() => schema.Voegtoe(new("1100", "Nog een bank", Rekeningsoort.Balans)));
        var fout = Assert.Throws<ArgumentException>(() => schema.Voegtoe(new("4999", "Fout", Rekeningsoort.Resultaat, "BLimKasKas")));
        Assert.Contains("resultaatrekening", fout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rekening_op_functie()
    {
        var schema = Testadministratie.Schema();

        Assert.Equal("1600", schema.Enige(Rekeningfunctie.Crediteuren).Nummer);
        Assert.Throws<InvalidOperationException>(() => schema.Enige(Rekeningfunctie.Kas));
    }
}

public sealed class JournaalTests
{
    private static readonly DateOnly Januari = new(2026, 1, 15);

    [Fact]
    public void Journaalpost_in_evenwicht_wordt_geboekt()
    {
        var journaal = Testadministratie.Journaal();

        journaal.Boek(Memoriaal("2026-0001", Januari, ("0400", 1000m), ("0500", -1000m)));

        Assert.Single(journaal.Posten);
    }

    [Fact]
    public void Fouten_worden_allemaal_gemeld()
    {
        var journaal = Testadministratie.Journaal();
        var post = new Journaalpost("XXX", "", Januari, " ", [new("9999", 10m), new("0400", 0m), new("0500", -0.005m)]);

        var fouten = journaal.Controleer(post);

        Assert.Contains("Dagboek XXX bestaat niet.", fouten);
        Assert.Contains("Geef de boeking een boekstuknummer.", fouten);
        Assert.Contains("Geef de boeking een omschrijving.", fouten);
        Assert.Contains("Regel 1 heeft een onbekende rekening.", fouten);
        Assert.Contains("Regel 2 heeft geen bedrag.", fouten);
        Assert.Contains("Regel 3 heeft meer dan twee decimalen.", fouten);
        Assert.Contains(fouten, f => f.StartsWith("De boeking is niet in evenwicht", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => journaal.Boek(post));
    }

    [Fact]
    public void Boekstuk_mag_per_dagboek_maar_een_keer_voorkomen()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2026-0001", Januari, ("0400", 1m), ("0500", -1m)));

        var fouten = journaal.Controleer(Memoriaal("2026-0001", Januari, ("0400", 1m), ("0500", -1m)));

        Assert.Contains("Boekstuk 2026-0001 bestaat al in dagboek MEM.", fouten);
        Assert.Equal("2026-0002", journaal.VolgendBoekstuk("MEM", 2026));
        Assert.Equal("2027-0001", journaal.VolgendBoekstuk("MEM", 2027));
    }

    [Fact]
    public void Grootboekkaart_heeft_beginsaldo_lopend_saldo_en_eindsaldo()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(Memoriaal("2026-0002", new(2026, 2, 3), ("4300", 12.50m), ("1100", -12.50m)));
        journaal.Boek(Memoriaal("2026-0003", new(2026, 3, 3), ("4300", 7.50m), ("1100", -7.50m)));

        var kaart = journaal.Kaart("1100", new(2026, 2, 1), new(2026, 12, 31));

        Assert.Equal(5000m, kaart.Beginsaldo);
        Assert.Equal([4987.50m, 4980m], kaart.Mutaties.Select(m => m.Saldo));
        Assert.Equal(4980m, kaart.Eindsaldo);
        Assert.Equal(20m, kaart.TotaalCredit);
    }

    [Fact]
    public void Resultaatrekening_begint_elk_jaar_op_nul()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2025-0001", new(2025, 6, 1), ("4300", 100m), ("1100", -100m)));
        journaal.Boek(Memoriaal("2026-0001", new(2026, 6, 1), ("4300", 30m), ("1100", -30m)));

        var kaart = journaal.Kaart("4300", new(2026, 1, 1), new(2026, 12, 31));
        var bank = journaal.Kaart("1100", new(2026, 1, 1), new(2026, 12, 31));

        Assert.Equal(0m, kaart.Beginsaldo);
        Assert.Equal(30m, kaart.Eindsaldo);
        Assert.Equal(-100m, bank.Beginsaldo);
    }

    [Fact]
    public void Proefbalans_sluit_met_het_resultaat_van_eerdere_jaren_op_onverdeeld_resultaat()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2025-0001", new(2025, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(Memoriaal("2025-0002", new(2025, 6, 1), ("1100", 1200m), ("8000", -1200m)));
        journaal.Boek(Memoriaal("2026-0001", new(2026, 6, 1), ("4300", 30m), ("1100", -30m)));

        var proefbalans = journaal.Proefbalans(2026);

        Assert.Equal(0m, proefbalans.Saldo);
        Assert.Equal(-1200m, Saldo(proefbalans, "0590"));
        Assert.Equal(0m, Saldo(proefbalans, "8000"));
        Assert.Equal(6170m, Saldo(proefbalans, "1100"));
        Assert.Equal(-30m, proefbalans.Resultaat);
    }

    [Fact]
    public void Kolommenbalans_met_beginstand_mutaties_en_resultaat_die_alle_kolommen_sluit()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2025-0001", new(2025, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(Memoriaal("2025-0002", new(2025, 6, 1), ("1100", 1200m), ("8000", -1200m)));
        journaal.Boek(Memoriaal("2026-0001", new(2026, 3, 1), ("1100", 800m), ("8000", -800m)));
        journaal.Boek(Memoriaal("2026-0002", new(2026, 6, 1), ("4300", 30m), ("1100", -30m)));

        var koba = journaal.Kolommenbalans(2026);
        var proefbalans = journaal.Proefbalans(2026);

        // Bank: beginstand 6.200 debet, plus 800 debet en 30 credit in 2026.
        var bank = koba.Regels.Single(r => r.Rekening.Nummer == "1100");
        Assert.Equal((7000m, 30m, 6970m), (bank.Debet, bank.Credit, bank.BalansDebet));
        Assert.Equal(1200m, koba.Regels.Single(r => r.Rekening.Nummer == "0590").BalansCredit);
        Assert.Equal(800m, koba.Regels.Single(r => r.Rekening.Nummer == "8000").WinstEnVerliesCredit);

        // Elk saldo gelijk aan de proefbalans, en alle kolommen sluiten (met het resultaat).
        Assert.All(koba.Regels, r => Assert.Equal(Saldo(proefbalans, r.Rekening.Nummer), r.Saldo));
        Assert.Equal(koba.TotaalDebet, koba.TotaalCredit);
        Assert.Equal(koba.SaldoDebet, koba.SaldoCredit);
        Assert.Equal(770m, koba.Resultaat);
        Assert.Equal(koba.BalansDebet, koba.BalansCredit + koba.Resultaat);
        Assert.Equal(koba.WinstEnVerliesDebet + koba.Resultaat, koba.WinstEnVerliesCredit);
    }

    [Fact]
    public void Saldibalans_voor_het_samenstelpakket_heeft_de_rgs_codes()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(Memoriaal("2026-0002", new(2026, 6, 1), ("4300", 30m), ("1100", -30m)));

        var saldibalans = journaal.Proefbalans(2026).NaarSaldibalans(2026, "Bakkerij Voorbeeld");

        Assert.True(saldibalans.IsInEvenwicht);
        Assert.Equal(3, saldibalans.Regels.Count);
        Assert.Equal("WFbeOrlRkb", saldibalans.Regels.Single(r => r.Grootboeknummer == "4300").RgsCode);
    }

    internal static Journaalpost Memoriaal(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal", regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag)).ToList());

    private static decimal Saldo(Proefbalans proefbalans, string rekening) => proefbalans.Regels.Single(r => r.Rekening.Nummer == rekening).Saldo;
}

public sealed class JaaroverzichtTests
{
    [Fact]
    public void Balans_en_wv_per_rgs_hoofdpost_sluiten_met_het_resultaat()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(JournaalTests.Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(JournaalTests.Memoriaal("2026-0002", new(2026, 2, 1), ("1300", 1210m), ("8000", -1000m), ("1520", -210m)));
        journaal.Boek(JournaalTests.Memoriaal("2026-0003", new(2026, 3, 1), ("4100", 25m), ("4300", 5m), ("1100", -30m)));
        journaal.Boek(JournaalTests.Memoriaal("2026-0004", new(2026, 4, 1), ("1100", 100m), ("8100", -100m)));

        var overzicht = Jaaroverzicht.Maak(journaal.Proefbalans(2026), Testadministratie.Rgs());

        Assert.Equal(["BVor", "BLim"], overzicht.Activa.Select(p => p.Code));
        Assert.Equal(["BEiv", "BSch"], overzicht.Passiva.Select(p => p.Code));
        Assert.Equal(["WOmz", "WBed", "WFbe", null], overzicht.Resultaatposten.Select(p => p.Code));
        Assert.Equal(Jaaroverzicht.NietGekoppeld, overzicht.Resultaatposten[^1].Naam);
        Assert.Equal(1070m, overzicht.Resultaat);
        Assert.Equal(6280m, overzicht.TotaalActiva);
        Assert.Equal(overzicht.TotaalActiva, overzicht.TotaalPassiva);
    }

    [Fact]
    public void Zonder_rgs_staat_alles_onder_niet_gekoppeld_en_sluit_de_balans_toch()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(JournaalTests.Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(JournaalTests.Memoriaal("2026-0002", new(2026, 3, 1), ("4100", 25m), ("1100", -25m)));

        var overzicht = Jaaroverzicht.Maak(journaal.Proefbalans(2026), rgs: null);

        Assert.Equal(4975m, overzicht.TotaalActiva);
        Assert.Equal(overzicht.TotaalActiva, overzicht.TotaalPassiva);
        Assert.All(overzicht.Activa.Concat(overzicht.Passiva), p => Assert.Equal(Jaaroverzicht.NietGekoppeld, p.Naam));
    }
}

public sealed class RekeningzoekerTests
{
    private static readonly Rekening[] Rekeningen =
    [
        new("4300", "Kantoorbenodigdheden", Rekeningsoort.Resultaat),
        new("4310", "Telefoon en internet", Rekeningsoort.Resultaat),
        new("4330", "Contributies en abonnementen", Rekeningsoort.Resultaat),
        new("4900", "Algemene kosten", Rekeningsoort.Resultaat),
        new("8000", "Omzet diensten", Rekeningsoort.Resultaat),
    ];

    [Theory]
    [InlineData("43", "4300,4310,4330")]
    [InlineData("tele", "4310")]
    [InlineData("INTERNET", "4310")]
    [InlineData("abon", "4330")]
    [InlineData("kosten", "4900")]
    [InlineData("xyz", "")]
    public void Zoeken_op_nummer_of_naam(string tekst, string verwacht) =>
        Assert.Equal(verwacht, string.Join(",", Rekeningzoeker.Zoek(tekst, Rekeningen).Select(r => r.Nummer)));

    [Fact]
    public void Voorkeur_staat_bovenaan() =>
        Assert.Equal(["4900", "4310", "4300"], Rekeningzoeker.Zoek("", Rekeningen, ["4900", "4310"]).Take(3).Select(r => r.Nummer));

    [Theory]
    [InlineData("4310", "4310")]
    [InlineData("telef", "4310")]
    [InlineData("43", null)]
    [InlineData("", null)]
    public void De_enige_passende_rekening(string tekst, string? verwacht) =>
        Assert.Equal(verwacht, Rekeningzoeker.Enige(tekst, Rekeningen)?.Nummer);
}
