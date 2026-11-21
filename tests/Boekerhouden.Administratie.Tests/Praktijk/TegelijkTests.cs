using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Gebruikers die op hetzelfde moment aan hetzelfde werken: dezelfde factuur boeken, dezelfde mutatie afletteren,
/// tegelijk facturen definitief maken (nummering), de aangifte vastleggen, afschrijven, de periode afsluiten terwijl er
/// geboekt wordt, en een factuur terugdraaien terwijl een ander hem aflettert. Steeds mag het hoogstens één keer
/// lukken, krijgt de rest een gewone melding (geen uitzondering), en kloppen daarna de vaste controles.
/// </summary>
public sealed class TegelijkTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const int Rondes = 5;

    [Fact]
    public async Task Tegelijk_aan_hetzelfde_werken()
    {
        var fouten = new List<string>();
        try
        {
            await Werk(fouten);
        }
        finally
        {
            foreach (var f in fouten)
            {
                uitvoer.WriteLine("  - " + f);
            }
        }

        Assert.True(fouten.Count == 0, $"{fouten.Count} afwijkingen:\n" + string.Join("\n", fouten));
    }

    private async Task Werk(List<string> fouten)
    {
        var w = await Werkadministratie.Maak(app, "Tegelijk Werk B.V.");
        var (wie, id) = (w.Wie, w.Id);
        var verkoop = app.Dienst<VerkoopDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        var bank = app.Dienst<BankDienst>();

        async Task<Uitkomst[]> Tegelijk(string wat, int aantal, Func<int, Task<Uitkomst>> werk)
        {
            using var start = new ManualResetEventSlim();
            var taken = Enumerable.Range(0, aantal).Select(i => Task.Run(async () =>
            {
                start.Wait();
                try
                {
                    return await werk(i);
                }
                catch (Exception e)
                {
                    lock (fouten)
                    {
                        fouten.Add($"{wat}: uitzondering {e.GetType().Name}: {e.Message}");
                    }

                    return Uitkomst.Mislukt("uitzondering");
                }
            })).ToList();
            start.Set();
            return await Task.WhenAll(taken);
        }

        void HoogstensEen(string wat, Uitkomst[] uitkomsten, bool minstensEen = true)
        {
            var gelukt = uitkomsten.Count(u => u.IsGelukt);
            if (gelukt > 1 || (minstensEen && gelukt == 0))
            {
                fouten.Add($"{wat}: {gelukt} keer gelukt ({string.Join(" | ", uitkomsten.Where(u => !u.IsGelukt).SelectMany(u => u.Fouten).Distinct())}).");
            }
        }

        for (var ronde = 1; ronde <= Rondes; ronde++)
        {
            var dag = new DateOnly(2025, 2, ronde * 2);

            // Dezelfde inkoopfactuur door vier mensen tegelijk geboekt.
            var nummer = $"RG-T-{ronde:000}";
            var document = await w.InkoopDocument(nummer, dag, 100m * ronde);
            // Wie het document pas laadt als het al geboekt is, past het aan (nieuwe versie): dat mag. Er mag maar één
            // boeking en één openstaande post zijn.
            var boeken = await Tegelijk("inkoop boeken", 4, _ => w.BoekInkoop(document, nummer, dag, 100m * ronde));
            if (!boeken.Any(u => u.IsGelukt))
            {
                fouten.Add($"Inkoop {nummer} vier keer tegelijk boeken: geen enkele keer gelukt ({string.Join(" | ", boeken.SelectMany(u => u.Fouten).Distinct())}).");
            }

            var keren = (await inkoop.Inkoopboek(wie, id, dag, dag)).Count(r => r.Factuurnummer == nummer);
            if (keren != 1)
            {
                fouten.Add($"Inkoop {nummer}: {keren} keer in het inkoopboek.");
            }

            // Hetzelfde concept vier keer tegelijk definitief maken: één nummer.
            var concept = await w.Concept(dag, 250m);
            HoogstensEen($"Concept ronde {ronde} vier keer definitief", await Tegelijk("definitief", 4, async _ => await verkoop.MaakDefinitief(wie, id, concept)));

            // Dezelfde mutatie vier keer tegelijk afletteren op dezelfde factuur.
            var open = (await bank.OpenPosten(wie, id)).Where(p => p.Factuurnummer == nummer).ToList();
            if (open.Count != 1)
            {
                fouten.Add($"Inkoop {nummer}: {open.Count} openstaande posten na vier keer tegelijk boeken.");
            }

            var post = open[0];
            var mutatie = (await w.Bank(new Bankmutatie(dag.AddDays(1), -100m * ronde, $"Betaling {ronde}", "NL02ABNA0123456789", "Onbekend")))
                .Single(m => m.Omschrijving == $"Betaling {ronde}");
            HoogstensEen($"Mutatie ronde {ronde} vier keer afletteren", await Tegelijk("afletteren", 4, _ => bank.Afletteren(wie, id, mutatie.Id, [post.Id])));
        }

        // Twintig concepten tegelijk definitief: twintig verschillende nummers zonder gaten.
        var concepten = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            concepten.Add(await w.Concept(new DateOnly(2025, 3, 3), 10m + i));
        }

        var definitief = await Tegelijk("twintig definitief", 20, async i => await verkoop.MaakDefinitief(wie, id, concepten[i]));
        if (definitief.Count(u => u.IsGelukt) != 20)
        {
            fouten.Add($"Twintig tegelijk definitief: {definitief.Count(u => u.IsGelukt)} gelukt ({string.Join(" | ", definitief.SelectMany(u => u.Fouten).Distinct())}).");
        }

        var nummers = (await verkoop.Lijst(wie, id, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))).Where(v => v.Nummer is not null).Select(v => v.Nummer!).ToList();
        if (nummers.Distinct().Count() != nummers.Count || (await verkoop.Gaten(wie, id, 2025)).Count > 0)
        {
            fouten.Add($"Nummering na tegelijk definitief: {nummers.Count} nummers, {nummers.Distinct().Count()} verschillend, gaten {string.Join(", ", await verkoop.Gaten(wie, id, 2025))}.");
        }

        // Een factuur terugdraaien terwijl een ander hem aflettert: niet allebei.
        for (var ronde = 1; ronde <= Rondes; ronde++)
        {
            var dag = new DateOnly(2025, 3, 10 + ronde);
            var nummer = $"RG-X-{ronde:000}";
            var document = await w.InkoopDocument(nummer, dag, 50m);
            Assert.True((await w.BoekInkoop(document, nummer, dag, 50m)).IsGelukt);
            var post = (await bank.OpenPosten(wie, id)).Single(p => p.Factuurnummer == nummer);
            var mutatie = (await w.Bank(new Bankmutatie(dag, -40m, $"Kruis {ronde}", "NL02ABNA0123456789", "Onbekend"))).Single(m => m.Omschrijving == $"Kruis {ronde}");
            var beide = await Tegelijk("terugdraaien tegen afletteren", 2, i => i == 0 ? inkoop.Terugdraaien(wie, id, document) : bank.Afletteren(wie, id, mutatie.Id, [post.Id]));
            HoogstensEen($"Terugdraaien en afletteren van {nummer} tegelijk", beide);
        }

        // De aangifte vier keer tegelijk vastleggen.
        HoogstensEen("Btw vier keer tegelijk vastleggen", await Tegelijk("vastleggen", 4, _ => app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new(2025, 1, 1), new(2025, 3, 31))));

        // Drie keer tegelijk afschrijven: één keer geboekt.
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2025-0050", new(2025, 1, 2), "Werkbank", [new("0200", 1_200m), new("0500", -1_200m)]))).IsGelukt);
        Assert.True((await app.Dienst<ActivaDienst>().Bewaar(wie, id, null, new ActivumInvoer("Werkbank", new(2025, 1, 2), 1_200m, 0m, new(2025, 1, 1), 12, Afschrijvingsmethode.Lineair, "0200", "0210", "4800"))).IsGelukt);
        HoogstensEen("Drie keer tegelijk afschrijven", await Tegelijk("afschrijven", 3, async _ => await app.Dienst<ActivaDienst>().Afschrijven(wie, id, new(2025, 3, 31), Afschrijvingsfrequentie.Kwartaal)));
        var afschrijving = (await app.Dienst<ActivaDienst>().Activastaat(wie, id, 2025)).Single().Totaal.Afschrijving;
        if (afschrijving != 300m)
        {
            fouten.Add($"Afschrijving na drie keer tegelijk: {afschrijving} in plaats van 300.");
        }

        // De periode afsluiten terwijl er twintig boekingen in die periode binnenkomen.
        var grootboek = app.Dienst<GrootboekDienst>();
        var voor = (await grootboek.Posten(wie, id, "MEM")).Count;
        var race = await Tegelijk("afsluiten tegen boeken", 21, async i => i == 20
            ? await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new(2025, 3, 31))
            : await grootboek.Boek(wie, id, new Journaalpost("MEM", $"2025-{100 + i:0000}", new(2025, 3, 28), $"Race {i}", [new("4300", 1m), new("4900", -1m)])));
        var geboekt = race.Take(20).Count(u => u.IsGelukt);
        var na = (await grootboek.Posten(wie, id, "MEM")).Count;
        if (na - voor != geboekt)
        {
            fouten.Add($"Afsluiten tegen boeken: {geboekt} boekingen gemeld als gelukt, {na - voor} in het journaal.");
        }

        uitvoer.WriteLine($"Afsluiten tegen boeken: afsluiten {(race[20].IsGelukt ? "gelukt" : "geweigerd: " + string.Join(" ", race[20].Fouten))}, {geboekt} van 20 boekingen gelukt.");
        if (race[20].IsGelukt && (await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0200", new(2025, 3, 28), "Na afsluiten", [new("4300", 1m), new("4900", -1m)]))).IsGelukt)
        {
            fouten.Add("Na afsluiten kan nog in maart geboekt worden.");
        }

        fouten.AddRange(await Invarianten.Controleer(app, wie, id, "na alles tegelijk"));
        fouten.AddRange(await Samenhang.Controleer(app, wie, id, "na alles tegelijk"));
    }
}
