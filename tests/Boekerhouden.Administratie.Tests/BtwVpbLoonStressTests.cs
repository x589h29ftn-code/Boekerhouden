using System.Diagnostics;
using System.Globalization;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Loon;
using Boekerhouden.Documenten.Tests;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Stresstest (alleen met BOEKERHOUDEN_STRESS) van de btw-aangifte, de rondrekening en jaaropstelling, de fiscale
/// positie en het loonjournaal in de app, met de database: duizenden boekingen in een jaar (grote bedragen,
/// creditnota's, privégebruik), aanpassingen, en loonjournaals met honderden rekeningen en nieuwe looncomponenten.
/// </summary>
public sealed class BtwVpbLoonStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static int Aantal => StressFactAttribute.Aantal == 30 ? 2_000 : StressFactAttribute.Aantal;

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private void Meet(string wat, Stopwatch klok)
    {
        uitvoer.WriteLine($"{wat,-50} {klok.ElapsedMilliseconds,7} ms");
        klok.Restart();
    }

    [StressFact]
    public async Task Duizenden_boekingen_btw_rondrekening_en_vpb()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Btw Vpb Stress B.V.", schema: Schemakeuze.Ovpa);
        var grootboek = app.Dienst<GrootboekDienst>();
        var klok = Stopwatch.StartNew();
        var willekeurig = new Random(11);
        for (var i = 0; i < Aantal; i++)
        {
            var datum = new DateOnly(2026, 1, 1).AddDays(willekeurig.Next(365));
            var bedrag = Math.Round((decimal)willekeurig.NextDouble() * (i % 10 == 0 ? 10_000_000m : 5_000m), 2) + 0.01m;
            var teken = i % 9 == 0 ? -1 : 1;
            var belasting = Math.Round(bedrag * 0.21m, 2) * teken;
            var post = i % 2 == 0
                ? new Journaalpost("MEM", $"2026-{i + 1:0000}", datum, "Verkoop", [new("8000", -bedrag * teken, BtwCode: "V21"), new("1800", -belasting, BtwCode: "V21"), new("0610", (bedrag * teken) + belasting)])
                : new Journaalpost("MEM", $"2026-{i + 1:0000}", datum, "Kosten", [new(i % 5 == 1 ? "4530" : "4300", bedrag * teken, BtwCode: "I21"), new("1805", belasting, BtwCode: "I21"), new("0610", -((bedrag * teken) + belasting))]);
            Assert.True((await grootboek.Boek(wie, id, post)).IsGelukt);
        }

        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-9998", new(2026, 12, 31), "Btw privégebruik", [new("4481", 1_215m), new("1804", -1_215m)]))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-9999", new(2026, 6, 30), "Voorlopige aanslag vpb", [new("1780", 25_000m), new("0610", -25_000m)]))).IsGelukt);
        Meet($"{Aantal} boekingen", klok);

        var btw = app.Dienst<BtwaangifteDienst>();
        var stand = await btw.Jaar(wie, id, 2026);
        Meet("btw-jaar (perioden, aansluiting, rondrekening)", klok);
        Assert.Equal(stand.Jaar.Jaaraangifte.TeBetalen, stand.Jaar.Perioden.Sum(p => p.Berekend.TeBetalen));
        Assert.Equal(1_215m, stand.Jaar.Jaaraangifte.Rubriek("1d").Btw);
        Assert.All(stand.Aansluitingen, a => Assert.Equal(0m, a.Verschil));
        _ = stand.Jaaropstelling;
        Meet("jaaropstelling", klok);

        // Vastleggen van alle kwartalen, aanpassen in de jaaropstelling, en opnieuw: de suppletie is nul.
        foreach (var p in stand.Jaar.Perioden)
        {
            Assert.True((await btw.Vastleggen(wie, id, p.Periode.Van, p.Periode.Tot)).IsGelukt);
        }

        foreach (var (sleutel, i) in stand.Rondrekening.Aanpasbaar.Distinct().Take(20).Select((s, i) => (s, i)))
        {
            Assert.True((await btw.PasAan(wie, id, 2026, sleutel, 1_000_000m * i)).IsGelukt);
        }

        var na = await btw.Jaar(wie, id, 2026);
        Meet("vastleggen, 20 aanpassingen, opnieuw", klok);
        Assert.Equal(0m, na.Jaar.Suppletie);
        Assert.Equal(20, na.Aanpassingen.Count);

        // De fiscale positie: resultaat uit duizenden boekingen, beperkt aftrekbaar (4530), voorlopige aanslag.
        var vpb = app.Dienst<VpbDienst>();
        var positie = (await vpb.Positie(wie, id, 2026))!.Positie;
        Meet("fiscale positie", klok);
        Assert.Equal(25_000m, positie.Regels.Single(r => r.Omschrijving == "Betaalde voorlopige aanslagen").Totaal!.Bedrag);
        Assert.Contains(positie.Beperkt, r => r.Nummer == "4530");
        Assert.True(positie.Vennootschapsbelasting >= 0);
        Assert.Equal(positie.Vennootschapsbelasting - 25_000m, positie.TeBetalen);
        foreach (var sleutel in positie.Aanpasbaar.Take(15))
        {
            Assert.True((await vpb.PasAan(wie, id, 2026, sleutel, -999_999.99m)).IsGelukt);
        }

        Assert.True((await vpb.Positie(wie, id, 2026))!.Positie.Vennootschapsbelasting >= 0);
        Meet("15 aanpassingen vpb, opnieuw", klok);

        // De btw ziet de aanpassingen van de vpb niet, en andersom.
        Assert.Equal(20, (await btw.Jaar(wie, id, 2026)).Aanpassingen.Count);
    }

    [StressFact]
    public async Task Loonjournaals_met_honderden_rekeningen_en_nieuwe_looncomponenten()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Loon Stress B.V.");
        var dienst = app.Dienst<LoonjournaalDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var klok = Stopwatch.StartNew();

        for (var maand = 1; maand <= 12; maand++)
        {
            // Elke maand meer looncomponenten (de laatste 5 zijn nieuw), grote en negatieve bedragen, sluitend.
            var aantal = 100 + (maand * 5);
            var rekeningen = new List<FictiefLoonjournaal.Rekening>();
            var totaal = 0m;
            for (var i = 0; i < aantal; i++)
            {
                var bedrag = i % 10 == 0 ? 9_876_543.21m : (i * 13 % 5_000) + 0.5m;
                var correctie = i % 17 == 0 ? -1 : 1;
                totaal += bedrag * correctie;
                rekeningen.Add(new($"{40000 + i}", $"Looncomponent {i}", (bedrag * correctie).ToString("#,##0.00", Nl), false));
                rekeningen.Add(new($"{20000 + i}", $"Af te dragen {i}", (bedrag * correctie).ToString("#,##0.00", Nl), true));
            }

            var pdf = FictiefLoonjournaal.Maak(rekeningen, totaal.ToString("#,##0.00", Nl), totaal.ToString("#,##0.00", Nl), tweeKolommen: maand % 2 == 0);
            var voorstel = (await dienst.Lees(wie, id, new MemoryStream(pdf), $"loon-{maand}.pdf")).Waarde!;
            Assert.True(voorstel.Signalen.All(s => s.StartsWith("Nieuw", StringComparison.Ordinal)), $"maand {maand}: {string.Join(" / ", voorstel.Signalen.Where(s => !s.StartsWith("Nieuw", StringComparison.Ordinal)))}");
            Assert.Equal(aantal * 2, voorstel.Koppelingen.Count);
            Assert.Equal(maand == 1 ? 0 : 10, voorstel.Koppelingen.Count(k => k.Bron == Loonkoppelbron.Nieuw));

            var keuzes = voorstel.Koppelingen.Select(k => (k.Regel, Rekening: k.Rekening ?? (k.Regel.Bedrag > 0 ? "4000" : "1800"))).ToList();
            Assert.True((await dienst.Onthoud(wie, id, keuzes)).IsGelukt);
            var post = Loonkoppelingen.Boeking("MEM", new DateOnly(2026, maand, 1).AddMonths(1).AddDays(-1), $"Loonjournaal {maand}", keuzes.Select(k => (k.Rekening, k.Regel)))
                with
            { Boekstuk = $"2026-L{maand:00}" };
            var geboekt = await grootboek.Boek(wie, id, post);
            Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        }

        Meet("12 loonjournaals (105-160 regels ×2) inlezen en boeken", klok);
        Assert.Equal(2 * (100 + (12 * 5)), (await dienst.Koppelingen(wie, id)).Count);
    }
}
