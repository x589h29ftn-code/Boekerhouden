using System.Diagnostics;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Loon;
using Boekerhouden.Boekhouding.Vpb;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Stresstest van de btw (aangifte per periode, rondrekening, jaaropstelling, aansluiting, privégebruik), de fiscale
/// positie (vpb) en de loonkoppelingen: tienduizenden boekingen met grote bedragen en creditnota's, willekeurige
/// resultaten van min tot plus een miljard, grenzen van de tarieven, en honderden rekeningen uit een salarispakket.
/// </summary>
public sealed class BtwVpbStressTests
{
    private static readonly Rekeningschema Schema = new([
        .. Rekeningschemas.Rekeningen(Schemakeuze.Ovpa, Ondernemingsvorm.BV),
    ]);

    private static readonly IReadOnlyList<BtwCode> Codes = BtwCodes.Standaard;

    /// <summary>
    /// Een jaar met <paramref name="aantal"/> boekingen: verkopen en inkopen (hoog en laag tarief), creditnota's (negatief),
    /// bedragen van centen tot honderd miljoen, betalingen aan de Belastingdienst en de btw privégebruik aan het eind.
    /// </summary>
    private static List<Journaalpost> Jaar(int aantal, int zaad = 42)
    {
        var willekeurig = new Random(zaad);
        var posten = new List<Journaalpost>();
        for (var i = 0; i < aantal; i++)
        {
            var datum = new DateOnly(2026, 1, 1).AddDays(willekeurig.Next(365));
            var grootte = willekeurig.Next(10) switch { 0 => 100_000_000m, < 3 => 100_000m, _ => 1_000m };
            var bedrag = Math.Round((decimal)willekeurig.NextDouble() * grootte, 2) + 0.01m;
            var credit = willekeurig.Next(8) == 0 ? -1 : 1;
            var laag = willekeurig.Next(4) == 0;
            var btw = Math.Round(bedrag * (laag ? 0.09m : 0.21m), 2) * credit;
            var netto = bedrag * credit;
            posten.Add(willekeurig.Next(2) == 0
                ? new Journaalpost("VRK", $"V{i}", datum, "Verkoop",
                    [new("8000", -netto, BtwCode: laag ? "V9" : "V21"), new(laag ? "1803" : "1800", -btw, BtwCode: laag ? "V9" : "V21"), new("1300", netto + btw)])
                : new Journaalpost("INK", $"I{i}", datum, "Inkoop",
                    [new("4300", netto, BtwCode: laag ? "I9" : "I21"), new(laag ? "1807" : "1805", btw, BtwCode: laag ? "I9" : "I21"), new("1600", -(netto + btw))]));
        }

        posten.Add(new Journaalpost("MEM", "PRIVE", new(2026, 12, 31), "Btw privégebruik auto", [new("4481", 1_215m), new("1804", -1_215m)]));
        posten.Add(new Journaalpost("BNK", "BETAAL", new(2026, 4, 30), "Btw 1e kwartaal", [new("1820", 5_000m), new("1100", -5_000m)]));
        return posten;
    }

    [Fact]
    public void Btw_tienduizenden_boekingen_alles_sluit_per_kwartaal_en_per_maand()
    {
        var posten = Jaar(20_000);
        var klok = Stopwatch.StartNew();

        foreach (var frequentie in new[] { Aangiftefrequentie.Kwartaal, Aangiftefrequentie.Maand, Aangiftefrequentie.Jaar })
        {
            var jaar = Btwjaar.Maak(2026, frequentie, posten, Schema, Codes, []);
            var rond = Btwrondrekening.Maak(jaar, posten, Schema, Codes);

            // De perioden tellen op tot het jaar; niets aangegeven, dus geen afwijking.
            Assert.Equal(jaar.Jaaraangifte.TeBetalen, jaar.Perioden.Sum(p => p.Berekend.TeBetalen));
            Assert.Equal(jaar.Jaaraangifte.TeBetalen, jaar.NietAangegeven);
            Assert.Equal(0m, jaar.Rest);
            var vijfC = rond.Regels.Single(r => r.Code == "5c");
            Assert.Equal((vijfC.Administratie, 0m), (vijfC.Totaal, vijfC.Afwijking));

            // Privégebruik (zonder btw-code op 1804) in 1d.
            Assert.Equal(1_215m, jaar.Jaaraangifte.Rubriek("1d").Btw);

            // De jaaropstelling: omzet sluit op het grootboek, 5c gelijk, en elke bron klikt door naar regels die optellen.
            var opstelling = rond.Jaaropstelling();
            Assert.Equal(0m, opstelling.Single(r => r.Omschrijving == "Overige verschillen").Omzet!.Bedrag);
            Assert.Equal(jaar.Jaaraangifte.TeBetalen, opstelling.Single(r => r.Code == "5c").Btw!.Bedrag);
            Assert.Equal(jaar.Jaaraangifte.Rubriek("1a").Btw, rond.Bronnen(new(Rondrekeningbron.Rubriek, "1a", Btwpost.Btw), new(2026, 1, 1), new(2026, 12, 31)).Sum(b => b.Bedrag));
        }

        // De aansluiting met de btw-rekeningen sluit, ook met de betaling en het privégebruik.
        Assert.Equal(0m, Btwaansluiting.Bereken(posten, Schema, Codes, new(2026, 1, 1), new(2026, 12, 31)).Verschil);
        Assert.True(klok.ElapsedMilliseconds < 30_000, $"{klok.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Btw_jaaropstelling_met_extreme_aanpassingen_rekent_door()
    {
        var posten = Jaar(500);
        var jaar = Btwjaar.Maak(2026, Aangiftefrequentie.Kwartaal, posten, Schema, Codes, []);
        var rond = Btwrondrekening.Maak(jaar, posten, Schema, Codes);
        var aanpassingen = rond.Aanpasbaar.Distinct().ToDictionary(s => s, s => s.StartsWith("afdracht", StringComparison.Ordinal) ? 999_999_999.99m : -123_456.78m);

        var opstelling = rond.Jaaropstelling(aanpassingen);

        // Alles aangepast: de totalen volgen de aangepaste cellen exact.
        var btwCellen = opstelling.Where(r => r.Soort == Rondrekeningsoort.Regel && r.Btw?.Sleutel is not null).Select(r => r.Btw!.Bedrag!.Value).Sum();
        Assert.Equal(btwCellen, opstelling.Single(r => r.Code == "5a").Btw!.Bedrag);
        Assert.All(opstelling.SelectMany(r => new[] { r.Omzet, r.Btw }).Where(c => c?.Sleutel is not null), c => Assert.True(c!.IsAangepast));
        Assert.Equal(4 * 999_999_999.99m, opstelling.Single(r => r.Omschrijving == "Totaal aangegeven").Btw!.Bedrag);
        Assert.DoesNotContain(opstelling, r => r.Omschrijving == "Nog niet aangegeven");
    }

    [Fact]
    public void Privegebruik_honderd_autos_door_de_jaren_heen()
    {
        var autos = Enumerable.Range(0, 100).Select(i => new Vervoermiddel(Guid.NewGuid(), i + 1, $"Auto {i + 1}",
            new DateOnly(2018 + (i % 9), (i % 12) + 1, 1), new DateOnly(2018 + (i % 9), (i % 12) + 1, 1),
            i % 10 == 0 ? new DateOnly(2026, 6, 30) : null, (Privegebruik)(i % 7), i % 4 == 0 ? null : 20_000m + (i * 1_000m))).ToList();

        foreach (var jaar in Enumerable.Range(2018, 10))
        {
            foreach (var v in autos)
            {
                var maanden = Privegebruikcontrole.Maanden(v, jaar);
                Assert.InRange(maanden, 0, 12);
                if (Privegebruikcontrole.Verwacht(v, jaar) is { } verwacht)
                {
                    Assert.InRange(verwacht, 0m, v.Cataloguswaarde!.Value * 0.027m);
                }
            }

            var signalen = Privegebruikcontrole.Signalen(jaar, autos, jaar == 2026 ? 1_000m : 0m);
            Assert.All(signalen, s => Assert.False(string.IsNullOrWhiteSpace(s.Tekst)));
        }
    }

    [Fact]
    public void Vpb_duizenden_willekeurige_resultaten_nooit_negatief_en_oplopend()
    {
        var willekeurig = new Random(7);
        var vorige = (Grondslag: decimal.MinValue, Vpb: 0m);
        var resultaten = Enumerable.Range(0, 5_000).Select(_ => Math.Round(((decimal)willekeurig.NextDouble() * 2_000_000_000m) - 1_000_000_000m, 2))
            .Append(0m).Append(0.01m).Append(-0.01m).Append(200_000m).Append(200_004.99m).Append(200_005m).Order().ToList();

        foreach (var resultaat in resultaten)
        {
            var positie = Fiscalepositie.Bereken(Gegevens(2026, resultaat, beperkt: Math.Abs(resultaat) / 100m, betaald: 1_000m, investeringen: 50_000m));

            Assert.True(positie.Vennootschapsbelasting >= 0, $"{resultaat}");
            if (positie.Belastbaar <= 0)
            {
                Assert.Equal(0m, positie.Vennootschapsbelasting);
                Assert.Contains("nihil (verlies)", positie.Regels.Single(r => r.Soort == Fiscaleregelsoort.Totaal && r.Omschrijving.StartsWith("Vennootschapsbelasting", StringComparison.Ordinal)).Omschrijving, StringComparison.Ordinal);
            }

            // Hoger belastbaar bedrag geeft nooit minder belasting.
            Assert.True(positie.Belastbaar < vorige.Grondslag || positie.Vennootschapsbelasting >= vorige.Vpb, $"{resultaat}");
            vorige = (positie.Belastbaar, positie.Vennootschapsbelasting);
            Assert.Equal(positie.Vennootschapsbelasting - 1_000m, positie.TeBetalen);
        }

        // Een miljard winst: 19% over 200.000 en 25,8% over de rest (per schijf naar beneden afgerond).
        var groot = Fiscalepositie.Bereken(Gegevens(2026, 1_000_000_000m, 0m, 0m, 0m));
        Assert.Equal(38_000m + Math.Floor((Math.Floor(groot.Belastbaar / 5m) * 5m - 200_000m) * 0.258m), groot.Vennootschapsbelasting);
    }

    [Fact]
    public void Vpb_grenzen_van_kia_en_gemengde_kosten_voor_elk_jaar()
    {
        foreach (var jaar in Enumerable.Range(2015, 21))
        {
            var t = Vpbtarieven.Voor(jaar);
            var vorige = -1m;
            foreach (var investering in new[] { 0m, t.KiaOndergrens, t.KiaOndergrens + 1, t.KiaGrens1, t.KiaGrens1 + 1, t.KiaGrens2, t.KiaGrens2 + 1, t.KiaBovengrens, t.KiaBovengrens + 1, 10_000_000m })
            {
                var kia = t.Kia(investering);
                Assert.InRange(kia, 0m, t.KiaVast);
                Assert.True(kia <= Math.Ceiling(investering * t.KiaPercentage / 100m), $"{jaar} {investering}");
                vorige = kia;
            }

            Assert.Equal(0m, t.Kia(t.KiaBovengrens + 1));
            Assert.Equal(t.KiaVast, t.Kia(t.KiaGrens2));

            // Gemengde kosten: nooit meer dan de kosten, nooit negatief (ook niet bij een creditboeking).
            foreach (var kosten in new[] { -50_000m, 0m, 1m, 5_000m, t.Drempel / (t.NietAftrekbaarPercentage / 100m), 1_000_000m })
            {
                foreach (var loonsom in new[] { 0m, 1_000_000m, 1_000_000_000m })
                {
                    var na = t.NietAftrekbaar(kosten, loonsom);
                    Assert.InRange(na, 0m, Math.Max(0m, kosten));
                }
            }
        }
    }

    [Fact]
    public void Vpb_aanpassingen_tarief_nul_en_verlies_groter_dan_winst()
    {
        var gegevens = Gegevens(2026, 500_000m, 10_000m, 0m, 0m);

        Assert.Equal(0m, Fiscalepositie.Bereken(gegevens, new Dictionary<string, decimal> { ["vpb.tarief.laag"] = 0m, ["vpb.tarief.hoog"] = 0m }).Vennootschapsbelasting);

        var verlies = Fiscalepositie.Bereken(gegevens, new Dictionary<string, decimal> { ["vpb.verlies"] = 10_000_000m });
        Assert.Equal((0m, 0m), (verlies.Belastbaar, verlies.Vennootschapsbelasting));

        // Negatief ingevulde verrekenbare verliezen tellen niet.
        Assert.Equal(Fiscalepositie.Bereken(gegevens).Belastbaar, Fiscalepositie.Bereken(gegevens, new Dictionary<string, decimal> { ["vpb.verlies"] = -1_000m }).Belastbaar);
    }

    [Fact]
    public void Loonkoppelingen_honderden_rekeningen_met_onthouden_en_nieuw()
    {
        var regels = Enumerable.Range(0, 400).Select(i => new Loonjournaalregel($"{9000 + i}", $"Looncomponent {i}", i % 2 == 0 ? 1_000m + (i / 2) : -(1_000m + (i / 2)))).ToList();
        var journaal = new Loonjournaal(null, null, null, regels, null, null);
        var onthouden = regels.Where((r, i) => i % 3 != 0).ToDictionary(r => r.Nummer, _ => "4000");
        var klok = Stopwatch.StartNew();

        var koppelingen = Loonkoppelingen.Stel(journaal, Schema, onthouden);

        Assert.True(klok.ElapsedMilliseconds < 5_000, $"{klok.ElapsedMilliseconds} ms");
        Assert.Equal(regels.Count - onthouden.Count, koppelingen.Count(k => k.Bron == Loonkoppelbron.Nieuw));
        Assert.All(koppelingen.Where(k => k.Bron == Loonkoppelbron.Nieuw), k => Assert.Null(k.Rekening));
        Assert.Equal(0m, Loonkoppelingen.Boeking("MEM", new(2026, 1, 31), "Loon", koppelingen.Select(k => (k.Rekening ?? "4000", k.Regel))).Regels.Sum(r => r.Bedrag));
    }

    private static Fiscalegegevens Gegevens(int jaar, decimal resultaat, decimal beperkt, decimal betaald, decimal investeringen) =>
        new(jaar, Schema, new Dictionary<string, decimal> { ["8000"] = -resultaat - beperkt, ["4530"] = beperkt }, betaald, investeringen);
}
