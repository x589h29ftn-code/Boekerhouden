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
        // Een cent te veel betaald: te vorderen op de factuur, niet naar de vraagposten.
        Assert.Equal(0.01m, (await db.OpenstaandePosten.SingleAsync()).Openstaand);
        Assert.Equal(["NL63TRIO0212345678"], (await db.Relaties.SingleAsync()).Ibans);
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((0.01m, 0m), (Saldo(proef, "1600"), Saldo(proef, "2000")));
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

        // Aanpassen: de kostenrekening mag anders, de bankregel en de datum niet; de mutatie wijst naar de nieuwe boeking.
        var grootboek = app.Dienst<GrootboekDienst>();
        var boeking = (await grootboek.Posten(wie, id, "BNK")).Single().Post;
        var anders = boeking with { Regels = [.. boeking.Regels.Select(r => r.Rekeningnummer == "4300" ? r with { Rekeningnummer = "4320" } : r)] };
        var bankAnders = anders with { Regels = [.. anders.Regels.Select(r => r.Rekeningnummer == "1100" ? r with { Bedrag = -199m } : r.Rekeningnummer == "4320" ? r with { Bedrag = r.Bedrag - 0.65m } : r)] };
        Assert.Equal(["De regels op 1100 horen bij het bankafschrift of de openstaande posten en blijven gelijk."], (await grootboek.PasAan(wie, id, "BNK", boeking.Boekstuk, bankAnders)).Fouten);
        var aangepast = await grootboek.PasAan(wie, id, "BNK", boeking.Boekstuk, anders);
        Assert.True(aangepast.IsGelukt, string.Join("; ", aangepast.Fouten));
        Assert.Equal((0m, 165m, -199.65m), (Saldo(await grootboek.Proefbalans(wie, id, 2026), "4300"), Saldo(await grootboek.Proefbalans(wie, id, 2026), "4320"), Saldo(await grootboek.Proefbalans(wie, id, 2026), "1100")));
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen))
        {
            var nieuweBoeking = await db.Geldend.SingleAsync(b => b.Dagboek == "BNK" && b.Boekstuk == boeking.Boekstuk);
            Assert.Equal((2, nieuweBoeking.Id), (nieuweBoeking.Versie, (await db.Banktransacties.SingleAsync(t => t.Id == jansen.Id)).BoekingId));
        }

        var regel = await app.Dienst<InrichtingDienst>().VoegRegelToe(wie, id,
            new Verwerkingsregel("Jansen", "4300", "I21", Automatisch: true, Soort: Verwerkingssoort.Bankmutatie, TegenIban: "NL36INGB0007654321"));
        Assert.True(regel.IsGelukt, string.Join("; ", regel.Fouten));
        var april = await bank.Lees(wie, id, Afschrift(april: true), "april.mt940");

        Assert.Equal(1, april.Waarde!.Automatisch);
        Assert.Equal(165m, Saldo(await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026), "4300"));
    }

    [Fact]
    public async Task Factuur_geupload_bij_een_betaling_wordt_na_het_boeken_gekoppeld()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        await bank.Lees(wie, id, Afschrift(belnet: "24,99"), "ing.mt940");
        var betaling = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -24.99m);

        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-2026-0311", "2026-03-01")), "belnet.xml", null, betaling.Id);
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(null, new NieuweRelatie("Belnet B.V.", null, null, null), "BN-2026-0311",
            new(2026, 3, 1), new(2026, 3, 15), "Abonnement", [new Boekhouding.Facturen.Factuurregel("Abonnement", "4310", "I21", 20.65m, 4.34m)]));

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.DoesNotContain(await bank.Lijst(wie, id), m => m.Id == betaling.Id);
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(0m, (await db.OpenstaandePosten.SingleAsync()).Openstaand);
        Assert.Equal(["NL63TRIO0212345678"], (await db.Relaties.SingleAsync()).Ibans);
        Assert.StartsWith("Gekoppeld", (await db.Banktransacties.SingleAsync(t => t.Id == betaling.Id)).Melding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Een_factuur_die_later_binnenkomt_lettert_de_betaling_alsnog_af()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(belnet: "24,99"), "ing.mt940");
        Assert.Equal(3, (await bank.Lijst(wie, id)).Count);

        await BoekBelnet(wie, id);

        Assert.Equal(2, (await bank.Lijst(wie, id)).Count);
        Assert.Empty(await bank.OpenPosten(wie, id));
    }

    [Fact]
    public async Task Een_deelbetaling_met_factuurnummer_en_iban_lettert_automatisch_af()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await BoekBelnet(wie, id);
        var open = (await bank.OpenPosten(wie, id)).Single();
        Assert.Equal(("Belnet B.V.", -24.99m, false), (open.Naam, open.Openstaand, open.Deels));

        Assert.Equal(1, (await bank.Lees(wie, id, Afschrift(belnet: "20,00"), "ing.mt940")).Waarde!.Automatisch);

        Assert.DoesNotContain(await bank.Lijst(wie, id), m => m.Bedrag == -20m);
        var post = (await bank.OpenPosten(wie, id)).Single();
        Assert.Equal((-4.99m, true, new DateOnly(2026, 3, 15)), (post.Openstaand, post.Deels, post.Vervaldatum));
        Assert.Equal(("2000", 0m), await bank.Vraagposten(wie, id));
        var buiten = await app.Gebruiker("Buiten");
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.OpenPosten(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Vraagposten(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.NaFactuur(buiten, id, post.Id, null));
    }

    [Fact]
    public async Task Openstaande_posten_crediteuren_op_een_peildatum_met_pdf_opmaak()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Voorbeeld Crediteuren B.V.");
        var bank = app.Dienst<BankDienst>();
        await BoekBelnet(wie, id);
        await bank.Lees(wie, id, Afschrift(belnet: "20,00"), "ing.mt940");
        var openposten = app.Dienst<OpenpostenDienst>();

        var voor = await openposten.Crediteuren(wie, id, new(2026, 3, 1));
        var na = await openposten.Crediteuren(wie, id, new(2026, 3, 31));

        var post = Assert.Single(Assert.Single(voor.Relaties).Posten);
        Assert.Equal(("Belnet B.V.", "BN-2026-0311", 24.99m, 0m, 24.99m, 0), (voor.Relaties[0].Naam, post.Factuurnummer, post.Factuurbedrag, post.Betaald, post.Openstaand, post.Ouderdom));
        Assert.Equal(("INK", true), (post.Dagboek, post.Document is not null));
        var later = Assert.Single(Assert.Single(na.Relaties).Posten);
        Assert.Equal((20m, 4.99m, 30), (later.Betaald, later.Openstaand, later.Ouderdom));
        Assert.Equal((24.99m, 20m, 4.99m), (na.Factuurbedrag, na.Betaald, na.Openstaand));
        Assert.Empty((await openposten.Crediteuren(wie, id, new(2026, 2, 28))).Relaties);
        Assert.Empty((await openposten.Crediteuren(wie, id, new(2026, 3, 31), "ANDERE")).Relaties);

        var html = Export.OpenpostenHtml.Maak(na);
        Assert.Contains("Voorbeeld Crediteuren B.V.", html, StringComparison.Ordinal);
        Assert.Contains("31-03-2026", html, StringComparison.Ordinal);
        Assert.Contains("Totaal generaal", html, StringComparison.Ordinal);
        Assert.Contains("4,99", html, StringComparison.Ordinal);
        var buiten = await app.Gebruiker("Buiten Crediteuren");
        await Assert.ThrowsAsync<GeenToegangException>(() => openposten.Crediteuren(buiten, id, new(2026, 3, 31)));
    }

    [Fact]
    public async Task Te_veel_betaald_via_de_bank_blijft_als_te_vorderen_op_de_factuur()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Overbetaling B.V.");
        var bank = app.Dienst<BankDienst>();
        await BoekBelnet(wie, id);
        await bank.Lees(wie, id, Afschrift(belnet: "30,00"), "ing.mt940");
        var mutatie = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -30m);

        Assert.True((await bank.Afletteren(wie, id, mutatie.Id, [(await bank.OpenPosten(wie, id)).Single().Id])).IsGelukt);

        var post = Assert.Single(Assert.Single((await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, new(2026, 3, 31))).Relaties).Posten);
        Assert.Equal((24.99m, 30m, -5.01m), (post.Factuurbedrag, post.Betaald, post.Openstaand));
        Assert.Equal(5.01m, (await bank.OpenPosten(wie, id)).Single().Openstaand);
        Assert.Equal(("2000", 0m), await bank.Vraagposten(wie, id));
        Assert.Equal(24.99m, (await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, new(2026, 3, 1))).Openstaand);
    }

    [Fact]
    public async Task Opnieuw_afletteren_boekt_wat_na_het_inlezen_zeker_is_geworden()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(), "ing.mt940");
        Assert.True((await app.Dienst<InrichtingDienst>().VoegRegelToe(wie, id,
            new Verwerkingsregel("Jansen", "4300", "I21", Automatisch: true, Soort: Verwerkingssoort.Bankmutatie, TegenIban: "NL36INGB0007654321"))).IsGelukt);

        var opnieuw = await bank.VerwerkOpnieuw(wie, id);

        Assert.Equal(new Herverwerking(3, 1), opnieuw);
        Assert.DoesNotContain(await bank.Lijst(wie, id), m => m.Bedrag == -199.65m);
        Assert.Equal(new Herverwerking(2, 0), await bank.VerwerkOpnieuw(wie, id));
        var inzien = await app.Gebruiker("Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.VerwerkOpnieuw(inzien, id));
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
