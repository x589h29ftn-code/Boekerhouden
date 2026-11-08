using System.Diagnostics;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Stresstest van de vaste activa (alleen met BOEKERHOUDEN_STRESS): honderden activa over vijf jaar (2022-2026) op
/// inventaris en vervoermiddelen, met wisselende looptijd en restwaarde, elke maand afgeschreven, en elk jaar
/// verkopen, gedeeltelijke verkopen en sloop. Daarna: sluit de activastaat van elk jaar op het grootboek, sluit elk
/// jaar aan op het vorige, sluit de aansluiting, en hoe snel zijn de lijst, de staat en de exports.
/// </summary>
public sealed class ActivaStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static int Aantal => StressFactAttribute.Aantal == 30 ? 500 : StressFactAttribute.Aantal;

    private void Meet(string wat, Stopwatch klok, string? extra = null)
    {
        uitvoer.WriteLine($"{wat,-44} {klok.ElapsedMilliseconds,7} ms  {extra}");
        klok.Restart();
    }

    [StressFact]
    public async Task Honderden_activa_vijf_jaar_met_desinvesteringen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Activa Stress B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        var dienst = app.Dienst<ActivaDienst>();
        var klok = Stopwatch.StartNew();

        // 1. Aanschaffingen verspreid over 2022 tot en met juni 2026.
        for (var i = 0; i < Aantal; i++)
        {
            var datum = new DateOnly(2022, 1, 1).AddDays(i * 1640 / Aantal);
            var (rekening, bedrag) = i % 4 == 0 ? ("0300", 15000m + i * 37) : ("0200", 400m + (i * 53 % 4000) + 0.35m * (i % 3));
            Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", $"{datum.Year}-{9000 + i}", datum, $"Aanschaf {i + 1}",
                [new Journaalregel(rekening, bedrag, $"Actief {i + 1}"), new Journaalregel("1800", -bedrag)]))).IsGelukt);
        }

        Meet($"{Aantal} aanschaffingen geboekt", klok);

        // 2. Activeren uit de voorstellen, met wisselende looptijd en restwaarde.
        var kandidaten = await dienst.Kandidaten(wie, id);
        Assert.Equal(Aantal, kandidaten.Count);
        Meet("voorstellen (kandidaten)", klok, $"{kandidaten.Count}");
        var activa = new List<Guid>();
        foreach (var (k, i) in kandidaten.Select((k, i) => (k, i)))
        {
            var invoer = k.Voorstel with { Looptijd = 24 + (i % 5) * 12, Restwaarde = k.Rekening == "0300" ? 2000m : i % 7 == 0 ? 100m : 0m };
            var uitkomst = await dienst.Bewaar(wie, id, null, invoer);
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
            activa.Add(uitkomst.Waarde);
        }

        Meet($"{activa.Count} activa aangemaakt", klok);

        // 3. Per jaar: afschrijven per maand tot en met december, dan verkopen, deels verkopen en slopen.
        var random = new Random(2026);
        var weg = new HashSet<Guid>();
        var desinvesteringen = 0;
        foreach (var jaar in new[] { 2022, 2023, 2024, 2025, 2026 })
        {
            var tot = jaar == 2026 ? new DateOnly(2026, 10, 31) : new DateOnly(jaar, 12, 31);
            var afgeschreven = await dienst.Afschrijven(wie, id, tot, Afschrijvingsfrequentie.Maand);
            Assert.True(afgeschreven.IsGelukt, string.Join("; ", afgeschreven.Fouten));
            Meet($"afschrijven {jaar} (per maand)", klok, $"{afgeschreven.Waarde} boekingen");

            var lijst = (await dienst.Lijst(wie, id)).Where(a => a.BuitenGebruikOp is null && a.Aanschafdatum < tot.AddMonths(-1) && !weg.Contains(a.Id)).ToList();
            foreach (var a in lijst.OrderBy(_ => random.Next()).Take(Math.Max(3, Aantal / 25)))
            {
                var deel = desinvesteringen % 3 == 1 ? 50m : 100m;
                var soort = desinvesteringen % 5 == 4 ? Desinvesteringssoort.Sloop : Desinvesteringssoort.Verkoop;
                var datum = new DateOnly(tot.Year, tot.Month, 15);
                var uitkomst = await dienst.Desinvesteer(wie, id, a.Id,
                    new Desinvesteringsinvoer(datum, soort, deel, Math.Round(a.Boekwaarde * deel / 100m * 0.9m, 2), "1800", "4900"));
                Assert.True(uitkomst.IsGelukt, $"{a.Omschrijving}: {string.Join("; ", uitkomst.Fouten)}");
                if (deel == 100m)
                {
                    weg.Add(a.Id);
                }

                desinvesteringen++;
            }

            Meet($"desinvesteringen {jaar}", klok, $"totaal {desinvesteringen}");
        }

        // 4. De lijst, een kaart en wat er nog af te schrijven is.
        var alles = await dienst.Lijst(wie, id);
        Meet("lijst vaste activa", klok, $"{alles.Count} activa, {alles.Count(a => a.BuitenGebruikOp is not null)} weg");
        var kaart = await dienst.Open(wie, id, activa[^1]);
        Meet("activakaart openen", klok, $"{kaart!.Plan.Count} maanden in het plan");
        Assert.Empty(await dienst.TeAfschrijven(wie, id, new DateOnly(2026, 10, 31), Afschrijvingsfrequentie.Maand));
        Meet("te afschrijven t/m oktober", klok);

        // 5. De activastaat per jaar: sluit op het grootboek en sluit aan op het vorige jaar.
        Activastaatregel? vorige = null;
        foreach (var jaar in new[] { 2022, 2023, 2024, 2025, 2026 })
        {
            var staat = await dienst.Activastaat(wie, id, jaar);
            var totaal = staat.Aggregate(Activastaatregel.Nul, (som, g) => som + g.Totaal);
            Meet($"activastaat {jaar}", klok, $"{staat.Sum(g => g.Posten.Count)} activa, boekwaarde 31-12 {totaal.BoekwaardeEind:N2}");
            Assert.All(staat, g => Assert.True(g.Sluit, $"{jaar} {g.Activarekening}: {g.GrootboekAanschaf}/{g.Totaal.AanschafEind}, {g.GrootboekAfschrijving}/{g.Totaal.AfschrijvingEind}"));
            var stand = await dienst.Stand(wie, id, new DateOnly(jaar, 12, 31));
            var koba = await grootboek.Kolommenbalans(wie, id, new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
            Assert.All(stand, s => Assert.Equal(koba.Regels.SingleOrDefault(r => r.Rekening.Nummer == s.Key)?.Saldo ?? 0m, s.Value));
            Meet($"aansluiting kolommenbalans {jaar}", klok);
            if (vorige is not null)
            {
                Assert.Equal(vorige.AanschafEind, totaal.AanschafBegin);
                Assert.Equal(vorige.AfschrijvingEind, totaal.AfschrijvingBegin);
            }

            vorige = totaal;
            if (jaar == 2026)
            {
                var excel = Export.OverzichtExport.ActivastaatExcel(staat, jaar, "Activa Stress B.V.");
                Meet("activastaat 2026 naar Excel", klok, $"{excel.Length / 1024} kB");
                var html = Export.OverzichtExport.ActivastaatHtml(staat, jaar, "Activa Stress B.V.");
                Meet("activastaat 2026 als html (voor de PDF)", klok, $"{html.Length / 1024} kB");
            }
        }

        // 6. De aansluiting: alle activa- en afschrijvingsrekeningen sluiten.
        var aansluiting = (await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id)).Where(a => a.Soort == Aansluitingsoort.Activa).ToList();
        Meet("aansluitingen", klok, $"{aansluiting.Count} activa-rekeningen");
        Assert.All(aansluiting, a => Assert.True(a.Sluit, $"{a.Rekening}: {a.Grootboek} / {a.Bron} ({a.Toelichting})"));
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Meet("hash-keten controleren", klok);
    }
}
