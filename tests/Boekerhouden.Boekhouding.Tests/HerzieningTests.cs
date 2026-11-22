using Boekerhouden.Boekhouding.Activa;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// De afschrijving wijzigen per datum: vanaf een maand een ander percentage per jaar of een andere restwaarde, en een
/// extra afschrijving op een datum; alleen vooruit, wat ervoor ligt blijft.
/// </summary>
public sealed class HerzieningTests
{
    private static Activum Laptop(decimal aanschaf = 1200m, decimal rest = 0m, int looptijd = 60, params Herziening[] herzieningen) =>
        new("Laptop", new(2026, 1, 5), aanschaf, rest, new(2026, 1, 1), looptijd, Afschrijvingsmethode.Lineair, "0200", "0210", "4800")
        {
            Herzieningen = herzieningen,
        };

    [Fact]
    public void Van_twintig_naar_tien_procent_per_een_datum()
    {
        var actief = Laptop(herzieningen: new Herziening(new(2027, 1, 1), 10m));
        Assert.Equal(20m, Laptop().PercentagePerJaar);

        var plan = Afschrijvingsplan.Maak(actief);

        // 2026 nog 20%: 1.200 / 60 = 20 per maand; vanaf 2027 10%: 1.200 × 10% / 12 = 10 per maand.
        Assert.All(plan.Where(m => m.Maand.Year == 2026), m => Assert.Equal(20m, m.Bedrag));
        Assert.All(plan.Where(m => m.Maand.Year >= 2027), m => Assert.Equal(10m, m.Bedrag));
        Assert.Equal(1200m, plan.Sum(m => m.Bedrag));
        Assert.Equal(12 + 96, plan.Count);
        Assert.Equal(new DateOnly(2034, 12, 1), plan[^1].Maand);
        Assert.Equal(0m, plan[^1].Boekwaarde);
    }

    [Fact]
    public void Geboekte_maanden_blijven_en_het_restje_valt_in_de_laatste_maand()
    {
        var geboekt = Enumerable.Range(0, 6).ToDictionary(i => new DateOnly(2026, 1 + i, 1), _ => 20m);
        var actief = Laptop(1000m, 0m, 60, new Herziening(new(2026, 7, 15), 30m));

        var plan = Afschrijvingsplan.Maak(actief, geboekt);

        Assert.Equal(geboekt.Values, plan.Take(6).Select(m => m.Bedrag));
        Assert.All(plan.Take(6), m => Assert.True(m.Geboekt));
        // 1.000 × 30% / 12 = 25 per maand over de resterende 880: 35 maanden van 25 en een laatste van 5.
        Assert.Equal(new DateOnly(2026, 7, 1), plan[6].Maand);
        Assert.Equal(25m, plan[6].Bedrag);
        Assert.Equal(5m, plan[^1].Bedrag);
        Assert.Equal(1000m, plan.Sum(m => m.Bedrag));
    }

    [Fact]
    public void Met_restwaarde_en_twee_herzieningen_na_elkaar()
    {
        var actief = Laptop(24000m, 4000m, 60, new Herziening(new(2027, 1, 1), 10m), new Herziening(new(2028, 1, 1), 25m));

        var plan = Afschrijvingsplan.Maak(actief);

        Assert.All(plan.Where(m => m.Maand.Year == 2026), m => Assert.InRange(m.Bedrag, 333.33m, 333.34m));
        Assert.All(plan.Where(m => m.Maand.Year == 2027), m => Assert.Equal(166.67m, m.Bedrag));
        Assert.Equal(416.67m, plan.First(m => m.Maand.Year == 2028).Bedrag);
        Assert.Equal(20000m, plan.Sum(m => m.Bedrag));
        Assert.Equal(4000m, plan[^1].Boekwaarde);
    }

    [Fact]
    public void Controle_op_percentage_ingang_en_al_geboekte_maanden()
    {
        var actief = Laptop();
        Assert.Empty(new Herziening(new(2027, 1, 1), 10m).Controleer(actief, new DateOnly(2026, 12, 1)));
        Assert.Contains(new Herziening(new(2026, 6, 1), 10m).Controleer(actief, new DateOnly(2026, 6, 1)), f => f.Contains("al afgeschreven", StringComparison.Ordinal));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), 0m).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), 101m).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2025, 12, 1), 10m).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), 10m).Controleer(actief with { Methode = Afschrijvingsmethode.Geen }, null));
    }

    [Fact]
    public void Restwaarde_per_een_datum_wat_ervoor_ligt_blijft()
    {
        // 1.200 in 60 maanden: 20 per maand. Vanaf 2028 restwaarde 300: nog 1.200 - 300 - 480 = 420 over 36 maanden.
        var actief = Laptop(herzieningen: new Herziening(new(2028, 1, 1), null, 300m));

        var plan = Afschrijvingsplan.Maak(actief);

        Assert.All(plan.Where(m => m.Maand.Year < 2028), m => Assert.Equal(20m, m.Bedrag));
        Assert.Equal(420m, plan.Where(m => m.Maand.Year >= 2028).Sum(m => m.Bedrag));
        Assert.Equal(new DateOnly(2030, 12, 1), plan[^1].Maand);
        Assert.Equal(300m, plan[^1].Boekwaarde);
        Assert.All(plan.Where(m => m.Maand.Year >= 2028), m => Assert.InRange(m.Bedrag, 11.66m, 11.67m));
    }

    [Fact]
    public void Percentage_en_restwaarde_in_dezelfde_maand_en_lagere_restwaarde_daarna()
    {
        // Vanaf 2027: 10% per jaar van (1.200 - 200) = 8,33 per maand tot de restwaarde 200; vanaf 2029 restwaarde 0.
        var actief = Laptop(herzieningen: [new Herziening(new(2027, 1, 1), 10m), new Herziening(new(2027, 1, 1), null, 200m), new Herziening(new(2029, 1, 1), null, 0m)]);

        var plan = Afschrijvingsplan.Maak(actief);

        Assert.All(plan.Where(m => m.Maand.Year is 2027 or 2028), m => Assert.Equal(8.33m, m.Bedrag));
        Assert.Equal(1200m, plan.Sum(m => m.Bedrag));
        Assert.Equal(0m, plan[^1].Boekwaarde);
        // Vanaf 2029 weer 10% van de nieuwe grondslag (1.200 - 0): 10 per maand.
        Assert.Equal(10m, plan.First(m => m.Maand.Year == 2029).Bedrag);
    }

    [Fact]
    public void Extra_afschrijving_maakt_de_maanden_erna_kleiner_en_die_ervoor_niet()
    {
        // Zes maanden geboekt (120), in juli een extra afschrijving van 300: nog 1.200 - 120 - 300 = 780 over 54 maanden.
        var geboekt = Enumerable.Range(0, 6).ToDictionary(i => new DateOnly(2026, 1 + i, 1), _ => 20m);
        var plan = Afschrijvingsplan.Maak(Laptop(), geboekt, null, [new Extraafschrijving(new(2026, 7, 20), 300m)]);

        var juli = plan.Single(m => m.Maand == new DateOnly(2026, 7, 1));
        Assert.Equal((300m, false), (juli.Extra, juli.Geboekt));
        Assert.Equal(780m, plan.Where(m => !m.Geboekt).Sum(m => m.Bedrag));
        Assert.Equal(new DateOnly(2030, 12, 1), plan[^1].Maand);
        Assert.Equal(0m, plan[^1].Boekwaarde);
        Assert.Equal(1200m, plan.Sum(m => m.Bedrag + m.Extra));

        // Een extra afschrijving later dan de open maanden ervoor: die houden hun 20.
        var later = Afschrijvingsplan.Maak(Laptop(), geboekt, null, [new Extraafschrijving(new(2027, 1, 10), 300m)]);
        Assert.All(later.Where(m => m.Maand >= new DateOnly(2026, 7, 1) && m.Maand < new DateOnly(2027, 1, 1)), m => Assert.Equal(20m, m.Bedrag));
        Assert.Equal(1200m, later.Sum(m => m.Bedrag + m.Extra));
        Assert.Equal(0m, later[^1].Boekwaarde);
    }

    [Fact]
    public void Extra_afschrijving_tot_de_restwaarde_laat_niets_meer_over()
    {
        var plan = Afschrijvingsplan.Maak(Laptop(1200m, 200m), null, null, [new Extraafschrijving(new(2026, 1, 31), 1000m)]);

        Assert.Equal(1000m, plan.Sum(m => m.Bedrag + m.Extra));
        Assert.Equal(200m, plan[^1].Boekwaarde);

        // Ook bij een actief dat niet wordt afgeschreven (grond): de extra afschrijving staat in het plan.
        var grond = Afschrijvingsplan.Maak(Laptop() with { Methode = Afschrijvingsmethode.Geen }, null, null, [new Extraafschrijving(new(2026, 5, 1), 100m)]);
        Assert.Equal((100m, 1100m), (Assert.Single(grond).Extra, grond[0].Boekwaarde));
    }

    [Fact]
    public void Controle_op_restwaarde_en_samenvoegen_in_dezelfde_maand()
    {
        var actief = Laptop();
        Assert.Empty(new Herziening(new(2027, 1, 1), null, 300m).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), null, null).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), null, -1m).Controleer(actief, null));
        Assert.NotEmpty(new Herziening(new(2027, 1, 1), null, 1300m).Controleer(actief, null));
        Assert.Equal(new Herziening(new(2027, 1, 1), 10m, 300m), new Herziening(new(2027, 1, 1), 10m).Met(new Herziening(new(2027, 1, 15), null, 300m)));
    }
}
