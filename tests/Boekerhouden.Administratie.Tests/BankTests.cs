using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Regels;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De bank in de app (plakje A8): het verzonnen ING-afschrift van maart 2026 met drie mutaties (Belnet € 25,00 met
/// factuurnummer, J. de Vries € 1.210 ontvangen, Kantoorhandel Jansen € 199,65 betaald) en de e-factuur van Belnet.
/// </summary>
public sealed class BankTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private const string EigenIban = "NL20INGB0001234567";

    /// <summary>Het afschrift; met <paramref name="belnet"/> een ander bedrag voor Belnet, met <paramref name="april"/> een maand later.</summary>
    private static MemoryStream Afschrift(string belnet = "25,00", bool april = false, string? eindsaldo = null)
    {
        var tekst = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "ing-2026-03.mt940"));
        var eind = 5985.35m + 25m - decimal.Parse(belnet, new System.Globalization.CultureInfo("nl-NL"));
        tekst = tekst.Replace("D000000000025,00", $"D{belnet.PadLeft(15, '0')}", StringComparison.Ordinal)
            .Replace("C260331EUR000000005985,35", $"C260331EUR{eindsaldo ?? eind.ToString("0.00", new System.Globalization.CultureInfo("nl-NL")).PadLeft(15, '0')}", StringComparison.Ordinal);
        if (april)
        {
            tekst = tekst.Replace("C260301EUR000000005000,00", "C260401EUR000000006000,00", StringComparison.Ordinal)
                .Replace("C260331EUR", "C260430EUR", StringComparison.Ordinal)
                .Replace(":20:P260331000000001", ":20:P260430000000001", StringComparison.Ordinal)
                .Replace("2603020302", "2604020402", StringComparison.Ordinal)
                .Replace("2603050305", "2604050405", StringComparison.Ordinal)
                .Replace("2603200320", "2604200420", StringComparison.Ordinal)
                .Replace(":62F:C260430EUR000000005985,35", ":62F:C260430EUR000000006985,35", StringComparison.Ordinal);
        }

        return new MemoryStream(Encoding.Latin1.GetBytes(tekst));
    }

    /// <summary>De factuur BN-2026-0311 van Belnet (€ 24,99), geboekt; zonder IBAN als <paramref name="metIban"/> onwaar is.</summary>
    private async Task BoekBelnet(Uitvoerder wie, Guid id, bool metIban = true)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-2026-0311", "2026-03-01")), "belnet.xml", null);
        var herkend = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!.Herkend!;
        var nieuw = new NieuweRelatie("Belnet B.V.", metIban ? herkend.Iban : null, null, null);
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId.Value, new Factuurinvoer(null, nieuw, "BN-2026-0311", new(2026, 3, 1), new(2026, 3, 15), "Abonnement",
            [new Boekhouding.Facturen.Factuurregel("Abonnement", "4310", "I21", 20.65m, 4.34m)]));
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }

    [Fact]
    public async Task Inlezen_koppelt_het_bankdagboek_en_slaat_dezelfde_mutaties_een_tweede_keer_over()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();

        var eerste = await bank.Lees(wie, id, Afschrift(), "ing.mt940");
        Assert.True(eerste.IsGelukt, string.Join("; ", eerste.Fouten));
        Assert.Equal((1, 3, 0, 0), (eerste.Waarde!.Afschriften, eerste.Waarde.Nieuw, eerste.Waarde.Dubbel, eerste.Waarde.Automatisch));
        Assert.Empty(eerste.Waarde.Meldingen);

        var tweede = await bank.Lees(wie, id, Afschrift(), "ing-nog-eens.mt940");
        Assert.Equal((0, 3), (tweede.Waarde!.Nieuw, tweede.Waarde.Dubbel));

        var lijst = await bank.Lijst(wie, id);
        Assert.Equal(3, lijst.Count);
        Assert.All(lijst, m => Assert.Equal(("BNK", Banktransactiestatus.TeVerwerken), (m.Dagboek, m.Status)));
        Assert.Contains(lijst, m => m.Bedrag == 1210m && m.TegenNaam == "J. de Vries");
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(EigenIban, (await db.Dagboeken.SingleAsync(d => d.Code == "BNK")).Iban);
        Assert.Contains(EigenIban, (await db.Administraties.SingleAsync(a => a.Id == id)).Ibans);
        Assert.Equal(1, await db.Afschriften.CountAsync());
    }

    [Fact]
    public async Task Een_afschrift_dat_niet_sluit_wordt_geweigerd_en_er_wordt_niets_ingelezen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();

        var uitkomst = await bank.Lees(wie, id, Afschrift(eindsaldo: "000000005000,00"), "ing.mt940");

        Assert.False(uitkomst.IsGelukt);
        Assert.Contains("klopt niet", uitkomst.Fouten[0], StringComparison.Ordinal);
        Assert.Empty(await bank.Lijst(wie, id, ookGeboekt: true));
    }

    [Fact]
    public async Task Een_rekening_die_niet_van_de_administratie_is_wordt_geweigerd()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden))
        {
            (await db.Administraties.SingleAsync(a => a.Id == id)).Ibans = ["NL91ABNA0417164300"];
            await db.SaveChangesAsync();
        }

        var uitkomst = await app.Dienst<BankDienst>().Lees(wie, id, Afschrift(), "ing.mt940");

        Assert.False(uitkomst.IsGelukt);
        Assert.Contains("hoort niet bij deze administratie", uitkomst.Fouten[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_beginsaldo_dat_niet_aansluit_op_het_vorige_afschrift_geeft_een_melding()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(), "maart.mt940");

        var april = await bank.Lees(wie, id, Afschrift(april: true), "april.mt940");

        Assert.True(april.IsGelukt, string.Join("; ", april.Fouten));
        Assert.Equal(3, april.Waarde!.Nieuw);
        Assert.Contains("sluit niet aan", Assert.Single(april.Waarde.Meldingen), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Factuurnummer_en_bedrag_gelijk_wordt_automatisch_afgeletterd()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await BoekBelnet(wie, id);

        var gelezen = await app.Dienst<BankDienst>().Lees(wie, id, Afschrift(belnet: "24,99"), "ing.mt940");

        Assert.Equal(1, gelezen.Waarde!.Automatisch);
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(0m, (await db.OpenstaandePosten.SingleAsync()).Openstaand);
        var belnet = await db.Banktransacties.SingleAsync(t => t.Bedrag == -24.99m);
        Assert.Equal(Banktransactiestatus.Geboekt, belnet.Status);
        Assert.StartsWith("Afgeletterd", belnet.Melding, StringComparison.Ordinal);
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((0m, -24.99m), (Saldo(proef, "1600"), Saldo(proef, "1100")));
        Assert.Empty(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Voorstel_bevestigen_lettert_af_en_leert_het_iban_bij_de_relatie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await BoekBelnet(wie, id, metIban: false);
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(), "ing.mt940");
        var mutatie = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -25m);

        var verwerking = (await bank.Verwerking(wie, id, mutatie.Id))!;
        var kandidaat = verwerking.Voorstel.Afletteren.Kandidaten[0];
        Assert.Equal(("Factuurnummer, ander bedrag", Boekhouding.Afletteren.Zekerheid.Voorstel), (kandidaat.Reden, kandidaat.Zekerheid));
        Assert.Single(verwerking.OpenPosten);

        var uitkomst = await bank.Afletteren(wie, id, mutatie.Id, [.. kandidaat.Posten.Select(p => p.Id)]);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(0m, (await db.OpenstaandePosten.SingleAsync()).Openstaand);
        Assert.Equal(["NL63TRIO0212345678"], (await db.Relaties.SingleAsync()).Ibans);
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((0m, 0.01m), (Saldo(proef, "1600"), Saldo(proef, "2000")));
        Assert.False((await bank.Afletteren(wie, id, mutatie.Id, [kandidaat.Posten[0].Id])).IsGelukt);
    }

    [Fact]
    public async Task Op_rekening_met_btw_en_een_automatische_regel_bij_het_volgende_afschrift()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(), "maart.mt940");
        var jansen = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -199.65m);

        Assert.False((await bank.OpRekening(wie, id, jansen.Id, "1600", null, null)).IsGelukt);
        var uitkomst = await bank.OpRekening(wie, id, jansen.Id, "4300", "I21", "Kantoorartikelen");

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((165m, 34.65m, -199.65m), (Saldo(proef, "4300"), Saldo(proef, "1500"), Saldo(proef, "1100")));
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen))
        {
            var keuze = await db.Leerkeuzes.SingleAsync();
            Assert.Equal(("B|iban|NL36INGB0007654321", "4300", "I21", -199.65m), (keuze.Sleutel, keuze.Rekeningnummer, keuze.BtwCode, keuze.Bedrag));
        }

        var regel = await app.Dienst<InrichtingDienst>().VoegRegelToe(wie, id,
            new Verwerkingsregel("Jansen", "4300", "I21", Automatisch: true, Soort: Verwerkingssoort.Bankmutatie, TegenIban: "NL36INGB0007654321"));
        Assert.True(regel.IsGelukt, string.Join("; ", regel.Fouten));
        var april = await bank.Lees(wie, id, Afschrift(april: true), "april.mt940");

        Assert.Equal(1, april.Waarde!.Automatisch);
        Assert.Equal(330m, Saldo(await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026), "4300"));
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_geen_bank()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(beheerder, id, Afschrift(), "ing.mt940");
        var mutatie = (await bank.Lijst(beheerder, id))[0];
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, beheerder);

        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Lees(buiten, id, Afschrift(), "x.mt940"));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Lijst(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Verwerking(buiten, id, mutatie.Id));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Lees(inzien, id, Afschrift(), "y.mt940"));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.OpRekening(inzien, id, mutatie.Id, "4900", null, null));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Afletteren(inzien, id, mutatie.Id, [Guid.NewGuid()]));
        Assert.Equal(3, (await bank.Lijst(inzien, id)).Count);
    }

    private static decimal Saldo(Boekhouding.Grootboek.Proefbalans proef, string rekening) =>
        proef.Regels.Single(r => r.Rekening.Nummer == rekening).Saldo;
}
