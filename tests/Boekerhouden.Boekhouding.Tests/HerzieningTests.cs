using Boekerhouden.Boekhouding.Activa;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De afschrijving wijzigen per datum: vanaf een maand een ander percentage per jaar, alleen vooruit.</summary>
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
}
