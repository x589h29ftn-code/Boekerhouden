using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Het memoriaal aan de randen: wat geweigerd moet worden (onbekende btw-code, drie decimalen, een lege regel), wat
/// moet kloppen bij herhalen en kopiëren over maandgrenzen (31 januari naar februari), aanpassen (de nieuwe versie
/// telt, de oude blijft bewaard), de nummering in een nieuw jaar, heel grote bedragen, en memoriaal op debiteuren met
/// relatie en factuurnummer (een openstaande post die de bank kan afletteren, sluitend op het grootboek).
/// </summary>
public sealed class MemoriaalRandgevallenTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Het_memoriaal_aan_de_randen()
    {
        var w = await Werkadministratie.Maak(app, "Memoriaal Randen B.V.");
        var (wie, id) = (w.Wie, w.Id);
        var g = app.Dienst<GrootboekDienst>();
        var fouten = new List<string>();
        Journaalpost Post(string boekstuk, DateOnly datum, params Journaalregel[] regels) => new("MEM", boekstuk, datum, $"Test {boekstuk}", regels);
        async Task Weiger(string wat, Journaalpost post)
        {
            var uitkomst = await g.Boek(wie, id, post);
            uitvoer.WriteLine($"{wat}: {(uitkomst.IsGelukt ? "GEBOEKT" : string.Join(" ", uitkomst.Fouten))}");
            if (uitkomst.IsGelukt)
            {
                fouten.Add($"{wat} werd geboekt.");
            }
        }

        // ---- weigeren
        await Weiger("onbekende btw-code", Post("2025-0101", new(2025, 3, 1), new("4300", 100m, BtwCode: "X99"), new("1100", -100m)));
        await Weiger("drie decimalen", Post("2025-0102", new(2025, 3, 1), new("4300", 100.005m), new("1100", -100.005m)));
        await Weiger("lege regel", Post("2025-0103", new(2025, 3, 1), new("4300", 100m), new("4900", 0m), new("1100", -100m)));
        await Weiger("niet in evenwicht", Post("2025-0104", new(2025, 3, 1), new("4300", 100m), new("1100", -99.99m)));

        // ---- heel grote bedragen: exact
        Assert.True((await g.Boek(wie, id, Post("2025-0110", new(2025, 3, 2), new("0200", 9_999_999_999.99m), new("0700", -9_999_999_999.99m)))).IsGelukt);
        var kb = await g.Kolommenbalans(wie, id, new(2025, 1, 1), new(2025, 12, 31));
        if (kb.Regels.Single(r => r.Rekening.Nummer == "0200").Saldo != 9_999_999_999.99m)
        {
            fouten.Add("Een heel groot bedrag kwam niet exact in de kolommenbalans.");
        }

        // ---- herhalen vanaf 31 januari: elke maand de laatste dag, eigen boekstukken
        var huur = Post(await g.VolgendBoekstuk(wie, id, "MEM", 2025), new(2025, 1, 31), new("4100", 1_000m), new("1800", -1_000m)) with { Omschrijving = "Huur januari" };
        var herhaald = await g.BoekPerMaand(wie, id, huur, 12);
        Assert.True(herhaald.IsGelukt, string.Join(" ", herhaald.Fouten));
        var huurposten = (await g.Posten(wie, id, "MEM")).Where(p => p.Post.Omschrijving.StartsWith("Huur", StringComparison.Ordinal)).Select(p => p.Post).ToList();
        var verwacht = Enumerable.Range(1, 12).Select(m => new DateOnly(2025, m, DateTime.DaysInMonth(2025, m))).ToList();
        if (!huurposten.Select(p => p.Datum).Order().SequenceEqual(verwacht))
        {
            fouten.Add($"Herhalen vanaf 31 januari: {string.Join(", ", huurposten.Select(p => p.Datum).Order())}.");
        }

        if (huurposten.Select(p => p.Boekstuk).Distinct().Count() != 12 || !huurposten.Any(p => p.Omschrijving == "Huur februari"))
        {
            fouten.Add($"Herhalen: boekstukken of omschrijvingen kloppen niet ({string.Join(", ", huurposten.Select(p => $"{p.Boekstuk} {p.Omschrijving}"))}).");
        }

        // ---- kopiëren van 31 januari naar februari
        var kopie = await g.KopieerNaarMaand(wie, id, "MEM", huur.Boekstuk, 2025, 2);
        var gekopieerd = (await g.Posten(wie, id, "MEM")).Single(p => p.Post.Boekstuk == kopie.Waarde).Post;
        if (gekopieerd.Datum != new DateOnly(2025, 2, 28))
        {
            fouten.Add($"Kopiëren van 31 januari naar februari gaf {gekopieerd.Datum}.");
        }

        // ---- aanpassen: de nieuwe versie telt, de oude blijft bewaard
        var voor = (await g.Kolommenbalans(wie, id, new(2025, 1, 1), new(2025, 12, 31))).Regels.Single(r => r.Rekening.Nummer == "4100").Saldo;
        var aangepast = await g.PasAan(wie, id, "MEM", huur.Boekstuk, huur with { Regels = [new("4100", 1_250m), new("1800", -1_250m)] });
        Assert.True(aangepast.IsGelukt, string.Join(" ", aangepast.Fouten));
        var na = (await g.Kolommenbalans(wie, id, new(2025, 1, 1), new(2025, 12, 31))).Regels.Single(r => r.Rekening.Nummer == "4100").Saldo;
        if (na - voor != 250m)
        {
            fouten.Add($"Na aanpassen van 1.000 naar 1.250 ging huur van {voor} naar {na}.");
        }

        if ((await g.Boeking(wie, id, "MEM", huur.Boekstuk)) is not { } detail || detail.Versie != 2 || detail.Eerder is not { Count: 1 })
        {
            fouten.Add("De vorige versie van de aangepaste boeking is niet bewaard.");
        }

        // ---- een nieuw jaar begint opnieuw te nummeren
        var nieuwJaar = await g.VolgendBoekstuk(wie, id, "MEM", 2026);
        if (nieuwJaar != "2026-0001")
        {
            fouten.Add($"Het eerste memoriaalboekstuk van 2026 is {nieuwJaar}.");
        }

        // ---- memoriaal op debiteuren en crediteuren: de openstaande posten sluiten nog op het grootboek
        var opDebiteuren = await g.Boek(wie, id, Post("2025-0900", new(2025, 5, 1), new("1300", 500m, Relatie: w.Klant, Factuurnummer: "M-1"), new("8000", -500m)));
        Assert.True(opDebiteuren.IsGelukt, string.Join(" ", opDebiteuren.Fouten));
        var post = (await app.Dienst<BankDienst>().OpenPosten(wie, id)).SingleOrDefault(p => p.Factuurnummer == "M-1");
        if (post?.Openstaand != 500m)
        {
            fouten.Add($"Een memoriaalregel op debiteuren met relatie en factuurnummer gaf geen openstaande post om af te letteren ({post?.Openstaand}).");
        }
        fouten.AddRange(await Invarianten.Controleer(app, wie, id, "na het memoriaal"));
        fouten.AddRange(await Samenhang.Controleer(app, wie, id, "na het memoriaal"));

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }
}
