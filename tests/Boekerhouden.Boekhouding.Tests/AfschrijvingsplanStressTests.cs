using Boekerhouden.Boekhouding.Activa;
using Xunit.Abstractions;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Het afschrijvingsplan onder druk: duizenden willekeurige activa met geboekte maanden, wijzigingen van percentage en
/// restwaarde per datum en extra afschrijvingen, en randgevallen (alles in één keer, na het eind, heel klein percentage,
/// vijftig jaar). Wat altijd moet gelden: geboekte maanden houden hun bedrag, geen negatieve of halve centen, de
/// cumulatieve afschrijving loopt op, de maanden vóór de eerste wijziging veranderen niet, en het plan eindigt op de
/// restwaarde (of eronder als de extra afschrijvingen meer waren dan er nog af moest).
/// </summary>
public sealed class AfschrijvingsplanStressTests(ITestOutputHelper uitvoer)
{
    private static Activum Actief(Random r, out DateOnly eerste)
    {
        var aanschaf = Math.Round((decimal)(r.NextDouble() * 99_000 + 100), 2);
        var rest = r.Next(3) == 0 ? Math.Round(aanschaf * (decimal)r.NextDouble() * 0.5m, 2) : 0m;
        var looptijd = r.Next(1, 121);
        eerste = new DateOnly(2024 + r.Next(3), r.Next(1, 13), 1);
        var voor = r.Next(5) == 0 ? Math.Round((aanschaf - rest) * (decimal)r.NextDouble() * 0.5m, 2) : 0m;
        return new Activum("Actief", eerste.AddDays(r.Next(0, 27)), aanschaf, rest, eerste, looptijd, Afschrijvingsmethode.Lineair, "0200", "0210", "4800", voor);
    }

    [Fact]
    public void Duizenden_willekeurige_plannen_houden_zich_aan_de_regels()
    {
        var r = new Random(2026);
        var fouten = new List<string>();
        var klok = System.Diagnostics.Stopwatch.StartNew();
        for (var geval = 0; geval < 3000 && fouten.Count < 20; geval++)
        {
            var basis = Actief(r, out var eerste);
            var zonder = Afschrijvingsplan.Maak(basis);

            // Een deel van het plan is al geboekt, met de bedragen van toen.
            var aantalGeboekt = r.Next(0, Math.Min(zonder.Count, 40) + 1);
            var geboekt = zonder.Take(aantalGeboekt).ToDictionary(m => m.Maand, m => m.Bedrag);
            var laatst = aantalGeboekt == 0 ? eerste.AddMonths(-1) : zonder[aantalGeboekt - 1].Maand;

            // Wijzigingen na de laatst geboekte maand en extra afschrijvingen op elke datum. Een restwaarde hoger dan de
            // boekwaarde op dat moment weigert de dienst (WijzigRestwaarde): die laten we hier ook weg.
            var kandidaten = Enumerable.Range(0, r.Next(0, 4)).Select(_ =>
            {
                var maand = laatst.AddMonths(r.Next(1, 48));
                return r.Next(3) switch
                {
                    0 => new Herziening(maand, Math.Round((decimal)(r.NextDouble() * 49 + 1), 2)),
                    1 => new Herziening(maand, null, Math.Round(basis.Aanschafwaarde * (decimal)r.NextDouble() * 0.6m, 2)),
                    _ => new Herziening(maand, Math.Round((decimal)(r.NextDouble() * 49 + 1), 2), Math.Round(basis.Aanschafwaarde * (decimal)r.NextDouble() * 0.3m, 2)),
                };
            }).OrderBy(h => h.Maand).ToList();
            var herzieningen = new List<Herziening>();
            foreach (var kandidaat in kandidaten)
            {
                var tot = Afschrijvingsplan.Maak(basis with { Herzieningen = herzieningen }, geboekt);
                var boekwaarde = tot.LastOrDefault(m => m.Maand < kandidaat.Maand)?.Boekwaarde ?? basis.Aanschafwaarde - basis.AfgeschrevenVoorBegin;
                if (kandidaat.Restwaarde is not { } nieuw || nieuw <= boekwaarde)
                {
                    herzieningen.Add(kandidaat);
                }
            }

            var extras = Enumerable.Range(0, r.Next(0, 4)).Select(_ =>
                new Extraafschrijving(eerste.AddMonths(r.Next(0, 150)).AddDays(r.Next(0, 27)), Math.Round(basis.Aanschafwaarde * (decimal)r.NextDouble() * 0.4m, 2))).ToList();
            var actief = basis with { Herzieningen = herzieningen };

            var plan = Afschrijvingsplan.Maak(actief, geboekt, null, extras);
            var wie = $"geval {geval}";

            // Geboekte maanden houden hun bedrag.
            foreach (var (maand, bedrag) in geboekt)
            {
                if (plan.SingleOrDefault(m => m.Maand == maand) is not { Geboekt: true } m || m.Bedrag != bedrag)
                {
                    fouten.Add($"{wie}: geboekte maand {maand} veranderd.");
                }
            }

            // Geen negatieve bedragen of halve centen; cumulatief loopt op en klopt met de som.
            var cumulatief = actief.AfgeschrevenVoorBegin;
            DateOnly? vorige = null;
            foreach (var m in plan)
            {
                if (m.Bedrag < 0 || m.Extra < 0 || m.Bedrag != Math.Round(m.Bedrag, 2))
                {
                    fouten.Add($"{wie}: {m.Maand} bedrag {m.Bedrag}, extra {m.Extra}.");
                }

                cumulatief += m.Bedrag + m.Extra;
                if (m.Cumulatief != cumulatief || m.Boekwaarde != actief.Aanschafwaarde - cumulatief || (vorige is { } v && m.Maand <= v))
                {
                    fouten.Add($"{wie}: {m.Maand} cumulatief {m.Cumulatief} (verwacht {cumulatief}) of volgorde.");
                }

                vorige = m.Maand;
            }

            // De maanden vóór de eerste wijziging of extra afschrijving zijn gelijk aan het plan zonder.
            var eersteGebeurtenis = herzieningen.Select(h => h.Maand).Concat(extras.Select(x => x.Maand)).DefaultIfEmpty(DateOnly.MaxValue).Min();
            var ongewijzigd = Afschrijvingsplan.Maak(basis, geboekt);
            foreach (var m in ongewijzigd.Where(m => m.Maand < eersteGebeurtenis))
            {
                if (plan.SingleOrDefault(p => p.Maand == m.Maand)?.Bedrag != m.Bedrag)
                {
                    fouten.Add($"{wie}: {m.Maand} vóór de eerste wijziging veranderd.");
                }
            }

            // Het eind: de restwaarde die als laatste geldt, of eronder door extra afschrijvingen (en dan niets meer gepland erna).
            var rest = herzieningen.OrderBy(h => h.Maand).LastOrDefault(h => h.Restwaarde is not null)?.Restwaarde ?? actief.Restwaarde;
            var eind = plan.Count == 0 ? actief.Aanschafwaarde - actief.AfgeschrevenVoorBegin : plan[^1].Boekwaarde;
            var teveel = actief.AfgeschrevenVoorBegin + geboekt.Values.Sum() > actief.Aanschafwaarde - rest;
            if (eind != rest && !(eind < rest && (extras.Count > 0 || teveel)))
            {
                fouten.Add($"{wie}: eindigt op {eind}, restwaarde {rest} ({actief.Aanschafwaarde} in {actief.Looptijd} maanden vanaf {eerste}, rest {actief.Restwaarde}, {geboekt.Count} geboekt t/m {laatst}, wijzigingen {string.Join("; ", herzieningen.Select(h => $"{h.Maand} {h.PercentagePerJaar}% R{h.Restwaarde}"))}, laatste plan {plan[^1].Maand}).");
            }

            var laatsteGebeurtenis = herzieningen.Select(h => h.Maand).Concat(extras.Select(x => x.Maand)).DefaultIfEmpty(DateOnly.MinValue).Max();
            if (eind < rest && extras.Count > 0 && plan.Any(m => !m.Geboekt && m.Bedrag > 0 && m.Maand >= laatsteGebeurtenis))
            {
                fouten.Add($"{wie}: onder de restwaarde en toch nog afschrijvingen na de laatste extra afschrijving.");
            }
        }

        uitvoer.WriteLine($"3000 plannen in {klok.ElapsedMilliseconds} ms");
        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
        Assert.True(klok.Elapsed < TimeSpan.FromSeconds(30), $"te traag: {klok.Elapsed}");
    }

    [Fact]
    public void Randgevallen()
    {
        var laptop = new Activum("Laptop", new(2026, 1, 5), 1200m, 0m, new(2026, 1, 1), 60, Afschrijvingsmethode.Lineair, "0200", "0210", "4800");

        // Alles in één keer: niets meer te plannen, boekwaarde 0.
        var alles = Afschrijvingsplan.Maak(laptop, null, null, [new Extraafschrijving(new(2026, 1, 10), 1200m)]);
        Assert.Equal((1200m, 0m), (alles.Sum(m => m.Bedrag + m.Extra), alles[^1].Boekwaarde));
        Assert.All(alles.Where(m => m.Maand > new DateOnly(2026, 1, 1)), m => Assert.Equal(0m, m.Bedrag));

        // Twee extra afschrijvingen in dezelfde maand tellen op.
        var twee = Afschrijvingsplan.Maak(laptop, null, null, [new Extraafschrijving(new(2026, 3, 1), 100m), new Extraafschrijving(new(2026, 3, 31), 50m)]);
        Assert.Equal(150m, twee.Single(m => m.Maand == new DateOnly(2026, 3, 1)).Extra);
        Assert.Equal(1200m, twee.Sum(m => m.Bedrag + m.Extra));

        // Een extra afschrijving na het eind van het plan: een eigen regel, onder de restwaarde.
        var na = Afschrijvingsplan.Maak(laptop, null, null, [new Extraafschrijving(new(2032, 6, 1), 10m)]);
        Assert.Equal((10m, -10m), (na[^1].Extra, na[^1].Boekwaarde));

        // Restwaarde gelijk aan de aanschafwaarde vanaf een maand: daarna niets meer.
        var stop = Afschrijvingsplan.Maak(laptop with { Herzieningen = [new Herziening(new(2027, 1, 1), null, 1200m)] });
        Assert.All(stop.Where(m => m.Maand >= new DateOnly(2027, 1, 1)), m => Assert.Equal(0m, m.Bedrag));
        Assert.Equal(240m, stop.Sum(m => m.Bedrag));

        // Een heel klein percentage loopt hooguit vijftig jaar door (en het plan blijft snel).
        var klok = System.Diagnostics.Stopwatch.StartNew();
        var traag = Afschrijvingsplan.Maak(laptop with { Herzieningen = [new Herziening(new(2026, 2, 1), 0.01m)] });
        Assert.True(klok.ElapsedMilliseconds < 1000);
        Assert.True(traag.Count <= Activum.MaximaleLooptijd + 2);
        Assert.All(traag, m => Assert.True(m.Bedrag >= 0));

        // Vijftig jaar met tien wijzigingen en tien extra afschrijvingen: klopt tot op de cent.
        var lang = laptop with
        {
            Aanschafwaarde = 9_999_999.99m,
            Looptijd = Activum.MaximaleLooptijd,
            Herzieningen = [.. Enumerable.Range(1, 10).Select(i => new Herziening(new DateOnly(2026 + (i * 4), 1, 1), i % 2 == 0 ? 2m + (i / 10m) : null, i % 3 == 0 ? 100_000m * (10 - i) : null))],
        };
        var extra = Enumerable.Range(1, 10).Select(i => new Extraafschrijving(new DateOnly(2027 + (i * 3), 6, 15), 12_345.67m)).ToList();
        var groot = Afschrijvingsplan.Maak(lang, null, null, extra);
        var laatsteRest = lang.Herzieningen.Where(h => h.Restwaarde is not null).OrderBy(h => h.Maand).Last().Restwaarde!.Value;
        Assert.Equal(laatsteRest, groot[^1].Boekwaarde);
        Assert.Equal(lang.Aanschafwaarde - laatsteRest, groot.Sum(m => m.Bedrag + m.Extra));
    }
}
