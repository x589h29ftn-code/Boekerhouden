using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>Btw privégebruik: uit de rekening privégebruik in rubriek 1d, en de controle tegen de vervoermiddelen.</summary>
public sealed class PrivegebruikTests
{
    private static readonly Rekeningschema Ovpa = new(Rekeningschemas.Rekeningen(Schemakeuze.Ovpa, Ondernemingsvorm.BV));

    private static Vervoermiddel Auto(Privegebruik p, decimal? catalogus = 50_000m, DateOnly? aanschaf = null) =>
        new(Guid.NewGuid(), 1, "Personenauto", aanschaf ?? new(2022, 3, 1), aanschaf ?? new(2022, 3, 1), null, p, catalogus);

    [Fact]
    public void Btw_privegebruik_zonder_code_telt_in_1d_niet_de_betaling_of_overboeking()
    {
        Assert.True(Btwaangifte.IsPrivegebruikrekening(Ovpa.Zoek("1804")!));
        Assert.False(Btwaangifte.IsPrivegebruikrekening(Ovpa.Zoek("1800")!));
        Journaalpost[] posten =
        [
            new("MEM", "2026-0010", new(2026, 12, 31), "Btw privégebruik auto", [new("4481", 1_350m), new("1804", -1_350m)]),
            new("MEM", "2026-0011", new(2026, 12, 31), "Overboeken naar afgedragen", [new("1804", 1_350m), new("1820", -1_350m)]),
        ];

        var aangifte = Btwaangifte.Bereken(posten, Ovpa, BtwCodes.Standaard, new(2026, 10, 1), new(2026, 12, 31));

        Assert.Equal(1_350m, aangifte.Rubriek("1d").Btw);
        Assert.Equal(1_350m, aangifte.TeBetalen);
        // De aansluiting telt het niet dubbel: geen verschil.
        Assert.Equal(0m, Btwaansluiting.Bereken(posten, Ovpa, BtwCodes.Standaard, new(2026, 1, 1), new(2026, 12, 31)).Verschil);
    }

    [Fact]
    public void Vervoermiddel_forfait_en_maanden()
    {
        Assert.True(Privegebruikcontrole.IsVervoermiddel(Ovpa.Zoek("0250"), "Volkswagen Golf"));
        Assert.True(Privegebruikcontrole.IsVervoermiddel(null, "Auto kenteken AB-123-C"));
        Assert.False(Privegebruikcontrole.IsVervoermiddel(Ovpa.Zoek("0230"), "Bureau"));
        Assert.False(Privegebruikcontrole.IsVervoermiddel(null, "Automatisering server"));

        // 2,7% van 50.000; vanaf het vijfde jaar na 2022 (2027) 1,5%; in het jaar van aanschaf naar rato.
        Assert.Equal(1_350m, Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaForfait), 2026));
        Assert.Equal(750m, Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaForfait), 2027));
        Assert.Equal(1_125m, Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaForfait), 2022));
        Assert.Null(Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaWerkelijk), 2026));
    }

    [Fact]
    public void Signalen_bij_ontbrekende_of_afwijkende_btw_privegebruik()
    {
        Assert.Contains("geen btw privégebruik geboekt in 2026 (forfait 2,7%: 1.350,00)",
            Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfait)], 0m)).Tekst, StringComparison.Ordinal);
        Assert.Contains("Is dat juist?", Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.Onbekend)], 0m)).Tekst, StringComparison.Ordinal);
        Assert.StartsWith("Nieuwe auto", Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.Onbekend, aanschaf: new(2026, 5, 1))], 0m)).Tekst, StringComparison.Ordinal);
        Assert.Empty(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfait)], 1_350m));
        Assert.Empty(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.NeeRittenregistratie)], 0m));
        Assert.Contains("volgens het forfait 1.350,00", Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfait)], 900m)).Tekst, StringComparison.Ordinal);
        Assert.Contains("geen vervoermiddel met privégebruik", Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.NeeVerklaring)], 900m)).Tekst, StringComparison.Ordinal);
        Assert.Contains("zonder cataloguswaarde", Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfait, catalogus: null)], 500m)[0].Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Zonder_btw_aftrek_bij_aankoop_is_het_forfait_vanaf_het_begin_anderhalf_procent()
    {
        // Met btw-aftrek: 2,7% in de eerste vijf jaar (met het jaar van ingebruikname), daarna 1,5%.
        Assert.Equal(2.7m, Privegebruikcontrole.Forfaitpercentage(Privegebruik.JaForfait, new(2022, 6, 1), 2026));
        Assert.Equal(1.5m, Privegebruikcontrole.Forfaitpercentage(Privegebruik.JaForfait, new(2022, 6, 1), 2027));
        Assert.Equal("2,7%: btw afgetrokken bij aankoop; 1,5% vanaf 2027", Privegebruikcontrole.Forfaitreden(Privegebruik.JaForfait, new(2022, 6, 1), 2026));
        Assert.Equal("1,5%: vanaf het vijfde jaar na ingebruikname (2022)", Privegebruikcontrole.Forfaitreden(Privegebruik.JaForfait, new(2022, 6, 1), 2027));

        // Zonder btw-aftrek (particulier, marge): altijd 1,5%.
        Assert.Equal(1.5m, Privegebruikcontrole.Forfaitpercentage(Privegebruik.JaForfaitZonderBtw, new(2026, 1, 1), 2026));
        Assert.Equal("1,5%: bij aankoop geen btw afgetrokken", Privegebruikcontrole.Forfaitreden(Privegebruik.JaForfaitZonderBtw, new(2026, 1, 1), 2026));
        Assert.Equal(750m, Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaForfaitZonderBtw), 2026));
        Assert.Equal(1_350m, Privegebruikcontrole.Verwacht(Auto(Privegebruik.JaForfait), 2026));

        // Hij telt als ja en als forfait, niet als nee; en staat bij de andere ja-keuzes.
        Assert.True(Privegebruikcontrole.IsJa(Privegebruik.JaForfaitZonderBtw) && Privegebruikcontrole.IsForfait(Privegebruik.JaForfaitZonderBtw));
        Assert.False(Privegebruikcontrole.IsNee(Privegebruik.JaForfaitZonderBtw));
        Assert.Equal(Enum.GetValues<Privegebruik>().Order(), Privegebruikcontrole.Keuzes.Order());
        Assert.Equal(2, Privegebruikcontrole.Keuzes.ToList().IndexOf(Privegebruik.JaForfaitZonderBtw));
        Assert.Contains("zonder cataloguswaarde", Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfaitZonderBtw, catalogus: null)], 500m)[0].Tekst, StringComparison.Ordinal);
        Assert.Contains("forfait 1,5%: 750,00", Assert.Single(Privegebruikcontrole.Signalen(2026, [Auto(Privegebruik.JaForfaitZonderBtw)], 0m)).Tekst, StringComparison.Ordinal);
    }
}
