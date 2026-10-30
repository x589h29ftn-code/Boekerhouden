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
    public async Task Betaling_op_een_onbekend_factuurnummer_of_een_andere_leverancier_telt_niet()
    {
        var (wie, id) = await Administratie("Onbekend Nummer B.V.");
        await Factuur(wie, id, "O-1", new(2026, 3, 1));
        await Betaal(wie, id, "O-999", 24.99m, new(2026, 3, 10));
        await Betaal(wie, id, "O-1", 24.99m, new(2026, 3, 10), relatie: "ANDERE");

        Assert.Equal(24.99m, (await Lijst(wie, id, new(2026, 3, 31))).Openstaand);
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

    }
}
