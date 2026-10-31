using System.Globalization;
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
    public void Grootboekkaart_toont_per_regel_de_btw_code_en_de_btw_uit_dezelfde_boeking()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(new Journaalpost("MEM", "2026-0001", new(2026, 3, 1), "Inkoop",
        [
            new Journaalregel("4300", 100m, "Papier", "I21"),
            new Journaalregel("4300", 50.01m, "Pennen", "I21"),
            new Journaalregel("4300", 20m, "Zonder btw"),
            new Journaalregel("1510", 31.50m, "Btw", "I21"),
            new Journaalregel("1100", -201.51m),
        ]));

        var kaart = journaal.Kaart("4300", new(2026, 1, 1), new(2026, 12, 31));
        var btw = journaal.Kaart("1510", new(2026, 1, 1), new(2026, 12, 31));

        Assert.Equal([("I21", 21m), ("I21", 10.50m), (null, null)], kaart.Mutaties.Select(m => (m.BtwCode, m.Btw)));
        Assert.Equal(31.50m, kaart.Mutaties.Sum(m => m.Btw ?? 0));
        Assert.Equal(("I21", (decimal?)null), (btw.Mutaties[0].BtwCode, btw.Mutaties[0].Btw));
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
    public void Kolommenbalans_per_maand_met_begin_mutaties_en_eind_gelijk_aan_het_totaal()
    {
        var journaal = Testadministratie.Journaal();
        journaal.Boek(Memoriaal("2025-0001", new(2025, 1, 1), ("1100", 5000m), ("0500", -5000m)));
        journaal.Boek(Memoriaal("2025-0002", new(2025, 6, 1), ("1100", 1200m), ("8000", -1200m)));
        journaal.Boek(Memoriaal("2026-0001", new(2026, 3, 1), ("1100", 800m), ("8000", -800m)));
        journaal.Boek(Memoriaal("2026-0002", new(2026, 3, 31), ("4200", 30m), ("1100", -30m)));
        journaal.Boek(Memoriaal("2026-0003", new(2026, 12, 31), ("4200", 20m), ("1100", -20m)));

        var maanden = journaal.Maandoverzicht(new(2026, 1, 1), new(2026, 12, 31));
        var koba = journaal.Kolommenbalans(2026);

        Assert.Equal(12, maanden.Maanden.Count);
        var bank = maanden.Regels.Single(r => r.Rekening.Nummer == "1100");
        Assert.Equal((6200m, 770m, 0m, -20m, 6950m), (bank.Begin, bank.PerMaand[2], bank.PerMaand[3], bank.PerMaand[11], bank.Eind));
        Assert.Equal(-1200m, maanden.Regels.Single(r => r.Rekening.Nummer == "0590").Begin);
        Assert.Equal((0m, -800m), (maanden.Regels.Single(r => r.Rekening.Nummer == "8000").Begin, maanden.Regels.Single(r => r.Rekening.Nummer == "8000").Eind));
        Assert.All(maanden.Regels, r => Assert.Equal(koba.Regels.Single(k => k.Rekening == r.Rekening).Saldo, r.Eind));
        Assert.Equal((770m, -20m), (maanden.ResultaatPerMaand[2], maanden.ResultaatPerMaand[11]));
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)), maanden.Periode(3));

        // Een periode midden in een maand: de eerste en laatste maand alleen binnen de periode.
        var deel = journaal.Maandoverzicht(new(2026, 3, 15), new(2026, 4, 10));
        Assert.Equal([new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1)], deel.Maanden);
        Assert.Equal((7000m, -30m), (deel.Regels.Single(r => r.Rekening.Nummer == "1100").Begin, deel.Regels.Single(r => r.Rekening.Nummer == "1100").PerMaand[0]));
        Assert.Equal((new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 31)), deel.Periode(0));
        Assert.Throws<ArgumentException>(() => journaal.Maandoverzicht(new(2024, 1, 1), new(2026, 12, 31)));
    }

    [Fact]
    public void Uitgebreide_indeling_volgt_de_rgs_hoofdpost()
    {
        var schema = Testadministratie.Schema();

        Assert.Equal(("Activa", "Vaste activa", "Materiële vaste activa"), Indeling(schema, "0400"));
        Assert.Equal(("Activa", "Vlottende activa", "Liquide middelen"), Indeling(schema, "1100"));
        Assert.Equal(("Passiva", null, "Eigen vermogen"), Indeling(schema, "0500"));
        Assert.Equal(("Passiva", null, "Kortlopende schulden"), Indeling(schema, "1600"));
        Assert.Equal(("Winst- en verliesrekening", "Overige bedrijfskosten", "Kantoorkosten"), Indeling(schema, "4200"));
        Assert.Equal(("Passiva", null, "Niet ingedeeld (balans)"), Indeling(schema, "2000"));
        Assert.Equal(("Winst- en verliesrekening", null, "Niet ingedeeld (W&V)"), Indeling(schema, "8100"));

        var groepen = Rekeningindeling.Deel(schema.Alle, r => r);
        Assert.Equal(["Materiële vaste activa", "Vorderingen", "Liquide middelen", "Eigen vermogen", "Kortlopende schulden", "Niet ingedeeld (balans)",
            "Netto-omzet", "Kantoorkosten", "Financiële baten en lasten", "Niet ingedeeld (W&V)"], groepen.Select(g => g.Groep.Naam));
        Assert.Equal(["4100", "4200"], groepen.Single(g => g.Groep.Code == "WBedKan").Regels.Select(r => r.Nummer));
        Assert.Equal("Andere bedrijfskosten", Rekeningindeling.Groep(new Rekening("4999", "X", Rekeningsoort.Resultaat, "WBed")).Naam);
    }

    private static (string, string?, string) Indeling(Rekeningschema schema, string nummer) =>
        Rekeningindeling.Groep(schema.Zoek(nummer)!) is var g ? (g.Sectie, g.Tussenkop, g.Naam) : default;

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

public sealed class TegenboekingTests
{
    [Fact]
    public void Tegenboeking_keert_elke_regel_om_en_heft_de_boeking_op()
    {
        var post = new Journaalpost("INK", "2026-0001", new(2026, 3, 1), "Belnet", [new("4310", 20.65m, "Abonnement", "I21", "BELNET", "BN-1"), new("1500", 4.34m), new("1600", -24.99m)]);

        var tegen = post.Tegenboeking("2026-0002", new(2026, 3, 1), "Terugdraaien INK 2026-0001");

        Assert.Equal(("INK", "2026-0002"), (tegen.Dagboek, tegen.Boekstuk));
        Assert.Equal([-20.65m, -4.34m, 24.99m], tegen.Regels.Select(r => r.Bedrag));
        Assert.Equal(("I21", "BELNET", "BN-1"), (tegen.Regels[0].BtwCode, tegen.Regels[0].Relatie, tegen.Regels[0].Factuurnummer));
        Assert.All(post.Regels.Zip(tegen.Regels), p => Assert.Equal(0m, p.First.Bedrag + p.Second.Bedrag));
    }
}

public sealed class PeriodeTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-12-31", 1, "2027-01-01", "2027-12-31")]
    [InlineData("2026-01-01", "2026-12-31", -1, "2025-01-01", "2025-12-31")]
    [InlineData("2026-01-01", "2026-03-31", 1, "2026-04-01", "2026-06-30")]
    [InlineData("2028-02-01", "2028-02-29", 1, "2028-03-01", "2028-03-31")]
    [InlineData("2026-03-01", "2026-03-31", -1, "2026-02-01", "2026-02-28")]
    [InlineData("2026-03-10", "2026-03-16", 1, "2026-03-17", "2026-03-23")]
    public void Vorige_en_volgende_periode(string van, string tot, int stappen, string nieuwVan, string nieuwTot) =>
        Assert.Equal((DateOnly.Parse(nieuwVan, CultureInfo.InvariantCulture), DateOnly.Parse(nieuwTot, CultureInfo.InvariantCulture)),
            Periode.Schuif(DateOnly.Parse(van, CultureInfo.InvariantCulture), DateOnly.Parse(tot, CultureInfo.InvariantCulture), stappen));
}
