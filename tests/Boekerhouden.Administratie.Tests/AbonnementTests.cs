using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Terugkerende verkoopfacturen: huur per maand, een jaarabonnement verdeeld, automatisch definitief.</summary>
public sealed class AbonnementTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id, string Klant)> Administratie(bool metGegevens = true)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Verhuur Voorbeeld");
        var inrichting = app.Dienst<InrichtingDienst>();
        if (metGegevens)
        {
            var nu = await inrichting.Instellingen(wie, id);
            Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
            {
                BtwNummer = "NL123456782B01",
                Ibans = ["NL20INGB0001234567"],
                Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens"),
            })).IsGelukt);
        }

        var klant = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Huurder B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Betalingstermijn: 14)), null);
        return (wie, id, (await inrichting.Relaties(wie, id)).Single(r => r.Id == klant.Waarde).Code);
    }

    private static Abonnementinvoer Huur(string klant, bool automatisch = false) =>
        new("Huur kantoor", klant, Herhaling.Maand, new(2026, 1, 1), null, "Huur #periode",
            [new Verkoopregel("Huur kantoorruimte #maand", 1m, 1000m, "8000", "V21"), new Verkoopregel("Servicekosten", 1m, 50m, "8000", "V21")],
            Automatisch: automatisch);

    [Fact]
    public async Task Huur_per_maand_wordt_tot_vandaag_gemaakt_als_concept_en_nooit_dubbel()
    {
        var (wie, id, klant) = await Administratie();
        var abonnementen = app.Dienst<AbonnementDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var abonnement = (await abonnementen.Bewaar(wie, id, null, Huur(klant))).Waarde;

        Assert.Equal(3, await abonnementen.TeMaken(wie, id, new(2026, 3, 15)));
        var verslag = (await abonnementen.Maak(wie, id, new(2026, 3, 15))).Waarde!;

        Assert.Equal((3, 0), (verslag.Facturen.Count, verslag.Definitief));
        var concepten = await verkoop.Lijst(wie, id, DateOnly.MinValue, DateOnly.MaxValue);
        Assert.Equal(["Huur januari 2026", "Huur februari 2026", "Huur maart 2026"], concepten.OrderBy(c => c.Datum).Select(c => c.Omschrijving));
        Assert.All(concepten, c => Assert.Equal((Verkoopstatus.Concept, 1270.5m), (c.Status, c.Totaal)));
        var maart = (await verkoop.Open(wie, id, concepten.Single(c => c.Datum == new DateOnly(2026, 3, 1)).Id))!;
        Assert.Equal("Huur kantoorruimte maart 2026", maart.Factuur.Regels[0].Omschrijving);
        Assert.Equal(new DateOnly(2026, 3, 15), maart.Factuur.Vervaldatum);

        Assert.Empty((await abonnementen.Maak(wie, id, new(2026, 3, 31))).Waarde!.Facturen);
        Assert.Single((await abonnementen.Maak(wie, id, new(2026, 4, 1))).Waarde!.Facturen);
        var regel = (await abonnementen.Lijst(wie, id)).Single(a => a.Id == abonnement);
        Assert.Equal((4, new DateOnly(2026, 5, 1), 1050m), (regel.Gemaakt, regel.VolgendeDatum, regel.Bedrag));
    }

    [Fact]
    public async Task Automatisch_wordt_elke_factuur_definitief_met_doorlopende_nummers()
    {
        var (wie, id, klant) = await Administratie();
        var abonnementen = app.Dienst<AbonnementDienst>();
        await abonnementen.Bewaar(wie, id, null, Huur(klant, automatisch: true));

        var verslag = (await abonnementen.Maak(wie, id, new(2026, 3, 1))).Waarde!;

        Assert.Equal(["20260001", "20260002", "20260003"], verslag.Facturen.Select(f => f.Nummer));
        Assert.Empty(verslag.Meldingen);
        var debiteuren = await app.Dienst<OpenpostenDienst>().Debiteuren(wie, id, new(2026, 3, 31));
        Assert.Equal(3 * 1270.5m, debiteuren.Openstaand);
    }

    [Fact]
    public async Task Een_jaarabonnement_in_januari_wordt_over_het_jaar_verdeeld()
    {
        var (wie, id, klant) = await Administratie();
        var abonnementen = app.Dienst<AbonnementDienst>();
        await abonnementen.Bewaar(wie, id, null, new Abonnementinvoer("Onderhoudscontract", klant, Herhaling.Jaar, new(2026, 1, 1), null, "Onderhoud #periode",
            [new Verkoopregel("Onderhoudscontract #jaar", 1m, 1200m, "8000", "V21")], Automatisch: true, Verdelen: true, Verdeelrekening: "1710"));

        var verslag = (await abonnementen.Maak(wie, id, new(2026, 12, 31))).Waarde!;

        Assert.Equal("20260001", Assert.Single(verslag.Facturen).Nummer);
        var factuur = (await app.Dienst<VerkoopDienst>().Open(wie, id, verslag.Facturen[0].Id))!;
        Assert.Equal("Onderhoud 2026", factuur.Factuur.Omschrijving);
        Assert.Equal(12, factuur.Verdeelboekstukken.Count);
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.Equal(-300m, (await grootboek.Kaart(wie, id, "8000", new(2026, 1, 1), new(2026, 3, 31))).Eindsaldo);
        Assert.Equal(-900m, (await grootboek.Kaart(wie, id, "1710", new(2026, 1, 1), new(2026, 3, 31))).Eindsaldo);
        Assert.Equal(-1200m, (await grootboek.Kaart(wie, id, "8000", new(2026, 1, 1), new(2026, 12, 31))).Eindsaldo);
    }

    [Fact]
    public async Task Na_het_einde_of_gestopt_komt_er_niets_meer()
    {
        var (wie, id, klant) = await Administratie();
        var abonnementen = app.Dienst<AbonnementDienst>();
        var abonnement = (await abonnementen.Bewaar(wie, id, null, Huur(klant) with { Einde = new(2026, 2, 28) })).Waarde;

        Assert.Equal(2, (await abonnementen.Maak(wie, id, new(2026, 12, 31))).Waarde!.Facturen.Count);
        Assert.Null((await abonnementen.Lijst(wie, id)).Single().VolgendeDatum);

        var tweede = (await abonnementen.Bewaar(wie, id, null, Huur(klant) with { Naam = "Opslag", Actief = false })).Waarde;
        Assert.Empty((await abonnementen.Maak(wie, id, new(2026, 12, 31))).Waarde!.Facturen);
        Assert.True((await abonnementen.Verwijder(wie, id, abonnement)).IsGelukt);
        Assert.Equal(2, (await app.Dienst<VerkoopDienst>().Lijst(wie, id, DateOnly.MinValue, DateOnly.MaxValue)).Count);
        Assert.Equal([tweede], (await abonnementen.Lijst(wie, id)).Select(a => a.Id));
    }

    [Fact]
    public async Task Kan_een_factuur_niet_definitief_dan_blijft_hij_concept_met_de_reden()
    {
        var (wie, id, klant) = await Administratie(metGegevens: false);
        var abonnementen = app.Dienst<AbonnementDienst>();
        await abonnementen.Bewaar(wie, id, null, Huur(klant, automatisch: true));

        var verslag = (await abonnementen.Maak(wie, id, new(2026, 1, 31))).Waarde!;

        Assert.Equal((1, 0), (verslag.Facturen.Count, verslag.Definitief));
        Assert.Contains(verslag.Meldingen, m => m.StartsWith("Huur kantoor: als concept bewaard", StringComparison.Ordinal));
        Assert.Equal(Verkoopstatus.Concept, Assert.Single(await app.Dienst<VerkoopDienst>().Lijst(wie, id, DateOnly.MinValue, DateOnly.MaxValue)).Status);
    }

    [Fact]
    public async Task Van_een_factuur_een_terugkerende_maken_en_ongeldige_invoer_weigeren()
    {
        var (wie, id, klant) = await Administratie();
        var verkoop = app.Dienst<VerkoopDienst>();
        var abonnementen = app.Dienst<AbonnementDienst>();
        var factuur = (await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 5, 1), new(2026, 5, 15), "Hosting mei",
            [new Verkoopregel("Hosting", 1m, 25m, "8000", "V21")]))).Waarde;

        var voorstel = (await abonnementen.VanFactuur(wie, id, factuur))!;

        Assert.Equal((klant, new DateOnly(2026, 6, 1), Herhaling.Maand), (voorstel.Relatie, voorstel.Begin, voorstel.Herhaling));
        Assert.Equal("Hosting", Assert.Single(voorstel.Regels).Omschrijving);
        var fouten = (await abonnementen.Bewaar(wie, id, null, voorstel with { Naam = "", Relatie = "ONBEKEND", Regels = [], Einde = new(2026, 1, 1), Verdelen = true, Verdeelrekening = "8000" })).Fouten;
        Assert.Contains(fouten, f => f.Contains("naam", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("klant", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("geen regels", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("einddatum", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("balansrekening", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_geen_terugkerende_facturen()
    {
        var (wie, id, klant) = await Administratie();
        var abonnementen = app.Dienst<AbonnementDienst>();
        var abonnement = (await abonnementen.Bewaar(wie, id, null, Huur(klant))).Waarde;
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.Lijst(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.Open(buiten, id, abonnement));
        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.TeMaken(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.Bewaar(inzien, id, abonnement, Huur(klant)));
        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.Maak(inzien, id, new(2026, 3, 1)));
        await Assert.ThrowsAsync<GeenToegangException>(() => abonnementen.Verwijder(inzien, id, abonnement));
        Assert.Single(await abonnementen.Lijst(inzien, id));

        var ander = await app.Administratie("Andere Zaak");
        Assert.Null(await abonnementen.Open(wie, ander, abonnement));
        Assert.Empty((await abonnementen.Maak(wie, ander, new(2026, 12, 31), abonnement)).Waarde!.Facturen);
    }
}
