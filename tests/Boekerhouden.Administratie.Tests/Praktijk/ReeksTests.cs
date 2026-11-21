using System.Globalization;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Willekeurige reeksen handelingen met vaste controles: met een vaste startwaarde (seed) worden honderden handelingen
/// door elkaar gedaan (factureren, crediteren, inkopen, terugdraaien, aanpassen, betalen, deels en te veel betalen,
/// memoriaal, aanpassen van boekingen, btw vastleggen en intrekken, perioden afsluiten en heropenen, activa kopen,
/// afschrijven, verkopen en terugdraaien). Na elke stap moeten de <see cref="Invarianten"/> kloppen; een geweigerde
/// handeling is goed (een gewone melding), een uitzondering of een afwijking niet. Bij een afwijking geeft de test de
/// seed en de stappen ervoor, zodat het precies na te spelen is. Meer stappen of andere seeds: BOEKERHOUDEN_STRESS_REEKS
/// = "stappen" of "stappen:seed1,seed2".
/// </summary>
public sealed class ReeksTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private sealed record Stand(Werkadministratie W)
    {
        public List<(Guid Id, string Nummer)> Verkoop { get; } = [];

        public List<(Guid Document, string Nummer, DateOnly Datum)> Inkoop { get; } = [];

        public List<string> Memoriaal { get; } = [];

        public List<Guid> Activa { get; } = [];

        public DateOnly Bankdag { get; set; } = new(2025, 1, 2);

        public DateOnly? Dicht { get; set; }

        public int Teller { get; set; }
    }

    [Fact]
    public async Task Willekeurige_reeksen_houden_de_administratie_kloppend()
    {
        var (stappen, seeds) = Instelling();
        var alleFouten = new List<string>();
        foreach (var seed in seeds)
        {
            var fouten = await Reeks(seed, stappen);
            alleFouten.AddRange(fouten.Select(f => $"seed {seed}: {f}"));
        }

        Assert.True(alleFouten.Count == 0, string.Join("\n", alleFouten));
    }

    private static (int Stappen, int[] Seeds) Instelling()
    {
        var waarde = Environment.GetEnvironmentVariable("BOEKERHOUDEN_STRESS_REEKS");
        if (string.IsNullOrWhiteSpace(waarde))
        {
            return (150, [1, 2]);
        }

        var delen = waarde.Split(':');
        var stappen = int.Parse(delen[0], CultureInfo.InvariantCulture);
        int[] seeds = delen.Length > 1 ? [.. delen[1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))] : [1, 2, 3];
        return (stappen, seeds);
    }

    private async Task<List<string>> Reeks(int seed, int stappen)
    {
        var toeval = new Random(seed);
        var w = await Werkadministratie.Maak(app, $"Reeks {seed} B.V.");
        var stand = new Stand(w);
        var verslag = new List<string>();
        var telling = new Dictionary<string, (int Gelukt, int Geweigerd)>();
        var fouten = new List<string>();
        var redenen = new List<string>();
        for (var stap = 1; stap <= stappen; stap++)
        {
            var (naam, werk) = Kies(toeval, stand);
            Uitkomst uitkomst;
            try
            {
                uitkomst = await werk();
            }
            catch (Exception e)
            {
                fouten.Add($"stap {stap} ({naam}): uitzondering {e.GetType().Name}: {e.Message}");
                break;
            }

            var (g, n) = telling.GetValueOrDefault(naam);
            telling[naam] = uitkomst.IsGelukt ? (g + 1, n) : (g, n + 1);
            verslag.Add($"{stap,4} {naam}: {(uitkomst.IsGelukt ? "gelukt" : "geweigerd: " + string.Join(" ", uitkomst.Fouten))}");
            if (!uitkomst.IsGelukt)
            {
                redenen.Add($"{naam}: {string.Join(" ", uitkomst.Fouten)}");
            }

            var afwijkingen = await Invarianten.Controleer(app, w.Wie, w.Id, $"stap {stap} ({naam})");
            if (afwijkingen.Count > 0)
            {
                fouten.AddRange(afwijkingen);
                fouten.Add("De laatste stappen:\n" + string.Join("\n", verslag.TakeLast(8)));
                break;
            }
        }

        if (fouten.Count == 0)
        {
            fouten.AddRange(await Samenhang.Controleer(app, w.Wie, w.Id, $"na seed {seed}"));
        }

        uitvoer.WriteLine($"Seed {seed}: {verslag.Count} stappen; " + string.Join(", ", telling.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => $"{t.Key} {t.Value.Gelukt}/{t.Value.Gelukt + t.Value.Geweigerd}")));
        foreach (var reden in redenen.GroupBy(r => r).OrderByDescending(g => g.Count()))
        {
            uitvoer.WriteLine($"    geweigerd {reden.Count()}x: {reden.Key}");
        }

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        return fouten;
    }

    /// <summary>Een willekeurige handeling met wat erbij hoort; het gewicht bepaalt hoe vaak hij voorkomt.</summary>
    private (string Naam, Func<Task<Uitkomst>> Werk) Kies(Random toeval, Stand s)
    {
        var w = s.W;
        var (wie, id) = (w.Wie, w.Id);
        DateOnly Datum()
        {
            var vanaf = s.Dicht is { } d && toeval.Next(10) > 0 ? d.AddDays(1) : new DateOnly(2025, 1, 1);
            var tot = new DateOnly(2025, 12, 31);
            return vanaf > tot ? tot : vanaf.AddDays(toeval.Next(tot.DayNumber - vanaf.DayNumber + 1));
        }

        decimal Bedrag() => Math.Round((decimal)(toeval.NextDouble() * 2_000 + 5), 2);
        T Een<T>(IReadOnlyList<T> lijst) => lijst[toeval.Next(lijst.Count)];

        var keuzes = new List<(int Gewicht, string Naam, Func<Task<Uitkomst>> Werk)>
        {
            (10, "verkoop", async () =>
            {
                var concept = await w.Concept(Datum(), Bedrag());
                var definitief = await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, concept);
                if (definitief.IsGelukt)
                {
                    s.Verkoop.Add((concept, definitief.Waarde!));
                }

                return definitief;
            }),
            (2, "crediteren", async () =>
            {
                if (s.Verkoop.Count == 0)
                {
                    return Uitkomst.Mislukt("geen verkoop");
                }

                var credit = await app.Dienst<VerkoopDienst>().Crediteer(wie, id, Een(s.Verkoop).Id);
                return credit.IsGelukt ? await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, credit.Waarde) : credit;
            }),
            (10, "inkoop", async () =>
            {
                var nummer = $"RG-R-{++s.Teller:00000}";
                var datum = Datum();
                var document = await w.InkoopDocument(nummer, datum, Bedrag());
                var geboekt = await w.BoekInkoop(document, nummer, datum, Bedrag());
                if (geboekt.IsGelukt)
                {
                    s.Inkoop.Add((document, nummer, datum));
                }

                return geboekt;
            }),
            (3, "inkoop terugdraaien", async () =>
            {
                if (s.Inkoop.Count == 0)
                {
                    return Uitkomst.Mislukt("geen inkoop");
                }

                var f = Een(s.Inkoop);
                var uitkomst = await app.Dienst<InkoopDienst>().Terugdraaien(wie, id, f.Document);
                if (uitkomst.IsGelukt)
                {
                    s.Inkoop.Remove(f);
                    // Het document staat weer te verwerken: soms meteen opnieuw boeken.
                    if (toeval.Next(2) == 0 && (await w.BoekInkoop(f.Document, f.Nummer, f.Datum, Bedrag())).IsGelukt)
                    {
                        s.Inkoop.Add(f);
                    }
                }

                return uitkomst;
            }),
            (3, "inkoop aanpassen", async () =>
            {
                if (s.Inkoop.Count == 0)
                {
                    return Uitkomst.Mislukt("geen inkoop");
                }

                var f = Een(s.Inkoop);
                return await w.BoekInkoop(f.Document, f.Nummer, f.Datum, Bedrag());
            }),
            (12, "betaling", async () =>
            {
                var bank = app.Dienst<BankDienst>();
                var open = (await bank.OpenPosten(wie, id)).Where(p => p.Openstaand != 0).ToList();
                if (open.Count == 0 || s.Bankdag > new DateOnly(2025, 12, 31))
                {
                    return Uitkomst.Mislukt("niets open");
                }

                var post = Een(open);
                // Precies, deels of te veel betaald.
                var factor = toeval.Next(4) switch { 0 => 0.5m, 1 => 1.1m, _ => 1m };
                var bedrag = Math.Round(post.Openstaand * factor, 2);
                s.Bankdag = s.Bankdag.AddDays(1);
                var omschrijving = $"Betaling {++s.Teller}";
                var lijst = await w.Bank(new Bankmutatie(s.Bankdag, bedrag, omschrijving, "NL02ABNA0123456789", "Iemand"));
                return lijst.FirstOrDefault(m => m.Omschrijving == omschrijving) is { } m
                    ? await bank.Afletteren(wie, id, m.Id, [post.Id])
                    : Uitkomst.Gelukt;
            }),
            (4, "bankkosten", async () =>
            {
                if (s.Bankdag > new DateOnly(2025, 12, 31))
                {
                    return Uitkomst.Mislukt("jaar voorbij");
                }

                s.Bankdag = s.Bankdag.AddDays(1);
                var omschrijving = $"Kosten {++s.Teller}";
                var lijst = await w.Bank(new Bankmutatie(s.Bankdag, -Math.Round(Bedrag() / 100, 2), omschrijving));
                return lijst.FirstOrDefault(m => m.Omschrijving == omschrijving) is { } m
                    ? await app.Dienst<BankDienst>().OpRekening(wie, id, m.Id, "9000", null, null)
                    : Uitkomst.Gelukt;
            }),
            (6, "memoriaal", async () =>
            {
                var grootboek = app.Dienst<GrootboekDienst>();
                var datum = Datum();
                var boekstuk = await grootboek.VolgendBoekstuk(wie, id, "MEM", datum.Year);
                var bedrag = Bedrag();
                var uitkomst = await grootboek.Boek(wie, id, new Journaalpost("MEM", boekstuk, datum, "Herrubricering",
                    [new(Een(["4300", "4310", "4900"]), bedrag), new(Een(["4800", "4900", "8000"]), -bedrag)]));
                if (uitkomst.IsGelukt)
                {
                    s.Memoriaal.Add(boekstuk);
                }

                return uitkomst;
            }),
            (3, "boeking aanpassen", async () =>
            {
                if (s.Memoriaal.Count == 0)
                {
                    return Uitkomst.Mislukt("geen memoriaal");
                }

                var grootboek = app.Dienst<GrootboekDienst>();
                var boekstuk = Een(s.Memoriaal);
                var nu = await grootboek.Boeking(wie, id, "MEM", boekstuk);
                if (nu is null)
                {
                    return Uitkomst.Mislukt("weg");
                }

                var bedrag = Bedrag();
                return await grootboek.PasAan(wie, id, "MEM", boekstuk, new Journaalpost("MEM", boekstuk, nu.Boeking.Post.Datum, "Aangepast", [new("4300", bedrag), new("4900", -bedrag)]));
            }),
            (2, "herboeken", async () =>
            {
                if (s.Memoriaal.Count == 0)
                {
                    return Uitkomst.Mislukt("geen memoriaal");
                }

                return await app.Dienst<GrootboekDienst>().Herboek(wie, id, [new Herboekregel("MEM", Een(s.Memoriaal), 0)], Een(["4300", "4310"]));
            }),
            (2, "btw vastleggen", () =>
            {
                var kwartaal = toeval.Next(4);
                return app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new DateOnly(2025, kwartaal * 3 + 1, 1), new DateOnly(2025, kwartaal * 3 + 1, 1).AddMonths(3).AddDays(-1));
            }),
            (1, "btw intrekken", () =>
            {
                var kwartaal = toeval.Next(4);
                return app.Dienst<BtwaangifteDienst>().Intrekken(wie, id, new DateOnly(2025, kwartaal * 3 + 1, 1), new DateOnly(2025, kwartaal * 3 + 1, 1).AddMonths(3).AddDays(-1));
            }),
            (1, "afsluiten", async () =>
            {
                // Hoogstens tot de maand voor de laatste bankdag, zodat de bank kan blijven inlezen.
                var tot = new DateOnly(s.Bankdag.Year, s.Bankdag.Month, 1).AddDays(-1);
                if (tot.Year < 2025)
                {
                    return Uitkomst.Mislukt("te vroeg");
                }

                var uitkomst = await app.Dienst<PeriodeDienst>().SluitAf(wie, id, tot);
                if (uitkomst.IsGelukt)
                {
                    s.Dicht = tot;
                }

                return uitkomst;
            }),
            (1, "heropenen", async () =>
            {
                if (s.Dicht is not { } dicht)
                {
                    return Uitkomst.Mislukt("niets dicht");
                }

                var vanaf = new DateOnly(dicht.Year, dicht.Month, 1);
                var uitkomst = await app.Dienst<PeriodeDienst>().Heropen(wie, id, vanaf, "Correctie");
                if (uitkomst.IsGelukt)
                {
                    s.Dicht = vanaf.AddDays(-1) is { Year: 2025 } d ? d : null;
                }

                return uitkomst;
            }),
            (2, "actief kopen", async () =>
            {
                var datum = Datum();
                var bedrag = Math.Round(Bedrag() * 3, 0);
                var grootboek = app.Dienst<GrootboekDienst>();
                var boekstuk = await grootboek.VolgendBoekstuk(wie, id, "MEM", datum.Year);
                var aanschaf = await grootboek.Boek(wie, id, new Journaalpost("MEM", boekstuk, datum, "Aanschaf", [new("0200", bedrag), new("0500", -bedrag)]));
                if (!aanschaf.IsGelukt)
                {
                    return aanschaf;
                }

                var actief = await app.Dienst<ActivaDienst>().Bewaar(wie, id, null, new ActivumInvoer("Machine", datum, bedrag, 0m, new DateOnly(datum.Year, datum.Month, 1), 36,
                    Afschrijvingsmethode.Lineair, "0200", "0210", "4800", Bron: $"MEM {boekstuk}"));
                if (actief.IsGelukt)
                {
                    s.Activa.Add(actief.Waarde);
                }

                return actief;
            }),
            (2, "afschrijven", async () =>
            {
                var maand = toeval.Next(1, 13);
                return await app.Dienst<ActivaDienst>().Afschrijven(wie, id, new DateOnly(2025, maand, 1).AddMonths(1).AddDays(-1), Afschrijvingsfrequentie.Maand);
            }),
            (1, "actief verkopen", async () =>
            {
                if (s.Activa.Count == 0)
                {
                    return Uitkomst.Mislukt("geen activa");
                }

                return await app.Dienst<ActivaDienst>().Desinvesteer(wie, id, Een(s.Activa),
                    new Desinvesteringsinvoer(Datum(), Desinvesteringssoort.Verkoop, Een([50m, 100m]), Bedrag(), "1800", "4900"));
            }),
            (1, "verkoop actief terugdraaien", async () =>
            {
                if (s.Activa.Count == 0)
                {
                    return Uitkomst.Mislukt("geen activa");
                }

                var activa = app.Dienst<ActivaDienst>();
                var detail = await activa.Open(wie, id, Een(s.Activa));
                return detail?.Desinvesteringen.LastOrDefault(d => d.Laatste) is { } laatste
                    ? await activa.TerugdraaienDesinvestering(wie, id, laatste.Id)
                    : Uitkomst.Mislukt("niet verkocht");
            }),
        };

        var totaal = keuzes.Sum(k => k.Gewicht);
        var trek = toeval.Next(totaal);
        foreach (var (gewicht, naam, werk) in keuzes)
        {
            if (trek < gewicht)
            {
                return (naam, werk);
            }

            trek -= gewicht;
        }

        throw new InvalidOperationException("Geen keuze.");
    }
}
