using System.Diagnostics;
using System.Globalization;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Een heel jaar en de jaarovergang: vijftien maanden (2025 en het eerste kwartaal van 2026) met verkoop, inkoop
/// (e-facturen), de bank per maand, een vast actief met afschrijving per kwartaal, de btw-aangifte per kwartaal
/// (vastgelegd en betaald in de maand erna), perioden die worden afgesloten, een inkoopfactuur van december die pas in
/// januari binnenkomt en facturen van december die in januari betaald worden. Daarna moet alles over de jaargrens
/// kloppen: de beginstand van 2026 is de eindstand van 2025 met het resultaat op het onverdeelde resultaat, de
/// nummering begint opnieuw, de openstaande posten per peildatum, de btw per kwartaal, de activastaat, de bank, de
/// keten en de auditfiles van beide jaren. Afwijkingen worden verzameld en samen gemeld.
/// </summary>
public sealed class JaarTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";
    private const decimal Beginsaldo = 20_000m;
    private const decimal Aanschaf = 6_000m;
    private static readonly DateOnly Start = new(2025, 1, 1);
    private static readonly DateOnly Einde = new(2026, 3, 31);

    private sealed record Factuur(string Nummer, DateOnly Datum, string Relatie, string Iban, decimal Exclusief, decimal Btw, bool Inkoop)
    {
        public decimal Totaal => Exclusief + Btw;
    }

    [Fact]
    public async Task Een_heel_jaar_en_de_jaarovergang()
    {
        var klok = Stopwatch.StartNew();
        var verschillen = new List<string>();
        var toeval = new Random(2025);
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var grootboek = app.Dienst<GrootboekDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var btw = app.Dienst<BtwaangifteDienst>();
        var perioden = app.Dienst<PeriodeDienst>();
        var activa = app.Dienst<ActivaDienst>();

        var klanten = new List<(string Code, Partijdata Partij)>();
        foreach (var p in new[] { Praktijkset.Fietsplan, Praktijkset.Hotel, Praktijkset.Steen })
        {
            klanten.Add((await Klant(wie, id, p), p));
        }

        var leveranciers = new[] { (Praktijkset.Groothandel, "RG", "7000"), (Praktijkset.Energie, "EO", "4110"), (Praktijkset.Kantoor, "KV", "4300") };
        var facturen = new List<Factuur>();
        var bankkosten = new Dictionary<int, decimal>();
        var saldo = Beginsaldo;
        var afgesloten = new List<DateOnly>();

        for (var maand = Start; maand <= Einde; maand = maand.AddMonths(1))
        {
            var laatste = maand.AddMonths(1).AddDays(-1);

            // ---- het kwartaal ervoor: eerst de late inkoopfactuur van december, dan de aangifte vastleggen en afsluiten
            if (maand.Month % 3 == 1 && maand > Start)
            {
                var kwartaalEind = maand.AddDays(-1);
                if (kwartaalEind == new DateOnly(2025, 12, 31))
                {
                    facturen.Add(await Inkoop(wie, id, verschillen, Praktijkset.Hamer, "H-25-099", new DateOnly(2025, 12, 29), [new Regeldata("Reparatie werkplaats", 1, 850m, "V21", "4900", "I21")]));
                }

                await Kwartaal(wie, id, verschillen, kwartaalEind, facturen);
                afgesloten.Add(kwartaalEind);
            }

            // ---- verkoop: elke klant een factuur, het hotel met 9% en 21%
            foreach (var (code, partij) in klanten)
            {
                var datum = maand.AddDays(4 + klanten.FindIndex(k => k.Code == code) * 5);
                List<Verkoopregel> regels = [new("Fietsen", toeval.Next(1, 5), toeval.Next(400, 2_400), "8000", "V21")];
                if (partij == Praktijkset.Hotel)
                {
                    regels.Add(new Verkoopregel("Onderhoud", toeval.Next(4, 12), 45m, "8000", "V9"));
                }

                var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(code, datum, datum.AddDays(14), $"Levering {datum.ToString("MMMM yyyy", Praktijkset.Nl)}", regels));
                var definitief = concept.IsGelukt ? await verkoop.MaakDefinitief(wie, id, concept.Waarde) : null;
                if (definitief?.IsGelukt != true)
                {
                    verschillen.Add($"Verkoop {datum}: {string.Join(" ", concept.Fouten.Concat(definitief?.Fouten ?? []))}");
                    continue;
                }

                var exclusief = regels.Sum(r => r.Bedrag);
                var btwBedrag = regels.GroupBy(r => r.BtwCode).Sum(g => Math.Round(g.Sum(r => r.Bedrag) * (g.Key == "V9" ? 0.09m : 0.21m), 2, MidpointRounding.AwayFromZero));
                facturen.Add(new Factuur(definitief.Waarde!, datum, code, partij.Iban, exclusief, btwBedrag, false));
            }

            // ---- inkoop: elke leverancier een e-factuur
            foreach (var (partij, voorvoegsel, rekening) in leveranciers)
            {
                var datum = maand.AddDays(2 + Array.FindIndex(leveranciers, l => l.Item2 == voorvoegsel) * 6);
                facturen.Add(await Inkoop(wie, id, verschillen, partij, $"{voorvoegsel}-{datum:yyyyMM}", datum,
                    [new Regeldata("Levering", toeval.Next(1, 4), toeval.Next(80, 900), "V21", rekening, "I21")]));
            }

            // ---- de bank van deze maand: de facturen van de vorige maand, de btw van het vorige kwartaal, de kosten
            var mutaties = new List<Bankmutatie>();
            var vorige = maand.AddMonths(-1);
            foreach (var f in facturen.Where(f => f.Datum.Year == vorige.Year && f.Datum.Month == vorige.Month && f.Nummer != "H-25-099"))
            {
                mutaties.Add(new Bankmutatie(maand.AddDays(f.Inkoop ? 7 : 2), f.Inkoop ? -f.Totaal : f.Totaal, $"Factuur {f.Nummer}", f.Iban, f.Relatie));
            }

            // De late factuur van december wordt in februari betaald.
            if (maand == new DateOnly(2026, 2, 1))
            {
                var laat = facturen.Single(f => f.Nummer == "H-25-099");
                mutaties.Add(new Bankmutatie(maand.AddDays(9), -laat.Totaal, "Factuur H-25-099", laat.Iban, "Bouwbedrijf Hamer B.V."));
            }

            if (maand.Month % 3 == 1 && maand > Start)
            {
                var vastgelegd = (await btw.Jaar(wie, id, maand.AddDays(-1).Year)).Jaar.Perioden.Single(p => p.Periode.Tot == maand.AddDays(-1)).Vastgelegd;
                if (vastgelegd is { AfgerondTeBetalen: not 0 } aangifte)
                {
                    mutaties.Add(new Bankmutatie(maand.AddDays(24), -aangifte.AfgerondTeBetalen, $"Omzetbelasting {aangifte.Van:MM-yyyy} t/m {aangifte.Tot:MM-yyyy}", Belastingherkenning.BelastingdienstIban, "Belastingdienst"));
                }
            }

            if (maand == new DateOnly(2025, 3, 1))
            {
                mutaties.Add(new Bankmutatie(new DateOnly(2025, 3, 10), -Aanschaf, "Aankoop werkbank", "NL91ABNA0417164300", "Machinehandel Draai"));
            }

            var kosten = Math.Round(12.5m + maand.Month * 0.35m, 2);
            mutaties.Add(new Bankmutatie(laatste, -kosten, $"Kosten zakelijk betalingsverkeer {maand:MM-yyyy}"));
            bankkosten[maand.Year] = bankkosten.GetValueOrDefault(maand.Year) + kosten;

            mutaties.Sort((a, b) => a.Datum.CompareTo(b.Datum));
            var afschrift = new Bankafschrift(Iban, $"{maand:yyyyMM}", "EUR", maand, saldo, laatste, saldo + mutaties.Sum(m => m.Bedrag), mutaties);
            saldo = afschrift.Eindsaldo;
            var gelezen = await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, $"BNK{maand:yyyyMM}"))), $"betaalrekening {maand:yyyy-MM}.mt940");
            if (!gelezen.IsGelukt)
            {
                verschillen.Add($"Bank {maand:yyyy-MM}: {string.Join(" ", gelezen.Fouten)}");
                continue;
            }

            foreach (var m in await bank.Lijst(wie, id))
            {
                var fout = await Verwerk(wie, id, m);
                if (fout is not null)
                {
                    verschillen.Add($"Bank {m.Datum} {m.Bedrag} {m.Omschrijving}: {fout}");
                }
            }

            // ---- het vaste actief na de aankoop, afschrijven per kwartaal
            if (maand == new DateOnly(2025, 3, 1))
            {
                var (afschrijving, kostenrekening, _) = await activa.Rekeningvoorstel(wie, id, "0200");
                var actief = await activa.Bewaar(wie, id, null, new ActivumInvoer("Werkbank", new DateOnly(2025, 3, 10), Aanschaf, 0m, new DateOnly(2025, 3, 1), 60,
                    Afschrijvingsmethode.Lineair, "0200", afschrijving ?? "0210", kostenrekening ?? "4800"));
                if (!actief.IsGelukt)
                {
                    verschillen.Add("Activum: " + string.Join(" ", actief.Fouten));
                }
            }

            if (maand.Month % 3 == 0)
            {
                var afgeschreven = await activa.Afschrijven(wie, id, laatste, Afschrijvingsfrequentie.Kwartaal);
                if (!afgeschreven.IsGelukt)
                {
                    verschillen.Add($"Afschrijven tot {laatste}: {string.Join(" ", afgeschreven.Fouten)}");
                }
            }
        }

        uitvoer.WriteLine($"{facturen.Count(f => !f.Inkoop)} verkoop, {facturen.Count(f => f.Inkoop)} inkoop, 15 afschriften in {klok.Elapsed.TotalSeconds:0.0} s.");

        // ---- in een afgesloten periode kan niets meer geboekt worden
        var memo = await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-9001", new DateOnly(2025, 12, 15), "Te laat", [new("4900", 10m), new("1100", -10m)]));
        if (memo.IsGelukt)
        {
            verschillen.Add("Boeken in de afgesloten periode december 2025 lukt.");
        }

        // ---- de jaarovergang: beginstand 2026 = eindstand 2025, het resultaat op het onverdeelde resultaat
        var schema = await grootboek.Schema(wie, id);
        var onverdeeld = schema.MetFunctie(Rekeningfunctie.OnverdeeldResultaat).First().Nummer;
        var p26 = await grootboek.Proefbalans(wie, id, 2025);
        var p27 = await grootboek.Proefbalans(wie, id, 2026);
        var overzicht = await grootboek.Maandoverzicht(wie, id, new DateOnly(2026, 1, 1), Einde);
        foreach (var r in overzicht.Regels)
        {
            var eind26 = p26.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Rekening.Nummer)?.Saldo ?? 0m;
            var verwacht = !r.Rekening.IsBalans ? 0m : r.Rekening.Nummer == onverdeeld ? eind26 - p26.Resultaat : eind26;
            Controleer(verschillen, $"Beginstand 2026 rekening {r.Rekening.Nummer}", r.Begin, verwacht);
            var eind27 = p27.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Rekening.Nummer)?.Saldo ?? 0m;
            Controleer(verschillen, $"Eindstand 2026 rekening {r.Rekening.Nummer} (maandoverzicht tegen proefbalans)", r.Eind, eind27);
        }

        Controleer(verschillen, "Proefbalans 2025 in evenwicht", p26.Regels.Sum(r => r.Saldo), 0m);
        Controleer(verschillen, "Proefbalans 2026 in evenwicht", p27.Regels.Sum(r => r.Saldo), 0m);

        decimal Resultaat(int jaar) =>
            facturen.Where(f => f.Datum.Year == jaar).Sum(f => f.Inkoop ? -f.Exclusief : f.Exclusief) - bankkosten[jaar] - (jaar == 2025 ? 10 * 100m : 3 * 100m);
        Controleer(verschillen, "Resultaat 2025", p26.Resultaat, Resultaat(2025));
        Controleer(verschillen, "Resultaat 2026 (1e kwartaal)", p27.Resultaat, Resultaat(2026));

        // ---- nummering per jaar
        var nummers26 = facturen.Where(f => !f.Inkoop && f.Datum.Year == 2025).Select(f => f.Nummer).ToList();
        var nummers27 = facturen.Where(f => !f.Inkoop && f.Datum.Year == 2026).Select(f => f.Nummer).ToList();
        if (nummers27.FirstOrDefault() != Factuurnummering.Nummer(2026, 1) || nummers26.FirstOrDefault() != Factuurnummering.Nummer(2025, 1))
        {
            verschillen.Add($"Nummering: eerste van 2025 {nummers26.FirstOrDefault()}, van 2026 {nummers27.FirstOrDefault()}.");
        }

        foreach (var jaar in new[] { 2025, 2026 })
        {
            if (await verkoop.Gaten(wie, id, jaar) is { Count: > 0 } gaten)
            {
                verschillen.Add($"Nummering {jaar}: gaten {string.Join(", ", gaten)}.");
            }
        }

        // ---- openstaande posten per peildatum: wat in de laatste maand gefactureerd is (en de late factuur)
        var openposten = app.Dienst<OpenpostenDienst>();
        foreach (var peil in new[] { new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31), Einde })
        {
            var debiteuren = (await openposten.Debiteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
            var crediteuren = (await openposten.Crediteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
            bool Open(Factuur f) => f.Datum <= peil && (f.Datum.Year == peil.Year && f.Datum.Month == peil.Month || (f.Nummer == "H-25-099" && peil < new DateOnly(2026, 2, 10)));
            Vergelijk(verschillen, $"Debiteuren {peil}", debiteuren, facturen.Where(f => !f.Inkoop && Open(f)).ToDictionary(f => f.Nummer, f => f.Totaal));
            Vergelijk(verschillen, $"Crediteuren {peil}", crediteuren, facturen.Where(f => f.Inkoop && Open(f)).ToDictionary(f => f.Nummer, f => f.Totaal));
        }

        // ---- btw: elk kwartaal zoals de facturen, de aansluitingen sluiten, niets na te geven
        foreach (var jaar in new[] { 2025, 2026 })
        {
            if (jaar == 2026)
            {
                await Kwartaal(wie, id, verschillen, Einde, facturen);
            }

            var stand = await btw.Jaar(wie, id, jaar);
            foreach (var periode in stand.Jaar.Perioden.Where(p => p.Periode.Tot <= Einde))
            {
                var inPeriode = facturen.Where(f => f.Datum >= periode.Periode.Van && f.Datum <= periode.Periode.Tot).ToList();
                var verwacht = inPeriode.Sum(f => f.Inkoop ? -f.Btw : f.Btw);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} berekend", periode.Berekend.TeBetalen, verwacht);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} vastgelegd (afgerond)", periode.Vastgelegd?.AfgerondTeBetalen, periode.Berekend.Afgerond().TeBetalen);
                Controleer(verschillen, $"Btw {periode.Periode.Van:MM-yyyy} verschil", periode.Verschil, 0m);
            }

            foreach (var a in stand.Aansluitingen.Where(a => a.Tot <= Einde && a.Verschil != 0))
            {
                verschillen.Add($"Btw-aansluiting {a.Van}–{a.Tot}: verschil {a.Verschil}.");
            }

            Controleer(verschillen, $"Btw {jaar} niet aangegeven", stand.Jaar.NietAangegeven, 0m);
            Controleer(verschillen, $"Btw {jaar} suppletie", stand.Jaar.Suppletie, 0m);
        }

        // ---- activastaat: eind 2025 = begin 2026
        var staat26 = (await activa.Activastaat(wie, id, 2025)).Single();
        var staat27 = (await activa.Activastaat(wie, id, 2026)).Single();
        Controleer(verschillen, "Afschrijving 2025", staat26.Totaal.Afschrijving, 1_000m);
        Controleer(verschillen, "Afschrijving 2026 (1e kwartaal)", staat27.Totaal.Afschrijving, 300m);
        Controleer(verschillen, "Activastaat boekwaarde begin 2026", staat27.Totaal.BoekwaardeBegin, staat26.Totaal.AanschafEind - staat26.Totaal.AfschrijvingEind);
        if (!staat26.Sluit || !staat27.Sluit)
        {
            verschillen.Add("Activastaat sluit niet op het grootboek.");
        }

        // ---- bank en keten
        var bankstand = (await bank.Stand(wie, id)).Single(b => b.Dagboek == "BNK");
        Controleer(verschillen, "Bank afschrift", bankstand.Saldo, saldo);
        Controleer(verschillen, "Bank grootboek", bankstand.Grootboek, saldo);
        if (await grootboek.ControleerKeten(wie, id) is { Count: > 0 } keten)
        {
            verschillen.Add("Keten: " + string.Join("; ", keten));
        }

        // ---- de auditfile van elk jaar teruglezen: dezelfde saldi
        var auditfile = app.Dienst<AuditfileDienst>();
        foreach (var (jaar, proef) in new[] { (2025, p26), (2026, p27) })
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

        uitvoer.WriteLine($"Resultaat 2025 {p26.Resultaat}, 2026 {p27.Resultaat}; afgesloten t/m {afgesloten.LastOrDefault()}; klaar in {klok.Elapsed.TotalSeconds:0.0} s; {verschillen.Count} verschillen.");
        foreach (var v in verschillen)
        {
            uitvoer.WriteLine("  - " + v);
        }

        Assert.True(verschillen.Count == 0, $"{verschillen.Count} verschillen:\n" + string.Join("\n", verschillen));
    }

    /// <summary>De aangifte van het kwartaal vastleggen en de periode afsluiten.</summary>
    private async Task Kwartaal(Uitvoerder wie, Guid id, List<string> verschillen, DateOnly tot, List<Factuur> facturen)
    {
        var van = new DateOnly(tot.Year, tot.Month, 1).AddMonths(-2);
        var vastgelegd = await app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, van, tot);
        if (!vastgelegd.IsGelukt)
        {
            verschillen.Add($"Btw vastleggen {van}–{tot}: {string.Join(" ", vastgelegd.Fouten)}");
        }

        var openstaand = await app.Dienst<PeriodeDienst>().Openstaand(wie, id, tot);
        var afgesloten = await app.Dienst<PeriodeDienst>().SluitAf(wie, id, tot);
        if (!afgesloten.IsGelukt)
        {
            verschillen.Add($"Afsluiten tot {tot}: {string.Join(" ", afgesloten.Fouten)} ({string.Join("; ", openstaand)})");
        }

        _ = facturen;
    }

    /// <summary>Een inkoopfactuur als e-factuur inlezen; niet vanzelf geboekt, dan met de hand zoals op het scherm.</summary>
    private async Task<Factuur> Inkoop(Uitvoerder wie, Guid id, List<string> verschillen, Partijdata partij, string nummer, DateOnly datum, IReadOnlyList<Regeldata> regels)
    {
        var f = new Factuurdata(true, partij, nummer, datum, datum.AddDays(14), "Levering", regels, Formaat.Ubl, Betaalwijze.Nummer);
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
                    nummer, datum, f.Vervaldatum, f.Omschrijving, [.. regels.Select((r, i) => new Factuurregel(r.Omschrijving, r.Rekening, r.Onzecode, r.Netto, f.RegelBtw(i)))], null));
                if (!uitkomst.IsGelukt)
                {
                    verschillen.Add($"Inkoop {nummer}: boeken lukt niet: {string.Join(" ", uitkomst.Fouten)}");
                }
            }
        }

        return new Factuur(nummer, datum, partij.Naam, partij.Iban, f.Exclusief, f.BtwTotaal, true);
    }

    /// <summary>Een mutatie die blijft staan verwerken: btw en aankoop op rekening, de bankkosten, anders afletteren op het genoemde nummer.</summary>
    private async Task<string?> Verwerk(Uitvoerder wie, Guid id, BankmutatieRegel m)
    {
        var bank = app.Dienst<BankDienst>();
        string? rekening = m.Omschrijving.StartsWith("Omzetbelasting", StringComparison.Ordinal) ? "1510"
            : m.Omschrijving.StartsWith("Aankoop werkbank", StringComparison.Ordinal) ? "0200"
            : m.Omschrijving.StartsWith("Kosten zakelijk", StringComparison.Ordinal) ? "9000"
            : null;
        if (rekening is not null)
        {
            var geboekt = await bank.OpRekening(wie, id, [m.Id], rekening, null);
            return geboekt.IsGelukt ? null : string.Join(" ", geboekt.Fouten);
        }

        var nummer = m.Omschrijving.Replace("Factuur ", "", StringComparison.Ordinal).Trim();
        var post = (await bank.OpenPosten(wie, id)).FirstOrDefault(p => p.Factuurnummer == nummer);
        if (post is null)
        {
            return $"factuur {nummer} staat niet open";
        }

        var uitkomst = await bank.Afletteren(wie, id, m.Id, [post.Id], null);
        return uitkomst.IsGelukt ? $"met de hand afgeletterd ({m.Melding})" : string.Join(" ", uitkomst.Fouten);
    }

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie("Jaar De Spaak B.V.");
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
        var begin = await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2025-0001", Start, "Beginbalans 2025",
            [new("1100", Beginsaldo, "Bank"), new("0500", -18_000m, "Aandelenkapitaal"), new("0510", -2_000m, "Overige reserves")]));
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
