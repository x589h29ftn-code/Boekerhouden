namespace Boekerhouden.Boekhouding.Vpb;

/// <summary>
/// De tarieven en grenzen van de vennootschapsbelasting voor één jaar: de schijven, de aftrekbeperking voor gemengde
/// kosten (percentage of drempel) en de kleinschaligheidsinvesteringsaftrek (KIA). Percentages als getal (19 = 19%).
/// Meegeleverd per jaar; <see cref="Bekend"/> is onwaar als het jaar niet in de tabel staat en de tarieven van het
/// dichtstbijzijnde jaar zijn genomen. Per administratie en jaar is elk getal aan te passen.
/// </summary>
public sealed record Vpbtarieven(
    int Jaar,
    decimal Schijfgrens,
    decimal TariefLaag,
    decimal TariefHoog,
    decimal NietAftrekbaarPercentage,
    decimal Drempel,
    decimal DrempelLoonsomPercentage,
    decimal KiaOndergrens,
    decimal KiaGrens1,
    decimal KiaPercentage,
    decimal KiaVast,
    decimal KiaGrens2,
    decimal KiaAfbouwPercentage,
    decimal KiaBovengrens,
    bool Bekend = true)
{
    // Vpb-tarieven: 2019 19/25%, 2020 16,5/25%, 2021 15% tot 245.000, 2022 15% tot 395.000, vanaf 2023 19% tot 200.000 en 25,8%.
    // Drempel gemengde kosten en KIA volgens de Belastingdienst; KIA en drempel vóór 2024 zijn die van 2024 (controleren).
    private static readonly Vpbtarieven[] Tabel =
    [
        new(2019, 200_000m, 19m, 25m, 26.5m, 5_600m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m, Bekend: false),
        new(2020, 200_000m, 16.5m, 25m, 26.5m, 5_600m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m, Bekend: false),
        new(2021, 245_000m, 15m, 25m, 26.5m, 5_600m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m, Bekend: false),
        new(2022, 395_000m, 15m, 25.8m, 26.5m, 5_600m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m, Bekend: false),
        new(2023, 200_000m, 19m, 25.8m, 26.5m, 5_500m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m, Bekend: false),
        new(2024, 200_000m, 19m, 25.8m, 26.5m, 5_600m, 0.4m, 2_800m, 69_765m, 28m, 19_535m, 129_194m, 7.56m, 387_580m),
        new(2025, 200_000m, 19m, 25.8m, 26.5m, 5_700m, 0.4m, 2_900m, 70_602m, 28m, 19_769m, 130_744m, 7.56m, 392_230m),
        new(2026, 200_000m, 19m, 25.8m, 26.5m, 5_700m, 0.4m, 2_900m, 71_683m, 28m, 20_072m, 132_746m, 7.56m, 398_236m),
    ];

    /// <summary>De sleutels waaronder elk getal per administratie en jaar is aan te passen, met de naam.</summary>
    public static IReadOnlyList<(string Sleutel, string Naam, bool Percentage)> Velden { get; } =
    [
        ("vpb.tarief.schijfgrens", "Grens eerste schijf", false),
        ("vpb.tarief.laag", "Tarief eerste schijf", true),
        ("vpb.tarief.hoog", "Tarief tweede schijf", true),
        ("vpb.tarief.nietaftrekbaar", "Gemengde kosten: niet aftrekbaar deel", true),
        ("vpb.tarief.drempel", "Gemengde kosten: drempel", false),
        ("vpb.tarief.loonsom", "Gemengde kosten: drempel als deel van de loonsom", true),
        ("vpb.kia.ondergrens", "KIA: geen aftrek tot en met", false),
        ("vpb.kia.grens1", "KIA: percentage tot en met", false),
        ("vpb.kia.percentage", "KIA: percentage", true),
        ("vpb.kia.vast", "KIA: vast bedrag", false),
        ("vpb.kia.grens2", "KIA: vast bedrag tot en met", false),
        ("vpb.kia.afbouw", "KIA: afbouw boven die grens", true),
        ("vpb.kia.bovengrens", "KIA: geen aftrek boven", false),
    ];

    /// <summary>De tarieven van een jaar (of van het dichtstbijzijnde jaar in de tabel, dan niet <see cref="Bekend"/>).</summary>
    public static Vpbtarieven Voor(int jaar)
    {
        if (Tabel.FirstOrDefault(t => t.Jaar == jaar) is { } t)
        {
            return t;
        }

        var dichtst = jaar < Tabel[0].Jaar ? Tabel[0] : Tabel[^1];
        return dichtst with { Jaar = jaar, Bekend = false };
    }

    public decimal Waarde(string sleutel) => sleutel switch
    {
        "vpb.tarief.schijfgrens" => Schijfgrens,
        "vpb.tarief.laag" => TariefLaag,
        "vpb.tarief.hoog" => TariefHoog,
        "vpb.tarief.nietaftrekbaar" => NietAftrekbaarPercentage,
        "vpb.tarief.drempel" => Drempel,
        "vpb.tarief.loonsom" => DrempelLoonsomPercentage,
        "vpb.kia.ondergrens" => KiaOndergrens,
        "vpb.kia.grens1" => KiaGrens1,
        "vpb.kia.percentage" => KiaPercentage,
        "vpb.kia.vast" => KiaVast,
        "vpb.kia.grens2" => KiaGrens2,
        "vpb.kia.afbouw" => KiaAfbouwPercentage,
        "vpb.kia.bovengrens" => KiaBovengrens,
        _ => throw new ArgumentOutOfRangeException(nameof(sleutel), sleutel, "Onbekend tarief."),
    };

    /// <summary>De tarieven met de aangepaste getallen (per sleutel uit <see cref="Velden"/>).</summary>
    public Vpbtarieven Met(IReadOnlyDictionary<string, decimal> aanpassingen)
    {
        ArgumentNullException.ThrowIfNull(aanpassingen);
        decimal W(string sleutel) => aanpassingen.TryGetValue(sleutel, out var b) ? b : Waarde(sleutel);
        return this with
        {
            Schijfgrens = W("vpb.tarief.schijfgrens"),
            TariefLaag = W("vpb.tarief.laag"),
            TariefHoog = W("vpb.tarief.hoog"),
            NietAftrekbaarPercentage = W("vpb.tarief.nietaftrekbaar"),
            Drempel = W("vpb.tarief.drempel"),
            DrempelLoonsomPercentage = W("vpb.tarief.loonsom"),
            KiaOndergrens = W("vpb.kia.ondergrens"),
            KiaGrens1 = W("vpb.kia.grens1"),
            KiaPercentage = W("vpb.kia.percentage"),
            KiaVast = W("vpb.kia.vast"),
            KiaGrens2 = W("vpb.kia.grens2"),
            KiaAfbouwPercentage = W("vpb.kia.afbouw"),
            KiaBovengrens = W("vpb.kia.bovengrens"),
        };
    }

    /// <summary>De kleinschaligheidsinvesteringsaftrek bij een totaal aan investeringen in het jaar.</summary>
    public decimal Kia(decimal investeringen) => investeringen switch
    {
        _ when investeringen <= KiaOndergrens => 0m,
        _ when investeringen <= KiaGrens1 => Math.Round(investeringen * KiaPercentage / 100m, 0, MidpointRounding.ToZero),
        _ when investeringen <= KiaGrens2 => KiaVast,
        _ when investeringen <= KiaBovengrens => Math.Max(0m, Math.Round(KiaVast - ((investeringen - KiaGrens2) * KiaAfbouwPercentage / 100m), 0, MidpointRounding.ToZero)),
        _ => 0m,
    };

    /// <summary>
    /// Het niet aftrekbare deel van de gemengde kosten: het percentage van de kosten, of (als dat minder is) de kosten
    /// tot de drempel (het vaste bedrag of, als dat hoger is, het percentage van de loonsom). De voordeligste.
    /// </summary>
    public decimal NietAftrekbaar(decimal kosten, decimal loonsom)
    {
        if (kosten <= 0)
        {
            return 0m;
        }

        var drempel = Math.Max(Drempel, Math.Round(loonsom * DrempelLoonsomPercentage / 100m, 2));
        return Math.Min(Math.Round(kosten * NietAftrekbaarPercentage / 100m, 2), Math.Min(kosten, drempel));
    }

    /// <summary>De vennootschapsbelasting over het belastbare bedrag, per schijf naar beneden afgerond op hele euro's; nihil bij verlies.</summary>
    public (decimal Laag, decimal Hoog) Belasting(decimal grondslag)
    {
        if (grondslag <= 0)
        {
            return (0m, 0m);
        }

        var laag = Math.Floor(Math.Min(grondslag, Schijfgrens) * TariefLaag / 100m);
        var hoog = Math.Floor(Math.Max(0m, grondslag - Schijfgrens) * TariefHoog / 100m);
        return (laag, hoog);
    }
}
