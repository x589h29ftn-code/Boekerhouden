using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Vpb;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De fiscale positie van een BV: correcties, beperkt aftrekbare kosten, investeringsaftrek, tarieven per jaar.</summary>
public sealed class FiscalepositieTests
{
    private static readonly Rekeningschema Schema = new([
        .. StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV),
        new Rekening("4460", "Boetes", Rekeningsoort.Resultaat, "WBedAutRoa"),
        new Rekening("4530", "Representatiekosten", Rekeningsoort.Resultaat, "WBedVkkRea"),
        new Rekening("9500", "Vennootschapsbelasting", Rekeningsoort.Resultaat, "WBel"),
    ]);

    private static Fiscalegegevens Gegevens(int jaar, params (string Rekening, decimal Saldo)[] saldi) =>
        new(jaar, Schema, saldi.ToDictionary(s => s.Rekening, s => s.Saldo), Betaald: 15_000m, Investeringen: 10_000m);

    private static readonly Fiscalegegevens Winst = Gegevens(2026, ("8000", -100_000m), ("4410", 4_000m), ("4530", 6_000m), ("4460", 500m), ("9500", 5_000m));

    private static Fiscaleregel Regel(Fiscalepositie p, string tekst) => p.Regels.First(r => r.Omschrijving.StartsWith(tekst, StringComparison.Ordinal));

    [Fact]
    public void Indeling_automatisch_op_rgs_en_naam_of_zoals_ingesteld()
    {
        Assert.Equal(Fiscaalsoort.BeperktAftrekbaar, Fiscaleindeling.Van(Schema.Zoek("4410")!));
        Assert.Equal(Fiscaalsoort.BeperktAftrekbaar, Fiscaleindeling.Van(Schema.Zoek("4530")!));
        Assert.Equal(Fiscaalsoort.NietAftrekbaar, Fiscaleindeling.Van(Schema.Zoek("4460")!));
        Assert.Equal(Fiscaalsoort.Last, Fiscaleindeling.Van(Schema.Zoek("9500")!));
        Assert.Equal(Fiscaalsoort.Balans, Fiscaleindeling.Van(Schema.Zoek("1550")!));
        Assert.Equal(Fiscaalsoort.Gewoon, Fiscaleindeling.Van(Schema.Zoek("4410")! with { Fiscaal = Fiscaalsoort.Gewoon }));
        Assert.Equal(Fiscaalsoort.Gewoon, Fiscaleindeling.Van(new Rekening("4400", "Reclame", Rekeningsoort.Resultaat, "WBedVkkRea")));
        Assert.Equal(Fiscaalsoort.Gewoon, Fiscaleindeling.Van(new Rekening("4092", "Representatiekostenvergoeding", Rekeningsoort.Resultaat, "WPer")));
    }

    [Fact]
    public void Van_resultaat_voor_belastingen_naar_vennootschapsbelasting_en_te_betalen()
    {
        var p = Fiscalepositie.Bereken(Winst);

        // Resultaat zonder de vpb-last: 100.000 - 4.000 - 6.000 - 500.
        Assert.Equal(89_500m, Regel(p, "Resultaat voor belastingen").Totaal!.Bedrag);
        Assert.Equal(500m, Regel(p, "Niet aftrekbare kosten").Bedrag!.Bedrag);

        // Gemengde kosten 10.000: 26,5% = 2.650, minder dan de drempel van 5.700.
        Assert.Equal(2_650m, Regel(p, "Beperkt aftrekbare kosten").Bedrag!.Bedrag);
        Assert.Equal(["4410", "4530"], p.Beperkt.Select(r => r.Nummer));

        // KIA 2026 over 10.000: 28% = 2.800.
        Assert.Equal(2_800m, Regel(p, "Investeringsaftrek").Bedrag!.Bedrag);
        Assert.Equal(89_850m, Regel(p, "Fiscaal resultaat").Totaal!.Bedrag);
        Assert.Equal(89_850m, p.Belastbaar);

        // 19% van 89.850 = 17.071,50, naar beneden afgerond; min 15.000 voorlopige aanslag.
        Assert.Equal(17_071m, p.Vennootschapsbelasting);
        Assert.Equal(2_071m, p.TeBetalen);
        Assert.Equal("19% tot en met 200.000", p.Regels.Single(r => r.Omschrijving.StartsWith("19%", StringComparison.Ordinal)).Omschrijving);
    }

    [Fact]
    public void Verlies_geeft_nihil()
    {
        var p = Fiscalepositie.Bereken(Gegevens(2026, ("8000", -1_000m), ("4410", 5_000m)));

        Assert.True(p.Belastbaar < 0);
        Assert.Equal(0m, p.Vennootschapsbelasting);
        Assert.Contains(p.Regels, r => r.Omschrijving == "Vennootschapsbelasting 2026: nihil (verlies)");
        Assert.Equal(-15_000m, p.TeBetalen);
    }

    [Fact]
    public void Aanpassingen_en_verliesverrekening_rekenen_door()
    {
        var p = Fiscalepositie.Bereken(Winst, new Dictionary<string, decimal>
        {
            ["vpb.af.kia"] = 0m,
            ["vpb.verlies"] = 50_000m,
            ["vpb.tarief.laag"] = 20m,
            ["vpb.betaald"] = 0m,
        });

        Assert.Equal((0m, 2_800m, true), (Regel(p, "Investeringsaftrek").Bedrag!.Bedrag, Regel(p, "Investeringsaftrek").Bedrag!.Berekend, Regel(p, "Investeringsaftrek").Bedrag!.IsAangepast));
        Assert.Equal(50_000m, Regel(p, "Af: verrekening verliezen").Totaal!.Bedrag);
        Assert.Equal(42_650m, p.Belastbaar);
        Assert.Equal(8_530m, p.Vennootschapsbelasting);
        Assert.Equal(20m, p.Tarieven.TariefLaag);
        Assert.Contains("vpb.tarief.hoog", p.Aanpasbaar);
        Assert.DoesNotContain("vpb.fiscaal", p.Aanpasbaar);
    }

    [Fact]
    public void Tarieven_per_jaar_drempel_en_kia()
    {
        Assert.Equal((15m, 395_000m), (Vpbtarieven.Voor(2022).TariefLaag, Vpbtarieven.Voor(2022).Schijfgrens));
        Assert.True(Vpbtarieven.Voor(2026).Bekend);
        Assert.False(Vpbtarieven.Voor(2030).Bekend);
        Assert.Equal(19m, Vpbtarieven.Voor(2030).TariefLaag);

        var t = Vpbtarieven.Voor(2024);
        Assert.Equal(5_600m, t.NietAftrekbaar(40_000m, 0m));
        Assert.Equal(8_000m, t.NietAftrekbaar(40_000m, 2_000_000m));
        Assert.Equal((0m, 19_535m, 0m), (t.Kia(2_800m), t.Kia(100_000m), t.Kia(400_000m)));
        Assert.Equal(19_535m - 756m, t.Kia(139_194m));
        Assert.Equal((38_000m, 25_800m), t.Belasting(300_000m));
    }
}
