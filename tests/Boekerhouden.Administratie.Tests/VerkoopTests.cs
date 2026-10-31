using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Verkoopfacturen maken (plakje A9): concept, definitief met doorlopend nummer, boeking, debiteuren, credit en verdeling.</summary>
public sealed class VerkoopTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>Een administratie met adres en btw-nummer, zodat er gefactureerd kan worden.</summary>
    private async Task<(Uitvoerder Wie, Guid Id)> Administratie(bool metGegevens = true)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Bakkerij Verkoop");
        if (metGegevens)
        {
            await EigenGegevens(wie, id);
        }

        return (wie, id);
    }

    private async Task EigenGegevens(Uitvoerder wie, Guid id)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var uitkomst = await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@bakkerij.test"),
        });
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }

    /// <summary>Een klant met adres; geeft de code.</summary>
    private async Task<string> Klant(Uitvoerder wie, Guid id, string naam, string? btwNummer = null, string? land = null, string? iban = null)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(naam, btwNummer, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    private static Verkoopinvoer Invoer(string klant, DateOnly datum, params Verkoopregel[] regels) =>
        new(klant, datum, datum.AddDays(14), "Werkzaamheden", regels.Length == 0 ? [new Verkoopregel("Advies", 10m, 100m, "8000", "V21")] : regels);

    /// <summary>Een concept dat meteen definitief wordt; geeft id en nummer.</summary>
    private async Task<(Guid Id, string Nummer)> Factuur(Uitvoerder wie, Guid id, Verkoopinvoer invoer)
    {
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, invoer);
        Assert.True(concept.IsGelukt, string.Join("; ", concept.Fouten));
        var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
        Assert.True(definitief.IsGelukt, string.Join("; ", definitief.Fouten));
        return (concept.Waarde, definitief.Waarde!);
    }

    [Fact]
    public async Task Definitief_geeft_het_volgende_nummer_een_kenmerk_en_boekt_op_debiteuren()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Klant B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();

        var (eerste, nummer) = await Factuur(wie, id, Invoer(klant, new(2026, 3, 1),
            new Verkoopregel("Advies", 10m, 85m, "8000", "V21"), new Verkoopregel("Broodjes", 20m, 2.5m, "8010", "V9")));

        Assert.Equal("20260001", nummer);
        var weergave = (await verkoop.Open(wie, id, eerste))!;
        Assert.Equal(Verkoopstatus.Definitief, weergave.Status);
        Assert.Equal(("VRK", "20260001"), weergave.Boeking);
        Assert.True(Betalingskenmerk.IsGeldig(weergave.Factuur.Betalingskenmerk));
        Assert.Equal((900m, 183m, 1083m), (weergave.Exclusief, weergave.BtwTotaal, weergave.Totaal));
        Assert.Equal(1083m, weergave.Openstaand);

        var boeking = (await app.Dienst<GrootboekDienst>().Boeking(wie, id, "VRK", "20260001"))!.Boeking;
        var regels = boeking.Post.Regels;
        Assert.Equal(1083m, regels.Where(r => r.Rekeningnummer == "1300").Sum(r => r.Bedrag));
        Assert.Equal(-850m, regels.Where(r => r.Rekeningnummer == "8000").Sum(r => r.Bedrag));
        Assert.Equal(-50m, regels.Where(r => r.Rekeningnummer == "8010").Sum(r => r.Bedrag));
        Assert.Equal(-183m, regels.Where(r => r.Rekeningnummer == "1510").Sum(r => r.Bedrag));
        Assert.Contains(regels, r => r.Rekeningnummer == "1510" && r.BtwCode == "V21" && r.Bedrag == -178.5m);
        Assert.Contains(regels, r => r.Rekeningnummer == "1510" && r.BtwCode == "V9" && r.Bedrag == -4.5m);

        Assert.Equal("20260002", (await Factuur(wie, id, Invoer(klant, new(2026, 3, 2)))).Nummer);
        Assert.Equal("20260003", (await Factuur(wie, id, Invoer(klant, new(2026, 2, 28)))).Nummer);
        Assert.Equal("20270001", (await Factuur(wie, id, Invoer(klant, new(2027, 1, 2)))).Nummer);
        Assert.Equal("20260004", (await Factuur(wie, id, Invoer(klant, new(2026, 12, 31)))).Nummer);
        Assert.Empty(await verkoop.Gaten(wie, id, 2026));

        var boek = await verkoop.Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(4, boek.Count);
        Assert.All(boek, r => Assert.Equal(r.Exclusief + r.Btw, r.Totaal));
        Assert.Equal((900m, 183m, 1083m, 1083m), (boek[1].Exclusief, boek[1].Btw, boek[1].Totaal, boek[1].Openstaand));
        Assert.Equal(eerste, boek.Single(r => r.Factuurnummer == "20260001").Document);

        var debiteuren = await app.Dienst<OpenpostenDienst>().Debiteuren(wie, id, new(2026, 12, 31));
        Assert.Equal(1083m + 3 * 1210m, debiteuren.Openstaand);
        var kaart = await app.Dienst<GrootboekDienst>().Kaart(wie, id, "1300", new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(kaart.Eindsaldo, debiteuren.Openstaand);
    }

    [Fact]
    public async Task Een_concept_is_te_wijzigen_en_te_verwijderen_een_definitieve_factuur_niet()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Klant B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = (await verkoop.Bewaar(wie, id, null, Invoer(klant, new(2026, 4, 1)))).Waarde;

        Assert.True((await verkoop.Bewaar(wie, id, concept, Invoer(klant, new(2026, 4, 2), new Verkoopregel("Training", 1m, 750m, "8000", "V21")))).IsGelukt);
        var gewijzigd = (await verkoop.Open(wie, id, concept))!;
        Assert.Equal((Verkoopstatus.Concept, (string?)null, 750m), (gewijzigd.Status, gewijzigd.Factuur.Nummer, gewijzigd.Exclusief));
        Assert.Single(gewijzigd.Factuur.Regels);

        var tweede = (await verkoop.Bewaar(wie, id, null, Invoer(klant, new(2026, 4, 3)))).Waarde;
        Assert.True((await verkoop.VerwijderConcept(wie, id, tweede)).IsGelukt);
        Assert.Null(await verkoop.Open(wie, id, tweede));

        Assert.True((await verkoop.MaakDefinitief(wie, id, concept)).IsGelukt);
        Assert.False((await verkoop.Bewaar(wie, id, concept, Invoer(klant, new(2026, 4, 2)))).IsGelukt);
        Assert.False((await verkoop.VerwijderConcept(wie, id, concept)).IsGelukt);
        Assert.False((await verkoop.MaakDefinitief(wie, id, concept)).IsGelukt);
        Assert.Single(await verkoop.Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31)));
    }

    [Fact]
    public async Task Een_onvolledige_factuur_wordt_niet_definitief_en_kost_geen_nummer()
    {
        var (wie, id) = await Administratie(metGegevens: false);
        var klant = await Klant(wie, id, "Klant B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = (await verkoop.Bewaar(wie, id, null, Invoer(klant, new(2026, 5, 1)))).Waarde;

        var mislukt = await verkoop.MaakDefinitief(wie, id, concept);

        Assert.False(mislukt.IsGelukt);
        Assert.Contains(mislukt.Fouten, f => f.Contains("adres van de eigen onderneming", StringComparison.Ordinal));
        Assert.Contains(mislukt.Fouten, f => f.Contains("btw-nummer van de eigen onderneming", StringComparison.Ordinal));
        Assert.Equal(Verkoopstatus.Concept, (await verkoop.Open(wie, id, concept))!.Status);

        await EigenGegevens(wie, id);
        Assert.Equal("20260001", (await verkoop.MaakDefinitief(wie, id, concept)).Waarde);
    }

    [Fact]
    public async Task Verlegd_en_icp_vragen_het_btw_nummer_van_de_klant_en_boeken_geen_btw()
    {
        var (wie, id) = await Administratie();
        var verkoop = app.Dienst<VerkoopDienst>();
        var zonder = await Klant(wie, id, "Bouw Zonder B.V.");
        var concept = (await verkoop.Bewaar(wie, id, null, Invoer(zonder, new(2026, 6, 1), new Verkoopregel("Montage", 8m, 60m, "8000", "VVL")))).Waarde;
        Assert.Contains((await verkoop.MaakDefinitief(wie, id, concept)).Fouten, f => f.Contains("verlegde btw", StringComparison.Ordinal));

        var belg = await Klant(wie, id, "Bakker Belge SRL", "BE0123456789", "BE");
        var (icp, nummer) = await Factuur(wie, id, Invoer(belg, new(2026, 6, 2), new Verkoopregel("Ovens", 2m, 2500m, "8010", "VEU")));

        var weergave = (await verkoop.Open(wie, id, icp))!;
        Assert.Equal(5000m, weergave.Totaal);
        Assert.Contains(weergave.Vermeldingen, v => v.Contains("Intracommunautaire levering", StringComparison.Ordinal));
        Assert.Equal("BE", weergave.Factuur.Klant.Adres.Landcode);
        var regels = (await app.Dienst<GrootboekDienst>().Boeking(wie, id, "VRK", nummer))!.Boeking.Post.Regels;
        Assert.DoesNotContain(regels, r => r.Rekeningnummer == "1510");
        Assert.Contains(regels, r => r.Rekeningnummer == "8010" && r.BtwCode == "VEU" && r.Bedrag == -5000m);
    }

    [Fact]
    public async Task Een_creditfactuur_krijgt_een_nummer_uit_dezelfde_reeks_en_wordt_verrekend()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Klant B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var (factuur, _) = await Factuur(wie, id, Invoer(klant, new(2026, 3, 1)));

        var credit = await verkoop.Crediteer(wie, id, factuur);
        Assert.True(credit.IsGelukt, string.Join("; ", credit.Fouten));
        var concept = (await verkoop.Open(wie, id, credit.Waarde))!;
        Assert.Equal((-1000m, -1210m, factuur), (concept.Exclusief, concept.Totaal, concept.CreditVoorId));
        Assert.Equal("20260001", concept.Factuur.CreditVoor);
        Assert.Equal("20260002", (await verkoop.MaakDefinitief(wie, id, credit.Waarde)).Waarde);

        Assert.Equal(0m, (await verkoop.Open(wie, id, factuur))!.Openstaand);
        Assert.Equal(0m, (await verkoop.Open(wie, id, credit.Waarde))!.Openstaand);
        Assert.Empty((await app.Dienst<OpenpostenDienst>().Debiteuren(wie, id, new(2026, 12, 31))).Relaties);
        var boek = await verkoop.Verkoopboek(wie, id, new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(0m, boek.Sum(r => r.Totaal));
        Assert.False((await verkoop.Crediteer(wie, id, credit.Waarde)).IsGelukt);
    }

    [Fact]
    public async Task Een_jaarabonnement_wordt_over_de_maanden_verdeeld()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Abonnee B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var context = await verkoop.Context(wie, id);
        Assert.Equal("1710", context.Verdeelrekening);
        Assert.Equal("8000", context.Omzetrekening);

        var invoer = Invoer(klant, new(2026, 1, 5), new Verkoopregel("Jaarabonnement 2026", 1m, 1200m, "8000", "V21")) with
        {
            Verdeling = new Periodeverdeling(new(2026, 1, 1), new(2026, 12, 31), "1710"),
        };
        var (factuur, nummer) = await Factuur(wie, id, invoer);

        var weergave = (await verkoop.Open(wie, id, factuur))!;
        Assert.Equal(12, weergave.Verdeelboekstukken.Count);
        var grootboek = app.Dienst<GrootboekDienst>();
        var regels = (await grootboek.Boeking(wie, id, "VRK", nummer))!.Boeking.Post.Regels;
        Assert.Contains(regels, r => r.Rekeningnummer == "1710" && r.Bedrag == -1200m);
        Assert.Contains(regels, r => r.Rekeningnummer == "1510" && r.Bedrag == -252m);
        Assert.Equal(-100m, (await grootboek.Kaart(wie, id, "8000", new(2026, 1, 1), new(2026, 1, 31))).Eindsaldo);
        Assert.Equal(-600m, (await grootboek.Kaart(wie, id, "8000", new(2026, 1, 1), new(2026, 6, 30))).Eindsaldo);
        Assert.Equal(-600m, (await grootboek.Kaart(wie, id, "1710", new(2026, 1, 1), new(2026, 6, 30))).Eindsaldo);
        Assert.Equal(0m, (await grootboek.Kaart(wie, id, "1710", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
        Assert.Equal(-1200m, (await grootboek.Kaart(wie, id, "8000", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
    }

    [Fact]
    public async Task De_bank_lettert_af_op_het_betalingskenmerk_ook_als_de_ontvangst_er_eerst_was()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "J. de Vries");
        var verkoop = app.Dienst<VerkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var concept = (await verkoop.Bewaar(wie, id, null, Invoer(klant, new(2026, 3, 1)))).Waarde;
        var kenmerk = Betalingskenmerk.Maak("20260001");

        // De ontvangst van € 1.210 met het kenmerk staat al op de bank voordat de factuur definitief is.
        var tekst = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "ing-2026-03.mt940")).Replace("1234567890123456", kenmerk, StringComparison.Ordinal);
        Assert.True((await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(tekst)), "ing.mt940")).IsGelukt);
        Assert.Contains(await bank.Lijst(wie, id), m => m.Bedrag == 1210m && m.Status == Banktransactiestatus.TeVerwerken);

        Assert.Equal("20260001", (await verkoop.MaakDefinitief(wie, id, concept)).Waarde);

        Assert.Equal(0m, (await verkoop.Open(wie, id, concept))!.Openstaand);
        Assert.Contains(await bank.Lijst(wie, id, ookGeboekt: true), m => m.Bedrag == 1210m && m.Status == Banktransactiestatus.Geboekt);
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_geen_verkoop()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Klant B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = (await verkoop.Bewaar(wie, id, null, Invoer(klant, new(2026, 3, 1)))).Waarde;
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);

        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Lijst(buiten, id, DateOnly.MinValue, DateOnly.MaxValue));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Open(buiten, id, concept));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Context(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Verkoopboek(buiten, id, DateOnly.MinValue, DateOnly.MaxValue));
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<OpenpostenDienst>().Debiteuren(buiten, id, new(2026, 12, 31)));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Bewaar(inzien, id, null, Invoer(klant, new(2026, 3, 1))));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.MaakDefinitief(inzien, id, concept));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.VerwijderConcept(inzien, id, concept));
        await Assert.ThrowsAsync<GeenToegangException>(() => verkoop.Crediteer(inzien, id, concept));
        Assert.Single(await verkoop.Lijst(inzien, id, DateOnly.MinValue, DateOnly.MaxValue));

        // De ondernemer mag factureren.
        Assert.Equal("20260001", (await verkoop.MaakDefinitief(ondernemer, id, concept)).Waarde);

        // Een factuur uit een andere administratie is hier niet te zien.
        var ander = await app.Administratie("Andere Zaak");
        Assert.Null(await verkoop.Open(wie, ander, concept));
        Assert.False((await verkoop.MaakDefinitief(wie, ander, concept)).IsGelukt);
    }

    [Fact]
    public async Task Elke_handeling_staat_in_het_log()
    {
        var (wie, id) = await Administratie();
        var klant = await Klant(wie, id, "Klant B.V.");
        await Factuur(wie, id, Invoer(klant, new(2026, 3, 1)));

        await using var db = await app.Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        var acties = await db.AuditRegels.Where(a => a.AdministratieId == id).Select(a => a.Actie).ToListAsync();
        Assert.Contains(AuditActies.VerkoopconceptGemaakt, acties);
        Assert.Contains(AuditActies.VerkoopfactuurDefinitief, acties);
    }
}
