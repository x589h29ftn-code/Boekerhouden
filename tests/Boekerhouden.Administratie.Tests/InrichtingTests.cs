using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Regels;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Relaties, regels en instellingen beheren (plakje A7b).</summary>
public sealed class InrichtingTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly Verwerkingsregel Telefoon = new("Belnet is telefoon", "4310", "I21", Automatisch: true,
        Soort: Verwerkingssoort.Inkoopfactuur, NaamBevat: "Belnet", BedragVan: 10, BedragTot: 30);

    [Fact]
    public async Task Weergave_van_de_overzichten_wijzigt_alleen_de_beheerder()
    {
        var wie = await app.Beheerder();
        var kantoor = app.Dienst<KantoorDienst>();
        Assert.Equal(Overzichtsweergave.Standaard, await kantoor.Weergave());

        Assert.True((await kantoor.WijzigWeergave(wie, new Overzichtsweergave(90, true))).IsGelukt);

        Assert.Equal(new Overzichtsweergave(90, true), await kantoor.Weergave());
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Onderwerp == "Weergave overzichten" && r.Details == "letter 100%, hele euro's uit → letter 90%, hele euro's aan");
        Assert.False((await kantoor.WijzigWeergave(wie, new Overzichtsweergave(73, false))).IsGelukt);
        var medewerker = await app.Gebruiker("Mila Medewerker");
        await Assert.ThrowsAsync<GeenToegangException>(() => kantoor.WijzigWeergave(medewerker, Overzichtsweergave.Standaard));
        Assert.True((await kantoor.WijzigWeergave(wie, Overzichtsweergave.Standaard)).IsGelukt);
    }

    [Fact]
    public async Task Relatie_toevoegen_en_wijzigen_met_controles()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<InrichtingDienst>();

        var fout = await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL001", null, "9999"), "NL00GEEN");
        var goed = await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", "12345678", "4310"), "nl63 trio 0212 3456 78");
        var gewijzigd = await dienst.WijzigRelatie(wie, id, goed.Waarde, new("Belnet Zakelijk B.V.", null, null, "4330"));

        Assert.Equal(["Het btw-nummer is niet geldig.", "Rekening 9999 bestaat niet.", "Het IBAN is niet geldig."], fout.Fouten);
        Assert.True(gewijzigd.IsGelukt);
        var relatie = Assert.Single(await dienst.Relaties(wie, id));
        Assert.Equal(("BELNET", "Belnet Zakelijk B.V.", "NL63TRIO0212345678", "4330"), (relatie.Code, relatie.Naam, relatie.Ibans.Single(), relatie.Tegenrekening));
    }

    [Fact]
    public async Task Iban_wijzigen_vraagt_het_wachtwoord_en_logt_oud_en_nieuw()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<InrichtingDienst>();
        var relatie = (await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", null, null, null), "NL63TRIO0212345678")).Waarde;

        var zonder = await dienst.ZetIbans(wie, id, relatie, ["NL44RABO0123456789"], "fout");
        var ongeldig = await dienst.ZetIbans(wie, id, relatie, ["NL44RABO0123456780"], AdministratieFixture.Wachtwoord);
        var met = await dienst.ZetIbans(wie, id, relatie, ["NL44RABO0123456789"], AdministratieFixture.Wachtwoord);

        Assert.Contains("wachtwoord klopt niet", zonder.Fouten[0], StringComparison.Ordinal);
        Assert.Contains("geen geldig IBAN", ongeldig.Fouten[0], StringComparison.Ordinal);
        Assert.True(met.IsGelukt);
        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.IbanWijzigingGeweigerd);
        Assert.Contains(log, r => r.Actie == AuditActies.IbanGewijzigd && r.Details == "van [NL63TRIO0212345678] naar [NL44RABO0123456789]");
    }

    [Fact]
    public async Task Ander_iban_dan_bekend_houdt_de_factuur_tegen_als_mogelijke_fraude()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await app.Dienst<InrichtingDienst>().VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", null, "4310"), "NL44RABO0123456789");
        await app.Dienst<InrichtingDienst>().VoegRegelToe(wie, id, Telefoon);

        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);

        Assert.Equal(Documentstatus.TeVerwerken, gelezen.Status);
        Assert.Contains("is niet het bekende IBAN van Belnet B.V.", gelezen.Melding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regels_toevoegen_volgorde_en_verwijderen_alleen_door_het_kantoor()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var dienst = app.Dienst<InrichtingDienst>();

        var fout = await dienst.VoegRegelToe(wie, id, new Verwerkingsregel("", "9999", "X", BedragVan: 30, BedragTot: 10));
        Assert.Equal(["Geef de regel een naam.", "Rekening 9999 bestaat niet.", "Btw-code X bestaat niet."], fout.Fouten);
        Assert.Equal("Een regel heeft minstens één voorwaarde nodig.", (await dienst.VoegRegelToe(wie, id, new Verwerkingsregel("Leeg", "4310"))).Fouten[0]);
        Assert.True((await dienst.VoegRegelToe(wie, id, Telefoon)).IsGelukt);
        Assert.True((await dienst.VoegRegelToe(wie, id, new Verwerkingsregel("Bankkosten", "9000", Soort: Verwerkingssoort.Bankmutatie, OmschrijvingBevat: "kosten"))).IsGelukt);

        // Bedragen zonder teken: "0 t/m -30.000" (een afboeking naar de spaarrekening) wordt 0 t/m 30.000.
        Assert.True((await dienst.VoegRegelToe(wie, id, new Verwerkingsregel("Spaarrekening", "1110", Soort: Verwerkingssoort.Bankmutatie, OmschrijvingBevat: "sparen",
            BedragVan: 0m, BedragTot: -30_000m, Richting: Regelrichting.Betaling))).IsGelukt);
        var spaar = (await dienst.Regels(wie, id)).Single(r => r.Regel.Naam == "Spaarrekening");
        Assert.Equal((0m, 30_000m), (spaar.Regel.BedragVan, spaar.Regel.BedragTot));
        Assert.True(spaar.Regel.Past(new Verwerkingsgegevens(Verwerkingssoort.Bankmutatie, null, null, -30_000m, "Naar sparen")));
        await dienst.VerwijderRegel(wie, id, spaar.Id);

        var regels = await dienst.Regels(wie, id);
        await dienst.VerplaatsRegel(wie, id, regels[1].Id, omhoog: true);
        Assert.Equal(["Bankkosten", "Belnet is telefoon"], (await dienst.Regels(wie, id)).Select(r => r.Regel.Naam));
        await dienst.VerwijderRegel(wie, id, regels[1].Id);
        Assert.Equal(["Belnet is telefoon"], (await dienst.Regels(wie, id)).Select(r => r.Regel.Naam));

        Assert.NotEmpty(await dienst.Regels(ondernemer, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VoegRegelToe(ondernemer, id, Telefoon));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VerwijderRegel(ondernemer, id, regels[0].Id));
    }

    [Fact]
    public async Task Regel_wijzigen_met_controles_log_en_alleen_door_het_kantoor()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var dienst = app.Dienst<InrichtingDienst>();
        Assert.True((await dienst.VoegRegelToe(wie, id, Telefoon)).IsGelukt);
        var regel = Assert.Single(await dienst.Regels(wie, id));

        var fout = await dienst.WijzigRegel(wie, id, regel.Id, Telefoon with { Rekeningnummer = "9999" });
        var gewijzigd = await dienst.WijzigRegel(wie, id, regel.Id, Telefoon with { BedragTot = 40m, BtwCode = null, Automatisch = false });
        var weg = await dienst.WijzigRegel(wie, id, Guid.NewGuid(), Telefoon);

        Assert.Equal(["Rekening 9999 bestaat niet."], fout.Fouten);
        Assert.True(gewijzigd.IsGelukt, string.Join("; ", gewijzigd.Fouten));
        Assert.False(weg.IsGelukt);
        var nu = Assert.Single(await dienst.Regels(wie, id));
        Assert.Equal((regel.Id, 40m, null, false), (nu.Id, nu.Regel.BedragTot, nu.Regel.BtwCode, nu.Regel.Automatisch));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.RegelGewijzigd && r.Onderwerp == "Belnet is telefoon");

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.WijzigRegel(ondernemer, id, regel.Id, Telefoon));
        var buiten = await app.Gebruiker("Buiten Staander");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.WijzigRegel(buiten, id, regel.Id, Telefoon));
    }

    [Fact]
    public async Task Niet_btw_plichtig_boekt_inkoop_inclusief_btw_en_bank_zonder_btw()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<InrichtingDienst>();
        await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678");
        await dienst.VoegRegelToe(wie, id, Telefoon);
        await dienst.VoegRegelToe(wie, id, new Verwerkingsregel("Jansen", "4300", "I21", Soort: Verwerkingssoort.Bankmutatie, TegenIban: "NL36INGB0007654321"));
        Assert.True((await dienst.Instellingen(wie, id)).BtwPlichtig);

        var uitkomst = await dienst.WijzigInstellingen(wie, id, (await dienst.Instellingen(wie, id)) with { BtwPlichtig = false });

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.False((await dienst.Instellingen(wie, id)).BtwPlichtig);
        Assert.False((await app.Dienst<AdministratieDienst>().Open(wie, id))!.BtwPlichtig);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.InstellingenGewijzigd && r.Details!.Contains("btw-plichtig ja → nee", StringComparison.Ordinal));

        // De factuur van € 24,99 (20,65 + 4,34 btw) komt in zijn geheel op de kosten, zonder voorbelasting.
        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        Assert.Equal(Documentstatus.Geboekt, gelezen.Status);
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal(24.99m, proef.Regels.Single(r => r.Rekening.Nummer == "4310").Saldo);
        Assert.DoesNotContain(proef.Regels, r => r.Rekening.Functie == Boekhouding.Grootboek.Rekeningfunctie.Voorbelasting && r.Saldo != 0);

        // Op de bank: de regel heeft I21, maar het voorstel is zonder btw.
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940");
        var jansen = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -199.65m);
        var advies = (await bank.Verwerking(wie, id, jansen.Id))!.Voorstel.Advies;
        Assert.Equal(("4300", null), (advies.Rekeningnummer, advies.BtwCode));

        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var instellingen = await dienst.Instellingen(wie, id);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.WijzigInstellingen(ondernemer, id, instellingen with { BtwPlichtig = true }));
    }

    [Fact]
    public async Task Limiet_houdt_automatisch_boeken_tegen_en_eigen_iban_wordt_overgeslagen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<InrichtingDienst>();
        await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678");
        await dienst.VoegRegelToe(wie, id, Telefoon);
        var huidig = await dienst.Instellingen(wie, id);

        var ongeldig = await dienst.WijzigInstellingen(wie, id, huidig with { Ibans = ["NL00FOUT"], Afwijkend = huidig.Afwijkend with { Betalingstermijn = 400, LimietAfwijkend = true, Limiet = -1 } });
        var gelukt = await dienst.WijzigInstellingen(wie, id, huidig with { Ibans = ["NL20INGB0001234567"], Afwijkend = huidig.Afwijkend with { LimietAfwijkend = true, Limiet = 20m } });
        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);

        Assert.Equal(3, ongeldig.Fouten.Count);
        Assert.True(gelukt.IsGelukt);
        Assert.Equal(["NL20INGB0001234567"], (await dienst.Instellingen(wie, id)).Ibans);
        Assert.Equal(Documentstatus.TeVerwerken, gelezen.Status);
        Assert.Contains("hoger dan de limiet voor automatisch verwerken (€ 20,00)", gelezen.Melding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drempel_instellen_en_regel_met_omschrijving_vol_variabelen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<InrichtingDienst>();
        var huidig = await dienst.Instellingen(wie, id);
        Assert.Equal(2, huidig.Geldend.Drempel);
        Assert.Null(huidig.Afwijkend.Drempel);

        Assert.Equal(["Automatisch verwerken na 1 tot 10 gelijke keuzes."], (await dienst.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Drempel = 0 } })).Fouten);
        Assert.True((await dienst.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Drempel = 3 } })).IsGelukt);
        Assert.Equal(3, (await dienst.Instellingen(wie, id)).Geldend.Drempel);

        Assert.Equal(["Onbekende variabele in de omschrijving: #maandd."], (await dienst.VoegRegelToe(wie, id, Telefoon with { Omschrijving = "Telefoon #maandd" })).Fouten);
        await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678");
        await dienst.VoegRegelToe(wie, id, Telefoon with { Omschrijving = "Telefoon #maand #jaar (#factuurnummer)" });
        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);

        Assert.True(gelezen.IsGeboekt, gelezen.Melding);
        Assert.Equal("Telefoon april 2026 (BN-2026-0411)", (await app.Dienst<GrootboekDienst>().Posten(wie, id, "INK")).Single().Post.Omschrijving);
    }

    [Fact]
    public async Task Standaardomschrijving_van_het_kantoor_voor_factuur_en_grootboekregels()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var kantoor = app.Dienst<KantoorDienst>();
        var medewerker = await app.Gebruiker("Mia Medewerker");
        var standaard = Verwerkingsinstellingen.Standaard with { Omschrijving = "#leverancier #factuurnummer #maand" };

        await Assert.ThrowsAsync<GeenToegangException>(() => kantoor.Wijzig(medewerker, standaard));
        Assert.Equal(["De standaardomschrijving kent #maandd niet."], (await kantoor.Wijzig(wie, standaard with { Omschrijving = "#leverancier #maandd" })).Fouten);
        try
        {
            Assert.True((await kantoor.Wijzig(wie, standaard)).IsGelukt);
            Assert.Equal("#leverancier #factuurnummer #maand", (await kantoor.Instellingen()).Omschrijving);
            var dienst = app.Dienst<InrichtingDienst>();
            var inkoop = app.Dienst<InkoopDienst>();
            await dienst.VoegRelatieToe(wie, id, new("Belnet B.V.", "NL123456782B01", null, "4310"), "NL63TRIO0212345678");

            // Zonder regel: het scherm krijgt de standaard als voorstel; zelf boeken met een lege omschrijving geeft ook de standaard.
            var mei = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet("BN-2026-0511", "2026-05-01")), "belnet-mei.xml", null);
            Assert.False(mei.IsGeboekt, mei.Melding);
            Assert.Equal("#leverancier #factuurnummer #maand", (await inkoop.Verwerking(wie, id, mei.DocumentId!.Value))!.Standaardomschrijving);
            var relatie = (await dienst.Relaties(wie, id)).Single(r => r.Naam == "Belnet B.V.").Code;
            var geboekt = await inkoop.Boek(wie, id, mei.DocumentId.Value, new Factuurinvoer(relatie, null, "BN-2026-0511", new(2026, 5, 1), new(2026, 5, 15), "",
                [new Boekhouding.Facturen.Factuurregel("", "4310", "I21", 20.65m, 4.34m)]));
            Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));

            // Automatisch geboekt met een regel zonder eigen omschrijving: ook de standaard, op de factuur en de kostenregel.
            await dienst.VoegRegelToe(wie, id, Telefoon);
            var april = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
            Assert.True(april.IsGeboekt, april.Melding);

            var posten = (await app.Dienst<GrootboekDienst>().Posten(wie, id, "INK")).Select(p => p.Post).ToList();
            Assert.Equal(["Belnet B.V. BN-2026-0411 april", "Belnet B.V. BN-2026-0511 mei"], posten.Select(p => p.Omschrijving).Order());
            // Een kostenregel zonder eigen omschrijving toont die van de boeking; niet die van de factuurregel.
            Assert.All(posten, p => Assert.Equal(p.Omschrijving, p.Regels.Single(r => r.Rekeningnummer == "4310").Omschrijving ?? p.Omschrijving));
        }
        finally
        {
            await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard);
        }
    }

    [Fact]
    public async Task Kantoorinstellingen_gelden_tenzij_de_administratie_afwijkt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var kantoor = app.Dienst<KantoorDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var medewerker = await app.Gebruiker("Mia Medewerker");

        await Assert.ThrowsAsync<GeenToegangException>(() => kantoor.Wijzig(medewerker, Verwerkingsinstellingen.Standaard with { Drempel = 5 }));
        Assert.Equal(["De bandbreedte ligt tussen 1 en 10 keer het hoogste bedrag."], (await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard with { Bandbreedte = 0.5m })).Fouten);
        Assert.True((await kantoor.Wijzig(wie, new Verwerkingsinstellingen(3, 1.5m, 250m, 14, false, "/data/#administratie"))).IsGelukt);

        var volgt = await inrichting.Instellingen(wie, id);
        Assert.Equal(new Verwerkingsinstellingen(3, 1.5m, 250m, 14, false, "/data/#administratie"), volgt.Geldend);

        Assert.True((await inrichting.WijzigInstellingen(wie, id, volgt with { Afwijkend = new(Drempel: 1, LimietAfwijkend: true, Limiet: null, Adrescontrole: true) })).IsGelukt);
        var eigen = (await inrichting.Instellingen(wie, id)).Geldend;
        Assert.Equal(new Verwerkingsinstellingen(1, 1.5m, null, 14, true, "/data/#administratie"), eigen);

        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.KantoorinstellingenGewijzigd && r.Details!.Contains("adrescontrole aan → uit", StringComparison.Ordinal));
        Assert.Contains(log, r => r.Actie == AuditActies.InstellingenGewijzigd && r.Details!.Contains("drempel 3 → 1", StringComparison.Ordinal));

        // Weer terug naar de standaard, zodat andere tests in deze klasse er geen last van hebben.
        await kantoor.Wijzig(wie, Verwerkingsinstellingen.Standaard);
    }

    [Fact]
    public async Task Buitenstaander_ziet_en_wijzigt_niets()
    {
        await app.Beheerder();
        var id = await app.Administratie();
        var buiten = await app.Gebruiker("Bo Buiten");
        var dienst = app.Dienst<InrichtingDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Relaties(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VoegRelatieToe(buiten, id, new("X", null, null, null), null));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.ZetIbans(buiten, id, Guid.NewGuid(), [], AdministratieFixture.Wachtwoord));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Regels(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Instellingen(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.WijzigInstellingen(buiten, id, new("X", null, null, [], AfwijkendeInstellingen.Geen)));
    }
}
