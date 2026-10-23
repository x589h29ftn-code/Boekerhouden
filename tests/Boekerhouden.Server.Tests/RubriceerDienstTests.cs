using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Domain.Rubricering;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Tests.Gedeeld;

namespace Boekerhouden.Server.Tests;

public sealed class RubriceerDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private RubriceerDienst Rubriceer => server.Dienst<RubriceerDienst>();

    /// <summary>Een klant met dossier 2025 en ingelezen saldibalansen 2024 en 2025 (fictieve bakkerij).</summary>
    private async Task<(Guid KlantId, Guid DossierId)> NieuweBakkerij()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            var inhoud = TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv");
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, inhoud, $"{jaar}.csv", Tester)).IsGelukt);
        }

        return (klantId, dossierId);
    }

    private static Guid Rekening(RubriceerOverzicht o, string nummer) => o.Rekeningen.Single(r => r.Nummer == nummer).Id;

    [Fact]
    public async Task Rgs_inlezen_geeft_een_verslag_en_hetzelfde_bestand_kan_niet_twee_keer()
    {
        await server.MetRgs();
        var rgs = server.Dienst<RgsDienst>();

        var schema = await rgs.ActiefSchema();
        var bestand = RgsTestWerkboek.Maak();
        var eerste = await rgs.Importeer(bestand, "RGS 3.8-def.xlsx", Tester);
        var nogmaals = await rgs.Importeer(bestand, "RGS 3.8-def.xlsx", Tester);

        Assert.NotNull(schema);
        Assert.Equal("3.8", schema.Versie);
        Assert.Equal(RgsTestWerkboek.Regels.Length, schema.Aantal);
        Assert.True(eerste.IsGelukt);
        Assert.Equal("3.8", eerste.Waarde!.Versie);
        Assert.True(eerste.Waarde.Actief);
        Assert.Equal(RgsTestWerkboek.Regels.Length, eerste.Waarde.PerNiveau.Values.Sum());
        Assert.False(nogmaals.IsGelukt);
        Assert.Contains("al ingelezen", Assert.Single(nogmaals.Fouten), StringComparison.Ordinal);
        Assert.Single(await rgs.Versies(), v => v.Actief);
    }

    [Fact]
    public async Task Overzicht_zonder_koppelingen_met_voorstellen()
    {
        var (_, dossierId) = await NieuweBakkerij();

        var o = (await Rubriceer.Overzicht(dossierId))!;

        Assert.NotNull(o.Schema);
        Assert.Equal(0, o.Percentage);
        Assert.Equal(34, o.Rekeningen.Count);
        Assert.Contains("BEivGokGea", o.Codes); // BV: aandelenkapitaal
        Assert.DoesNotContain("BEivKapPrs", o.Codes); // geen privé-posten bij een BV
        // Herkomst "omschrijving", of "andere klanten" als een andere test de kas van een bakkerij al koppelde.
        Assert.Equal("BLimKasKas", o.Rekeningen.Single(r => r.Nummer == "1000").Voorstel?.Code);
        Assert.Equal(0m, o.Balans!.Ongekoppeld.Huidig); // saldo nihil, dus ongekoppeld totaal ook 0
        Assert.Equal(34, o.Balans.Ongekoppeld.Aantal);
    }

    [Fact]
    public async Task Handmatig_koppelen_op_subpost_en_grootboekcode()
    {
        var (klantId, dossierId) = await NieuweBakkerij();
        var o = (await Rubriceer.Overzicht(dossierId))!;

        Assert.True((await Rubriceer.Koppel(klantId, [Rekening(o, "0300"), Rekening(o, "0310")], "BMvaTev", false, Tester)).IsGelukt);
        Assert.True((await Rubriceer.Koppel(klantId, [Rekening(o, "1000")], "BLimKasKas", false, Tester)).IsGelukt);

        o = (await Rubriceer.Overzicht(dossierId))!;
        var vervoer = o.Rekeningen.Single(r => r.Nummer == "0300");
        Assert.Equal(new KoppelInfo("BMvaTev", null, KoppelBron.Handmatig), vervoer.Koppeling);
        var saldoVervoer = o.Rekeningen.Where(r => r.Nummer is "0300" or "0310").Sum(r => r.Saldo!.Value);
        Assert.Equal(saldoVervoer, o.Balans!.Totaal("BMvaTev").Huidig);
        Assert.Equal(saldoVervoer, o.Balans.Totaal("BMva").Huidig);
        Assert.Equal(2, o.Balans.Totaal("BMvaTev").Aantal);
        Assert.Equal(3 * 100 / 34, o.Percentage);
    }

    [Theory]
    [InlineData("BMva", "hoofdpost")]
    [InlineData("BMvaBegVvpBeg", "mutatie")]
    [InlineData("BXyz", "bestaat niet")]
    public async Task Koppelen_aan_een_hoofdpost_mutatie_of_onbekende_code_kan_niet(string code, string fout)
    {
        var (klantId, dossierId) = await NieuweBakkerij();
        var o = (await Rubriceer.Overzicht(dossierId))!;

        var uitkomst = await Rubriceer.Koppel(klantId, [Rekening(o, "0200")], code, false, Tester);

        Assert.False(uitkomst.IsGelukt);
        Assert.Contains(fout, Assert.Single(uitkomst.Fouten), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eigen_post_toevoegen_koppelen_hernoemen_en_verwijderen()
    {
        var (klantId, dossierId) = await NieuweBakkerij();
        var o = (await Rubriceer.Overzicht(dossierId))!;

        var postId = (await Rubriceer.VoegEigenPostToe(klantId, "BVorOvr", "Overige vorderingen 1", Tester)).Waarde;
        var sleutel = PostBalans.EigenSleutel(postId);
        Assert.True((await Rubriceer.Koppel(klantId, [Rekening(o, "1400")], sleutel, false, Tester)).IsGelukt);
        Assert.True((await Rubriceer.Hernoem(klantId, sleutel, "Vordering op de huurder", false, Tester)).IsGelukt);

        o = (await Rubriceer.Overzicht(dossierId))!;
        var saldo = o.Rekeningen.Single(r => r.Nummer == "1400").Saldo!.Value;
        Assert.Equal("Vordering op de huurder", o.Naam(sleutel));
        Assert.Equal(saldo, o.Balans!.Totaal(sleutel).Huidig);
        Assert.Equal(saldo, o.Balans.Totaal("BVorOvr").Huidig);
        Assert.Equal(new KoppelInfo("BVorOvr", postId, KoppelBron.Handmatig), o.Rekeningen.Single(r => r.Nummer == "1400").Koppeling);

        Assert.True((await Rubriceer.VerwijderEigenPost(klantId, postId, Tester)).IsGelukt);
        o = (await Rubriceer.Overzicht(dossierId))!;
        Assert.Empty(o.EigenPosten);
        Assert.Equal(new KoppelInfo("BVorOvr", null, KoppelBron.Handmatig), o.Rekeningen.Single(r => r.Nummer == "1400").Koppeling);
    }

    [Fact]
    public async Task Eigen_post_alleen_onder_subpost_of_grootboekcode()
    {
        var (klantId, _) = await NieuweBakkerij();

        var onderHoofdpost = await Rubriceer.VoegEigenPostToe(klantId, "BVor", "Te grof", Tester);
        var zonderNaam = await Rubriceer.VoegEigenPostToe(klantId, "BVorOvr", " ", Tester);

        Assert.False(onderHoofdpost.IsGelukt);
        Assert.False(zonderNaam.IsGelukt);
    }

    [Fact]
    public async Task Hernoemen_per_klant_gaat_voor_kantoornaam_en_leeg_zet_terug()
    {
        var (klantId, dossierId) = await NieuweBakkerij();

        await Rubriceer.Hernoem(klantId, "BMvaTev", "Vervoermiddelen", kantoorbreed: true, Tester);
        var o = (await Rubriceer.Overzicht(dossierId))!;
        Assert.Equal("Vervoermiddelen", o.Naam("BMvaTev"));

        await Rubriceer.Hernoem(klantId, "BMvaTev", "Bestelbussen", kantoorbreed: false, Tester);
        o = (await Rubriceer.Overzicht(dossierId))!;
        Assert.Equal("Bestelbussen", o.Naam("BMvaTev"));

        await Rubriceer.Hernoem(klantId, "BMvaTev", null, kantoorbreed: false, Tester);
        await Rubriceer.Hernoem(klantId, "BMvaTev", "", kantoorbreed: true, Tester);
        o = (await Rubriceer.Overzicht(dossierId))!;
        Assert.Equal("Automobielen en overige transportmiddelen", o.Naam("BMvaTev"));
    }

    [Fact]
    public async Task Rgs_code_uit_het_bestand_koppelt_automatisch_maar_handmatig_gaat_voor()
    {
        await server.MetRgs();
        var klant = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant met RGS", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossier = (await server.Dienst<DossierDienst>().Maak(klant, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var bestand = Encoding.UTF8.GetBytes("Boekjaar;2025\n\nGrootboeknummer;Omschrijving;Bedrag;RGS-code\n1000;Kas;10,00;BLimKasKas\n8000;Omzet;-10,00;WOmzNodOdh\n");
        await server.Dienst<ImportDienst>().Importeer(klant, 2025, bestand, "rgs.csv", Tester);

        var o = (await Rubriceer.Overzicht(dossier))!;
        Assert.Equal(100, o.Percentage);
        Assert.Equal(new KoppelInfo("BLimKasKas", null, KoppelBron.Bestand), o.Rekeningen.Single(r => r.Nummer == "1000").Koppeling);

        await Rubriceer.Koppel(klant, [Rekening(o, "1000")], "BLimKas", false, Tester);
        Assert.Equal(0, await Rubriceer.AutomatischKoppelen(klant, Tester));
        o = (await Rubriceer.Overzicht(dossier))!;
        Assert.Equal(new KoppelInfo("BLimKas", null, KoppelBron.Handmatig), o.Rekeningen.Single(r => r.Nummer == "1000").Koppeling);
    }

    [Fact]
    public async Task Voorstellen_accepteren_en_ontkoppelen()
    {
        var (klantId, dossierId) = await NieuweBakkerij();
        var o = (await Rubriceer.Overzicht(dossierId))!;
        var metVoorstel = o.Rekeningen.Count(r => r.Voorstel is not null);

        var geaccepteerd = await Rubriceer.AccepteerVoorstellen(dossierId, null, onthouden: false, Tester);

        Assert.True(geaccepteerd.IsGelukt);
        Assert.Equal(metVoorstel, geaccepteerd.Waarde);
        o = (await Rubriceer.Overzicht(dossierId))!;
        var kas = o.Rekeningen.Single(r => r.Nummer == "1000");
        Assert.Equal(KoppelBron.Voorstel, kas.Koppeling?.Bron);

        await Rubriceer.Ontkoppel(klantId, [kas.Id], Tester);
        o = (await Rubriceer.Overzicht(dossierId))!;
        Assert.Null(o.Rekeningen.Single(r => r.Nummer == "1000").Koppeling);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.Ontkoppeld), r => r.Details!.Contains("1000 Kas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Negatieve_bank_staat_bij_de_schulden()
    {
        await server.MetRgs();
        var klant = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Rood staan BV", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossier = (await server.Dienst<DossierDienst>().Maak(klant, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await server.Dienst<ImportDienst>().Importeer(klant, 2025, Csv(2025, "1100;Bank;-250,00", "1000;Kas;250,00"), "bank.csv", Tester);
        var o = (await Rubriceer.Overzicht(dossier))!;
        await Rubriceer.Koppel(klant, [Rekening(o, "1100")], "BLimBanRba", false, Tester);

        o = (await Rubriceer.Overzicht(dossier))!;

        Assert.Equal(-250m, o.Balans!.Totaal("BSchSakRba").Huidig);
        Assert.Equal(0m, o.Balans.Totaal("BLimBan").Huidig);
    }

    [Fact]
    public async Task Saldi_in_hele_euros_en_de_balans_blijft_sluiten()
    {
        await server.MetRgs();
        var klant = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Centen BV", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossier = (await server.Dienst<DossierDienst>().Maak(klant, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await server.Dienst<ImportDienst>().Importeer(klant, 2025, Csv(2025, "1000;Kas;100,40", "1100;Bank;200,40", "0800;Eigen vermogen;-300,80"), "centen.csv", Tester);

        var o = (await Rubriceer.Overzicht(dossier))!;
        var proefbalans = await server.Dienst<ImportDienst>().Proefbalans(klant, 2025);

        // 100 + 200 - 301 zou niet sluiten; de euro gaat naar het grootste bedrag met de grootste afrondingsrest.
        Assert.Equal(100m, o.Rekeningen.Single(r => r.Nummer == "1000").Saldo);
        Assert.Equal(201m, o.Rekeningen.Single(r => r.Nummer == "1100").Saldo);
        Assert.Equal(-301m, o.Rekeningen.Single(r => r.Nummer == "0800").Saldo);
        Assert.Equal(0m, o.Balans!.Ongekoppeld.Huidig);
        Assert.Equal([100m, 201m, -301m], proefbalans.Regels.OrderBy(r => r.Nummer == "0800").ThenBy(r => r.Nummer, StringComparer.Ordinal).Select(r => r.Bedrag!.Value));
        Assert.Equal(0m, proefbalans.Totaal);
    }

    private static byte[] Csv(int jaar, params string[] regels) =>
        Encoding.UTF8.GetBytes($"Boekjaar;{jaar}\n\nGrootboeknummer;Omschrijving;Bedrag\n{string.Join('\n', regels)}\n");
}

/// <summary>Het kantoorsjabloon geldt voor alle klanten; daarom een eigen database, los van de andere rubriceertests.</summary>
public sealed class KantoorsjabloonTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private RubriceerDienst Rubriceer => server.Dienst<RubriceerDienst>();

    private async Task<(Guid KlantId, Guid DossierId)> NieuweBakkerij()
    {
        await server.MetRgs();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var inhoud = TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv");
        Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, 2025, inhoud, "2025.csv", Tester)).IsGelukt);
        return (klantId, dossierId);
    }

    private static Guid Rekening(RubriceerOverzicht o, string nummer) => o.Rekeningen.Single(r => r.Nummer == nummer).Id;

    [Fact]
    public async Task Kantoorsjabloon_koppelt_dezelfde_rekening_bij_de_volgende_klant_automatisch()
    {
        var (klantA, dossierA) = await NieuweBakkerij();
        var a = (await Rubriceer.Overzicht(dossierA))!;
        Assert.True((await Rubriceer.Koppel(klantA, [Rekening(a, "4300")], "WBedKanKan", onthouden: true, Tester)).IsGelukt);

        var (_, dossierB) = await NieuweBakkerij();
        var b = (await Rubriceer.Overzicht(dossierB))!;

        Assert.Equal(new KoppelInfo("WBedKanKan", null, KoppelBron.Sjabloon), b.Rekeningen.Single(r => r.Nummer == "4300").Koppeling);
        Assert.Contains(await server.Dienst<SjabloonDienst>().Lijst("4300"), r => r.Code == "WBedKanKan");
    }

    [Fact]
    public async Task Sjabloon_met_andere_omschrijving_wordt_alleen_een_voorstel()
    {
        await server.MetRgs();
        var klantA = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant A", Rechtsvorm.BV, null, null), Tester)).Waarde;
        await server.Dienst<ImportDienst>().Importeer(klantA, 2025, Csv(2025, "4710;Bankkosten;10,00", "1000;Kas;-10,00"), "a.csv", Tester);
        var rekeningA = (await Rubriceer.Overzicht((await server.Dienst<DossierDienst>().Maak(klantA, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde))!;
        await Rubriceer.Koppel(klantA, [Rekening(rekeningA, "4710")], "WFbeOrlRkb", onthouden: true, Tester);

        var klantB = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), "Klant B", Rechtsvorm.BV, null, null), Tester)).Waarde;
        var dossierB = (await server.Dienst<DossierDienst>().Maak(klantB, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        await server.Dienst<ImportDienst>().Importeer(klantB, 2025, Csv(2025, "4710;Huur garage;10,00", "1000;Kas;-10,00"), "b.csv", Tester);

        var b = (await Rubriceer.Overzicht(dossierB))!.Rekeningen.Single(r => r.Nummer == "4710");
        Assert.Null(b.Koppeling);
        Assert.Equal(new VoorstelInfo("WFbeOrlRkb", "kantoorsjabloon"), b.Voorstel);
    }

    private static byte[] Csv(int jaar, params string[] regels) =>
        Encoding.UTF8.GetBytes($"Boekjaar;{jaar}\n\nGrootboeknummer;Omschrijving;Bedrag\n{string.Join('\n', regels)}\n");
}

public sealed class ZijdeUitBurenTests
{
    private static readonly (string, char)[] Bekend = [("0200", 'B'), ("1000", 'B'), ("1300", 'B'), ("4000", 'W'), ("4300", 'W'), ("8000", 'W')];

    [Theory]
    [InlineData("1100", 'B')]
    [InlineData("4100", 'W')]
    [InlineData("7000", 'W')]
    [InlineData("950", 'B')]
    public void Buren_aan_dezelfde_kant_bepalen_de_zijde(string nummer, char zijde) =>
        Assert.Equal(zijde, RubriceerDienst.Zijde(nummer, Bekend));

    [Theory]
    [InlineData("2000", 'B')]
    [InlineData("0100", 'B')]
    [InlineData("3100", 'B')]
    [InlineData("9000", 'W')]
    public void Zonder_eenduidige_buren_het_decimale_stelsel(string nummer, char zijde) =>
        Assert.Equal(zijde, RubriceerDienst.Zijde(nummer, Bekend));

    [Fact]
    public void Zonder_gekoppelde_rekeningen_het_decimale_stelsel()
    {
        Assert.Equal('B', RubriceerDienst.Zijde("1100", []));
        Assert.Equal('W', RubriceerDienst.Zijde("4100", []));
        Assert.Null(RubriceerDienst.Zijde("Kas", []));
    }

    [Theory]
    [InlineData("2000")]
    [InlineData("0100")]
    [InlineData("9000")]
    public void Geen_stelsel_als_de_klant_een_eigen_indeling_heeft(string nummer)
    {
        // Omzet op 1xxx en kosten op 2xxx: het decimale stelsel geldt hier niet, dus alleen de buren tellen.
        (string, char)[] eigenIndeling = [("0200", 'B'), ("0900", 'B'), ("1000", 'W'), ("1500", 'W'), ("2000", 'W'), ("8000", 'B')];
        Assert.Null(RubriceerDienst.Zijde(nummer, eigenIndeling));
    }
}
