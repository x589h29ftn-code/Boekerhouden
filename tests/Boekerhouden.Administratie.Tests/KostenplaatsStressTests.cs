using System.Diagnostics;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Stresstest van de kostenplaatsen (alleen met BOEKERHOUDEN_STRESS): honderden kostenplaatsen en duizenden
/// memoriaalboekingen over twee jaar met willekeurige kostenplaatsen per regel (ook zonder, en op een balansrekening),
/// aanpassingen die de kostenplaats of het bedrag veranderen (nieuwe versies), afsluiten en heropenen halverwege, en
/// vier gebruikers die tegelijk boeken. Alles wordt bijgehouden in een eigen rekenmodel; daarna moeten het overzicht,
/// de regels per kostenplaats, de export, de proefbalans en de hash-keten precies kloppen. Meet de tijden.
/// </summary>
public sealed class KostenplaatsStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static readonly string[] Kostenrekeningen = ["4300", "4310", "4320"];

    /// <summary>Het aantal boekingen; BOEKERHOUDEN_STRESS=1 geeft 3000.</summary>
    private static int Aantal => StressFactAttribute.Aantal == 30 ? 3000 : StressFactAttribute.Aantal;

    private static int Kostenplaatsen => Math.Max(20, Aantal / 15);

    private void Meet(string wat, Stopwatch klok, string? extra = null)
    {
        uitvoer.WriteLine($"{wat,-52} {klok.ElapsedMilliseconds,7} ms  {extra}");
        klok.Restart();
    }

    /// <summary>Wat er volgens het model op een boeking staat: per regel rekening, bedrag en kostenplaats.</summary>
    private sealed record Modelregel(string Rekening, decimal Bedrag, string? Kostenplaats);

    [StressFact]
    public async Task Honderden_kostenplaatsen_duizenden_boekingen_aanpassen_afsluiten_en_tegelijk()
    {
        var wie = await app.Beheerder();
        Assert.True((await app.Dienst<KantoorDienst>().WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        var id = await app.Administratie("Kostenplaats Stress B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var dienst = app.Dienst<KostenplaatsDienst>();
        var klok = Stopwatch.StartNew();
        var random = new Random(2611);

        // 1. Kostenplaatsen aanmaken.
        var codes = new List<string>();
        for (var i = 0; i < Kostenplaatsen; i++)
        {
            var code = $"KP{i:000}";
            var uitkomst = await dienst.VoegToe(wie, id, code, $"Kostenplaats {i}");
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
            codes.Add(code);
        }

        Meet($"{codes.Count} kostenplaatsen aangemaakt", klok);

        // 2. Boekingen over 2025 en 2026: 2 tot 6 regels, willekeurige kostenplaatsen (een derde zonder), soms omzet,
        //    soms een kostenplaats op de bankregel (balans: telt niet mee in kosten en opbrengsten).
        var model = new Dictionary<string, (DateOnly Datum, List<Modelregel> Regels)>(StringComparer.Ordinal);
        List<Modelregel> Willekeurig(IReadOnlyList<string> open)
        {
            var regels = new List<Modelregel>();
            for (var r = random.Next(1, 6); r > 0; r--)
            {
                var omzet = random.Next(5) == 0;
                var bedrag = Math.Round((decimal)random.NextDouble() * 900m + 0.01m, 2) * (omzet ? -1 : 1);
                regels.Add(new(omzet ? "8000" : Kostenrekeningen[random.Next(Kostenrekeningen.Length)], bedrag,
                    random.Next(3) == 0 ? null : open[random.Next(open.Count)]));
            }

            regels.Add(new("1100", -regels.Sum(r => r.Bedrag), random.Next(20) == 0 ? open[random.Next(open.Count)] : null));
            return regels;
        }

        static Journaalpost Post(string boekstuk, DateOnly datum, IEnumerable<Modelregel> regels) =>
            new("MEM", boekstuk, datum, $"Stress {boekstuk}", [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag, Kostenplaats: r.Kostenplaats))]);

        for (var i = 0; i < Aantal; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(random.Next(0, 670));
            var boekstuk = $"{datum.Year}-{i + 1:00000}";
            var regels = Willekeurig(codes);
            var uitkomst = await grootboek.Boek(wie, id, Post(boekstuk, datum, regels));
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
            model[boekstuk] = (datum, regels);
        }

        Meet($"{Aantal} boekingen geboekt", klok, $"{model.Values.Sum(m => m.Regels.Count)} regels");

        // 3. Een op de tien aanpassen: andere kostenplaatsen en bedragen (een nieuwe versie; de oude telt niet meer).
        var aangepast = 0;
        foreach (var boekstuk in model.Keys.Where((_, i) => i % 10 == 3).ToList())
        {
            var (datum, _) = model[boekstuk];
            var regels = Willekeurig(codes);
            var uitkomst = await grootboek.PasAan(wie, id, "MEM", boekstuk, Post(boekstuk, datum, regels));
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
            model[boekstuk] = (datum, regels);
            aangepast++;
        }

        Meet($"{aangepast} boekingen aangepast", klok);

        // 4. Een op de vijf kostenplaatsen afsluiten: daarop kan niet meer geboekt worden, een aanpassing die hem
        //    houdt wel; daarna de helft weer heropenen.
        var lijst = await dienst.Lijst(wie, id);
        var dicht = lijst.Where((_, i) => i % 5 == 0).ToList();
        foreach (var k in dicht)
        {
            Assert.True((await dienst.Wijzig(wie, id, k.Id, k.Naam, afgesloten: true)).IsGelukt);
        }

        Assert.Equal(codes.Count - dicht.Count, (await dienst.Keuze(wie, id)).Count);
        var geweigerd = await grootboek.Boek(wie, id, Post("2026-99999", new(2026, 6, 1), [new("4300", 10m, dicht[0].Code), new("1100", -10m, null)]));
        Assert.Equal([$"Kostenplaats {dicht[0].Code} is afgesloten."], geweigerd.Fouten);
        var metDicht = model.First(m => m.Value.Regels.Any(r => r.Kostenplaats == dicht[0].Code));
        var gehouden = metDicht.Value.Regels.Select(r => r.Rekening == "1100" ? r : r with { Bedrag = r.Bedrag }).ToList();
        var omschrijvingAnders = Post(metDicht.Key, metDicht.Value.Datum, gehouden) with { Omschrijving = "Aangepast met een afgesloten kostenplaats" };
        var houden = await grootboek.PasAan(wie, id, "MEM", metDicht.Key, omschrijvingAnders);
        Assert.True(houden.IsGelukt, string.Join("; ", houden.Fouten));
        foreach (var k in dicht.Where((_, i) => i % 2 == 0))
        {
            Assert.True((await dienst.Wijzig(wie, id, k.Id, k.Naam, afgesloten: false)).IsGelukt);
        }

        var open = (await dienst.Keuze(wie, id)).Select(k => k.Code).ToList();
        Meet($"{dicht.Count} afgesloten, {dicht.Count(k => !open.Contains(k.Code))} blijven dicht", klok);

        // 5. Vier gebruikers boeken tegelijk (de hash-keten moet heel blijven).
        var gebruikers = new List<Uitvoerder>();
        for (var g = 0; g < 4; g++)
        {
            var gebruiker = await app.Gebruiker($"Stress Boekhouder {g}");
            await app.Dienst<AdministratieDienst>().ZetLid(id, gebruiker.Id!.Value, Data.Lidmaatschapsrol.Kantoor, wie);
            gebruikers.Add(gebruiker);
        }

        var perGebruiker = Math.Max(10, Aantal / 40);
        var gelijktijdig = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateOnly, List<Modelregel>)>();
        var willekeurigPerGebruiker = gebruikers.Select((_, g) => new Random(100 + g)).ToList();
        await Task.WhenAll(gebruikers.Select((gebruiker, g) => Task.Run(async () =>
        {
            var eigen = willekeurigPerGebruiker[g];
            for (var i = 0; i < perGebruiker; i++)
            {
                var datum = new DateOnly(2026, 1, 1).AddDays(eigen.Next(0, 300));
                var boekstuk = $"2026-G{g}{i:0000}";
                var bedrag = Math.Round((decimal)eigen.NextDouble() * 500m + 1m, 2);
                List<Modelregel> regels = [new(Kostenrekeningen[eigen.Next(3)], bedrag, open[eigen.Next(open.Count)]), new("1100", -bedrag, null)];
                for (var poging = 0; ; poging++)
                {
                    var uitkomst = await grootboek.Boek(gebruiker, id, Post(boekstuk, datum, regels));
                    if (uitkomst.IsGelukt)
                    {
                        gelijktijdig[boekstuk] = (datum, regels);
                        break;
                    }

                    Assert.True(poging < 5, string.Join("; ", uitkomst.Fouten));
                }
            }
        })));
        foreach (var (boekstuk, waarde) in gelijktijdig)
        {
            model[boekstuk] = waarde;
        }

        Meet($"4 gebruikers tegelijk, {gelijktijdig.Count} boekingen", klok);

        // 6. Het overzicht per jaar tegen het model.
        var schema = await grootboek.Schema(wie, id);
        foreach (var jaar in new[] { 2025, 2026 })
        {
            var (van, tot) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
            var regelsInJaar = model.Values.Where(m => m.Datum.Year == jaar).SelectMany(m => m.Regels).Where(r => r.Kostenplaats is not null).ToList();
            var overzicht = await dienst.Overzicht(wie, id, van, tot);
            Meet($"overzicht {jaar}", klok, $"{overzicht.Count} kostenplaatsen");
            foreach (var t in overzicht)
            {
                var eigen = regelsInJaar.Where(r => r.Kostenplaats == t.Kostenplaats.Code).ToList();
                var perRekening = eigen.Where(r => schema.Zoek(r.Rekening) is { IsBalans: false }).GroupBy(r => r.Rekening).Select(g => g.Sum(r => r.Bedrag)).ToList();
                Assert.Equal(eigen.Count, t.Regels);
                Assert.Equal(perRekening.Where(s => s > 0).Sum(), t.Kosten);
                Assert.Equal(-perRekening.Where(s => s < 0).Sum(), t.Opbrengsten);
                Assert.Equal(open.Contains(t.Kostenplaats.Code), !t.Kostenplaats.Afgesloten);
            }

            // Alle kostenplaatsen samen plus wat zonder kostenplaats staat = het resultaat volgens de proefbalans.
            var proef = await grootboek.Proefbalans(wie, id, jaar);
            var resultaatGrootboek = proef.Regels.Where(r => !r.Rekening.IsBalans).Sum(r => r.Saldo);
            var zonder = model.Values.Where(m => m.Datum.Year == jaar).SelectMany(m => m.Regels)
                .Where(r => r.Kostenplaats is null && schema.Zoek(r.Rekening) is { IsBalans: false }).Sum(r => r.Bedrag);
            var met = model.Values.Where(m => m.Datum.Year == jaar).SelectMany(m => m.Regels)
                .Where(r => r.Kostenplaats is not null && schema.Zoek(r.Rekening) is { IsBalans: false }).Sum(r => r.Bedrag);
            Assert.Equal(resultaatGrootboek, met + zonder);
            Meet($"proefbalans {jaar} sluit op de kostenplaatsen", klok);

            // De regels van de drukste kostenplaats, en de export daarvan en van het overzicht.
            var drukste = overzicht.OrderByDescending(t => t.Regels).First();
            var boekingen = await dienst.Boekingen(wie, id, drukste.Kostenplaats.Code, van, tot);
            Assert.Equal(drukste.Regels, boekingen.Count);
            Assert.Equal(regelsInJaar.Where(r => r.Kostenplaats == drukste.Kostenplaats.Code).Sum(r => r.Bedrag), boekingen.Sum(b => b.Bedrag));
            Meet($"regels drukste kostenplaats {drukste.Kostenplaats.Code}", klok, $"{boekingen.Count} regels");
            var overzichtExcel = await app.Dienst<OverzichtDienst>().TabelExcel(wie, id, Kostenplaatsexport.Totaal(overzicht, van, tot));
            var plaatsExcel = await app.Dienst<OverzichtDienst>().TabelExcel(wie, id, Kostenplaatsexport.Kostenplaats(drukste.Kostenplaats, boekingen, van, tot));
            Assert.NotEmpty(overzichtExcel);
            Assert.NotEmpty(plaatsExcel);
            Meet("Excel overzicht en drukste kostenplaats", klok, $"{overzichtExcel.Length / 1024} + {plaatsExcel.Length / 1024} kB");
        }

        // 7. De keten is heel, ook na aanpassingen en gelijktijdig boeken; geen gebruikte kostenplaats is te verwijderen.
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Meet("hash-keten gecontroleerd", klok);
        var gebruikt = (await dienst.Lijst(wie, id)).First(k => k.Regels > 0);
        Assert.False((await dienst.Verwijder(wie, id, gebruikt.Id)).IsGelukt);
    }
}
