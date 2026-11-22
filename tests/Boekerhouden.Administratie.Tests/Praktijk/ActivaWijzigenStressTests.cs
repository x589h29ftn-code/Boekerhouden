using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Vaste activa onder druk: twintig activa en drie jaar lang een willekeurige reeks van afschrijven, extra afschrijven
/// (ook te veel), de restwaarde en het percentage per datum wijzigen, terugdraaien en gedeeltelijk desinvesteren. Na elke
/// stap: geen uitzonderingen, en op gezette tijden sluiten de activa op het grootboek, sluit de activastaat van elk
/// jaar, klopt elk actief met zichzelf en kloppen de vaste controles. Daarnaast tegelijk extra afschrijven op hetzelfde
/// actief (nooit meer dan de boekwaarde) en een afgesloten periode.
/// </summary>
public sealed class ActivaWijzigenStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static readonly string[] Rekeningen = ["0200", "0210"];

    private async Task<(Uitvoerder Wie, Guid Id, List<Guid> Activa)> Opzet(string naam, int aantal, int zaad)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie(naam);
        var grootboek = app.Dienst<GrootboekDienst>();
        var dienst = app.Dienst<ActivaDienst>();
        var r = new Random(zaad);
        var activa = new List<Guid>();
        for (var i = 0; i < aantal; i++)
        {
            var datum = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 700));
            var bedrag = Math.Round((decimal)(r.NextDouble() * 20_000 + 300), 2);
            var stuk = await grootboek.VolgendBoekstuk(wie, id, "MEM", datum.Year);
            Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", stuk, datum, $"Aanschaf {i}",
                [new Journaalregel("0200", bedrag, $"Machine {i}"), new Journaalregel("1800", -bedrag)]))).IsGelukt);
            var voorstel = (await dienst.Kandidaten(wie, id)).Single(k => k.Boekstuk == $"MEM {stuk}").Voorstel with
            {
                Omschrijving = $"Machine {i}",
                Looptijd = r.Next(12, 72),
                Restwaarde = r.Next(3) == 0 ? Math.Round(bedrag * 0.1m, 2) : 0m,
            };
            var opgeslagen = await dienst.Bewaar(wie, id, null, voorstel);
            Assert.True(opgeslagen.IsGelukt, string.Join("; ", opgeslagen.Fouten));
            activa.Add(opgeslagen.Waarde);
        }

        return (wie, id, activa);
    }

    private async Task<List<string>> Controleer(Uitvoerder wie, Guid id, IReadOnlyList<Guid> activa, DateOnly tot, string wanneer)
    {
        var fouten = new List<string>();
        var dienst = app.Dienst<ActivaDienst>();

        // De activa tegen de kolommenbalans.
        var stand = await dienst.Stand(wie, id, tot);
        var koba = await app.Dienst<GrootboekDienst>().Kolommenbalans(wie, id, new DateOnly(2025, 1, 1), tot);
        foreach (var rekening in Rekeningen)
        {
            var grootboek = koba.Regels.SingleOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;
            if (grootboek != stand.GetValueOrDefault(rekening))
            {
                fouten.Add($"{wanneer}: {rekening} activa {stand.GetValueOrDefault(rekening)}, grootboek {grootboek}.");
            }
        }

        // De activastaat van elk jaar sluit; het eind van het ene jaar is het begin van het volgende.
        Activastaatregel? vorig = null;
        foreach (var jaar in new[] { 2025, 2026, 2027, 2028 })
        {
            var staat = await dienst.Activastaat(wie, id, jaar);
            fouten.AddRange(staat.Where(g => !g.Sluit).Select(g => $"{wanneer}: activastaat {jaar} {g.Activarekening} sluit niet ({g.Totaal.AanschafEind}/{g.GrootboekAanschaf}, {g.Totaal.AfschrijvingEind}/{g.GrootboekAfschrijving})."));
            var totaal = staat.Aggregate(Activastaatregel.Nul, (som, g) => som + g.Totaal);
            if (vorig is { } v && (v.AanschafEind != totaal.AanschafBegin || v.AfschrijvingEind != totaal.AfschrijvingBegin))
            {
                fouten.Add($"{wanneer}: activastaat {jaar} begint niet waar {jaar - 1} eindigt.");
            }

            vorig = totaal;
        }

        // Elk actief klopt met zichzelf: boekwaarde, plan zonder negatieve of halve bedragen, cumulatief oplopend.
        foreach (var a in activa)
        {
            var d = (await dienst.Open(wie, id, a))!;
            if (d.Boekwaarde != d.Aanschafwaarde - d.Afgeschreven)
            {
                fouten.Add($"{wanneer}: actief {d.Nummer} boekwaarde.");
            }

            if (d.Plan.Any(m => m.Bedrag < 0 || m.Bedrag != Math.Round(m.Bedrag, 2)))
            {
                fouten.Add($"{wanneer}: actief {d.Nummer} heeft een negatief of onafgerond bedrag in het plan.");
            }

            var geboekt = d.Plan.Where(m => m.Geboekt).Sum(m => m.Bedrag) + (d.Extra ?? []).Where(x => x.Tegenboeking is null).Sum(x => x.Bedrag);
            var desinvesteerd = d.Desinvesteringen.Where(x => x.Tegenboeking is null).Sum(x => x.Afschrijving);
            if (d.Invoer.AfgeschrevenVoorBegin + geboekt - desinvesteerd != d.Afgeschreven)
            {
                fouten.Add($"{wanneer}: actief {d.Nummer} afgeschreven {d.Afgeschreven}, volgens plan en extra {d.Invoer.AfgeschrevenVoorBegin + geboekt - desinvesteerd}.");
            }
        }

        fouten.AddRange(await Invarianten.Controleer(app, wie, id, wanneer));
        return fouten;
    }

    [Fact]
    public async Task Een_willekeurige_reeks_van_drie_jaar_blijft_kloppen()
    {
        var (wie, id, activa) = await Opzet("Activa Stress B.V.", 20, 7);
        var dienst = app.Dienst<ActivaDienst>();
        var r = new Random(42);
        var fouten = new List<string>();
        var tellers = new Dictionary<string, (int Gelukt, int Geweigerd)>();
        var redenen = new Dictionary<string, int>();
        void Tel(string wat, Uitkomst uitkomst)
        {
            var (g, w) = tellers.GetValueOrDefault(wat);
            tellers[wat] = uitkomst.IsGelukt ? (g + 1, w) : (g, w + 1);
            foreach (var reden in uitkomst.Fouten)
            {
                var kort = $"{wat}: {System.Text.RegularExpressions.Regex.Replace(reden, @"[\d.,-]+", "#")}";
                redenen[kort] = redenen.GetValueOrDefault(kort) + 1;
            }
        }

        var maand = new DateOnly(2025, 1, 1);
        var klok = System.Diagnostics.Stopwatch.StartNew();
        for (var stap = 0; stap < 300; stap++)
        {
            if (r.Next(4) == 0 && maand < new DateOnly(2028, 12, 1))
            {
                maand = maand.AddMonths(1);
            }

            var eind = maand.AddMonths(1).AddDays(-1);
            var actief = activa[r.Next(activa.Count)];
            var datum = maand.AddDays(r.Next(0, 28));
            try
            {
                switch (r.Next(7))
                {
                    case 0 or 1:
                        {
                            var u = await dienst.Afschrijven(wie, id, eind, Afschrijvingsfrequentie.Maand);
                            Tel("afschrijven", u.IsGelukt ? Uitkomst.Gelukt : Uitkomst.Mislukt([.. u.Fouten]));
                        }
                        break;
                    case 2:
                        {
                            var d = (await dienst.Open(wie, id, actief))!;
                            var bedrag = Math.Round(d.Boekwaarde * (decimal)(r.NextDouble() * 0.7), 2);
                            if (r.Next(5) == 0)
                            {
                                bedrag = d.Boekwaarde + 1m;
                            }

                            var uitkomst = await dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(datum, bedrag));
                            if (uitkomst.IsGelukt && bedrag > d.Boekwaarde)
                            {
                                fouten.Add($"stap {stap}: extra afschrijving van {bedrag} bij boekwaarde {d.Boekwaarde} geboekt.");
                            }

                            Tel("extra", uitkomst);
                            break;
                        }

                    case 3:
                        {
                            var d = (await dienst.Open(wie, id, actief))!;
                            Tel("restwaarde", await dienst.WijzigRestwaarde(wie, id, actief, maand.AddMonths(r.Next(0, 12)), Math.Round(d.Boekwaarde * (decimal)(r.NextDouble() * 0.5), 2)));
                            break;
                        }

                    case 4:
                        Tel("percentage", await dienst.Herzie(wie, id, actief, maand.AddMonths(r.Next(0, 12)), Math.Round((decimal)(r.NextDouble() * 45 + 5), 2)));
                        break;
                    case 5:
                        {
                            var open = ((await dienst.Open(wie, id, actief))!.Extra ?? []).Where(x => x.Tegenboeking is null).ToList();
                            if (open.Count > 0)
                            {
                                Tel("terugdraaien", await dienst.TerugdraaienExtra(wie, id, open[r.Next(open.Count)].Id));
                            }

                            break;
                        }

                    default:
                        if (r.Next(4) == 0)
                        {
                            Tel("desinvesteren", await dienst.Desinvesteer(wie, id, actief,
                                new Desinvesteringsinvoer(datum, Desinvesteringssoort.Sloop, r.Next(2) == 0 ? 100m : 40m, 0m, null, "4900")));
                        }

                        break;
                }
            }
            catch (Exception fout)
            {
                fouten.Add($"stap {stap} ({maand:MM-yyyy}): {fout.GetType().Name}: {fout.Message}");
            }

            if (stap % 50 == 49)
            {
                fouten.AddRange(await Controleer(wie, id, activa, new DateOnly(2030, 12, 31), $"na stap {stap + 1}"));
            }
        }

        fouten.AddRange(await Controleer(wie, id, activa, new DateOnly(2030, 12, 31), "aan het eind"));
        foreach (var peil in new[] { new DateOnly(2025, 12, 31), new DateOnly(2026, 6, 30), new DateOnly(2027, 12, 31) })
        {
            var stand = await dienst.Stand(wie, id, peil);
            var koba = await app.Dienst<GrootboekDienst>().Kolommenbalans(wie, id, new DateOnly(2025, 1, 1), peil);
            fouten.AddRange(Rekeningen.Where(rek => (koba.Regels.SingleOrDefault(x => x.Rekening.Nummer == rek)?.Saldo ?? 0m) != stand.GetValueOrDefault(rek))
                .Select(rek => $"peildatum {peil}: {rek} sluit niet."));
        }

        // Snel genoeg: de lijst met alle activa en hun plannen.
        var lijstklok = System.Diagnostics.Stopwatch.StartNew();
        await dienst.Lijst(wie, id);
        if (lijstklok.Elapsed > TimeSpan.FromSeconds(3))
        {
            fouten.Add($"De activalijst duurde {lijstklok.Elapsed.TotalSeconds:0.0} s.");
        }

        uitvoer.WriteLine($"300 stappen in {klok.Elapsed.TotalSeconds:0} s; activalijst {lijstklok.ElapsedMilliseconds} ms");
        foreach (var (wat, (gelukt, geweigerd)) in tellers.OrderBy(t => t.Key))
        {
            uitvoer.WriteLine($"  {wat,-14} {gelukt,4} gelukt {geweigerd,4} geweigerd");
        }

        foreach (var (reden, aantal) in redenen.OrderBy(x => x.Key))
        {
            uitvoer.WriteLine($"    {aantal,3}× {reden}");
        }

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(tellers.GetValueOrDefault("extra").Gelukt > 10 && tellers.GetValueOrDefault("restwaarde").Gelukt > 5, "Te weinig gelukte wijzigingen om iets te zeggen.");
        Assert.True(fouten.Count == 0, $"{fouten.Count} afwijkingen:\n" + string.Join("\n", fouten.Take(30)));
    }

    [Fact]
    public async Task Tegelijk_extra_afschrijven_nooit_meer_dan_de_boekwaarde()
    {
        var (wie, id, activa) = await Opzet("Activa Tegelijk B.V.", 1, 3);
        var dienst = app.Dienst<ActivaDienst>();
        var actief = activa[0];
        var boekwaarde = (await dienst.Open(wie, id, actief))!.Boekwaarde;
        var stuk = Math.Round(boekwaarde / 4.5m, 2);

        // Tien tegelijk, elk een kwart en wat: hoogstens vier passen in de boekwaarde.
        var uitkomsten = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(new DateOnly(2027, 1, 15), stuk)))));
        var gelukt = uitkomsten.Count(u => u.IsGelukt);
        uitvoer.WriteLine($"{gelukt} van 10 gelukt bij boekwaarde {boekwaarde} en stukken van {stuk}");
        Assert.Equal(4, gelukt);
        Assert.All(uitkomsten.Where(u => !u.IsGelukt), u => Assert.Contains(u.Fouten, f => f.Contains("boekwaarde", StringComparison.Ordinal)));

        var detail = (await dienst.Open(wie, id, actief))!;
        Assert.Equal(4, detail.Extra!.Count);
        Assert.Equal(4, detail.Extra!.Select(x => x.Boekstuk).Distinct().Count());
        Assert.Equal(boekwaarde - (4 * stuk), detail.Boekwaarde);

        // Tegelijk afschrijven en extra afschrijven: alles telt precies één keer.
        await Task.WhenAll(
            Task.Run(() => dienst.Afschrijven(wie, id, new DateOnly(2027, 12, 31), Afschrijvingsfrequentie.Maand)),
            Task.Run(() => dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(new DateOnly(2027, 2, 1), 1m))),
            Task.Run(() => dienst.Afschrijven(wie, id, new DateOnly(2027, 12, 31), Afschrijvingsfrequentie.Maand)));
        var fouten = await Controleer(wie, id, activa, new DateOnly(2030, 12, 31), "na tegelijk");
        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }

    [Fact]
    public async Task In_een_afgesloten_periode_kan_niets_extra_en_niets_terug()
    {
        var (wie, id, activa) = await Opzet("Activa Afgesloten B.V.", 1, 5);
        var dienst = app.Dienst<ActivaDienst>();
        var actief = activa[0];
        Assert.True((await dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(new DateOnly(2026, 3, 10), 10m))).IsGelukt);
        Assert.True((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new DateOnly(2026, 6, 30))).IsGelukt);

        Assert.False((await dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(new DateOnly(2026, 5, 10), 10m))).IsGelukt);
        var extra = Assert.Single((await dienst.Open(wie, id, actief))!.Extra!);
        Assert.False((await dienst.TerugdraaienExtra(wie, id, extra.Id)).IsGelukt);
        Assert.True((await dienst.ExtraAfschrijven(wie, id, actief, new Extrainvoer(new DateOnly(2026, 7, 1), 10m))).IsGelukt);
        var fouten = await Controleer(wie, id, activa, new DateOnly(2030, 12, 31), "na afsluiten");
        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }
}
