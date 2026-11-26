using System.Diagnostics;
using System.Globalization;
using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Twee volle jaren (2024 en 2025) zoals een klant ze aanlevert, om de jaarovergangen en de lastige gevallen te
/// testen: elke maand verkoop- en inkoopfacturen (bij de stresstest samen ruim 500 van elk), de bank per maand met
/// deelbetalingen, een te veel betaalde factuur, verzamelbetalingen, facturen die nooit betaald worden, gecrediteerde
/// verkoopfacturen en inkoopcreditnota's die met de factuur worden verrekend. Investeringen in inventaris, een bestelbus
/// (in het tweede jaar verkocht) en een pand (op de rekeningen voor bedrijfsgebouwen) dat de bank een tijd negatief maakt tot de hypotheek binnenkomt; die wordt
/// daarna per maand afgelost. Afschrijven per maand, de btw per kwartaal vastleggen en betalen (of terugkrijgen) en de
/// periode afsluiten. Alles wordt tegen een eigen rekenmodel gecontroleerd; afwijkingen worden verzameld en samen gemeld.
/// </summary>
public sealed class TweejaarTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";
    private const decimal Beginsaldo = 40_000m;
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly Einde = new(2025, 12, 31);

    // Investeringen: bedragen zo gekozen dat de afschrijving per maand op de cent uitkomt.
    private const decimal Inventaris = 15_000m;        // 60 maanden, geen restwaarde: 250 per maand vanaf februari 2024
    private const decimal Bus = 41_000m;               // 60 maanden, restwaarde 5.000: 600 per maand vanaf april 2024
    private const decimal BusRest = 5_000m;
    private const decimal Pand = 350_000m;             // 360 maanden, restwaarde 98.000: 700 per maand vanaf juli 2024
    private const decimal PandRest = 98_000m;
    private const decimal Hypotheek = 300_000m;
    private const decimal Aflossing = 1_000m;
    private const decimal BusOpbrengst = 28_000m;      // verkocht op 30 september 2025

    /// <summary>Een factuur in het model; bedragen met teken zoals op het papier (een creditnota negatief).</summary>
    private sealed record Factuur(string Nummer, DateOnly Datum, string Relatie, string Iban, decimal Exclusief, decimal Btw, bool Inkoop, Guid? Id = null, string? CreditVoor = null)
    {
        public decimal Totaal => Exclusief + Btw;

        public bool Credit => Totaal < 0;
    }

    /// <summary>Hoe een factuur wordt betaald.</summary>
    private enum Betaling
    {
        Gewoon,
        InDelen,
        TeVeel,
        Verzameld,

        /// <summary>Al betaald als tweede factuur van een verzamelbetaling.</summary>
        Meebetaald,
        Nooit,
        Gecrediteerd,
    }

    /// <summary>Wat het afronden van de vastgelegde btw-aangiften per jaar scheelt (naar het resultaat geboekt).</summary>
    private readonly Dictionary<int, decimal> afrondingen = [];

    /// <summary>Het afronden van de laatst vastgelegde aangifte (het 4e kwartaal 2025, nog niet betaald).</summary>
    private decimal laatsteAfronding;

    [Fact]
    public Task Twee_jaar_met_alle_randgevallen() => Doorloop(3);

    [StressFact]
    public Task Twee_jaar_met_vijfhonderd_in_en_verkoop() => Doorloop(21);

    private async Task Doorloop(int perMaand)
    {
        var klok = Stopwatch.StartNew();
        var verschillen = new List<string>();
        var toeval = new Random(2026);
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var grootboek = app.Dienst<GrootboekDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var btw = app.Dienst<BtwaangifteDienst>();
        var activa = app.Dienst<ActivaDienst>();

        var klanten = new List<(string Code, Partijdata Partij)>();
        foreach (var p in new[] { Praktijkset.Fietsplan, Praktijkset.Hotel, Praktijkset.Steen, Praktijkset.Bezorg, Praktijkset.Strand })
        {
            klanten.Add((await Klant(wie, id, p), p));
        }

        var leveranciers = new[] { (Praktijkset.Groothandel, "RG", "4700"), (Praktijkset.Energie, "EO", "4110"), (Praktijkset.Kantoor, "KV", "4300"), (Praktijkset.Transport, "TS", "4210"), (Praktijkset.Reclame, "RV", "4400") };
        var facturen = new List<Factuur>();
        var betalingen = new List<(string Nummer, DateOnly Datum, decimal Bedrag)>();   // zoals de bank (ontvangst positief)
        var hoe = new Dictionary<string, Betaling>();
        var bankkosten = new Dictionary<int, decimal>();
        var rente = new Dictionary<int, decimal>();
        var saldo = Beginsaldo;
        var laagste = (Saldo: Beginsaldo, Datum: Start);
        var teLaat = new List<(DateOnly Datum, Bankmutatie Mutatie)>();   // mutaties voor een latere maand
        var activaIds = new Dictionary<string, Guid>();
        var mutatiesTotaal = 0;

        for (var maand = Start; maand <= Einde; maand = maand.AddMonths(1))
        {
            var laatste = maand.AddMonths(1).AddDays(-1);

            // ---- verkoop: elke klant om de beurt; elke tiende wordt gecrediteerd, de rest betaald zoals het uitkomt
            for (var i = 0; i < perMaand; i++)
            {
                var (code, partij) = klanten[(i + maand.Month) % klanten.Count];
                var datum = maand.AddDays(Math.Min(27, 1 + i * 27 / perMaand));
                List<Verkoopregel> regels = [new("Fietsen", toeval.Next(1, 6), toeval.Next(250, 3_000), "8000", "V21")];
                if (i % 3 == 0)
                {
                    regels.Add(new Verkoopregel("Onderhoud", toeval.Next(2, 10), 45m, "8000", "V9"));
                }

                var f = await Verkoopfactuur(wie, id, verschillen, code, partij, datum, regels);
                if (f is null)
                {
                    continue;
                }

                facturen.Add(f);
                var n = facturen.Count(x => !x.Inkoop && !x.Credit);
                hoe[f.Nummer] = n % 10 == 0 ? Betaling.Gecrediteerd : n % 13 == 0 ? Betaling.InDelen : n % 29 == 0 ? Betaling.Nooit
                    : n % 31 == 0 ? Betaling.TeVeel : n % 7 == 0 ? Betaling.Verzameld : Betaling.Gewoon;
                if (hoe[f.Nummer] == Betaling.Gecrediteerd)
                {
                    // Een paar dagen later volledig gecrediteerd: de creditfactuur wordt met de factuur verrekend.
                    // De creditfactuur krijgt de datum van vandaag; de gebruiker zet hem op de dag van het crediteren.
                    var credit = await verkoop.Crediteer(wie, id, f.Id!.Value);
                    if (credit.IsGelukt && await verkoop.Open(wie, id, credit.Waarde) is { } concept)
                    {
                        var bewaard = await verkoop.Bewaar(wie, id, credit.Waarde, concept.Invoer with { Datum = datum.AddDays(3), Vervaldatum = datum.AddDays(17) });
                        if (!bewaard.IsGelukt)
                        {
                            verschillen.Add($"Creditfactuur {f.Nummer} datum: {string.Join(" ", bewaard.Fouten)}");
                        }
                    }

                    var definitief = credit.IsGelukt ? await verkoop.MaakDefinitief(wie, id, credit.Waarde) : null;
                    if (definitief?.IsGelukt != true)
                    {
                        verschillen.Add($"Crediteren {f.Nummer}: {string.Join(" ", credit.Fouten.Concat(definitief?.Fouten ?? []))}");
                        hoe[f.Nummer] = Betaling.Gewoon;
                    }
                    else
                    {
                        facturen.Add(f with { Nummer = definitief.Waarde!, Datum = datum.AddDays(3), Exclusief = -f.Exclusief, Btw = -f.Btw, Id = credit.Waarde, CreditVoor = f.Nummer });
                        hoe[definitief.Waarde!] = Betaling.Gecrediteerd;
                    }
                }
            }

            // ---- inkoop: elke leverancier om de beurt als e-factuur; af en toe een creditnota op een eerdere factuur
            for (var i = 0; i < perMaand; i++)
            {
                var (partij, voorvoegsel, rekening) = leveranciers[(i + maand.Month * 2) % leveranciers.Length];
                var datum = maand.AddDays(Math.Min(26, 2 + i * 26 / perMaand));
                var nummer = $"{voorvoegsel}-{datum:yyMM}-{i + 1:00}";
                var f = await Inkoop(wie, id, verschillen, partij, nummer, datum, [new Regeldata("Levering", toeval.Next(1, 4), toeval.Next(60, 1_500), "V21", rekening, "I21")]);
                facturen.Add(f);
                hoe[nummer] = facturen.Count(x => x.Inkoop) % 17 == 0 ? Betaling.InDelen : Betaling.Gewoon;
                if (facturen.Count(x => x.Inkoop) % 11 == 0)
                {
                    // Een creditnota van een kwart, de factuur wordt netto betaald (beide in één betaling).
                    var cn = await Inkoop(wie, id, verschillen, partij, $"CN-{nummer}", datum.AddDays(1),
                        [new Regeldata("Retour", 1, Math.Round(f.Exclusief / 4, 2), "V21", rekening, "I21")], credit: true, creditVoor: nummer);
                    facturen.Add(cn);
                    hoe[nummer] = Betaling.Verzameld;
                    hoe[cn.Nummer] = Betaling.Verzameld;
                }
            }

            // ---- investeringen
            if (maand == new DateOnly(2024, 2, 1))
            {
                var f = await Inkoop(wie, id, verschillen, Praktijkset.Kantoor, "KV-INV-2502", maand.AddDays(9), [new Regeldata("Toonbank en stellingen", 1, Inventaris, "V21", "0200", "I21")]);
                facturen.Add(f);
                hoe[f.Nummer] = Betaling.Gewoon;
                await Activeer(wie, id, verschillen, f.Nummer, "inventaris", activaIds, looptijd: 60, rest: 0m);
            }

            if (maand == new DateOnly(2024, 4, 1))
            {
                var f = await Inkoop(wie, id, verschillen, Praktijkset.Lease, "LA-BUS-2504", maand.AddDays(3), [new Regeldata("Bestelbus", 1, Bus, "V21", "0300", "I21")]);
                facturen.Add(f);
                hoe[f.Nummer] = Betaling.Gewoon;
                await Activeer(wie, id, verschillen, f.Nummer, "bus", activaIds, looptijd: 60, rest: BusRest);
            }

            // ---- de bank van deze maand
            var mutaties = new List<Bankmutatie>();
            mutaties.AddRange(teLaat.Where(t => t.Datum >= maand && t.Datum <= laatste).Select(t => t.Mutatie));
            teLaat.RemoveAll(t => t.Datum <= laatste);
            var vorige = maand.AddMonths(-1);
            var teBetalen = facturen.Where(f => f.Datum.Year == vorige.Year && f.Datum.Month == vorige.Month).ToList();
            foreach (var f in teBetalen.Where(f => !f.Credit))
            {
                var dag = maand.AddDays(f.Inkoop ? 8 : 3 + (Math.Abs(f.Nummer.GetHashCode(StringComparison.Ordinal)) % 20));
                var teken = f.Inkoop ? -1 : 1;
                switch (hoe[f.Nummer])
                {
                    case Betaling.Gewoon:
                        mutaties.Add(new Bankmutatie(dag, teken * f.Totaal, $"Factuur {f.Nummer}", f.Iban, f.Relatie));
                        betalingen.Add((f.Nummer, dag, teken * f.Totaal));
                        break;
                    case Betaling.InDelen:
                        // 60% nu, de rest twee maanden later (bij december over de jaargrens).
                        var deel = Math.Round(f.Totaal * 0.6m, 2);
                        mutaties.Add(new Bankmutatie(dag, teken * deel, $"Deelbetaling factuur {f.Nummer}", f.Iban, f.Relatie));
                        betalingen.Add((f.Nummer, dag, teken * deel));
                        var later = dag.AddMonths(2);
                        if (later <= Einde)
                        {
                            teLaat.Add((later, new Bankmutatie(later, teken * (f.Totaal - deel), $"Restant factuur {f.Nummer}", f.Iban, f.Relatie)));
                            betalingen.Add((f.Nummer, later, teken * (f.Totaal - deel)));
                        }

                        break;
                    case Betaling.TeVeel:
                        mutaties.Add(new Bankmutatie(dag, f.Totaal + 10m, $"Factuur {f.Nummer}", f.Iban, f.Relatie));
                        betalingen.Add((f.Nummer, dag, f.Totaal + 10m));
                        break;
                    case Betaling.Verzameld when f.Inkoop:
                        // Factuur min creditnota in één keer.
                        var creditnota = teBetalen.Concat(facturen).FirstOrDefault(c => c.CreditVoor == f.Nummer);
                        var netto = f.Totaal + (creditnota?.Totaal ?? 0m);
                        mutaties.Add(new Bankmutatie(dag, -netto, $"Facturen {f.Nummer} {creditnota?.Nummer}".Trim(), f.Iban, f.Relatie));
                        betalingen.Add((f.Nummer, dag, -f.Totaal));
                        if (creditnota is not null)
                        {
                            betalingen.Add((creditnota.Nummer, dag, -creditnota.Totaal));
                        }

                        break;
                    case Betaling.Verzameld:
                        // Twee facturen van dezelfde klant in één betaling: deze en de volgende gewone van die klant.
                        var tweede = teBetalen.FirstOrDefault(x => !x.Inkoop && x.Relatie == f.Relatie && x.Nummer != f.Nummer && hoe[x.Nummer] == Betaling.Gewoon);
                        if (tweede is null)
                        {
                            mutaties.Add(new Bankmutatie(dag, f.Totaal, $"Factuur {f.Nummer}", f.Iban, f.Relatie));
                            betalingen.Add((f.Nummer, dag, f.Totaal));
                        }
                        else
                        {
                            hoe[tweede.Nummer] = Betaling.Meebetaald;
                            mutaties.RemoveAll(m => m.Omschrijving == $"Factuur {tweede.Nummer}");
                            betalingen.RemoveAll(b => b.Nummer == tweede.Nummer);
                            mutaties.Add(new Bankmutatie(dag, f.Totaal + tweede.Totaal, $"Facturen {f.Nummer} en {tweede.Nummer}", f.Iban, f.Relatie));
                            betalingen.Add((f.Nummer, dag, f.Totaal));
                            betalingen.Add((tweede.Nummer, dag, tweede.Totaal));
                        }

                        break;
                }
            }

            // De btw van het vorige kwartaal: betalen, of terugkrijgen (na de investeringen).
            if (maand.Month % 3 == 1 && maand > Start)
            {
                var kwartaal = (await btw.Jaar(wie, id, maand.AddDays(-1).Year)).Jaar.Perioden.Single(p => p.Periode.Tot == maand.AddDays(-1)).Vastgelegd;
                if (kwartaal is { AfgerondTeBetalen: not 0 } aangifte)
                {
                    mutaties.Add(new Bankmutatie(maand.AddDays(24), -aangifte.AfgerondTeBetalen, $"Omzetbelasting {aangifte.Van:MM-yyyy} t/m {aangifte.Tot:MM-yyyy}",
                        Belastingherkenning.BelastingdienstIban, "Belastingdienst"));
                }
            }

            // Het pand: de koopsom gaat in juni van de rekening (de bank staat dan rood), de hypotheek komt tien dagen later.
            if (maand == new DateOnly(2024, 6, 1))
            {
                mutaties.Add(new Bankmutatie(new DateOnly(2024, 6, 10), -Pand, "Aankoop pand Dorpsstraat 1 via notaris", "NL02ABNA0123456789", "Notariskantoor Akte"));
                mutaties.Add(new Bankmutatie(new DateOnly(2024, 6, 20), Hypotheek, "Hypothecaire lening", "NL69INGB0123456789", "Bank Hypotheken"));
            }

            if (maand > new DateOnly(2024, 6, 1))
            {
                mutaties.Add(new Bankmutatie(maand.AddDays(14), -Aflossing, "Aflossing hypothecaire lening", "NL69INGB0123456789", "Bank Hypotheken"));
                var r = Math.Round(Hypotheek * 0.045m / 12, 2);
                mutaties.Add(new Bankmutatie(maand.AddDays(14), -r, "Rente hypothecaire lening", "NL69INGB0123456789", "Bank Hypotheken"));
                rente[maand.Year] = rente.GetValueOrDefault(maand.Year) + r;
            }

            if (maand == new DateOnly(2025, 10, 1))
            {
                mutaties.Add(new Bankmutatie(new DateOnly(2025, 10, 5), BusOpbrengst, "Verkoop bestelbus", "NL91ABNA0417164300", "Autobedrijf Inruil"));
            }

            var kosten = Math.Round(12.5m + maand.Month * 0.35m, 2);
            mutaties.Add(new Bankmutatie(laatste, -kosten, $"Kosten zakelijk betalingsverkeer {maand:MM-yyyy}"));
            bankkosten[maand.Year] = bankkosten.GetValueOrDefault(maand.Year) + kosten;

            mutaties.Sort((a, b) => a.Datum.CompareTo(b.Datum));
            foreach (var m in mutaties)
            {
                saldo += m.Bedrag;
                if (saldo < laagste.Saldo)
                {
                    laagste = (saldo, m.Datum);
                }
            }

            var afschrift = new Bankafschrift(Iban, $"{maand:yyyyMM}", "EUR", maand, saldo - mutaties.Sum(m => m.Bedrag), laatste, saldo, mutaties);
            mutatiesTotaal += mutaties.Count;
            var gelezen = await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, $"BNK{maand:yyyyMM}"))), $"betaalrekening {maand:yyyy-MM}.mt940");
            if (!gelezen.IsGelukt)
            {
                verschillen.Add($"Bank {maand:yyyy-MM}: {string.Join(" ", gelezen.Fouten)}");
                continue;
            }

            foreach (var m in await bank.Lijst(wie, id))
            {
                if (await Verwerk(wie, id, m) is { } fout)
                {
                    verschillen.Add($"Bank {m.Datum} {m.Bedrag} {m.Omschrijving}: {fout}");
                }
            }

            // Het pand activeren zodra het geboekt is.
            if (maand == new DateOnly(2024, 6, 1))
            {
                var stuk = (await grootboek.Kaart(wie, id, "0100", new DateOnly(2024, 6, 1), laatste)).Mutaties is { Count: > 0 } kaart ? kaart[0] : null;
                if (stuk is null)
                {
                    verschillen.Add("Pand: niet op 0100 geboekt.");
                }
                else
                {
                    await Activeer(wie, id, verschillen, $"{stuk.Dagboek} {stuk.Boekstuk}", "pand", activaIds, looptijd: 360, rest: PandRest, begin: new DateOnly(2024, 7, 1), boekstuk: true);
                }
            }

            // ---- de bus verkocht (de opbrengst op overige vorderingen; de bank boekt daar de ontvangst)
            if (maand == new DateOnly(2025, 9, 1) && activaIds.TryGetValue("bus", out var bus))
            {
                var verkocht = await activa.Desinvesteer(wie, id, bus, new Desinvesteringsinvoer(new DateOnly(2025, 9, 30), Desinvesteringssoort.Verkoop, 100m, BusOpbrengst, "1400", "4900"));
                if (!verkocht.IsGelukt)
                {
                    verschillen.Add("Bus verkopen: " + string.Join(" ", verkocht.Fouten));
                }
            }

            // ---- afschrijven per maand
            var afgeschreven = await activa.Afschrijven(wie, id, laatste, Afschrijvingsfrequentie.Maand);
            if (!afgeschreven.IsGelukt && !afgeschreven.Fouten.All(f => f.Contains("niets meer af te schrijven", StringComparison.Ordinal)))
            {
                verschillen.Add($"Afschrijven tot {laatste}: {string.Join(" ", afgeschreven.Fouten)}");
            }

            // ---- het kwartaal: aangifte vastleggen en afsluiten (na de bank van de laatste maand)
            if (maand.Month % 3 == 0)
            {
                await Kwartaal(wie, id, verschillen, laatste);
                foreach (var f in await Invarianten.Controleer(app, wie, id, $"eind {laatste:MM-yyyy}"))
                {
                    verschillen.Add(f);
                }
            }
        }

        var verkoopAantal = facturen.Count(f => !f.Inkoop);
        var inkoopAantal = facturen.Count(f => f.Inkoop);
        uitvoer.WriteLine($"{verkoopAantal} verkoopfacturen ({facturen.Count(f => !f.Inkoop && f.Credit)} credit), {inkoopAantal} inkoopfacturen ({facturen.Count(f => f.Inkoop && f.Credit)} creditnota), {mutatiesTotaal} bankmutaties in {klok.Elapsed.TotalSeconds:0} s; laagste banksaldo {laagste.Saldo:N2} op {laagste.Datum:dd-MM-yyyy}.");

        await Controleer(wie, id, verschillen, facturen, betalingen, bankkosten, rente, saldo);
        uitvoer.WriteLine($"Klaar in {klok.Elapsed.TotalSeconds:0} s; {verschillen.Count} verschillen.");
        foreach (var v in verschillen)
        {
            uitvoer.WriteLine("  - " + v);
        }

        Assert.True(laagste.Saldo < 0, "De bank zou in juni 2024 rood moeten staan.");
        Assert.True(verschillen.Count == 0, $"{verschillen.Count} verschillen:\n" + string.Join("\n", verschillen));
    }

    /// <summary>Alles na twee jaar tegen het model.</summary>
    private async Task Controleer(Uitvoerder wie, Guid id, List<string> verschillen, List<Factuur> facturen, List<(string Nummer, DateOnly Datum, decimal Bedrag)> betalingen,
        Dictionary<int, decimal> bankkosten, Dictionary<int, decimal> rente, decimal saldo)
    {
        var grootboek = app.Dienst<GrootboekDienst>();
        var activa = app.Dienst<ActivaDienst>();
        var p25 = await grootboek.Proefbalans(wie, id, 2024);
        var p26 = await grootboek.Proefbalans(wie, id, 2025);
        decimal Saldo(Proefbalans p, string rekening) => p.Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;

        // ---- afschrijvingen volgens plan
        static int Maanden(DateOnly van, DateOnly tot) => tot < van ? 0 : (tot.Year - van.Year) * 12 + tot.Month - van.Month + 1;
        decimal Afschrijving(int jaar, DateOnly begin, decimal perMaand, DateOnly? tot = null)
        {
            var van = new DateOnly(jaar, 1, 1) > begin ? new DateOnly(jaar, 1, 1) : begin;
            var eind = new DateOnly(jaar, 12, 1);
            if (tot is { } t && t < eind)
            {
                eind = new DateOnly(t.Year, t.Month, 1);
            }

            return Maanden(van, eind) * perMaand;
        }

        var busTot = new DateOnly(2025, 9, 1);
        decimal Afschrijvingen(int jaar) =>
            Afschrijving(jaar, new DateOnly(2024, 2, 1), 250m) + Afschrijving(jaar, new DateOnly(2024, 4, 1), 600m, busTot) + Afschrijving(jaar, new DateOnly(2024, 7, 1), 700m);
        var busBoekwaarde = Bus - Maanden(new DateOnly(2024, 4, 1), busTot) * 600m;
        Controleer(verschillen, "Afschrijving 2024 (4800 + 4810 + 4820)", Saldo(p25, "4800") + Saldo(p25, "4810") + Saldo(p25, "4820"), Afschrijvingen(2024));
        Controleer(verschillen, "Afschrijving 2025 (4800 + 4810 + 4820)", Saldo(p26, "4800") + Saldo(p26, "4810") + Saldo(p26, "4820"), Afschrijvingen(2025));
        Controleer(verschillen, "Boekverlies bus (4900)", Saldo(p26, "4900"), busBoekwaarde - BusOpbrengst);

        // ---- resultaat per jaar
        decimal Resultaat(int jaar) =>
            facturen.Where(f => f.Datum.Year == jaar && !f.Inkoop).Sum(f => f.Exclusief)
            - facturen.Where(f => f.Datum.Year == jaar && f.Inkoop && !f.Nummer.Contains("INV", StringComparison.Ordinal) && !f.Nummer.Contains("BUS", StringComparison.Ordinal)).Sum(f => f.Exclusief)
            - bankkosten[jaar] - rente.GetValueOrDefault(jaar) - Afschrijvingen(jaar) - (jaar == 2025 ? busBoekwaarde - BusOpbrengst : 0m)
            + afrondingen.GetValueOrDefault(jaar);
        Controleer(verschillen, "Resultaat 2024", p25.Resultaat, Resultaat(2024));
        Controleer(verschillen, "Resultaat 2025", p26.Resultaat, Resultaat(2025));
        Controleer(verschillen, "Afronding btw 2024 (4910)", -Saldo(p25, "4910"), afrondingen.GetValueOrDefault(2024));
        Controleer(verschillen, "Afronding btw 2025 (4910)", -Saldo(p26, "4910"), afrondingen.GetValueOrDefault(2025));

        // ---- jaarovergang: beginstand 2025 = eindstand 2024, het resultaat op het onverdeelde resultaat
        var schema = await grootboek.Schema(wie, id);
        var onverdeeld = schema.MetFunctie(Rekeningfunctie.OnverdeeldResultaat).First().Nummer;
        var overzicht = await grootboek.Maandoverzicht(wie, id, new DateOnly(2025, 1, 1), Einde);
        foreach (var r in overzicht.Regels)
        {
            var eind25 = Saldo(p25, r.Rekening.Nummer);
            var verwacht = !r.Rekening.IsBalans ? 0m : r.Rekening.Nummer == onverdeeld ? eind25 - p25.Resultaat : eind25;
            Controleer(verschillen, $"Beginstand 2025 rekening {r.Rekening.Nummer}", r.Begin, verwacht);
        }

        // ---- bank, hypotheek, vorderingen
        Controleer(verschillen, "Bank grootboek eind 2025", Saldo(p26, "1100"), saldo);
        var bankstand = (await app.Dienst<BankDienst>().Stand(wie, id)).Single(b => b.Dagboek == "BNK");
        Controleer(verschillen, "Bank afschrift", bankstand.Saldo, saldo);
        Controleer(verschillen, "Hypotheek eind 2024 (0700)", -Saldo(p25, "0700"), Hypotheek - 6 * Aflossing);
        Controleer(verschillen, "Hypotheek eind 2025 (0700)", -Saldo(p26, "0700"), Hypotheek - 18 * Aflossing);
        Controleer(verschillen, "Overige vorderingen eind 2025 (verkoop bus ontvangen)", Saldo(p26, "1400"), 0m);
        Controleer(verschillen, "Vraagposten eind 2025", Saldo(p26, "2000"), 0m);

        // ---- activastaat
        foreach (var jaar in new[] { 2024, 2025 })
        {
            var staat = await activa.Activastaat(wie, id, jaar);
            if (staat.Any(g => !g.Sluit))
            {
                verschillen.Add($"Activastaat {jaar} sluit niet: {string.Join(", ", staat.Where(g => !g.Sluit).Select(g => g.Activarekening))}.");
            }

            Controleer(verschillen, $"Activastaat {jaar} afschrijving", staat.Sum(g => g.Totaal.Afschrijving), Afschrijvingen(jaar));
        }

        var staat25 = await activa.Activastaat(wie, id, 2024);
        var staat26 = await activa.Activastaat(wie, id, 2025);
        foreach (var groep in staat26)
        {
            var vorig = staat25.FirstOrDefault(g => g.Activarekening == groep.Activarekening);
            Controleer(verschillen, $"Activastaat {groep.Activarekening} boekwaarde begin 2025", groep.Totaal.BoekwaardeBegin, vorig is null ? 0m : vorig.Totaal.AanschafEind - vorig.Totaal.AfschrijvingEind);
        }

        Controleer(verschillen, "Vervoermiddelen eind 2025 (0300)", Saldo(p26, "0300"), 0m);
        Controleer(verschillen, "Afschrijving vervoermiddelen eind 2025 (0310)", Saldo(p26, "0310"), 0m);

        // ---- openstaande posten per jaareinde, per factuur
        var openposten = app.Dienst<OpenpostenDienst>();
        foreach (var peil in new[] { new DateOnly(2024, 12, 31), Einde })
        {
            var debiteuren = (await openposten.Debiteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
            var crediteuren = (await openposten.Crediteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
            Dictionary<string, decimal> Verwacht(bool inkoop) => facturen.Where(f => f.Inkoop == inkoop && f.Datum <= peil)
                .Select(f => (f.Nummer, Open: Open(f, peil)))
                .Where(x => x.Open != 0).ToDictionary(x => x.Nummer, x => x.Open);
            decimal Open(Factuur f, DateOnly peildatum)
            {
                // Gecrediteerd: de factuur en de creditfactuur heffen elkaar op vanaf de creditdatum.
                if (!f.Inkoop && facturen.FirstOrDefault(c => c.CreditVoor == f.Nummer) is { } c && c.Datum <= peildatum)
                {
                    return 0m;
                }

                if (!f.Inkoop && f.CreditVoor is not null)
                {
                    return 0m;
                }

                var betaald = betalingen.Where(b => b.Nummer == f.Nummer && b.Datum <= peildatum).Sum(b => f.Inkoop ? -b.Bedrag : b.Bedrag);
                return f.Totaal - betaald;
            }

            Vergelijk(verschillen, $"Debiteuren {peil:dd-MM-yyyy}", debiteuren, Verwacht(false));
            Vergelijk(verschillen, $"Crediteuren {peil:dd-MM-yyyy}", crediteuren, Verwacht(true));
        }

        // ---- btw per kwartaal zoals de facturen; aansluitingen; niets niet aangegeven
        var btw = app.Dienst<BtwaangifteDienst>();
        foreach (var jaar in new[] { 2024, 2025 })
        {
            var stand = await btw.Jaar(wie, id, jaar);
            foreach (var periode in stand.Jaar.Perioden)
            {
                var verwacht = facturen.Where(f => f.Datum >= periode.Periode.Van && f.Datum <= periode.Periode.Tot).Sum(f => f.Inkoop ? -f.Btw : f.Btw);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} berekend", periode.Berekend.TeBetalen, verwacht);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} vastgelegd", periode.Vastgelegd?.AfgerondTeBetalen, periode.Berekend.Afgerond().TeBetalen);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} verschil", periode.Verschil, 0m);
            }

            Controleer(verschillen, $"Btw {jaar} niet aangegeven", stand.Jaar.NietAangegeven, 0m);
            Controleer(verschillen, $"Btw {jaar} suppletie", stand.Jaar.Suppletie, 0m);
            foreach (var a in stand.Aansluitingen.Where(a => a.Verschil != 0))
            {
                verschillen.Add($"Btw-aansluiting {a.Van:dd-MM-yyyy}–{a.Tot:dd-MM-yyyy}: verschil {a.Verschil}.");
            }
        }

        // Te betalen btw eind 2025: het laatste kwartaal zoals ingediend (in hele euro's). Het afronden van alle aangiften is
        // bij het vastleggen naar het resultaat geboekt (4910): er blijft geen afrondingsverschil op de btw-rekeningen staan.
        var q4 = facturen.Where(f => f.Datum >= new DateOnly(2025, 10, 1)).Sum(f => f.Inkoop ? -f.Btw : f.Btw);
        Controleer(verschillen, "Btw-rekeningen eind 2025 (1500 + 1510)", -(Saldo(p26, "1500") + Saldo(p26, "1510")), q4 - laatsteAfronding);
        uitvoer.WriteLine($"Afronding van de btw-aangiften naar het resultaat: 2024 {afrondingen.GetValueOrDefault(2024):N2}, 2025 {afrondingen.GetValueOrDefault(2025):N2}.");

        // ---- nummering per jaar zonder gaten, de eerste van elk jaar opnieuw vanaf 1
        var verkoop = app.Dienst<VerkoopDienst>();
        foreach (var jaar in new[] { 2024, 2025 })
        {
            if (await verkoop.Gaten(wie, id, jaar) is { Count: > 0 } gaten)
            {
                verschillen.Add($"Nummering {jaar}: gaten {string.Join(", ", gaten)}.");
            }

            var eerste = facturen.Where(f => !f.Inkoop && f.Datum.Year == jaar).OrderBy(f => f.Datum).Select(f => f.Nummer).FirstOrDefault();
            if (eerste != Factuurnummering.Nummer(jaar, 1))
            {
                verschillen.Add($"Nummering {jaar}: eerste factuur {eerste}.");
            }
        }

        // ---- het inkoop- en verkoopboek tellen alles; hoeveel ging vanzelf
        var inkoopboek = await app.Dienst<InkoopDienst>().Inkoopboek(wie, id, Start, Einde);
        var verkoopboek = await verkoop.Verkoopboek(wie, id, Start, Einde);
        Controleer(verschillen, "Inkoopboek aantal", inkoopboek.Count, facturen.Count(f => f.Inkoop));
        Controleer(verschillen, "Verkoopboek aantal", verkoopboek.Count, facturen.Count(f => !f.Inkoop));
        Controleer(verschillen, "Inkoopboek totaal", inkoopboek.Sum(r => r.Totaal), facturen.Where(f => f.Inkoop).Sum(f => f.Totaal));
        Controleer(verschillen, "Verkoopboek totaal", verkoopboek.Sum(r => r.Totaal), facturen.Where(f => !f.Inkoop).Sum(f => f.Totaal));
        var bankLijst = await app.Dienst<BankDienst>().Lijst(wie, id, ookGeboekt: true);
        uitvoer.WriteLine($"Vanzelf: inkoop {Automatisering.Percentage(inkoopboek.Select(r => r.Verwerking)).Uitleg}; verkoop {Automatisering.Percentage(verkoopboek.Select(r => r.Verwerking)).Uitleg}; bank {Automatisering.Percentage(bankLijst.Select(m => m.Verwerking)).Uitleg}.");
        if (bankLijst.Any(m => m.Status == Banktransactiestatus.TeVerwerken))
        {
            verschillen.Add($"Bank: {bankLijst.Count(m => m.Status == Banktransactiestatus.TeVerwerken)} mutaties nog te verwerken.");
        }

        // ---- de auditfile van elk jaar teruglezen: dezelfde saldi
        var auditfile = app.Dienst<AuditfileDienst>();
        foreach (var (jaar, proef) in new[] { (2024, p25), (2025, p26) })
        {
            var xaf = await auditfile.Maak(wie, id, jaar);
            var terug = await auditfile.LeesIn(wie, new MemoryStream(xaf.Inhoud), xaf.Bestandsnaam, Ondernemingsvorm.BV);
            if (!terug.IsGelukt)
            {
                verschillen.Add($"Auditfile {jaar}: " + string.Join(" ", terug.Fouten));
                continue;
            }

            var kopie = await grootboek.Proefbalans(wie, terug.Waarde!.Administratie, jaar);
            foreach (var r in proef.Regels.Where(r => r.Saldo != 0))
            {
                Controleer(verschillen, $"Auditfile {jaar} rekening {r.Rekening.Nummer}", kopie.Regels.FirstOrDefault(k => k.Rekening.Nummer == r.Rekening.Nummer)?.Saldo ?? 0m, r.Saldo);
            }
        }

        verschillen.AddRange(await Samenhang.Controleer(app, wie, id, "na twee jaar"));
        uitvoer.WriteLine($"Resultaat 2024 {p25.Resultaat:N2}, 2025 {p26.Resultaat:N2}; bank {saldo:N2}.");
    }

    private async Task<Factuur?> Verkoopfactuur(Uitvoerder wie, Guid id, List<string> verschillen, string code, Partijdata partij, DateOnly datum, List<Verkoopregel> regels)
    {
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(code, datum, datum.AddDays(14), $"Levering {datum.ToString("MMMM yyyy", Praktijkset.Nl)}", regels));
        var definitief = concept.IsGelukt ? await verkoop.MaakDefinitief(wie, id, concept.Waarde) : null;
        if (definitief?.IsGelukt != true)
        {
            verschillen.Add($"Verkoop {datum}: {string.Join(" ", concept.Fouten.Concat(definitief?.Fouten ?? []))}");
            return null;
        }

        var exclusief = regels.Sum(r => r.Bedrag);
        var btwBedrag = regels.GroupBy(r => r.BtwCode).Sum(g => Math.Round(g.Sum(r => r.Bedrag) * (g.Key == "V9" ? 0.09m : 0.21m), 2, MidpointRounding.AwayFromZero));
        return new Factuur(definitief.Waarde!, datum, code, partij.Iban, exclusief, btwBedrag, false, concept.Waarde);
    }

    /// <summary>Een inkoopfactuur (of creditnota) als e-factuur inlezen; niet vanzelf geboekt, dan met de hand zoals op het scherm.</summary>
    private async Task<Factuur> Inkoop(Uitvoerder wie, Guid id, List<string> verschillen, Partijdata partij, string nummer, DateOnly datum, IReadOnlyList<Regeldata> regels,
        bool credit = false, string? creditVoor = null)
    {
        var f = new Factuurdata(true, partij, nummer, datum, datum.AddDays(14), credit ? "Creditnota" : "Levering", regels, Formaat.Ubl, Betaalwijze.Nummer, credit, creditVoor);
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Praktijkset.Document(f, partij, Praktijkset.Eigen)), f.Bestandsnaam, null);
        if (!gelezen.IsGeboekt)
        {
            if (gelezen.DocumentId is not { } document)
            {
                verschillen.Add($"Inkoop {nummer}: niet ingelezen: {gelezen.Melding}");
            }
            else
            {
                var code = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).FirstOrDefault(r => r.Naam == partij.Naam)?.Code;
                var uitkomst = await inkoop.Boek(wie, id, document, new Factuurinvoer(code, code is null ? new NieuweRelatie(partij.Naam, partij.Iban, partij.BtwNummer, partij.Kvk) : null,
                    nummer, datum, f.Vervaldatum, f.Omschrijving, [.. regels.Select((r, i) => new Factuurregel(r.Omschrijving, r.Rekening, r.Onzecode, r.Netto, f.RegelBtw(i)))], null, Creditnota: credit));
                if (!uitkomst.IsGelukt)
                {
                    verschillen.Add($"Inkoop {nummer}: boeken lukt niet: {string.Join(" ", uitkomst.Fouten)}");
                }
            }
        }

        var teken = credit ? -1 : 1;
        return new Factuur(nummer, datum, partij.Naam, partij.Iban, teken * f.Exclusief, teken * f.BtwTotaal, true, CreditVoor: creditVoor);
    }

    /// <summary>Een aanschaf activeren en looptijd en restwaarde zetten zoals het kantoor dat op de kaart doet.</summary>
    private async Task Activeer(Uitvoerder wie, Guid id, List<string> verschillen, string bron, string naam, Dictionary<string, Guid> ids, int looptijd, decimal rest,
        DateOnly? begin = null, bool boekstuk = false)
    {
        var activa = app.Dienst<ActivaDienst>();
        var stuk = bron;
        if (!boekstuk)
        {
            var document = (await app.Dienst<InkoopDienst>().Inkoopboek(wie, id, Start, Einde)).FirstOrDefault(r => r.Factuurnummer == bron);
            if (document is null)
            {
                verschillen.Add($"Activeren {naam}: factuur {bron} niet in het inkoopboek.");
                return;
            }

            stuk = $"{document.Dagboek} {document.Boekstuk}";
        }

        var uitkomst = await activa.Activeer(wie, id, stuk);
        if (!uitkomst.IsGelukt)
        {
            verschillen.Add($"Activeren {naam} ({stuk}): {string.Join(" ", uitkomst.Fouten)}");
            return;
        }

        var actief = (await activa.Open(wie, id, uitkomst.Waarde!.Single()))!;
        var bewaard = await activa.Bewaar(wie, id, actief.Id, actief.Invoer with { Omschrijving = naam, Looptijd = looptijd, Restwaarde = rest, BeginAfschrijving = begin ?? actief.Invoer.BeginAfschrijving });
        if (!bewaard.IsGelukt)
        {
            verschillen.Add($"Activum {naam}: {string.Join(" ", bewaard.Fouten)}");
        }

        ids[naam] = actief.Id;
    }

    /// <summary>Een mutatie die blijft staan verwerken zoals een gebruiker dat zou doen.</summary>
    private async Task<string?> Verwerk(Uitvoerder wie, Guid id, BankmutatieRegel m)
    {
        var bank = app.Dienst<BankDienst>();
        var o = m.Omschrijving;
        string? rekening = o.StartsWith("Omzetbelasting", StringComparison.Ordinal) ? "1510"
            : o.StartsWith("Aankoop pand", StringComparison.Ordinal) ? "0100"
            : o.StartsWith("Hypothecaire lening", StringComparison.Ordinal) || o.StartsWith("Aflossing", StringComparison.Ordinal) ? "0700"
            : o.StartsWith("Rente", StringComparison.Ordinal) ? "9000"
            : o.StartsWith("Kosten zakelijk", StringComparison.Ordinal) ? "9000"
            : o.StartsWith("Verkoop bestelbus", StringComparison.Ordinal) ? "1400"
            : null;
        if (rekening is not null)
        {
            var geboekt = await bank.OpRekening(wie, id, m.Id, rekening, null, null);
            return geboekt.IsGelukt ? null : string.Join(" ", geboekt.Fouten);
        }

        // Afletteren op de genoemde factuurnummers (ook "Deelbetaling factuur X", "Restant factuur X", "Facturen X en Y").
        var open = await bank.OpenPosten(wie, id);
        var posten = o.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => open.FirstOrDefault(p => p.Factuurnummer == w)).OfType<OpenpostRegel>().ToList();
        if (posten.Count == 0)
        {
            return $"geen openstaande post in \"{o}\"";
        }

        var uitkomst = await bank.Afletteren(wie, id, m.Id, [.. posten.Select(p => p.Id)]);
        return uitkomst.IsGelukt ? null : string.Join(" ", uitkomst.Fouten);
    }

    /// <summary>De aangifte van het kwartaal vastleggen en de periode afsluiten.</summary>
    private async Task Kwartaal(Uitvoerder wie, Guid id, List<string> verschillen, DateOnly tot)
    {
        var van = new DateOnly(tot.Year, tot.Month, 1).AddMonths(-2);
        var btwdienst = app.Dienst<BtwaangifteDienst>();
        var berekend = (await btwdienst.Jaar(wie, id, tot.Year)).Jaar.Perioden.Single(p => p.Periode.Van == van).Berekend;
        afrondingen[tot.Year] = afrondingen.GetValueOrDefault(tot.Year) + berekend.Afronding;
        laatsteAfronding = berekend.Afronding;
        var vastgelegd = await btwdienst.Vastleggen(wie, id, van, tot);
        if (!vastgelegd.IsGelukt)
        {
            verschillen.Add($"Btw vastleggen {van:MM-yyyy}–{tot:MM-yyyy}: {string.Join(" ", vastgelegd.Fouten)}");
        }

        var perioden = app.Dienst<PeriodeDienst>();
        var openstaand = await perioden.Openstaand(wie, id, tot);
        var afgesloten = await perioden.SluitAf(wie, id, tot);
        if (!afgesloten.IsGelukt)
        {
            verschillen.Add($"Afsluiten tot {tot:dd-MM-yyyy}: {string.Join(" ", afgesloten.Fouten)} ({string.Join("; ", openstaand)})");
        }
    }

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie("Twee Jaar De Spaak B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);

        // Het pand komt op de rekeningen voor bedrijfsgebouwen uit het standaardschema (0100, 0110 en 4820).
        var grootboek = app.Dienst<GrootboekDienst>();
        var begin = await grootboek.Boek(wie, id, new Journaalpost("MEM", "2024-0001", Start, "Beginbalans 2024",
            [new("1100", Beginsaldo, "Bank"), new("0500", -18_000m, "Aandelenkapitaal"), new("0510", -(Beginsaldo - 18_000m), "Overige reserves")]));
        Assert.True(begin.IsGelukt, string.Join("; ", begin.Fouten));
        return id;
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id, Partijdata p)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), p.Iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    private static void Controleer(List<string> verschillen, string wat, decimal? werkelijk, decimal verwacht)
    {
        if (werkelijk != verwacht)
        {
            verschillen.Add($"{wat}: {werkelijk} in plaats van {verwacht}.");
        }
    }

    private static void Vergelijk(List<string> verschillen, string wat, IReadOnlyDictionary<string, decimal> werkelijk, IReadOnlyDictionary<string, decimal> verwacht)
    {
        var tekst = new StringBuilder();
        foreach (var (nummer, bedrag) in werkelijk.Where(x => !verwacht.TryGetValue(x.Key, out var v) || v != x.Value))
        {
            tekst.Append(CultureInfo.InvariantCulture, $" {nummer} staat open met {bedrag}{(verwacht.TryGetValue(nummer, out var v) ? $" (verwacht {v})" : "")};");
        }

        foreach (var nummer in verwacht.Keys.Except(werkelijk.Keys))
        {
            tekst.Append(CultureInfo.InvariantCulture, $" {nummer} staat niet open (verwacht {verwacht[nummer]});");
        }

        if (tekst.Length > 0)
        {
            verschillen.Add($"{wat}:{tekst}");
        }
    }
}
