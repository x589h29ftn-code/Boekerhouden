using Boekerhouden.Administratie.Components.Gedeeld;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De handigheden rond overzichten en boekingen met uiteenlopende boekingen: herboeken (memoriaal met meer regels op
/// één rekening, een inkoopfactuur met btw, een teruggedraaide factuur, een periodeverdeling), herhalen over de
/// jaargrens, aansluitingen die niet sluiten en sorteren.
/// </summary>
public sealed class ScenarioTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly DateOnly Begin = new(2026, 1, 1);
    private static readonly DateOnly Eind = new(2027, 12, 31);

    [Fact]
    public async Task Herboeken_met_diverse_boekingen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Herboek Divers B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var inkoop = app.Dienst<InkoopDienst>();

        // Een memoriaalboeking met twee regels op 4300: alleen de tweede herboeken.
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 2, 1), ("4300", 100m, null), ("4300", 50m, null), ("1100", -150m, null)))).IsGelukt);
        var tweede = (await Kaart(grootboek, wie, id, "4300")).Mutaties.Single(m => m.Bedrag == 50m);
        Assert.Equal(1, (await grootboek.Herboek(wie, id, [new Herboekregel(tweede.Dagboek, tweede.Boekstuk, tweede.Regel)], "4320")).Waarde);
        Assert.Equal((100m, 50m), ((await Kaart(grootboek, wie, id, "4300")).Eindsaldo, (await Kaart(grootboek, wie, id, "4320")).Eindsaldo));

        // Een inkoopfactuur met btw: de kosten naar een andere rekening; btw, crediteuren en de factuur blijven kloppen.
        var factuur = await BoekFactuur(inkoop, wie, id, "BN-2026-0201", "2026-02-01");
        var kosten = Assert.Single((await Kaart(grootboek, wie, id, "4310")).Mutaties);
        Assert.Equal(1, (await grootboek.Herboek(wie, id, [new Herboekregel(kosten.Dagboek, kosten.Boekstuk, kosten.Regel)], "4300")).Waarde);
        var verplaatst = (await Kaart(grootboek, wie, id, "4300")).Mutaties.Single(m => (m.Dagboek, m.Boekstuk) == (kosten.Dagboek, kosten.Boekstuk));
        Assert.Equal((20.65m, "I21", 4.34m), (verplaatst.Bedrag, verplaatst.BtwCode, verplaatst.Btw));
        Assert.Equal("4300", Assert.Single((await inkoop.Geboekt(wie, id, factuur))!.Regels).Rekeningnummer);
        var boekregel = Assert.Single(await inkoop.Inkoopboek(wie, id, Begin, Eind));
        Assert.Equal((20.65m, 4.34m, 24.99m), (boekregel.Exclusief, boekregel.Btw, boekregel.Totaal));
        Assert.Equal(2, (await grootboek.Boeking(wie, id, kosten.Dagboek, kosten.Boekstuk))!.Versie);
        Assert.All((await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id)).Where(a => a.Soort == Aansluitingsoort.Crediteuren), a => Assert.True(a.Sluit));

        // Naar dezelfde rekening: niets te doen; naar een onbekende of vaste rekening: een fout.
        var opnieuw = (await Kaart(grootboek, wie, id, "4300")).Mutaties.Select(m => new Herboekregel(m.Dagboek, m.Boekstuk, m.Regel)).ToList();
        Assert.Equal(0, (await grootboek.Herboek(wie, id, opnieuw, "4300")).Waarde);
        Assert.Contains("Kies de rekening", (await grootboek.Herboek(wie, id, opnieuw, "9999")).Fouten[0], StringComparison.Ordinal);
        Assert.Contains("hoort bij de bank", (await grootboek.Herboek(wie, id, opnieuw, "1500")).Fouten[0], StringComparison.Ordinal);
        Assert.Contains("intussen gewijzigd", (await grootboek.Herboek(wie, id, [new Herboekregel(kosten.Dagboek, kosten.Boekstuk, 9)], "4320")).Fouten[0], StringComparison.Ordinal);

        // Een teruggedraaide factuur en de tegenboeking moeten elkaar blijven opheffen: niet herboeken.
        var fout = await BoekFactuur(inkoop, wie, id, "BN-2026-0202", "2026-02-02", rekening: "4330");
        Assert.True((await inkoop.Terugdraaien(wie, id, fout)).IsGelukt);
        var teruggedraaid = (await Kaart(grootboek, wie, id, "4330")).Mutaties;
        Assert.Equal(2, teruggedraaid.Count);
        foreach (var m in teruggedraaid)
        {
            Assert.Contains("teruggedraaid", (await grootboek.Herboek(wie, id, [new Herboekregel(m.Dagboek, m.Boekstuk, m.Regel)], "4300")).Fouten[0], StringComparison.Ordinal);
        }

        // Een periodeverdeling gaat via de factuur.
        var verdeeld = await BoekFactuur(inkoop, wie, id, "BN-2026-0203", "2026-03-01", verdeling: new Periodeverdeling(new(2026, 3, 1), new(2026, 4, 30), "1410"));
        var maand = (await Kaart(grootboek, wie, id, "4310")).Mutaties[0];
        Assert.Contains("periodeverdeling", (await grootboek.Herboek(wie, id, [new Herboekregel(maand.Dagboek, maand.Boekstuk, maand.Regel)], "4300")).Fouten[0], StringComparison.Ordinal);
        Assert.NotNull(await inkoop.Geboekt(wie, id, verdeeld));

        // Een mislukte reeks laat alles staan; de keten klopt.
        Assert.Equal(50m, (await Kaart(grootboek, wie, id, "4320")).Eindsaldo);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Assert.Equal(2, (await app.Dienst<AuditDienst>().Recent()).Count(r => r.Actie == AuditActies.Herboekt && r.AdministratieId == id));
    }

    [Fact]
    public async Task Herhalen_over_de_jaargrens_met_btw()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Herhaal Jaargrens B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();

        var uitkomst = await grootboek.BoekPerMaand(wie, id, Post("2026-0001", new(2026, 11, 30), ("4300", 100m, "I21"), ("1500", 21m, "I21"), ("1100", -121m, null)), 4);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var posten = (await grootboek.Posten(wie, id, "MEM")).Select(p => p.Post).OrderBy(p => p.Datum).ToList();
        Assert.Equal([new(2026, 11, 30), new(2026, 12, 30), new(2027, 1, 30), new(2027, 2, 28)], posten.Select(p => p.Datum));
        Assert.Equal(["2026-0001", "2026-0002", "2027-0001", "2027-0002"], posten.Select(p => p.Boekstuk));
        Assert.All(posten, p => Assert.Equal(("I21", 121m), (p.Regels[0].BtwCode, p.TotaalDebet)));
        Assert.Equal(200m, Saldo(await grootboek.Proefbalans(wie, id, 2027), "4300"));

        // Een reeks met een fout boekt niets; één maand is een gewone boeking.
        var fout = await grootboek.BoekPerMaand(wie, id, Post("2026-0010", new(2026, 5, 1), ("9999", 1m, null), ("1100", -1m, null)), 3);
        Assert.False(fout.IsGelukt);
        Assert.Equal(4, (await grootboek.Posten(wie, id, "MEM")).Count);
        Assert.Equal(1, (await grootboek.BoekPerMaand(wie, id, Post("2026-0010", new(2026, 5, 1), ("4300", 1m, null), ("1100", -1m, null)), 1)).Waarde);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Aansluitingen_die_niet_sluiten()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Aansluiting Verschil B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var aansluiting = app.Dienst<AansluitingDienst>();
        await BoekFactuur(app.Dienst<InkoopDienst>(), wie, id, "BN-2026-0301", "2026-03-01");
        var voor = await aansluiting.Aansluitingen(wie, id);
        Assert.All(voor.Where(a => a.Soort is Aansluitingsoort.Crediteuren or Aansluitingsoort.Debiteuren or Aansluitingsoort.Vraagposten), a => Assert.True(a.Sluit));
        Assert.Equal(4.34m, voor.Single(a => a.Onderwerp == "Voorbelasting").Grootboek);

        // Op crediteuren zonder leverancier, debiteuren zonder factuur en de vraagposten: alle drie sluiten niet meer.
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 3, 5), ("4300", 10m, null), ("1600", -10m, null)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 3, 6), ("1300", 50m, null), ("8000", -50m, null)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0003", new(2026, 3, 7), ("2000", 7.5m, null), ("1100", -7.5m, null)))).IsGelukt);
        var na = await aansluiting.Aansluitingen(wie, id);

        Assert.Equal((-10m, false), (na.Single(a => a.Soort == Aansluitingsoort.Crediteuren).Verschil, na.Single(a => a.Soort == Aansluitingsoort.Crediteuren).Sluit));
        Assert.Equal((50m, false), (na.Single(a => a.Soort == Aansluitingsoort.Debiteuren).Verschil, na.Single(a => a.Soort == Aansluitingsoort.Debiteuren).Sluit));
        Assert.Equal((7.5m, false), (na.Single(a => a.Soort == Aansluitingsoort.Vraagposten).Verschil, na.Single(a => a.Soort == Aansluitingsoort.Vraagposten).Sluit));

        // Rechtzetten: de vraagpost naar de kosten herboeken, dan sluiten de vraagposten weer.
        var vraag = Assert.Single((await Kaart(grootboek, wie, id, "2000")).Mutaties);
        Assert.True((await grootboek.Herboek(wie, id, [new Herboekregel(vraag.Dagboek, vraag.Boekstuk, vraag.Regel)], "4300")).IsGelukt);
        Assert.True((await aansluiting.Aansluitingen(wie, id)).Single(a => a.Soort == Aansluitingsoort.Vraagposten).Sluit);
    }

    [Fact]
    public async Task Betaalverschil_van_het_kantoor_uitzetten_per_administratie_en_ongeldige_rekeningen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoorverschil B.V.");
        var uit = await app.Administratie("Zonder Verschil B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var bank = app.Dienst<BankDienst>();
        var huidig = await inrichting.Instellingen(wie, id);
        async Task<string> Fout(string rekening) =>
            (await inrichting.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Betaalverschil = 1m, Verschilrekening = rekening } })).Fouten[0];
        Assert.Contains("bestaat niet", await Fout("9999"), StringComparison.Ordinal);
        Assert.Contains("hoort bij de bank", await Fout("1100"), StringComparison.Ordinal);
        Assert.Contains("hoort bij de bank", await Fout("1500"), StringComparison.Ordinal);
        Assert.Contains("hoogstens", (await inrichting.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Betaalverschil = 250m, Verschilrekening = "9000" } })).Fouten[0], StringComparison.Ordinal);

        // Het kantoor zet het aan; de tweede administratie zet het uit (0).
        var kantoor = app.Dienst<KantoorDienst>();
        Assert.True((await kantoor.Wijzig(wie, Boekhouding.Regels.Verwerkingsinstellingen.Standaard with { Betaalverschil = 0.05m, Verschilrekening = "9000" })).IsGelukt);
        try
        {
            var anders = await inrichting.Instellingen(wie, uit);
            Assert.True((await inrichting.WijzigInstellingen(wie, uit, anders with { Afwijkend = anders.Afwijkend with { Betaalverschil = 0m } })).IsGelukt);
            foreach (var adm in new[] { id, uit })
            {
                await BoekFactuur(app.Dienst<InkoopDienst>(), wie, adm, "BN-2026-0311", "2026-03-01");
                await bank.Lees(wie, adm, BankTests.Afschrift(belnet: "25,00"), "ing.mt940");
            }

            Assert.Empty(await bank.OpenPosten(wie, id));
            Assert.Equal(0.01m, (await app.Dienst<GrootboekDienst>().Kaart(wie, id, "9000", Begin, Eind)).Eindsaldo);
            Assert.Equal(-24.99m, Assert.Single(await bank.OpenPosten(wie, uit)).Openstaand);
            Assert.Contains(await bank.Lijst(wie, uit), m => m.Bedrag == -25m);
        }
        finally
        {
            await kantoor.Wijzig(wie, Boekhouding.Regels.Verwerkingsinstellingen.Standaard);
        }
    }

    [Fact]
    public void Sorteren_keert_om_houdt_volgorde_bij_gelijke_waarde_en_zet_leeg_vooraan()
    {
        var regels = new[] { ("b", 2m, (string?)"x"), ("a", 2m, null), ("c", 1m, "y") };
        var sortering = new Sortering<(string Naam, decimal Bedrag, string? Code)>(
            new("Naam", r => r.Naam), new("Bedrag", r => r.Bedrag, EerstOplopend: false), new("Code", r => r.Code));

        Assert.Equal(["b", "a", "c"], sortering.Pas(regels).Select(r => r.Naam));
        sortering.Wissel("Bedrag");
        Assert.Equal(["b", "a", "c"], sortering.Pas(regels).Select(r => r.Naam));
        sortering.Wissel("Bedrag");
        Assert.Equal(["c", "b", "a"], sortering.Pas(regels).Select(r => r.Naam));
        sortering.Wissel("Naam");
        Assert.Equal((true, "Naam"), (sortering.Oplopend, sortering.Kolom));
        Assert.Equal(["a", "b", "c"], sortering.Pas(regels).Select(r => r.Naam));
        sortering.Wissel("Code");
        Assert.Equal(["a", "b", "c"], sortering.Pas(regels).Select(r => r.Naam));
        sortering.Wissel("Bestaat niet");
        Assert.Equal("Code", sortering.Kolom);
    }

    private static async Task<Grootboekkaart> Kaart(GrootboekDienst grootboek, Uitvoerder wie, Guid id, string rekening) =>
        await grootboek.Kaart(wie, id, rekening, Begin, Eind);

    /// <summary>Een factuur van Belnet (€ 20,65 plus € 4,34 btw), geboekt op <paramref name="rekening"/>.</summary>
    private static async Task<Guid> BoekFactuur(InkoopDienst inkoop, Uitvoerder wie, Guid id, string nummer, string datum, string rekening = "4310", Periodeverdeling? verdeling = null)
    {
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(nummer, datum)), nummer + ".xml", null);
        var document = gelezen.DocumentId!.Value;
        var verwerking = (await inkoop.Verwerking(wie, id, document))!;
        var bekend = verwerking.Relaties.FirstOrDefault(r => r.Naam == "Belnet B.V.");
        var dag = DateOnly.Parse(datum, System.Globalization.CultureInfo.InvariantCulture);
        var invoer = new Factuurinvoer(bekend?.Code, bekend is null ? new NieuweRelatie("Belnet B.V.", verwerking.Herkend!.Iban, null, null) : null, nummer, dag, dag.AddDays(14), "Abonnement",
            [new Factuurregel("Abonnement", rekening, "I21", 20.65m, 4.34m)], Verdeling: verdeling);
        var uitkomst = await inkoop.Boek(wie, id, document, invoer);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return document;
    }

    private static Journaalpost Post(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag, string? Btw)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal " + boekstuk, [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag, BtwCode: r.Btw))]);

    private static decimal Saldo(Proefbalans proef, string rekening) =>
        proef.Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;
}
