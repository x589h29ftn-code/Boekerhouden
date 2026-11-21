using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Wijzigen nadat iets is vastgelegd of afgesloten: een factuur terugdraaien of aanpassen na de btw-aangifte, een
/// boeking aanpassen en herboeken, crediteren, de periode afsluiten en dan alles proberen wat niet meer mag, heropenen
/// en opnieuw afsluiten, een desinvestering terugdraaien. Na elke stap moeten de vaste controles kloppen
/// (<see cref="Invarianten"/>); wat in een afgesloten periode geweigerd wordt, laat de cijfers ongemoeid.
/// </summary>
public sealed class WijzigenNaVastleggenTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";

    [Fact]
    public async Task Wijzigen_na_de_aangifte_en_na_afsluiten()
    {
        var fouten = new List<string>();
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var grootboek = app.Dienst<GrootboekDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var btw = app.Dienst<BtwaangifteDienst>();
        var perioden = app.Dienst<PeriodeDienst>();
        var activa = app.Dienst<ActivaDienst>();
        async Task Stap(string wat) => fouten.AddRange(await Invarianten.Controleer(app, wie, id, wat));
        void Moet(bool gelukt, string wat, Uitkomst uitkomst)
        {
            if (uitkomst.IsGelukt != gelukt)
            {
                fouten.Add($"{wat}: {(uitkomst.IsGelukt ? "lukt, maar hoort geweigerd te worden" : "geweigerd: " + string.Join(" ", uitkomst.Fouten))}");
            }
        }

        // ---- februari en maart: een verkoop, twee inkopen, een memoriaalboeking; de bank betaalt in februari
        var klant = await Klant(wie, id);
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2025, 2, 10), new(2025, 2, 24), "Fietsen", [new Verkoopregel("Fiets", 1, 1_000m, "8000", "V21")]));
        var v1 = concept.Waarde;
        var nummerV1 = (await verkoop.MaakDefinitief(wie, id, v1)).Waarde!;
        var (i1, totaalI1) = await Inkoop(wie, id, fouten, "RG-2025-0001", new(2025, 2, 12), 500m);
        var (i2, _) = await Inkoop(wie, id, fouten, "RG-2025-0002", new(2025, 3, 12), 400m);
        Moet(true, "Memoriaal", await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0002", new(2025, 3, 20), "Herrubricering", [new("4300", 50m), new("4900", -50m)])));
        var afschrift = new Bankafschrift(Iban, "1", "EUR", new(2025, 2, 1), 10_000m, new(2025, 2, 28), 10_000m + 1_210m - totaalI1,
            [new Bankmutatie(new(2025, 2, 20), 1_210m, $"Factuur {nummerV1}", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
             new Bankmutatie(new(2025, 2, 22), -totaalI1, "Factuur RG-2025-0001", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam)]);
        Moet(true, "Bank inlezen", await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, "FEB"))), "februari.mt940"));
        foreach (var m in await bank.Lijst(wie, id))
        {
            var post = (await bank.OpenPosten(wie, id)).First(p => m.Omschrijving.EndsWith(p.Factuurnummer, StringComparison.Ordinal));
            Moet(true, $"Afletteren {m.Omschrijving}", await bank.Afletteren(wie, id, m.Id, [post.Id]));
        }

        await Stap("na het eerste kwartaal");

        // ---- de aangifte van het eerste kwartaal vastleggen
        Moet(true, "Btw Q1 vastleggen", await btw.Vastleggen(wie, id, new(2025, 1, 1), new(2025, 3, 31)));
        var vastgelegd = (await btw.Jaar(wie, id, 2025)).Jaar.Perioden[0].Vastgelegd!;

        // Een betaalde factuur terugdraaien mag niet; een open factuur wel, en daarna anders boeken: een suppletie.
        Moet(false, "Betaalde inkoopfactuur terugdraaien", await inkoop.Terugdraaien(wie, id, i1));
        Moet(true, "Open inkoopfactuur terugdraaien", await inkoop.Terugdraaien(wie, id, i2));
        await Stap("na terugdraaien");
        Moet(true, "Opnieuw boeken met een ander bedrag", await BoekInkoop(wie, id, i2, "RG-2025-0002", new(2025, 3, 12), 700m));
        var q1 = (await btw.Jaar(wie, id, 2025)).Jaar.Perioden[0];
        if (q1.Verschil != q1.Berekend.Afgerond().TeBetalen - vastgelegd.AfgerondTeBetalen || q1.Verschil is not < 0)
        {
            fouten.Add($"Suppletie na aanpassen: verschil {q1.Verschil}, berekend {q1.Berekend.Afgerond().TeBetalen}, vastgelegd {vastgelegd.AfgerondTeBetalen}.");
        }

        // De memoriaalboeking aanpassen en een regel herboeken.
        Moet(true, "Memoriaal aanpassen", await grootboek.PasAan(wie, id, "MEM", "2025-0002", new Journaalpost("MEM", "2025-0002", new(2025, 3, 20), "Herrubricering", [new("4300", 75m), new("4900", -75m)])));
        Moet(true, "Herboeken", await grootboek.Herboek(wie, id, [new Herboekregel("MEM", "2025-0002", 0)], "4310"));

        // De verkoop crediteren (de creditnota komt in een latere periode).
        var credit = await verkoop.Crediteer(wie, id, v1);
        Moet(true, "Crediteren", credit);
        Moet(true, "Creditnota definitief", await verkoop.MaakDefinitief(wie, id, credit.Waarde));
        await Stap("na aanpassen, herboeken en crediteren");

        // ---- het eerste kwartaal afsluiten; daarna mag er niets meer in
        var openstaand = await perioden.Openstaand(wie, id, new(2025, 3, 31));
        var afgesloten = await perioden.SluitAf(wie, id, new(2025, 3, 31));
        Moet(true, $"Afsluiten ({string.Join("; ", openstaand)})", afgesloten);
        var voor = await Saldi(wie, id);

        Moet(false, "Na afsluiten: inkoop terugdraaien", await inkoop.Terugdraaien(wie, id, i2));
        Moet(false, "Na afsluiten: inkoop opnieuw boeken", await BoekInkoop(wie, id, i2, "RG-2025-0002", new(2025, 3, 12), 800m));
        Moet(false, "Na afsluiten: memoriaal aanpassen", await grootboek.PasAan(wie, id, "MEM", "2025-0002", new Journaalpost("MEM", "2025-0002", new(2025, 3, 20), "Herrubricering", [new("4300", 99m), new("4900", -99m)])));
        Moet(false, "Na afsluiten: herboeken", await grootboek.Herboek(wie, id, [new Herboekregel("MEM", "2025-0002", 0)], "4300"));
        Moet(false, "Na afsluiten: boeken in maart", await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0003", new(2025, 3, 25), "Te laat", [new("4300", 10m), new("4900", -10m)])));
        var laat = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2025, 3, 30), new(2025, 4, 13), "Te laat", [new Verkoopregel("Fiets", 1, 100m, "8000", "V21")]));
        Moet(false, "Na afsluiten: verkoop in maart definitief", laat.IsGelukt ? await verkoop.MaakDefinitief(wie, id, laat.Waarde) : Uitkomst.Mislukt("concept"));
        var (i3, _) = await Inkoop(wie, id, [], "RG-2025-0003", new(2025, 3, 28), 100m, boeken: false);
        Moet(false, "Na afsluiten: inkoop in maart boeken", await BoekInkoop(wie, id, i3, "RG-2025-0003", new(2025, 3, 28), 100m));
        Moet(false, "Na afsluiten: btw-aangifte intrekken", await btw.Intrekken(wie, id, new(2025, 1, 1), new(2025, 3, 31)));
        var na = await Saldi(wie, id);
        if (!voor.SequenceEqual(na))
        {
            fouten.Add("Na afsluiten zijn de saldi tot en met maart veranderd: " + string.Join(", ", na.Except(voor)));
        }

        await Stap("na geweigerde wijzigingen");

        // ---- heropenen vanaf maart, aanpassen, opnieuw afsluiten
        Moet(true, "Heropenen", await perioden.Heropen(wie, id, new(2025, 3, 1), "Correctie memoriaal"));
        Moet(true, "Na heropenen: memoriaal aanpassen", await grootboek.PasAan(wie, id, "MEM", "2025-0002", new Journaalpost("MEM", "2025-0002", new(2025, 3, 20), "Herrubricering", [new("4300", 60m), new("4900", -60m)])));
        Moet(false, "Na heropenen: februari blijft dicht", await inkoop.Terugdraaien(wie, id, i1));
        Moet(true, "Opnieuw afsluiten", await perioden.SluitAf(wie, id, new(2025, 3, 31)));
        await Stap("na heropenen");

        // ---- een actief: aanschaf, afschrijven, verkopen en de verkoop terugdraaien
        Moet(true, "Aanschaf actief", await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0010", new(2025, 4, 1), "Werkbank", [new("0200", 3_000m), new("0500", -3_000m)])));
        var actief = await activa.Bewaar(wie, id, null, new ActivumInvoer("Werkbank", new(2025, 4, 1), 3_000m, 0m, new(2025, 4, 1), 60, Afschrijvingsmethode.Lineair, "0200", "0210", "4800"));
        Moet(true, "Actief", actief);
        Moet(true, "Afschrijven", await activa.Afschrijven(wie, id, new(2025, 6, 30), Afschrijvingsfrequentie.Kwartaal));
        Moet(false, "Afschrijven in de afgesloten periode", await activa.Afschrijven(wie, id, new(2025, 3, 31), Afschrijvingsfrequentie.Maand));
        Moet(true, "Verkopen", await activa.Desinvesteer(wie, id, actief.Waarde, new Desinvesteringsinvoer(new(2025, 7, 15), Desinvesteringssoort.Verkoop, 100m, 2_500m, "1800", "4900")));
        await Stap("na desinvestering");
        var desinvestering = (await activa.Open(wie, id, actief.Waarde))!.Desinvesteringen.Single();
        Moet(true, "Verkoop terugdraaien", await activa.TerugdraaienDesinvestering(wie, id, desinvestering.Id));
        Moet(false, "Verkopen met een datum in de afgesloten periode", await activa.Desinvesteer(wie, id, actief.Waarde, new Desinvesteringsinvoer(new(2025, 3, 15), Desinvesteringssoort.Verkoop, 100m, 2_500m, "1800", "4900")));
        await Stap("na terugdraaien desinvestering");
        fouten.AddRange(await Samenhang.Controleer(app, wie, id, "na alle wijzigingen"));

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(fouten.Count == 0, $"{fouten.Count} afwijkingen:\n" + string.Join("\n", fouten));
    }

    /// <summary>Gevonden met de samenhangcontrole: een tegoed van een klant telde op het dashboard als nog te ontvangen.</summary>
    [Fact]
    public async Task Een_betaalde_en_daarna_gecrediteerde_factuur_is_op_het_dashboard_een_tegoed()
    {
        var w = await Werkadministratie.Maak(app, "Tegoed Credit B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var factuur = await w.Concept(new(2025, 11, 10), 1_000m);
        var nummer = (await verkoop.MaakDefinitief(w.Wie, w.Id, factuur)).Waarde!;
        var bank = app.Dienst<BankDienst>();
        foreach (var m in await w.Bank(new Boekhouding.Bank.Bankmutatie(new(2025, 11, 20), 1_210m, $"Factuur {nummer}", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam)))
        {
            var post = (await bank.OpenPosten(w.Wie, w.Id)).Single(p => p.Factuurnummer == nummer);
            Assert.True((await bank.Afletteren(w.Wie, w.Id, m.Id, [post.Id])).IsGelukt);
        }

        var credit = await verkoop.Crediteer(w.Wie, w.Id, factuur);
        Assert.True((await verkoop.MaakDefinitief(w.Wie, w.Id, credit.Waarde)).IsGelukt);

        var dashboard = await app.Dienst<DashboardDienst>().Laad(w.Wie, w.Id, [new Widgetkeuze(Widget.Debiteuren)]);
        Assert.Equal(-1_210m, dashboard.Debiteuren!.Totaal);
        Assert.Empty(await Samenhang.Controleer(app, w.Wie, w.Id, "na crediteren van een betaalde factuur"));
    }

    [Fact]
    public async Task Een_latere_creditnota_verandert_de_openstaande_posten_op_een_eerdere_peildatum_niet()
    {
        var w = await Werkadministratie.Maak(app, "Peildatum Credit B.V.");
        var verkoop = app.Dienst<VerkoopDienst>();
        var factuur = await w.Concept(new(2025, 11, 10), 1_000m);
        var nummer = (await verkoop.MaakDefinitief(w.Wie, w.Id, factuur)).Waarde!;
        var credit = await verkoop.Crediteer(w.Wie, w.Id, factuur);
        Assert.True((await verkoop.MaakDefinitief(w.Wie, w.Id, credit.Waarde)).IsGelukt);

        // Eind 2025 bestond de creditnota nog niet: de factuur staat helemaal open, zoals in het grootboek.
        var eind2025 = (await app.Dienst<OpenpostenDienst>().Debiteuren(w.Wie, w.Id, new(2025, 12, 31))).Relaties.SelectMany(r => r.Posten).ToList();
        Assert.Equal(1_210m, Assert.Single(eind2025, p => p.Factuurnummer == nummer).Openstaand);

        // Nu is hij verrekend: niets open.
        Assert.Empty((await app.Dienst<OpenpostenDienst>().Debiteuren(w.Wie, w.Id, new(2026, 12, 31))).Relaties);
        Assert.Empty(await Invarianten.Controleer(app, w.Wie, w.Id, "na crediteren"));
    }

    /// <summary>Alle saldi tot en met de afgesloten periode, om te zien dat er niets veranderd is.</summary>
    private async Task<List<string>> Saldi(Uitvoerder wie, Guid id) =>
        [.. (await app.Dienst<GrootboekDienst>().Kolommenbalans(wie, id, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31))).Regels
            .Where(r => r.Saldo != 0).Select(r => $"{r.Rekening.Nummer} {r.Saldo}")];

    private async Task<(Guid Document, decimal Totaal)> Inkoop(Uitvoerder wie, Guid id, List<string> fouten, string nummer, DateOnly datum, decimal bedrag, bool boeken = true)
    {
        var f = new Factuurdata(true, Praktijkset.Groothandel, nummer, datum, datum.AddDays(14), "Levering", [new Regeldata("Levering", 1, bedrag, "V21", "7000", "I21")], Formaat.Ubl, Betaalwijze.Nummer);
        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(Praktijkset.Document(f, f.Partij, Praktijkset.Eigen)), f.Bestandsnaam, null);
        var document = gelezen.DocumentId ?? throw new InvalidOperationException(gelezen.Melding);
        if (boeken && !gelezen.IsGeboekt && await BoekInkoop(wie, id, document, nummer, datum, bedrag) is { IsGelukt: false } mislukt)
        {
            fouten.Add($"Inkoop {nummer}: {string.Join(" ", mislukt.Fouten)}");
        }

        return (document, f.Totaal);
    }

    private async Task<Uitkomst> BoekInkoop(Uitvoerder wie, Guid id, Guid document, string nummer, DateOnly datum, decimal bedrag)
    {
        var p = Praktijkset.Groothandel;
        var code = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).FirstOrDefault(r => r.Naam == p.Naam)?.Code;
        return await app.Dienst<InkoopDienst>().Boek(wie, id, document, new Factuurinvoer(code, code is null ? new NieuweRelatie(p.Naam, p.Iban, p.BtwNummer, p.Kvk) : null,
            nummer, datum, datum.AddDays(14), "Levering", [new Factuurregel("Levering", "7000", "I21", bedrag, Math.Round(bedrag * 0.21m, 2))], null));
    }

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie("Wijzigen B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2025-0001", new(2025, 1, 1), "Beginbalans",
            [new("1100", 10_000m), new("0500", -10_000m)]))).IsGelukt);
        return id;
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id)
    {
        var p = Praktijkset.Fietsplan;
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), p.Iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }
}
