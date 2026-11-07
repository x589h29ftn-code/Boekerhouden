using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class ActivaTests
{
    private static Activum Laptop(decimal aanschaf = 1000m, decimal rest = 0m, int looptijd = 36, decimal voorBegin = 0m, DateOnly? begin = null) =>
        new("Laptop", new(2026, 3, 14), aanschaf, rest, begin ?? new(2026, 3, 1), looptijd, Afschrijvingsmethode.Lineair, "0200", "0210", "4800", voorBegin);

    [Fact]
    public void Lineair_plan_verdeelt_tot_op_de_cent()
    {
        var plan = Afschrijvingsplan.Maak(Laptop());

        Assert.Equal(36, plan.Count);
        Assert.Equal(new DateOnly(2026, 3, 1), plan[0].Maand);
        Assert.Equal(new DateOnly(2029, 2, 1), plan[^1].Maand);
        Assert.Equal(1000m, plan.Sum(m => m.Bedrag));
        Assert.All(plan, m => Assert.InRange(m.Bedrag, 27.77m, 27.78m));
        Assert.Equal(0m, plan[^1].Boekwaarde);
        Assert.Equal(1000m, plan[^1].Cumulatief);
    }

    [Fact]
    public void Restwaarde_en_eerder_afgeschreven_tellen_mee()
    {
        var plan = Afschrijvingsplan.Maak(Laptop(1200m, rest: 200m, looptijd: 10, voorBegin: 400m));

        Assert.Equal(10, plan.Count);
        Assert.All(plan, m => Assert.Equal(60m, m.Bedrag));
        Assert.Equal(1000m, plan[^1].Cumulatief);
        Assert.Equal(200m, plan[^1].Boekwaarde);
    }

    [Fact]
    public void Geboekte_maanden_blijven_en_de_rest_gaat_over_de_nieuwe_looptijd()
    {
        var geboekt = new Dictionary<DateOnly, decimal> { [new(2026, 3, 1)] = 100m, [new(2026, 4, 1)] = 100m };

        // Eerst 10 maanden van 100; daarna blijkt hij maar 5 maanden mee te gaan: 800 over de resterende 3 maanden.
        var plan = Afschrijvingsplan.Maak(Laptop(looptijd: 5), geboekt);

        Assert.Equal([100m, 100m, 266.67m, 266.66m, 266.67m], plan.Select(m => m.Bedrag));
        Assert.Equal([true, true, false, false, false], plan.Select(m => m.Geboekt));
        Assert.Equal(0m, plan[^1].Boekwaarde);
    }

    [Fact]
    public void Niet_afschrijven_geeft_een_leeg_plan()
    {
        var grond = Laptop() with { Omschrijving = "Grond", Methode = Afschrijvingsmethode.Geen, Looptijd = 0 };

        Assert.Empty(Afschrijvingsplan.Maak(grond));
        Assert.Empty(grond.Controleer());
    }

    [Theory]
    [InlineData("2026-02-15", Afschrijvingsfrequentie.Maand, "2026-02-28")]
    [InlineData("2026-02-15", Afschrijvingsfrequentie.Kwartaal, "2026-03-31")]
    [InlineData("2026-11-01", Afschrijvingsfrequentie.Kwartaal, "2026-12-31")]
    [InlineData("2026-04-01", Afschrijvingsfrequentie.Jaar, "2026-12-31")]
    public void Periodeeinde_per_frequentie(string maand, Afschrijvingsfrequentie frequentie, string verwacht) =>
        Assert.Equal(DateOnly.Parse(verwacht, System.Globalization.CultureInfo.InvariantCulture), Afschrijvingsplan.Periodeeinde(DateOnly.Parse(maand, System.Globalization.CultureInfo.InvariantCulture), frequentie));

    [Fact]
    public void Controle_van_een_activum()
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));
        Assert.Empty(Laptop().Controleer(schema));

        var fout = Laptop(aanschaf: 0m, looptijd: 0, begin: new(2026, 1, 1)) with { Omschrijving = " ", Kostenrekening = "0210", Activarekening = "1100" };
        var fouten = fout.Controleer(schema).ToList();
        Assert.Contains("Vul een omschrijving in.", fouten);
        Assert.Contains("De aanschafwaarde moet groter zijn dan 0.", fouten);
        Assert.Contains(fouten, f => f.StartsWith("De looptijd", StringComparison.Ordinal));
        Assert.Contains("Afschrijven begint niet vóór de maand van aanschaf.", fouten);
        Assert.Contains(fouten, f => f.Contains("kostenrekening 0210 hoort in de winst-en-verliesrekening", StringComparison.Ordinal));
        Assert.Contains(fouten, f => f.Contains("Rekening 1100 is voor de bank", StringComparison.Ordinal));
        Assert.Contains("De restwaarde ligt tussen 0 en de aanschafwaarde.", Laptop(rest: 2000m).Controleer().ToList());
    }

    [Fact]
    public void Desinvestering_met_winst_en_bij_sloop()
    {
        // Aanschaf 1000, afgeschreven 700: boekwaarde 300. Verkocht voor 450 (op tussenrekening 1800): winst 150.
        var verkoop = Desinvestering.Regels(Laptop(), 700m, 450m, "1800", "9100", "Verkoop laptop");
        Assert.Equal([("0210", 700m), ("0200", -1000m), ("1800", 450m), ("9100", -150m)], verkoop.Select(r => (r.Rekeningnummer, r.Bedrag)));
        Assert.Equal(0m, verkoop.Sum(r => r.Bedrag));

        // Gesloopt zonder opbrengst: verlies ter grootte van de boekwaarde.
        var sloop = Desinvestering.Regels(Laptop(), 700m, 0m, null, "9100", "Sloop");
        Assert.Equal([("0210", 700m), ("0200", -1000m), ("9100", 300m)], sloop.Select(r => (r.Rekeningnummer, r.Bedrag)));
    }

    [Fact]
    public void Activastaat_per_jaar()
    {
        var geboekt = Enumerable.Range(0, 22).ToDictionary(i => new DateOnly(2026, 3, 1).AddMonths(i), _ => 25m);

        var jaar1 = Activastaat.Regel(Laptop(), geboekt, [], 2026)!;
        Assert.Equal((0m, 0m, 1000m, 250m, 750m), (jaar1.AanschafBegin, jaar1.AfschrijvingBegin, jaar1.Investering, jaar1.Afschrijving, jaar1.BoekwaardeEind));

        // In 2027 verkocht na 12 maanden: alles gaat eraf.
        Desinvesteringsstap[] verkocht = [new(new DateOnly(2027, 12, 15), 1000m, 550m, 0m)];
        var jaar2 = Activastaat.Regel(Laptop(), geboekt, verkocht, 2027)!;
        Assert.Equal((1000m, 250m, 300m, 1000m, 550m, 0m), (jaar2.AanschafBegin, jaar2.AfschrijvingBegin, jaar2.Afschrijving, jaar2.DesinvesteringAanschaf, jaar2.DesinvesteringAfschrijving, jaar2.BoekwaardeEind));

        Assert.Null(Activastaat.Regel(Laptop(), geboekt, verkocht, 2028));
        Assert.Null(Activastaat.Regel(Laptop(), geboekt, [], 2025));
        Assert.Equal((1000m, 550m), ((jaar1 + jaar2).Investering, (jaar1 + jaar2).Afschrijving));
    }

    [Fact]
    public void Gedeeltelijke_desinvestering_naar_rato_en_het_plan_gaat_verder_met_de_rest()
    {
        // 1.200 in 12 maanden, restwaarde 200; na 4 maanden (afgeschreven 333,33) 40% verkocht voor 300.
        var activum = Laptop(1200m, rest: 200m, looptijd: 12);
        var geboekt = Afschrijvingsplan.Maak(activum).Take(4).ToDictionary(m => m.Maand, m => m.Bedrag);
        var afgeschreven = geboekt.Values.Sum();

        var deel = Desinvestering.Bereken(activum.Aanschafwaarde, afgeschreven, activum.Restwaarde, 0.4m, 300m);
        Assert.Equal((480m, Math.Round(afgeschreven * 0.4m, 2), 80m), (deel.Aanschaf, deel.Afschrijving, deel.Restwaarde));
        Assert.Equal(300m - (480m - deel.Afschrijving), deel.Resultaat);
        var regels = Desinvestering.Regels(activum, deel, "1800", "9100", "Verkoop 40% 1 Laptop");
        Assert.Equal(0m, regels.Sum(r => r.Bedrag));
        Assert.Equal(-480m, regels.Single(r => r.Rekeningnummer == "0200").Bedrag);

        // Daarna: aanschaf 720, rest 120, wat nog af moet over de laatste 8 maanden, eindigend op de restwaarde.
        var plan = Afschrijvingsplan.Maak(activum, geboekt, [new(new DateOnly(2026, 6, 20), deel.Aanschaf, deel.Afschrijving, deel.Restwaarde)]);
        Assert.Equal(12, plan.Count);
        Assert.Equal(120m, plan[^1].Boekwaarde);
        Assert.Equal(720m - 120m, plan[^1].Cumulatief);

        Assert.Equal("Verkoop 40% 1 Laptop", Desinvestering.Omschrijving(Desinvesteringssoort.Verkoop, 0.4m, 1, "Laptop"));
        Assert.Equal("Sloop 1 Laptop", Desinvestering.Omschrijving(Desinvesteringssoort.Sloop, 1m, 1, "Laptop"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Desinvestering.Bereken(1m, 0m, 0m, 1.5m, 0m));
    }

    [Fact]
    public void Helemaal_gedesinvesteerd_geeft_geen_open_maanden_meer()
    {
        var geboekt = Afschrijvingsplan.Maak(Laptop()).Take(3).ToDictionary(m => m.Maand, m => m.Bedrag);
        var plan = Afschrijvingsplan.Maak(Laptop(), geboekt, [new(new DateOnly(2026, 5, 10), 1000m, geboekt.Values.Sum(), 0m)]);

        Assert.All(plan, m => Assert.True(m.Geboekt));
        Assert.Equal(3, plan.Count);
    }
}
