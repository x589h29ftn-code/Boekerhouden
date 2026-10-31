using System.Diagnostics;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De lijst openstaande posten crediteuren in lastige gevallen: te veel betaald, deelbetalingen, creditnota's, aanpassen en terugdraaien, en veel posten.</summary>
public sealed class CrediteurenTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id)> Administratie(string naam)
    {
        var wie = await app.Beheerder();
        return (wie, await app.Administratie(naam));
    }

    /// <summary>Een factuur van Belnet (24,99 inclusief btw) op de datum, geboekt; geeft het document.</summary>
    private async Task<Guid> Factuur(Uitvoerder wie, Guid id, string nummer, DateOnly datum, decimal exclusief = 20.65m, bool creditnota = false)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(nummer, datum.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))), $"{nummer}.xml", null);
        var bestaat = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).Any(r => r.Code == "BELNET");
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(bestaat ? "BELNET" : null, bestaat ? null : new NieuweRelatie("Belnet B.V.", null, null, null),
            nummer, datum, datum.AddDays(14), "Abonnement", [new Factuurregel("Abonnement", "4310", "I21", exclusief, null)], Creditnota: creditnota));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return gelezen.DocumentId.Value;
    }

    /// <summary>Een betaling in het memoriaal op crediteuren, zoals een bankboeking die aflettert.</summary>
    private async Task Betaal(Uitvoerder wie, Guid id, string factuurnummer, decimal bedrag, DateOnly datum, string relatie = "BELNET")
    {
        var grootboek = app.Dienst<GrootboekDienst>();
        var post = new Journaalpost("MEM", await grootboek.VolgendBoekstuk(wie, id, "MEM", datum.Year), datum, $"Betaling {factuurnummer}",
            [new Journaalregel("1600", bedrag, $"Betaling {factuurnummer}", null, relatie, factuurnummer), new Journaalregel("1100", -bedrag)]);
        var uitkomst = await grootboek.Boek(wie, id, post);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }

    private async Task<Openpostenlijst> Lijst(Uitvoerder wie, Guid id, DateOnly peildatum) =>
        await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, peildatum);

    /// <summary>Een factuur van een willekeurige leverancier met eigen regels; geeft de code van de leverancier.</summary>
    private async Task<string> Inkoop(Uitvoerder wie, Guid id, string leverancier, string nummer, DateOnly datum, IReadOnlyList<Factuurregel> regels, Periodeverdeling? verdeling = null)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var bestand = $"{leverancier[..3]}-{nummer}";
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(bestand, datum.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))), $"{bestand}.xml", null);
        var code = (await inrichting.Relaties(wie, id)).FirstOrDefault(r => r.Naam == leverancier)?.Code;
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(leverancier, null, null, null) : null,
            nummer, datum, datum.AddDays(30), $"Factuur {nummer}", regels, Verdeling: verdeling));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return code ?? (await inrichting.Relaties(wie, id)).Single(r => r.Naam == leverancier).Code;
    }

    /// <summary>Een memoriaalboeking met losse regels (rekening, bedrag, relatie, factuurnummer); geeft het boekstuk.</summary>
    private async Task<string> Memoriaal(Uitvoerder wie, Guid id, DateOnly datum, string omschrijving, params (string Rekening, decimal Bedrag, string? Relatie, string? Factuurnummer)[] regels) =>
        await Memoriaal(wie, id, "MEM", datum, omschrijving, regels);

    private async Task<string> Memoriaal(Uitvoerder wie, Guid id, string dagboek, DateOnly datum, string omschrijving, params (string Rekening, decimal Bedrag, string? Relatie, string? Factuurnummer)[] regels)
    {
        var grootboek = app.Dienst<GrootboekDienst>();
        var boekstuk = await grootboek.VolgendBoekstuk(wie, id, dagboek, datum.Year);
        var uitkomst = await grootboek.Boek(wie, id, new Journaalpost(dagboek, boekstuk, datum, omschrijving,
            [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag, null, null, r.Relatie, r.Factuurnummer))]));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return boekstuk;
    }

    /// <summary>Het totaal van de lijst is het saldo crediteuren in het grootboek op de peildatum (te betalen = credit).</summary>
    private async Task SluitOpHetGrootboek(Uitvoerder wie, Guid id, Openpostenlijst lijst)
    {
        var kaart = await app.Dienst<GrootboekDienst>().Kaart(wie, id, "1600", new(lijst.Peildatum.Year, 1, 1), lijst.Peildatum);
        Assert.Equal(-kaart.Eindsaldo, lijst.Openstaand);
    }

    /// <summary>Wat er volgens de openstaande posten (die de bank gebruikt voor afletteren) nog openstaat per factuur.</summary>
    private async Task<Dictionary<string, decimal>> Open(Uitvoerder wie, Guid id) =>
        (await app.Dienst<BankDienst>().OpenPosten(wie, id)).ToDictionary(p => $"{p.Relatie} {p.Factuurnummer}", p => -p.Openstaand);

    [Fact]
    public async Task Deelbetalingen_tellen_tot_en_met_de_peildatum()
    {
        var (wie, id) = await Administratie("Deelbetalingen B.V.");
        await Factuur(wie, id, "D-1", new(2026, 3, 1));
        await Betaal(wie, id, "D-1", 10m, new(2026, 3, 10));
        await Betaal(wie, id, "D-1", 5m, new(2026, 3, 20));

        Assert.Equal(24.99m, (await Lijst(wie, id, new(2026, 3, 5))).Openstaand);
        Assert.Equal(14.99m, (await Lijst(wie, id, new(2026, 3, 10))).Openstaand);
        Assert.Equal((15m, 9.99m), ((await Lijst(wie, id, new(2026, 3, 31))).Betaald, (await Lijst(wie, id, new(2026, 3, 31))).Openstaand));
        await Betaal(wie, id, "D-1", 9.99m, new(2026, 4, 2));
        Assert.Empty((await Lijst(wie, id, new(2026, 4, 30))).Relaties);
        Assert.Equal(9.99m, (await Lijst(wie, id, new(2026, 4, 1))).Openstaand);
    }

    [Fact]
    public async Task Te_veel_betaald_staat_negatief_open()
    {
        var (wie, id) = await Administratie("Te Veel Betaald B.V.");
        await Factuur(wie, id, "T-1", new(2026, 3, 1));
        await Betaal(wie, id, "T-1", 30m, new(2026, 3, 10));

        var post = Assert.Single(Assert.Single((await Lijst(wie, id, new(2026, 3, 31))).Relaties).Posten);

        Assert.Equal((24.99m, 30m, -5.01m), (post.Factuurbedrag, post.Betaald, post.Openstaand));
        Assert.Equal(24.99m, (await Lijst(wie, id, new(2026, 3, 9))).Openstaand);
    }

    [Fact]
    public async Task Creditnota_staat_negatief_open_en_telt_mee_in_het_totaal()
    {
        var (wie, id) = await Administratie("Creditnota B.V.");
        await Factuur(wie, id, "C-1", new(2026, 3, 1));
        await Factuur(wie, id, "C-2", new(2026, 3, 5), exclusief: 5m, creditnota: true);

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        var relatie = Assert.Single(lijst.Relaties);
        Assert.Equal([("C-1", 24.99m), ("C-2", -6.05m)], relatie.Posten.Select(p => (p.Factuurnummer, p.Openstaand)));
        Assert.Equal(18.94m, lijst.Openstaand);
        Assert.Equal(24.99m, (await Lijst(wie, id, new(2026, 3, 4))).Openstaand);
    }

    [Fact]
    public async Task Creditnota_verrekend_met_de_factuur_laat_alleen_het_restant_open()
    {
        var (wie, id) = await Administratie("Verrekenen B.V.");
        await Factuur(wie, id, "R-1", new(2026, 3, 1));
        var factuur = (await app.Dienst<BankDienst>().OpenPosten(wie, id)).Single();
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("R-2", "2026-03-05")), "r2.xml", null);
        Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer("BELNET", null, "R-2", new(2026, 3, 5), new(2026, 3, 19), "Correctie",
            [new Factuurregel("Correctie", "4310", "I21", 5m, null)], Creditnota: true, VerrekenMet: factuur.Id))).IsGelukt);

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        var post = Assert.Single(Assert.Single(lijst.Relaties).Posten);
        Assert.Equal(("R-1", 6.05m, 18.94m), (post.Factuurnummer, post.Betaald, post.Openstaand));
    }

    [Fact]
    public async Task Betaling_op_een_onbekend_factuurnummer_of_een_andere_leverancier_staat_los()
    {
        var (wie, id) = await Administratie("Onbekend Nummer B.V.");
        await Factuur(wie, id, "O-1", new(2026, 3, 1));
        await Betaal(wie, id, "O-999", 24.99m, new(2026, 3, 10));
        await Betaal(wie, id, "O-1", 24.99m, new(2026, 3, 10), relatie: "ANDERE");

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        var belnet = Assert.Single(lijst.Relaties, r => r.Relatie == "BELNET");
        Assert.Equal([("O-1", 24.99m), ("O-999", -24.99m)], belnet.Posten.Select(p => (p.Factuurnummer, p.Openstaand)));
        Assert.Equal(Guid.Empty, belnet.Posten[1].Id);
        Assert.Equal(-24.99m, Assert.Single(lijst.Relaties, r => r.Relatie == "ANDERE").Openstaand);
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Aangepaste_factuur_telt_met_het_nieuwe_bedrag_en_een_teruggedraaide_niet()
    {
        var (wie, id) = await Administratie("Aanpassen B.V.");
        var aangepast = await Factuur(wie, id, "A-1", new(2026, 3, 1));
        var teruggedraaid = await Factuur(wie, id, "A-2", new(2026, 3, 2));
        var inkoop = app.Dienst<InkoopDienst>();
        var geboekt = (await inkoop.Geboekt(wie, id, aangepast))!;
        Assert.True((await inkoop.Boek(wie, id, aangepast, new Factuurinvoer("BELNET", null, "A-1", geboekt.Datum, geboekt.Vervaldatum, "Abonnement",
            [new Factuurregel("Abonnement", "4310", "I21", 100m, null)]))).IsGelukt);
        Assert.True((await inkoop.Terugdraaien(wie, id, teruggedraaid)).IsGelukt);

        var post = Assert.Single(Assert.Single((await Lijst(wie, id, new(2026, 3, 31))).Relaties).Posten);

        Assert.Equal(("A-1", 121m), (post.Factuurnummer, post.Openstaand));
        Assert.Empty((await Lijst(wie, id, new(2026, 2, 28))).Relaties);
    }

    [Fact]
    public async Task Veel_posten_sluiten_op_de_openstaande_posten_en_zijn_snel()
    {
        var (wie, id) = await Administratie("Veel Posten B.V.");
        var verwacht = 0m;
        for (var i = 1; i <= 60; i++)
        {
            var datum = new DateOnly(2026, 1, 1).AddDays(i * 3);
            await Factuur(wie, id, $"V-{i}", datum, exclusief: 10m * i);
            var totaal = decimal.Round(10m * i * 1.21m, 2);
            var betaald = (i % 3) switch { 0 => totaal, 1 => decimal.Round(totaal / 2, 2), _ => 0m };
            if (betaald != 0)
            {
                await Betaal(wie, id, $"V-{i}", betaald, datum.AddDays(5));
            }

            verwacht += totaal - betaald;
        }

        var klok = Stopwatch.StartNew();
        var lijst = await Lijst(wie, id, new(2026, 12, 31));
        klok.Stop();

        Assert.Equal(verwacht, lijst.Openstaand);
        Assert.Equal(40, lijst.Relaties.Sum(r => r.Posten.Count));
        Assert.Equal(lijst.Relaties.SelectMany(r => r.Posten).Sum(p => p.Factuurbedrag) - lijst.Betaald, lijst.Openstaand);
        Assert.True(klok.ElapsedMilliseconds < 2000, $"{klok.ElapsedMilliseconds} ms");
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Betaling_in_het_memoriaal_werkt_de_openstaande_post_bij()
    {
        var (wie, id) = await Administratie("Memoriaal Betaalt B.V.");
        var document = await Factuur(wie, id, "M-1", new(2026, 3, 1));
        await Betaal(wie, id, "M-1", 10m, new(2026, 3, 10));

        Assert.Equal(14.99m, (await Open(wie, id))["BELNET M-1"]);
        var inkoop = app.Dienst<InkoopDienst>();
        var geboekt = (await inkoop.Geboekt(wie, id, document))!;
        var aanpassen = await inkoop.Boek(wie, id, document, new Factuurinvoer("BELNET", null, "M-1", geboekt.Datum, geboekt.Vervaldatum, "Abonnement",
            [new Factuurregel("Abonnement", "4310", "I21", 100m, null)]));
        Assert.Contains("betaald", Assert.Single(aanpassen.Fouten), StringComparison.Ordinal);
        Assert.False((await inkoop.Terugdraaien(wie, id, document)).IsGelukt);

        await Betaal(wie, id, "M-1", 14.99m, new(2026, 3, 20));

        Assert.Empty(await Open(wie, id));
        Assert.Empty((await Lijst(wie, id, new(2026, 3, 31))).Relaties);
        Assert.Equal(14.99m, (await Lijst(wie, id, new(2026, 3, 15))).Openstaand);
    }

    [Fact]
    public async Task Creditnota_verrekend_en_het_restant_in_het_memoriaal_betaald()
    {
        var (wie, id) = await Administratie("Verrekend En Betaald B.V.");
        await Factuur(wie, id, "R-1", new(2026, 3, 1));
        var factuur = (await app.Dienst<BankDienst>().OpenPosten(wie, id)).Single();
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("R-2", "2026-03-05")), "r2.xml", null);
        Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer("BELNET", null, "R-2", new(2026, 3, 5), new(2026, 3, 19), "Correctie",
            [new Factuurregel("Correctie", "4310", "I21", 5m, null)], Creditnota: true, VerrekenMet: factuur.Id))).IsGelukt);
        await Betaal(wie, id, "R-1", 18.94m, new(2026, 3, 20));

        Assert.Empty((await Lijst(wie, id, new(2026, 3, 31))).Relaties);
        var voor = Assert.Single(Assert.Single((await Lijst(wie, id, new(2026, 3, 10))).Relaties).Posten);
        Assert.Equal(("R-1", 6.05m, 18.94m), (voor.Factuurnummer, voor.Betaald, voor.Openstaand));
        Assert.Empty(await Open(wie, id));
    }

    [Fact]
    public async Task Correcties_in_het_memoriaal_verhogen_of_verlagen_de_schuld()
    {
        var (wie, id) = await Administratie("Correctie Memoriaal B.V.");
        await Factuur(wie, id, "K-1", new(2026, 3, 1));
        await Memoriaal(wie, id, new(2026, 3, 5), "Nagekomen kosten K-1", ("4310", 10m, null, null), ("1600", -10m, "BELNET", "K-1"));
        Assert.Equal(34.99m, (await Open(wie, id))["BELNET K-1"]);

        await Memoriaal(wie, id, new(2026, 3, 8), "Korting K-1", ("1600", 4.99m, "BELNET", "K-1"), ("4310", -4.99m, null, null));

        var lijst = await Lijst(wie, id, new(2026, 3, 31));
        var post = Assert.Single(Assert.Single(lijst.Relaties).Posten);
        Assert.Equal((24.99m, -5.01m, 30m), (post.Factuurbedrag, post.Betaald, post.Openstaand));
        Assert.Equal(30m, (await Open(wie, id))["BELNET K-1"]);
        Assert.Equal(34.99m, (await Lijst(wie, id, new(2026, 3, 6))).Openstaand);
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Verzamelbetaling_in_een_eigen_memoriaaldagboek_voor_meer_leveranciers()
    {
        var (wie, id) = await Administratie("Verzamelbetaling B.V.");
        Assert.True((await app.Dienst<GrootboekDienst>().VoegMemoriaalToe(wie, id, "MACC", "Memoriaal accountant")).IsGelukt);
        await Factuur(wie, id, "V-1", new(2026, 3, 1));
        await Factuur(wie, id, "V-2", new(2026, 3, 2));
        var kantoor = await Inkoop(wie, id, "Kantoorhuis B.V.", "K-9", new(2026, 3, 3), [new Factuurregel("Papier", "4300", "I21", 100m, null)]);

        await Memoriaal(wie, id, "MACC", new(2026, 3, 31), "Betaald via rekening-courant directeur",
            ("1600", 24.99m, "BELNET", "V-1"), ("1600", 10m, "BELNET", "V-2"), ("1600", 50m, kantoor, "K-9"), ("1560", -84.99m, null, null));

        var lijst = await Lijst(wie, id, new(2026, 3, 31));
        Assert.Equal([("BELNET", "V-2", 14.99m), (kantoor, "K-9", 71m)], lijst.Relaties.SelectMany(r => r.Posten.Select(p => (r.Relatie, p.Factuurnummer, p.Openstaand))));
        Assert.Equal(new Dictionary<string, decimal> { ["BELNET V-2"] = 14.99m, [$"{kantoor} K-9"] = 71m }, await Open(wie, id));
        Assert.Equal(170.98m, (await Lijst(wie, id, new(2026, 3, 30))).Openstaand);
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Aanpassen_van_een_memoriaalboeking_telt_de_betaling_niet_dubbel()
    {
        var (wie, id) = await Administratie("Memoriaal Aanpassen B.V.");
        await Factuur(wie, id, "P-1", new(2026, 3, 1));
        var boekstuk = await Memoriaal(wie, id, new(2026, 3, 10), "Betaling P-1", ("1600", 10m, "BELNET", "P-1"), ("1560", -10m, null, null));
        var grootboek = app.Dienst<GrootboekDienst>();

        var anders = new Journaalpost("MEM", boekstuk, new(2026, 3, 10), "Betaling P-1 door de aandeelhouder",
            [new Journaalregel("1600", 10m, null, null, "BELNET", "P-1"), new Journaalregel("1800", -10m)]);
        var aangepast = await grootboek.PasAan(wie, id, "MEM", boekstuk, anders);
        Assert.True(aangepast.IsGelukt, string.Join("; ", aangepast.Fouten));
        Assert.Equal(2, aangepast.Waarde);
        Assert.False((await grootboek.PasAan(wie, id, "MEM", boekstuk, anders with
        {
            Regels = [new Journaalregel("1600", 20m, null, null, "BELNET", "P-1"), new Journaalregel("1800", -20m)],
        })).IsGelukt);

        Assert.Equal(14.99m, (await Open(wie, id))["BELNET P-1"]);
        var lijst = await Lijst(wie, id, new(2026, 3, 31));
        Assert.Equal((10m, 14.99m), (lijst.Betaald, lijst.Openstaand));
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Regels_zonder_factuur_staan_los_en_de_lijst_sluit_op_het_grootboek()
    {
        var (wie, id) = await Administratie("Losse Regels B.V.");
        await Memoriaal(wie, id, new(2026, 1, 1), "Beginbalans crediteuren", ("1800", 500m, null, null), ("1600", -500m, null, null));
        await Factuur(wie, id, "L-1", new(2026, 3, 1));
        await Memoriaal(wie, id, new(2026, 3, 5), "Vooruitbetaling Belnet", ("1600", 100m, "BELNET", null), ("1100", -100m, null, null));
        await Memoriaal(wie, id, new(2026, 4, 5), "Factuur buiten de inkoop om", ("4310", 40m, null, null), ("1600", -40m, "BELNET", "HAND-1"));

        var maart = await Lijst(wie, id, new(2026, 3, 31));

        Assert.Equal([("Belnet B.V.", -75.01m), (OpenpostenDienst.ZonderRelatie, 500m)], maart.Relaties.Select(r => (r.Naam, r.Openstaand)));
        var vooruit = Assert.Single(maart.Relaties[0].Posten, p => p.Factuurnummer.Length == 0);
        Assert.Equal(("Vooruitbetaling Belnet", 0m, 100m, "MEM"), (vooruit.Omschrijving, vooruit.Factuurbedrag, vooruit.Betaald, vooruit.Dagboek));
        await SluitOpHetGrootboek(wie, id, maart);

        var april = await Lijst(wie, id, new(2026, 4, 30));
        var hand = Assert.Single(april.Relaties[0].Posten, p => p.Factuurnummer == "HAND-1");
        Assert.Equal((40m, 0m, 40m), (hand.Factuurbedrag, hand.Betaald, hand.Openstaand));
        await SluitOpHetGrootboek(wie, id, april);
        Assert.Equal(-75.01m, (await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, new(2026, 3, 31), "BELNET")).Openstaand);
    }

    [Fact]
    public async Task Factuur_met_meer_tarieven_verlegd_en_buiten_de_EU()
    {
        var (wie, id) = await Administratie("Tarieven B.V.");
        var code = await Inkoop(wie, id, "Groothandel Mix B.V.", "T-1", new(2026, 3, 1),
        [
            new Factuurregel("Papier", "4300", "I21", 100m, null),
            new Factuurregel("Koffie", "7000", "I9", 50m, null),
            new Factuurregel("Contributie", "4330", "I0", 30m, null),
            new Factuurregel("Onderaanneming", "7100", "IVL", 200m, null),
        ]);
        await Inkoop(wie, id, "Software GmbH", "EU-1", new(2026, 3, 2), [new Factuurregel("Licentie", "4330", "IEU", 80m, null)]);

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        Assert.Equal(405.50m, Assert.Single(lijst.Relaties, r => r.Relatie == code).Openstaand);
        Assert.Equal(80m, Assert.Single(lijst.Relaties, r => r.Naam == "Software GmbH").Openstaand);
        Assert.Equal(405.50m, (await Open(wie, id))[$"{code} T-1"]);
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Factuur_met_korting_eigen_btw_en_periodeverdeling()
    {
        var (wie, id) = await Administratie("Bijzondere Facturen B.V.");
        await Inkoop(wie, id, "Korting B.V.", "KO-1", new(2026, 3, 1),
            [new Factuurregel("Bureau", "4320", "I21", 300m, null), new Factuurregel("Korting", "4320", "I21", -30m, null)]);
        await Inkoop(wie, id, "Afronding B.V.", "AF-1", new(2026, 3, 2), [new Factuurregel("Abonnement", "4310", "I21", 20.65m, 4.33m)]);
        await Inkoop(wie, id, "Verzekeraar N.V.", "VZ-1", new(2026, 3, 3), [new Factuurregel("Jaarpremie", "4500", "I0", 1200m, null)],
            new Periodeverdeling(new(2026, 3, 1), new(2027, 2, 28), "1410"));

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        Assert.Equal([("Afronding B.V.", 24.98m), ("Korting B.V.", 326.70m), ("Verzekeraar N.V.", 1200m)], lijst.Relaties.Select(r => (r.Naam, r.Openstaand)));
        var korting = (await app.Dienst<InkoopDienst>().Geboekt(wie, id, lijst.Relaties[1].Posten[0].Document!.Value))!;
        Assert.Equal([300m, -30m], korting.Regels.Select(r => r.Bedrag));
        Assert.False(korting.Creditnota);
        await SluitOpHetGrootboek(wie, id, lijst);
    }

    [Fact]
    public async Task Factuur_van_vorig_jaar_betaald_in_het_nieuwe_jaar()
    {
        var (wie, id) = await Administratie("Jaarovergang B.V.");
        await Factuur(wie, id, "J-1", new(2025, 12, 15));
        await Betaal(wie, id, "J-1", 24.99m, new(2026, 1, 10));

        var oud = Assert.Single(Assert.Single((await Lijst(wie, id, new(2025, 12, 31))).Relaties).Posten);
        Assert.Equal((24.99m, 16), (oud.Openstaand, oud.Ouderdom));
        Assert.Empty((await Lijst(wie, id, new(2026, 1, 31))).Relaties);
        Assert.Equal(24.99m, (await Lijst(wie, id, new(2026, 1, 9))).Openstaand);
    }

    [Fact]
    public async Task Hetzelfde_factuurnummer_bij_twee_leveranciers()
    {
        var (wie, id) = await Administratie("Zelfde Nummer B.V.");
        await Factuur(wie, id, "2026-001", new(2026, 3, 1));
        var ander = await Inkoop(wie, id, "Drukkerij B.V.", "2026-001", new(2026, 3, 2), [new Factuurregel("Drukwerk", "4400", "I21", 50m, null)]);
        await Betaal(wie, id, "2026-001", 60.50m, new(2026, 3, 10), relatie: ander);

        var lijst = await Lijst(wie, id, new(2026, 3, 31));

        Assert.Equal([("BELNET", 24.99m)], lijst.Relaties.Select(r => (r.Relatie, r.Openstaand)));
        Assert.Equal(new Dictionary<string, decimal> { ["BELNET 2026-001"] = 24.99m }, await Open(wie, id));
        await SluitOpHetGrootboek(wie, id, lijst);
    }
}
