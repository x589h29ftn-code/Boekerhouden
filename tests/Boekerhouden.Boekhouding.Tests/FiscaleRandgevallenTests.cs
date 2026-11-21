using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Vpb;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Fiscale randgevallen met de hand uitgerekend (2025): de vennootschapsbelasting rond de schijfgrens en met de
/// afronding op € 5, de kleinschaligheidsinvesteringsaftrek op elke grens, de gemengde kosten met en zonder loonsom,
/// en de verliesverrekening boven het miljoen. Elk antwoord staat hier vast, los van de code die het berekent.
/// </summary>
public sealed class FiscaleRandgevallenTests
{
    private static readonly Rekeningschema Schema = new(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

    /// <summary>De vpb over een resultaat (met de hand ingevuld), zonder investeringen of andere posten.</summary>
    private static Fiscalepositie Vpb(decimal resultaat, decimal verlies = 0m) =>
        Fiscalepositie.Bereken(new Fiscalegegevens(2025, Schema, new Dictionary<string, decimal>(), 0m, 0m),
            new Dictionary<string, decimal> { ["vpb.resultaat"] = resultaat, ["vpb.verlies"] = verlies });

    [Theory]
    [InlineData(-5_000, 0)] // verlies: nihil
    [InlineData(0, 0)]
    [InlineData(4.99, 0)] // naar beneden op € 5: grondslag 0
    [InlineData(5, 0)] // 19% van 5 = 0,95; per schijf naar beneden op hele euro's
    [InlineData(10, 1)] // 1,90 -> 1
    [InlineData(200_000, 38_000)] // precies de schijfgrens
    [InlineData(200_004.99, 38_000)] // grondslag 200.000
    [InlineData(200_005, 38_001)] // 25,8% van 5 = 1,29 -> 1
    [InlineData(250_000, 50_900)] // 38.000 + 12.900
    [InlineData(1_000_000, 244_400)] // 38.000 + 206.400
    public void Vennootschapsbelasting_2025(decimal resultaat, decimal verwacht)
    {
        Assert.Equal(verwacht, Vpb(resultaat).Vennootschapsbelasting);
    }

    [Fact]
    public void Verliesverrekening_2025_boven_een_miljoen_tot_de_helft_en_onder_een_miljoen_helemaal()
    {
        // Winst 3 mln, verlies 5 mln: 1 mln + helft van 2 mln = 2 mln te verrekenen; belastbaar 1 mln.
        Assert.Equal(1_000_000m, Vpb(3_000_000m, 5_000_000m).Belastbaar);

        // Winst 800.000, verlies 300.000: helemaal te verrekenen.
        Assert.Equal(500_000m, Vpb(800_000m, 300_000m).Belastbaar);

        // Verlies groter dan de winst onder een miljoen: belastbaar nul, geen vpb.
        var p = Vpb(400_000m, 900_000m);
        Assert.Equal((0m, 0m), (p.Belastbaar, p.Vennootschapsbelasting));
    }

    [Theory]
    [InlineData(2_900, 0)] // tot en met de ondergrens: niets
    [InlineData(2_901, 812)] // 28% = 812,28 -> 812
    [InlineData(70_602, 19_768)] // 28% = 19.768,56 -> 19.768
    [InlineData(70_603, 19_769)] // vast bedrag
    [InlineData(130_744, 19_769)]
    [InlineData(130_745, 19_768)] // 19.769 - 7,56% van 1 = 19.768,92 -> 19.768
    [InlineData(392_230, 0)] // 19.769 - 7,56% van 261.486 = 0,66 -> 0
    [InlineData(392_231, 0)] // boven de bovengrens: niets
    public void Kleinschaligheidsinvesteringsaftrek_2025(decimal investeringen, decimal verwacht) =>
        Assert.Equal(verwacht, Vpbtarieven.Voor(2025).Kia(investeringen));

    [Theory]
    [InlineData(-100, 0, 0)] // geen kosten (of een opbrengst): niets
    [InlineData(10_000, 0, 2_650)] // 26,5% = 2.650, minder dan de drempel 5.700
    [InlineData(30_000, 0, 5_700)] // 26,5% = 7.950; de drempel 5.700 is voordeliger
    [InlineData(30_000, 2_000_000, 7_950)] // drempel 0,4% van de loonsom = 8.000; 26,5% = 7.950 is voordeliger
    [InlineData(4_000, 0, 1_060)] // 26,5% = 1.060, minder dan de kosten zelf
    public void Gemengde_kosten_2025(decimal kosten, decimal loonsom, decimal verwacht) =>
        Assert.Equal(verwacht, Vpbtarieven.Voor(2025).NietAftrekbaar(kosten, loonsom));
}
