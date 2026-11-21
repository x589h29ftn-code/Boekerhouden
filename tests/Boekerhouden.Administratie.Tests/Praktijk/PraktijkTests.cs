using System.Diagnostics;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// De testprocedure met een verzonnen kwartaal (<see cref="Praktijkset"/>): 100 inkoopfacturen en 100 verkoopfacturen
/// inlezen, wat het pakket niet zelf boekt met de hand boeken, de bankafschriften inlezen en de mutaties die blijven
/// staan verwerken zoals een gebruiker dat zou doen; daarna moet alles kloppen: bank, debiteuren, crediteuren,
/// vraagposten, kruisposten, lonen, de btw-aangifte van het kwartaal, de keten en een auditfile die terug te lezen is.
/// Afwijkingen worden verzameld en samen gemeld, zodat één run alles laat zien.
/// </summary>
public sealed class PraktijkTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public void De_set_is_compleet_en_de_afschriften_sluiten()
    {
        var set = Praktijkset.Maak();
        var bestanden = set.Bestanden();

        Assert.Equal(100, set.Inkoop.Count);
        Assert.Equal(100, set.Verkoop.Count);
        Assert.Equal((101, 100, 6), (bestanden.Count(b => b.Map == "inkoop"), bestanden.Count(b => b.Map == "verkoop"), bestanden.Count(b => b.Map == "bank")));
        Assert.Equal(bestanden.Count, bestanden.Select(b => b.Map + "/" + b.Naam).Distinct().Count());
        Assert.All(set.Inkoop.Concat(set.Verkoop), f => Assert.True(Iban.IsGeldig(f.Partij.Iban), f.Partij.Naam));
        Assert.All(set.Inkoop.Concat(set.Verkoop).Where(f => f.Partij.BtwNummer is not null), f => Assert.True(BtwNummer.IsGeldig(f.Partij.BtwNummer), f.Partij.BtwNummer));
        Assert.All(set.Verkoop.Where(f => !f.Credit), f => Assert.True(Betalingskenmerk.IsGeldig(f.Kenmerk), f.Nummer));
        Assert.Equal(4, set.Verkoop.Count(f => f.Credit));
        Assert.Equal(2, set.Inkoop.Count(f => f.Credit));

        // Elk afschrift sluit; de verzamelbetalingen worden losse mutaties.
        foreach (var bank in bestanden.Where(b => b.Map == "bank"))
        {
            var afschriften = Bankbestand.Lees(new MemoryStream(bank.Inhoud));
            Assert.All(afschriften, a => Assert.True(a.SluitAan, $"{bank.Naam}: verschil {a.Verschil}"));
        }

        var januari = Camt053Lezer.Lees(new MemoryStream(bestanden.Single(b => b.Naam.StartsWith("01", StringComparison.Ordinal)).Inhoud));
        Assert.Equal(set.Betaal.Where(p => p.Datum.Month == 1).Sum(p => p.Batch?.Count ?? 1), januari.Sum(a => a.Mutaties.Count));
        Assert.True(set.AangifteQ1.Afgerond().TeBetalen > 0);
    }

    [Fact]
    public async Task Een_kwartaal_inkoop_verkoop_en_bank()
    {
        var verschillen = await Doorloop(1);
        Assert.True(verschillen.Count == 0, $"{verschillen.Count} verschillen:\n" + string.Join("\n", verschillen));
    }

    /// <summary>
    /// Dezelfde procedure met de set vijf keer zo groot (500 inkoop, 500 verkoop, de bank erbij): meldt hoeveel er vanzelf
    /// gaat en wat er misgaat, zonder te falen op verschillen; alleen een fout in het pakket zelf laat hem falen.
    /// </summary>
    [PraktijkStressFact]
    public async Task Vijf_keer_zo_groot()
    {
        var verschillen = await Doorloop(PraktijkStressFactAttribute.Rondes);
        uitvoer.WriteLine($"Verschillen: {verschillen.Count}");
    }

    private async Task<List<string>> Doorloop(int rondes)
    {
        var set = Praktijkset.Maak(rondes);
        var bestanden = set.Bestanden();
        var verschillen = new List<string>();
        var (aantalInkoop, aantalVerkoop) = (set.Inkoop.Count, set.Verkoop.Count);
        var klok = Stopwatch.StartNew();
        var wie = await app.Beheerder();
        var id = await Inrichten(wie, set);
        var grootboek = app.Dienst<GrootboekDienst>();

        // ---- inkoop: per bestand inlezen; wat niet vanzelf geboekt is, met de hand (zoals op het scherm)
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var inkoopAuto = 0;
        var inkoopDubbel = 0;
        var perNaam = set.Inkoop.ToDictionary(f => f.Bestandsnaam);
        var nietVanzelf = new List<string>();
        foreach (var bestand in bestanden.Where(b => b.Map == "inkoop").OrderBy(b => b.Naam, StringComparer.Ordinal))
        {
            var gelezen = await inkoop.Lees(wie, id, new MemoryStream(bestand.Inhoud), bestand.Naam, null);
            if (gelezen.Dubbel)
            {
                inkoopDubbel++;
                continue;
            }

            if (!perNaam.TryGetValue(bestand.Naam, out var f))
            {
                verschillen.Add($"Inkoop {bestand.Naam}: niet als dubbel herkend ({gelezen.Melding}).");
                continue;
            }

            if (gelezen.IsGeboekt)
            {
                inkoopAuto++;
                continue;
            }

            nietVanzelf.Add($"inkoop {f.Nummer} {f.Partij.Naam} ({f.Formaat}): {gelezen.Melding}");

            var code = (await inrichting.Relaties(wie, id)).FirstOrDefault(r => r.Naam == f.Partij.Naam)?.Code;
            var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(f.Partij.Naam, f.Partij.Iban, f.Partij.BtwNummer, f.Partij.Kvk) : null,
                f.Nummer, f.Datum, f.Vervaldatum, f.Omschrijving,
                [.. f.Regels.Select((r, i) => new Factuurregel(r.Omschrijving, r.Rekening, r.Onzecode, r.Netto, Praktijkset.Percentage(r.Papiercode) > 0 ? f.RegelBtw(i) : null))],
                f.Kenmerk, Creditnota: f.Credit));
            if (!uitkomst.IsGelukt)
            {
                verschillen.Add($"Inkoop {f.Nummer}: boeken lukt niet: {string.Join(" ", uitkomst.Fouten)}");
            }
        }

        var inkoopboek = await inkoop.Inkoopboek(wie, id, Praktijkset.Begin, Praktijkset.Eind);
        foreach (var f in set.Inkoop)
        {
            if (inkoopboek.FirstOrDefault(r => r.Factuurnummer == f.Nummer) is not { } regel)
            {
                verschillen.Add($"Inkoop {f.Nummer}: niet in het inkoopboek.");
            }
            else if (regel.Totaal != f.Getekend)
            {
                verschillen.Add($"Inkoop {f.Nummer} ({f.Partij.Naam}): totaal {regel.Totaal} in plaats van {f.Getekend} (excl. {regel.Exclusief}, btw {regel.Btw}).");
            }
        }

        uitvoer.WriteLine($"Inkoop: {inkoopAuto} van {aantalInkoop} vanzelf geboekt, {inkoopDubbel} dubbel herkend ({klok.Elapsed.TotalSeconds:0.0} s).");

        // ---- verkoop: de facturen uit het kassapakket inlezen; een onbekende klant met de hand kiezen
        var import = app.Dienst<VerkoopimportDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var verkoopAuto = 0;
        foreach (var f in set.Verkoop)
        {
            var bestand = bestanden.Single(b => b.Map == "verkoop" && b.Naam == f.Bestandsnaam);
            var gelezen = await import.Lees(wie, id, new MemoryStream(bestand.Inhoud), bestand.Naam, null);
            if (gelezen.Geboekt)
            {
                verkoopAuto++;
                continue;
            }

            nietVanzelf.Add($"verkoop {f.Nummer} {f.Partij.Naam} ({f.Formaat}): {gelezen.Melding}");
            if (gelezen.Id is not { } document)
            {
                verschillen.Add($"Verkoop {f.Nummer}: niet ingelezen: {gelezen.Melding}");
                continue;
            }

            var klant = await Klant(wie, id, f);
            var invoer = new Verkoopinvoer(klant, f.Datum, f.Vervaldatum, f.Omschrijving,
                [.. f.Regels.Select(r => new Verkoopregel(r.Omschrijving, f.Credit ? -r.Aantal : r.Aantal, r.Prijs, r.Rekening, r.Onzecode))], Factuurnummer: f.Nummer, Betalingskenmerk: f.Kenmerk);
            var bewaard = await verkoop.Bewaar(wie, id, document, invoer);
            var definitief = bewaard.IsGelukt ? await verkoop.MaakDefinitief(wie, id, document) : null;
            if (!bewaard.IsGelukt || definitief?.IsGelukt != true)
            {
                verschillen.Add($"Verkoop {f.Nummer}: met de hand boeken lukt niet: {string.Join(" ", bewaard.Fouten.Concat(definitief?.Fouten ?? []))}");
            }
        }

        var verkoopboek = await verkoop.Verkoopboek(wie, id, Praktijkset.Begin, Praktijkset.Eind);
        foreach (var f in set.Verkoop)
        {
            if (verkoopboek.FirstOrDefault(r => r.Factuurnummer == f.Nummer) is not { } regel)
            {
                verschillen.Add($"Verkoop {f.Nummer}: niet in het verkoopboek.");
            }
            else if (regel.Totaal != f.Getekend || regel.Btw != (f.Credit ? -f.BtwTotaal : f.BtwTotaal))
            {
                verschillen.Add($"Verkoop {f.Nummer} ({f.Partij.Naam}): totaal {regel.Totaal} btw {regel.Btw} in plaats van {f.Getekend} btw {f.BtwTotaal}.");
            }
        }

        uitvoer.WriteLine($"Verkoop: {verkoopAuto} van {aantalVerkoop} vanzelf geboekt ({klok.Elapsed.TotalSeconds:0.0} s).");

        // ---- bank: per afschrift inlezen en wat blijft staan meteen verwerken (zoals de stappenlijst zegt), dan het
        // volgende; april komt twee keer (overlappend).
        var bank = app.Dienst<BankDienst>();
        var alle = set.Betaal.SelectMany(p => p.Batch ?? [p]).ToDictionary(p => p.EndToEnd);
        var perSoort = new Dictionary<string, int>(StringComparer.Ordinal);
        var metDeHand = 0;
        var (bankNieuw, bankAuto) = (0, 0);
        foreach (var bestand in bestanden.Where(b => b.Map == "bank").OrderBy(b => b.Naam, StringComparer.Ordinal))
        {
            var gelezen = await bank.Lees(wie, id, new MemoryStream(bestand.Inhoud), bestand.Naam);
            if (!gelezen.IsGelukt)
            {
                verschillen.Add($"Bank {bestand.Naam}: niet ingelezen: {string.Join(" ", gelezen.Fouten)}");
                continue;
            }

            var w = gelezen.Waarde!;
            (bankNieuw, bankAuto) = (bankNieuw + w.Nieuw, bankAuto + w.Automatisch);
            var teVerwerken = await bank.Lijst(wie, id);
            uitvoer.WriteLine($"Bank {bestand.Naam}: {w.Nieuw} nieuw, {w.Dubbel} al ingelezen, {w.MogelijkDubbel} mogelijk dubbel, {w.Automatisch} vanzelf geboekt, {teVerwerken.Count} met de hand. {string.Join(" ", w.Meldingen)}");
            if (w.MogelijkDubbel > 0)
            {
                verschillen.Add($"Bank {bestand.Naam}: {w.MogelijkDubbel} mutaties als mogelijk dubbel gemeld.");
            }

            metDeHand += teVerwerken.Count;
            foreach (var m in teVerwerken.OrderBy(m => m.Datum))
            {
                var post = m.EndToEndId is { } e && alle.TryGetValue(e, out var p) ? p
                    : set.Spaar.FirstOrDefault(s => s.Datum == m.Datum && s.Bedrag == m.Bedrag);
                if (post is null)
                {
                    verschillen.Add($"Bank {m.Datum} {m.Bedrag} {m.TegenNaam}: onbekende mutatie.");
                    continue;
                }

                var soort = post.Afhandeling.Toelichting.Split(':')[0];
                perSoort[soort] = perSoort.GetValueOrDefault(soort) + 1;
                if (post.Afhandeling.Soort == Afhandelsoort.Afletteren)
                {
                    uitvoer.WriteLine($"  met de hand {m.Datum:dd-MM} {m.Bedrag,10} {m.TegenNaam}: {post.Afhandeling.Toelichting} — {m.Melding}");
                }

                var fout = await Verwerk(wie, id, m, post);
                if (fout is not null)
                {
                    verschillen.Add($"Bank {m.Datum} {m.Bedrag} {m.TegenNaam} ({post.Afhandeling.Toelichting}): {fout}");
                }
            }
        }

        uitvoer.WriteLine($"Bank: {bankNieuw} nieuwe mutaties, {bankAuto} vanzelf geboekt, {metDeHand} met de hand ({klok.Elapsed.TotalSeconds:0.0} s): " + string.Join(", ", perSoort.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
        if ((await bank.Lijst(wie, id)).Count is > 0 and var rest)
        {
            verschillen.Add($"Bank: nog {rest} mutaties te verwerken.");
        }

        // ---- eindcontroles
        var stand = await bank.Stand(wie, id);
        foreach (var b in stand)
        {
            if (b.Grootboek != b.Saldo || b.Beginverschil is not 0m)
            {
                verschillen.Add($"Bank {b.Dagboek}: grootboek {b.Grootboek} tegen afschrift {b.Saldo} (tot {b.Tot}), beginverschil {b.Beginverschil}.");
            }
        }

        var eindBetaal = Praktijkset.SaldoBetaal(set.Betaal, Praktijkset.Eind);
        Controleer(verschillen, "Eindsaldo betaalrekening", stand.Single(b => b.Dagboek == "BNK").Saldo, eindBetaal);
        Controleer(verschillen, "Eindsaldo spaarrekening", stand.Single(b => b.Dagboek == "SPA").Saldo, Praktijkset.BeginSpaar + set.Spaar.Sum(p => p.Bedrag));

        var debiteuren = await app.Dienst<OpenpostenDienst>().Debiteuren(wie, id, Praktijkset.Eind);
        Vergelijk(verschillen, "Debiteuren", debiteuren.Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand), set.OpenDebiteuren);
        var crediteuren = await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, Praktijkset.Eind);
        Vergelijk(verschillen, "Crediteuren", crediteuren.Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand), set.OpenCrediteuren);

        var proef = await grootboek.Proefbalans(wie, id, 2026);
        decimal Saldo(string rekening) => proef.Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;
        Controleer(verschillen, "Debiteuren (1300)", Saldo("1300"), set.OpenDebiteuren.Values.Sum());
        Controleer(verschillen, "Crediteuren (1600)", Saldo("1600"), -set.OpenCrediteuren.Values.Sum());
        Controleer(verschillen, "Kruisposten (1190)", Saldo(Praktijkset.Kruisposten), 0m);
        Controleer(verschillen, "Vraagposten (2000)", Saldo("2000"), -set.Vraagpost);
        Controleer(verschillen, "Loonheffing (1520)", Saldo("1520"), 0m);
        Controleer(verschillen, "Nettolonen (1800)", Saldo("1800"), 0m);
        Controleer(verschillen, "Betaalverschil (9000 zonder bankkosten)", Saldo("9000") - set.Betaal.Where(p => p.Afhandeling.Rekening == "9000").Sum(p => -p.Bedrag), 0.02m * set.Rondes);
        Controleer(verschillen, "Btw (1500 + 1510) na betaling Q1", Saldo("1500") + Saldo("1510"), -set.AangifteQ1.Afronding);
        Controleer(verschillen, "Proefbalans in evenwicht", proef.Regels.Sum(r => r.Saldo), 0m);

        // De btw-aangifte van het eerste kwartaal, rubriek voor rubriek.
        var btwjaar = await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, 2026);
        var q1 = btwjaar.Jaar.Perioden[0].Berekend;
        foreach (var rubriek in set.AangifteQ1.Rubrieken.Select(r => r.Rubriek).Union(q1.Rubrieken.Select(r => r.Rubriek)).Order(StringComparer.Ordinal))
        {
            var (berekend, verwacht) = (q1.Rubriek(rubriek), set.AangifteQ1.Rubriek(rubriek));
            if (berekend.Grondslag != verwacht.Grondslag || berekend.Btw != verwacht.Btw)
            {
                verschillen.Add($"Btw rubriek {rubriek}: {berekend.Grondslag} / {berekend.Btw} in plaats van {verwacht.Grondslag} / {verwacht.Btw}.");
            }
        }

        Controleer(verschillen, "Btw 5b voorbelasting", q1.Voorbelasting, set.AangifteQ1.Voorbelasting);

        foreach (var a in await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id))
        {
            if (a.Soort is Aansluitingsoort.Bank or Aansluitingsoort.Debiteuren or Aansluitingsoort.Crediteuren && !a.Sluit)
            {
                verschillen.Add($"Aansluiting {a.Soort} {a.Onderwerp}: verschil {a.Verschil} {a.Toelichting} {string.Join("; ", a.Ontbrekend.Select(o => $"{o.Datum} {o.Bedrag} {o.Omschrijving}"))}");
            }
        }

        if (await grootboek.ControleerKeten(wie, id) is { Count: > 0 } keten)
        {
            verschillen.Add("Keten: " + string.Join("; ", keten));
        }

        // De auditfile van het jaar teruglezen als nieuwe administratie: dezelfde saldi.
        var auditfile = app.Dienst<AuditfileDienst>();
        var xaf = await auditfile.Maak(wie, id, 2026);
        var terug = await auditfile.LeesIn(wie, new MemoryStream(xaf.Inhoud), xaf.Bestandsnaam, Ondernemingsvorm.BV);
        if (!terug.IsGelukt)
        {
            verschillen.Add("Auditfile teruglezen: " + string.Join(" ", terug.Fouten));
        }
        else
        {
            var kopie = await grootboek.Proefbalans(wie, terug.Waarde!.Administratie, 2026);
            foreach (var r in proef.Regels.Where(r => r.Saldo != 0))
            {
                var andere = kopie.Regels.FirstOrDefault(k => k.Rekening.Nummer == r.Rekening.Nummer)?.Saldo ?? 0m;
                if (andere != r.Saldo)
                {
                    verschillen.Add($"Auditfile: rekening {r.Rekening.Nummer} {andere} in plaats van {r.Saldo}.");
                }
            }
        }

        uitvoer.WriteLine("Niet vanzelf geboekt:");
        foreach (var n in nietVanzelf)
        {
            uitvoer.WriteLine("  " + n);
        }

        uitvoer.WriteLine($"Klaar in {klok.Elapsed.TotalSeconds:0.0} s; {verschillen.Count} verschillen.");
        foreach (var v in verschillen)
        {
            uitvoer.WriteLine("  - " + v);
        }

        return verschillen;
    }

    /// <summary>De administratie zoals bij de start van de procedure: gegevens, betaalverschil, spaarrekening, kruisposten en het memoriaal.</summary>
    private async Task<Guid> Inrichten(Uitvoerder wie, Praktijkset set)
    {
        var id = await app.Administratie(Praktijkset.Eigen.Naam);
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [e.Iban, Praktijkset.SpaarIban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
            Afwijkend = nu.Afwijkend with { Betaalverschil = 0.05m, Verschilrekening = "9000" },
        })).IsGelukt);
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.WijzigBank(wie, id, "BNK", "Betaalrekening", e.Iban, "1100")).IsGelukt);
        Assert.True((await bank.VoegBankToe(wie, id, "SPA", "Spaarrekening", Praktijkset.SpaarIban, "1110")).IsGelukt);
        var grootboek = app.Dienst<GrootboekDienst>();
        var kruis = await grootboek.VoegRekeningToe(wie, id, new RekeningInvoer(Praktijkset.Kruisposten, "Kruisposten", Rekeningsoort.Balans, "BLimKru"));
        if (!kruis.IsGelukt)
        {
            Assert.True((await grootboek.VoegRekeningToe(wie, id, new RekeningInvoer(Praktijkset.Kruisposten, "Kruisposten", Rekeningsoort.Balans, null))).IsGelukt);
        }

        foreach (var post in set.Memoriaal)
        {
            var geboekt = await grootboek.Boek(wie, id, post);
            Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        }

        return id;
    }

    /// <summary>De klant bij een factuur die niet vanzelf geboekt is: bestaat hij nog niet, dan aanmaken (zoals vanaf het concept).</summary>
    private async Task<string> Klant(Uitvoerder wie, Guid id, Factuurdata f)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        if ((await inrichting.Relaties(wie, id)).FirstOrDefault(r => r.Naam == f.Partij.Naam) is { } bekend)
        {
            return bekend.Code;
        }

        var p = f.Partij;
        var regel = f.Regels[0];
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: regel.Rekening, Verkoopbtwcode: regel.Onzecode)), p.Iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    /// <summary>Een mutatie verwerken zoals de stappenlijst zegt; geeft de fout, of leeg als het gelukt is.</summary>
    private async Task<string?> Verwerk(Uitvoerder wie, Guid id, BankmutatieRegel m, Bankpost post)
    {
        var bank = app.Dienst<BankDienst>();
        var a = post.Afhandeling;
        if (a.Soort == Afhandelsoort.OpRekening)
        {
            var advies = (await bank.Verwerking(wie, id, m.Id))?.Voorstel.Advies;
            var geboekt = await bank.OpRekening(wie, id, [m.Id], a.Rekening!, a.BtwCode);
            var fout = geboekt.IsGelukt ? null : string.Join(" ", geboekt.Fouten);
            // Bij de Belastingdienst hoort het pakket de rekening zelf voor te stellen.
            return fout ?? (m.TegenIban == Belastingherkenning.BelastingdienstIban && advies?.Rekeningnummer != a.Rekening
                ? $"voorstel {advies?.Rekeningnummer ?? "geen"} in plaats van {a.Rekening} (wel zo geboekt)"
                : null);
        }

        var verwerking = await bank.Verwerking(wie, id, m.Id);
        var kandidaten = verwerking?.Voorstel.Afletteren.Kandidaten.SelectMany(k => k.Posten).Select(p => (p.Id, p.Factuurnummer)) ?? [];
        var open = (await bank.OpenPosten(wie, id)).Select(p => (p.Id, p.Factuurnummer));
        var posten = kandidaten.Concat(open).Concat(verwerking?.OpenPosten.Select(p => (p.Id, p.Factuurnummer)) ?? []).DistinctBy(p => p.Id).ToList();
        var gekozen = new List<Guid>();
        foreach (var nummer in a.Nummers)
        {
            if (posten.FirstOrDefault(p => p.Factuurnummer == nummer) is { Id: var gevonden } && gevonden != Guid.Empty)
            {
                gekozen.Add(gevonden);
            }
            else
            {
                return $"factuur {nummer} is niet te kiezen";
            }
        }

        var verschil = a.Verschilrekening is { } rekening ? new Verschilafboeking(rekening, a.Verschilcode, "Betalingskorting")
            : post.Omschrijving.Contains("Twee cent", StringComparison.Ordinal) || a.Toelichting.StartsWith("Twee cent", StringComparison.Ordinal) ? new Verschilafboeking("9000", null, "Betaalverschil")
            : null;
        var uitkomst = await bank.Afletteren(wie, id, m.Id, gekozen, verschil);
        return uitkomst.IsGelukt ? null : string.Join(" ", uitkomst.Fouten);
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
            tekst.Append(System.Globalization.CultureInfo.InvariantCulture, $" {nummer} staat open met {bedrag}{(verwacht.TryGetValue(nummer, out var v) ? $" (verwacht {v})" : "")};");
        }

        foreach (var nummer in verwacht.Keys.Except(werkelijk.Keys))
        {
            tekst.Append(System.Globalization.CultureInfo.InvariantCulture, $" {nummer} staat niet open (verwacht {verwacht[nummer]});");
        }

        if (tekst.Length > 0)
        {
            verschillen.Add($"{wat}:{tekst}");
        }
    }
}

/// <summary>De praktijktest op grotere schaal: alleen als <c>BOEKERHOUDEN_STRESS_PRAKTIJK</c> het aantal rondes geeft (5 = 500 + 500).</summary>
internal sealed class PraktijkStressFactAttribute : FactAttribute
{
    public PraktijkStressFactAttribute()
    {
        if (Rondes == 0)
        {
            Skip = "Zet BOEKERHOUDEN_STRESS_PRAKTIJK op het aantal rondes (bijv. 5 voor 500 inkoop- en 500 verkoopfacturen) om deze stresstest te draaien.";
        }
    }

    public static int Rondes =>
        Environment.GetEnvironmentVariable("BOEKERHOUDEN_STRESS_PRAKTIJK") is { Length: > 0 } w && int.TryParse(w, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;
}
