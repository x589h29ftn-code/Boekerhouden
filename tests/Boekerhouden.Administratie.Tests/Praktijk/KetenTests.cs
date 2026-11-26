using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// De keten inkoop, verkoop en bank met de gevallen die in de praktijk lastig zijn, samen in één administratie, en na
/// elke stap de vaste controles (<see cref="Invarianten"/>). Per bankmutatie staat vast wat Sifer zelf moet doen
/// (vanzelf afletteren, of juist niet omdat het niet zeker is) en wat de gebruiker daarna doet. Verkoop: korting als
/// negatieve regel en twee tarieven, betaling op alleen het betalingskenmerk, een EU-klant (ICP) die betalingskorting
/// neemt, uitvoer buiten de EU, een deelbetaling waarna de factuur wordt gecrediteerd en het tegoed terugbetaald, twee
/// facturen met hetzelfde bedrag en een betaling zonder nummer, een aanbetaling vóór de factuur, een incasso die wordt
/// gestorneerd en daarna opnieuw betaald. Inkoop: een dienst uit de EU (verlegd, 4b), verlegde btw van een aannemer
/// (2a), een deelbetaling en een creditnota daarna, hetzelfde factuurnummer bij twee leveranciers, betalingskorting
/// met btw-correctie, een terugbetaling van een leverancier, een betaling die de factuur en de bankkosten samen
/// verdeelt, een te verwerken betaling die terug moet en opnieuw. Bank: een afschrift dat deels overlapt met het
/// vorige. Aan het eind: openstaande posten per factuur, de btw-aangifte per rubriek, aansluitingen en samenhang.
/// </summary>
public sealed class KetenTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";

    private readonly List<string> verschillen = [];

    [Fact]
    public async Task Inkoop_verkoop_en_bank_met_de_lastige_gevallen()
    {
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var verkoop = app.Dienst<VerkoopDienst>();
        var bank = app.Dienst<BankDienst>();

        // ---------------------------------------------------------------- januari: facturen
        var hotel = await Klant(wie, id, Praktijkset.Hotel, "V21");
        var muller = await Klant(wie, id, Praktijkset.Muller, "VEU");
        var velo = await Klant(wie, id, Praktijkset.Velo, "VEX");
        var steen = await Klant(wie, id, Praktijkset.Steen, "V21");
        var bezorg = await Klant(wie, id, Praktijkset.Bezorg, "V21");
        var fietsplan = await Klant(wie, id, Praktijkset.Fietsplan, "V21");
        var strand = await Klant(wie, id, Praktijkset.Strand, "V21");

        // V1: twee tarieven en korting als negatieve regel: 450 + 40,50 en 2.400 + 504 = 3.394,50.
        var v1 = await Verkoop(wie, id, hotel, new(2026, 1, 5),
            [new("Onderhoud", 10m, 45m, "8000", "V9"), new("Fietsen", 2m, 1_250m, "8010", "V21"), new("Korting", 1m, -100m, "8010", "V21")]);
        var v2 = await Verkoop(wie, id, muller, new(2026, 1, 6), [new("Lastenrad", 4m, 2_000m, "8010", "VEU")]);
        var v3 = await Verkoop(wie, id, velo, new(2026, 1, 7), [new("E-bike", 2m, 2_500m, "8010", "VEX")]);
        var v4 = await Verkoop(wie, id, steen, new(2026, 1, 8), [new("Werkplaatsuren", 20m, 50m, "8000", "V21")]);
        var v5a = await Verkoop(wie, id, bezorg, new(2026, 1, 9), [new("Bakfiets huur", 1m, 500m, "8000", "V21")]);
        var v5b = await Verkoop(wie, id, bezorg, new(2026, 1, 10), [new("Bakfiets huur", 1m, 500m, "8000", "V21")]);
        var v6 = await Verkoop(wie, id, fietsplan, new(2026, 1, 12), [new("Leasefietsen onderhoud", 1m, 600m, "8000", "V21")]);
        Controleer("V1 totaal", v1.Totaal, 3_394.50m);

        var i1 = await Inkoop(wie, id, Praktijkset.Cloud, "INV-9001", new(2026, 1, 3), [new("Kassasoftware", 1, 1_000m, "VVL", "4330", "IEU")]);
        var i2 = await Inkoop(wie, id, Praktijkset.Hamer, "H-26-01", new(2026, 1, 4), [new("Verbouwing werkplaats", 1, 5_000m, "VVL", "4900", "IVL")]);
        var i3 = await Inkoop(wie, id, Praktijkset.Groothandel, "RG-1001", new(2026, 1, 5), [new("Fietsen", 4, 250m, "V21", "7000", "I21")]);
        var i4 = await Inkoop(wie, id, Praktijkset.Kantoor, "2026-001", new(2026, 1, 6), [new("Papier", 1, 200m, "V21", "4300", "I21")]);
        var i5 = await Inkoop(wie, id, Praktijkset.Reclame, "2026-001", new(2026, 1, 7), [new("Advertentie", 1, 300m, "V21", "4400", "I21")]);
        var i6 = await Inkoop(wie, id, Praktijkset.Energie, "EO-555", new(2026, 1, 8), [new("Voorschot energie", 1, 500m, "V21", "4110", "I21")]);
        var i7 = await Inkoop(wie, id, Praktijkset.Transport, "TS-77", new(2026, 1, 9), [new("Transport", 1, 1_000m, "V21", "4210", "I21")]);
        await Stap(wie, id, "januari geboekt");

        // ---------------------------------------------------------------- februari: het afschrift
        var kenmerk = (await verkoop.Open(wie, id, v1.Document))!.Factuur.Betalingskenmerk;
        Assert.False(string.IsNullOrEmpty(kenmerk));
        var feb = new List<Bankmutatie>
        {
            new(new(2026, 2, 2), 3_394.50m, kenmerk!, Praktijkset.Hotel.Iban, Praktijkset.Hotel.Naam, Betalingskenmerk: kenmerk),
            new(new(2026, 2, 3), 7_840m, $"Rechnung {v2.Nummer} abzgl. 2% Skonto", Praktijkset.Muller.Iban, Praktijkset.Muller.Naam),
            new(new(2026, 2, 4), 5_000m, $"Invoice {v3.Nummer}", Praktijkset.Velo.Iban, Praktijkset.Velo.Naam),
            new(new(2026, 2, 5), 500m, $"Deelbetaling factuur {v4.Nummer}", Praktijkset.Steen.Iban, Praktijkset.Steen.Naam),
            new(new(2026, 2, 6), 605m, "Betaling huur bakfiets", Praktijkset.Bezorg.Iban, Praktijkset.Bezorg.Naam),
            new(new(2026, 2, 7), 1_210m, "Aanbetaling order 77", Praktijkset.Strand.Iban, Praktijkset.Strand.Naam),
            new(new(2026, 2, 9), 726m, $"Incasso factuur {v6.Nummer}", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
            new(new(2026, 2, 10), -1_000m, "Invoice INV-9001", Praktijkset.Cloud.Iban, Praktijkset.Cloud.Naam),
            new(new(2026, 2, 11), -5_000m, "Factuur H-26-01", Praktijkset.Hamer.Iban, Praktijkset.Hamer.Naam),
            new(new(2026, 2, 12), -600m, "Deelbetaling RG-1001", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam),
            new(new(2026, 2, 13), -363m, "Factuur 2026-001", Praktijkset.Reclame.Iban, Praktijkset.Reclame.Naam),
            new(new(2026, 2, 13), -242m, "Factuur 2026-001", Praktijkset.Kantoor.Iban, Praktijkset.Kantoor.Naam),
            new(new(2026, 2, 16), -1_185.80m, "Factuur TS-77 min 2% betalingskorting", Praktijkset.Transport.Iban, Praktijkset.Transport.Naam),
            new(new(2026, 2, 17), -617.50m, "Factuur EO-555 en kosten spoedbetaling", Praktijkset.Energie.Iban, Praktijkset.Energie.Naam),
            new(new(2026, 2, 27), -12.50m, "Kosten zakelijk betalingsverkeer"),
        };
        var saldo = await Afschrift(wie, id, "202602", new(2026, 2, 1), new(2026, 2, 27), 50_000m, feb);
        var lijst = await bank.Lijst(wie, id, ookGeboekt: true);
        BankmutatieRegel M(string tekst, decimal bedrag) => lijst.Single(m => m.Omschrijving == tekst && m.Bedrag == bedrag);

        // Wat Sifer zelf moet doen.
        Vanzelf(M(kenmerk!, 3_394.50m), "betaling op alleen het betalingskenmerk");
        Vanzelf(M($"Invoice {v3.Nummer}", 5_000m), "uitvoer, precies betaald");
        Vanzelf(M($"Incasso factuur {v6.Nummer}", 726m), "incasso precies betaald");
        Vanzelf(M("Invoice INV-9001", -1_000m), "EU-dienst, verlegd: alleen het netto bedrag");
        Vanzelf(M("Factuur H-26-01", -5_000m), "verlegd door de aannemer");
        Vanzelf(M("Factuur 2026-001", -363m), "hetzelfde nummer bij twee leveranciers (Reclame)");
        Vanzelf(M("Factuur 2026-001", -242m), "hetzelfde nummer bij twee leveranciers (Kantoor)");
        NietVanzelf(M("Betaling huur bakfiets", 605m), "twee open facturen met hetzelfde bedrag, geen nummer");
        NietVanzelf(M("Aanbetaling order 77", 1_210m), "aanbetaling vóór de factuur");
        NietVanzelf(M($"Rechnung {v2.Nummer} abzgl. 2% Skonto", 7_840m), "betalingskorting: bedrag wijkt af");
        NietVanzelf(M("Factuur TS-77 min 2% betalingskorting", -1_185.80m), "betalingskorting inkoop");
        NietVanzelf(M("Factuur EO-555 en kosten spoedbetaling", -617.50m), "factuur plus kosten in één betaling");
        await Stap(wie, id, "februari ingelezen");

        // Hetzelfde nummer bij twee leveranciers: elk op zijn eigen factuur.
        await Open(wie, id, "I4 (Kantoor 2026-001)", i4, 0m);
        await Open(wie, id, "I5 (Reclame 2026-001)", i5, 0m);

        // Wat de gebruiker daarna doet.
        // Een deelbetaling met het factuurnummer mag vanzelf (de rest blijft open); anders met de hand.
        await LetterAlsNodig(wie, id, M($"Deelbetaling factuur {v4.Nummer}", 500m), [v4]);
        await Open(wie, id, "V4 na de deelbetaling", v4, 710m);
        await Letter(wie, id, M("Betaling huur bakfiets", 605m), [v5a]);
        await Letter(wie, id, M($"Rechnung {v2.Nummer} abzgl. 2% Skonto", 7_840m), [v2], new Verschilafboeking("8010", "VEU", "Betalingskorting"));
        await LetterAlsNodig(wie, id, M("Deelbetaling RG-1001", -600m), [i3]);
        await Letter(wie, id, M("Factuur TS-77 min 2% betalingskorting", -1_185.80m), [i7], new Verschilafboeking("4210", "I21", "Betalingskorting"));
        var energie = M("Factuur EO-555 en kosten spoedbetaling", -617.50m);
        var post6 = await Post(wie, id, i6);
        Controleer("Verdelen factuur en kosten", (await bank.Verdeel(wie, id, energie.Id, [post6], [new Bankregelinvoer("9000", null, 12.50m, "Kosten spoedbetaling")])).IsGelukt ? 1 : 0, 1);
        if (lijst.FirstOrDefault(m => m.Omschrijving.StartsWith("Kosten zakelijk", StringComparison.Ordinal)) is { Status: Banktransactiestatus.TeVerwerken } kosten)
        {
            Controleer("Bankkosten op 9000", (await bank.OpRekening(wie, id, kosten.Id, "9000", null, null)).IsGelukt ? 1 : 0, 1);
        }

        await Stap(wie, id, "februari verwerkt");
        foreach (var (f, open) in new[] { (v1, 0m), (v2, 0m), (v3, 0m), (v4, 710m), (v5a, 0m), (v5b, 605m), (v6, 0m), (i1, 0m), (i2, 0m), (i3, -610m), (i6, 0m), (i7, 0m) })
        {
            await Open(wie, id, $"{f.Nummer} eind februari", f, open);
        }

        // ---------------------------------------------------------------- maart
        // V4 wordt gecrediteerd na de deelbetaling: de klant heeft 500 tegoed, dat wordt terugbetaald.
        var credit4 = await verkoop.Crediteer(wie, id, v4.Document);
        Assert.True(credit4.IsGelukt, string.Join(" ", credit4.Fouten));
        var concept = (await verkoop.Open(wie, id, credit4.Waarde))!;
        Assert.True((await verkoop.Bewaar(wie, id, credit4.Waarde, concept.Invoer with { Datum = new(2026, 3, 2), Vervaldatum = new(2026, 3, 16) })).IsGelukt);
        var c4nummer = (await verkoop.MaakDefinitief(wie, id, credit4.Waarde)).Waarde!;
        var c4 = new Factuur(c4nummer, new(2026, 3, 2), steen, false, -1_210m, credit4.Waarde);

        // De aanbetaling: nu komt de factuur. Dezelfde klant (IBAN), precies het bedrag en zijn enige open factuur: bij
        // het definitief maken lettert Sifer de aanbetaling vanzelf af.
        var v7 = await Verkoop(wie, id, strand, new(2026, 3, 3), [new("Order 77 fietsen", 1m, 1_000m, "8010", "V21")]);
        lijst = await bank.Lijst(wie, id, ookGeboekt: true);
        Vanzelf(M("Aanbetaling order 77", 1_210m), "aanbetaling, na het boeken van de factuur");
        await LetterAlsNodig(wie, id, M("Aanbetaling order 77", 1_210m), [v7]);

        var mrt = new List<Bankmutatie>
        {
            // Overlap: de laatste dag van februari staat er nog eens op (moet als dubbel worden herkend).
            new(new(2026, 2, 27), -12.50m, "Kosten zakelijk betalingsverkeer"),
            new(new(2026, 3, 4), -726m, $"Storno incasso factuur {v6.Nummer} reden MD06", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
            new(new(2026, 3, 6), -500m, $"Terugbetaling tegoed creditfactuur {c4nummer}", Praktijkset.Steen.Iban, Praktijkset.Steen.Naam),
            new(new(2026, 3, 9), -605m, "Factuur 2026-001", Praktijkset.Kantoor.Iban, Praktijkset.Kantoor.Naam),
            new(new(2026, 3, 16), 605m, "Betaling tweede bakfiets", Praktijkset.Bezorg.Iban, Praktijkset.Bezorg.Naam),
            new(new(2026, 3, 20), 726m, $"Factuur {v6.Nummer} opnieuw", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
            new(new(2026, 3, 23), -610m, "Restant RG-1001", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam),
        };

        // Maart: leverancier Groothandel stuurt een creditnota op RG-1001 (één fiets retour, 302,50), maar we betalen
        // het restant toch helemaal: daarna is er een tegoed van 302,50, dat de leverancier terugbetaalt.
        var i3c = await Inkoop(wie, id, Praktijkset.Groothandel, "RG-1001C", new(2026, 3, 18), [new("Retour fiets", 1, 250m, "V21", "7000", "I21")], credit: true, creditVoor: "RG-1001");
        mrt.Add(new(new(2026, 3, 27), 302.50m, "Terugbetaling creditnota RG-1001C", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam));
        var inlezing = await AfschriftInlezen(wie, id, "202603", new(2026, 2, 27), new(2026, 3, 27), saldo - -12.50m, mrt);
        Controleer("Maart: dubbele mutatie van de overlap herkend", inlezing.Dubbel, 1);
        saldo = inlezing.Eindsaldo;
        lijst = await bank.Lijst(wie, id, ookGeboekt: true);

        // Een betaling van 605 aan Kantoor met nummer 2026-001: die factuur is al betaald (242) en het bedrag hoort
        // bij geen enkele factuur: niet vanzelf (zeker niet op Reclame, ook 2026-001).
        NietVanzelf(M("Factuur 2026-001", -605m), "nummer van een betaalde factuur, ander bedrag");
        var storno = M($"Storno incasso factuur {v6.Nummer} reden MD06", -726m);
        NietVanzelf(storno, "storno: alleen een voorstel");
        var stornovoorstel = (await bank.Verwerking(wie, id, storno.Id))!.Voorstel.Afletteren.Kandidaten;
        Controleer("Storno voorgesteld op de betaalde factuur", stornovoorstel.Any(k => k.Posten.Any(p => p.Factuurnummer == v6.Nummer)) ? 1 : 0, 1);
        await Letter(wie, id, storno, [v6]);
        await Open(wie, id, "V6 na de storno", v6, 726m);

        // Terugdraaien en opnieuw: de factuur staat weer betaald, en na opnieuw afletteren weer open.
        Controleer("Storno terugdraaien", (await bank.Terugdraaien(wie, id, storno.Id)).IsGelukt ? 1 : 0, 1);
        await Open(wie, id, "V6 na terugdraaien van de storno", v6, 0m);
        await Stap(wie, id, "storno teruggedraaid");
        await Letter(wie, id, storno, [v6]);
        await Open(wie, id, "V6 na opnieuw de storno", v6, 726m);

        // Nu één factuur van Bezorg open (V5b): de tweede betaling zonder nummer kan nu wel vanzelf, na opnieuw verwerken.
        await bank.VerwerkOpnieuw(wie, id);
        lijst = await bank.Lijst(wie, id, ookGeboekt: true);
        if (M("Betaling tweede bakfiets", 605m).Status == Banktransactiestatus.TeVerwerken)
        {
            await Letter(wie, id, M("Betaling tweede bakfiets", 605m), [v5b]);
        }

        if (M($"Factuur {v6.Nummer} opnieuw", 726m).Status == Banktransactiestatus.TeVerwerken)
        {
            verschillen.Add("V6 opnieuw betaald (precies het bedrag, met nummer) niet vanzelf afgeletterd na de storno.");
            await Letter(wie, id, M($"Factuur {v6.Nummer} opnieuw", 726m), [v6]);
        }

        // Terugbetaling van het tegoed van Steen: op de creditfactuur en de factuur samen (saldo 500 te betalen).
        var terug = M($"Terugbetaling tegoed creditfactuur {c4nummer}", -500m);
        if (terug.Status == Banktransactiestatus.TeVerwerken)
        {
            await Letter(wie, id, terug, [v4, c4]);
        }

        // Restant RG-1001 (610) terwijl de creditnota al binnen is: 302,50 te veel betaald; dat komt terug.
        foreach (var tekst in new[] { "Restant RG-1001", "Terugbetaling creditnota RG-1001C" })
        {
            var m = lijst.Single(x => x.Omschrijving == tekst);
            if (m.Status == Banktransactiestatus.TeVerwerken)
            {
                await Letter(wie, id, m, tekst.StartsWith("Restant", StringComparison.Ordinal) ? [i3] : [i3c]);
            }
        }

        // De betaling van 605 aan Kantoor: een fout van onszelf (een andere factuur). Eerst op vraagposten, dan terug
        // naar te verwerken en alsnog op de juiste rekening (kantoorbenodigdheden, met btw).
        var fout = M("Factuur 2026-001", -605m);
        Controleer("Op vraagposten", (await bank.OpRekening(wie, id, fout.Id, "2000", null, "Uitzoeken")).IsGelukt ? 1 : 0, 1);
        Controleer("Terugdraaien", (await bank.Terugdraaien(wie, id, fout.Id)).IsGelukt ? 1 : 0, 1);
        Controleer("Vraagposten na terugdraaien", (await bank.Vraagposten(wie, id)).Saldo, 0m);
        Controleer("Alsnog op 4300 met btw", (await bank.OpRekening(wie, id, fout.Id, "4300", "I21", "Bureaustoelen")).IsGelukt ? 1 : 0, 1);
        await Stap(wie, id, "maart verwerkt");

        // ---------------------------------------------------------------- eind: alles nagaan
        foreach (var (f, open) in new[] { (v4, 0m), (c4, 0m), (v5b, 0m), (v6, 0m), (v7, 0m), (i3, 0m), (i3c, 0m) })
        {
            await Open(wie, id, $"{f.Nummer} eind maart", f, open);
        }

        Controleer("Vraagposten leeg", (await bank.Vraagposten(wie, id)).Saldo, 0m);
        var stand = (await bank.Stand(wie, id)).Single(b => b.Dagboek == "BNK");
        Controleer("Bank: grootboek = afschrift", stand.Grootboek, saldo);
        Controleer("Bank: niets meer te verwerken", stand.TeVerwerken, 0);

        // De btw van het eerste kwartaal per rubriek.
        var q1 = (await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, 2026)).Jaar.Perioden[0].Berekend;
        // 1a: 2.400 (V1) + 1.000 (V4) - 1.000 (C4) + 500 + 500 (V5) + 600 (V6) + 1.000 (V7) = 5.000; btw 1.050.
        Controleer("1a grondslag", q1.Rubriek("1a").Grondslag, 5_000m);
        Controleer("1a btw", q1.Rubriek("1a").Btw, 1_050m);
        Controleer("1b grondslag", q1.Rubriek("1b").Grondslag, 450m);
        Controleer("1b btw", q1.Rubriek("1b").Btw, 40.50m);
        Controleer("2a grondslag (verlegd)", q1.Rubriek("2a").Grondslag, 5_000m);
        Controleer("2a btw", q1.Rubriek("2a").Btw, 1_050m);
        Controleer("3a uitvoer", q1.Rubriek("3a").Grondslag, 5_000m);
        Controleer("3b ICP (na betalingskorting 160)", q1.Rubriek("3b").Grondslag, 7_840m);
        Controleer("4b EU-dienst", q1.Rubriek("4b").Grondslag, 1_000m);
        Controleer("4b btw", q1.Rubriek("4b").Btw, 210m);
        // Voorbelasting: verlegd 1.050 + EU 210 + RG-1001 210 - retour 52,50 + papier 42 + advertentie 63 + energie 105
        // + transport 210 - korting 4,20 + bureaustoelen 105 = 1.938,30.
        Controleer("5b voorbelasting", q1.Voorbelasting, 1_938.30m);

        verschillen.AddRange(await Samenhang.Controleer(app, wie, id, "eind maart"));
        foreach (var v in verschillen)
        {
            uitvoer.WriteLine("  - " + v);
        }

        Assert.True(verschillen.Count == 0, $"{verschillen.Count} verschillen:\n" + string.Join("\n", verschillen));
    }

    // ---------------------------------------------------------------- hulp

    private sealed record Factuur(string Nummer, DateOnly Datum, string Relatie, bool Inkoop, decimal Totaal, Guid Document);

    private async Task Stap(Uitvoerder wie, Guid id, string wanneer) => verschillen.AddRange(await Invarianten.Controleer(app, wie, id, wanneer));

    private void Controleer(string wat, decimal? werkelijk, decimal verwacht)
    {
        if (werkelijk != verwacht)
        {
            verschillen.Add($"{wat}: {werkelijk} in plaats van {verwacht}.");
        }
    }

    private void Vanzelf(BankmutatieRegel m, string geval)
    {
        if (m.Status != Banktransactiestatus.Geboekt)
        {
            verschillen.Add($"Niet vanzelf verwerkt ({geval}): {m.Omschrijving} {m.Bedrag}: {m.Melding}");
        }
    }

    private void NietVanzelf(BankmutatieRegel m, string geval)
    {
        if (m.Status == Banktransactiestatus.Geboekt)
        {
            verschillen.Add($"Vanzelf geboekt terwijl dat niet zeker is ({geval}): {m.Omschrijving} {m.Bedrag} ({m.Verwerking}, {m.Melding})");
        }
    }

    private async Task<Guid> Post(Uitvoerder wie, Guid id, Factuur f)
    {
        var soort = f.Inkoop ? Factuursoort.Inkoop : Factuursoort.Verkoop;
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        var namen = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).Where(r => r.Code == f.Relatie || r.Naam == f.Relatie).Select(r => r.Code).ToList();
        return db.OpenstaandePosten.Single(p => p.Factuurnummer == f.Nummer && p.Soort == soort && namen.Contains(p.Relatie)).Id;
    }

    private async Task Open(Uitvoerder wie, Guid id, string wat, Factuur f, decimal verwacht)
    {
        var soort = f.Inkoop ? Factuursoort.Inkoop : Factuursoort.Verkoop;
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        var namen = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).Where(r => r.Code == f.Relatie || r.Naam == f.Relatie).Select(r => r.Code).ToList();
        var post = db.OpenstaandePosten.SingleOrDefault(p => p.Factuurnummer == f.Nummer && p.Soort == soort && namen.Contains(p.Relatie));
        Controleer($"Openstaand {wat}", post?.Openstaand, verwacht);
    }

    private async Task Letter(Uitvoerder wie, Guid id, BankmutatieRegel m, Factuur[] facturen, Verschilafboeking? verschil = null)
    {
        var posten = new List<Guid>();
        foreach (var f in facturen)
        {
            posten.Add(await Post(wie, id, f));
        }

        var uitkomst = await app.Dienst<BankDienst>().Afletteren(wie, id, m.Id, posten, verschil);
        if (!uitkomst.IsGelukt)
        {
            verschillen.Add($"Afletteren {m.Omschrijving} op {string.Join(", ", facturen.Select(f => f.Nummer))}: {string.Join(" ", uitkomst.Fouten)}");
        }
    }

    private async Task LetterAlsNodig(Uitvoerder wie, Guid id, BankmutatieRegel m, Factuur[] facturen)
    {
        if (m.Status == Banktransactiestatus.TeVerwerken)
        {
            await Letter(wie, id, m, facturen);
        }
    }

    private async Task<decimal> Afschrift(Uitvoerder wie, Guid id, string nummer, DateOnly van, DateOnly tot, decimal begin, List<Bankmutatie> mutaties) =>
        (await AfschriftInlezen(wie, id, nummer, van, tot, begin, mutaties)).Eindsaldo;

    private async Task<(decimal Eindsaldo, int Dubbel)> AfschriftInlezen(Uitvoerder wie, Guid id, string nummer, DateOnly van, DateOnly tot, decimal begin, List<Bankmutatie> mutaties)
    {
        var eind = begin + mutaties.Sum(m => m.Bedrag);
        var afschrift = new Bankafschrift(Iban, nummer, "EUR", van, begin, tot, eind, mutaties);
        var gelezen = await app.Dienst<BankDienst>().Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, $"BNK{nummer}"))), $"{nummer}.mt940");
        Assert.True(gelezen.IsGelukt, string.Join(" ", gelezen.Fouten));
        uitvoer.WriteLine($"Afschrift {nummer}: {gelezen.Waarde!.Nieuw} nieuw, {gelezen.Waarde.Dubbel} dubbel, {gelezen.Waarde.Automatisch} vanzelf. {string.Join(" ", gelezen.Waarde.Meldingen)}");
        verschillen.AddRange(gelezen.Waarde.Meldingen.Where(m => m.Contains("sluit niet aan", StringComparison.Ordinal)).Select(m => $"Afschrift {nummer}: {m}"));
        return (eind, gelezen.Waarde.Dubbel);
    }

    private async Task<Factuur> Verkoop(Uitvoerder wie, Guid id, string klant, DateOnly datum, List<Verkoopregel> regels)
    {
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, datum, datum.AddDays(14), "Levering", regels));
        Assert.True(concept.IsGelukt, string.Join(" ", concept.Fouten));
        var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
        Assert.True(definitief.IsGelukt, string.Join(" ", definitief.Fouten));
        var totaal = (await verkoop.Open(wie, id, concept.Waarde))!.Factuur.Regels.Sum(r => r.Bedrag)
            + (await verkoop.Open(wie, id, concept.Waarde))!.Btw.Sum(b => b.Btw);
        return new Factuur(definitief.Waarde!, datum, klant, false, totaal, concept.Waarde);
    }

    private async Task<Factuur> Inkoop(Uitvoerder wie, Guid id, Partijdata partij, string nummer, DateOnly datum, IReadOnlyList<Regeldata> regels, bool credit = false, string? creditVoor = null)
    {
        var f = new Factuurdata(true, partij, nummer, datum, datum.AddDays(14), credit ? "Creditnota" : "Levering", regels, Formaat.Ubl, Betaalwijze.Nummer, credit, creditVoor);
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Praktijkset.Document(f, partij, Praktijkset.Eigen)), f.Bestandsnaam, null);
        if (!gelezen.IsGeboekt)
        {
            Assert.NotNull(gelezen.DocumentId);
            var code = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).FirstOrDefault(r => r.Naam == partij.Naam)?.Code;
            var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(partij.Naam, partij.Iban, partij.BtwNummer, partij.Kvk) : null,
                nummer, datum, f.Vervaldatum, f.Omschrijving, [.. regels.Select((r, i) => new Factuurregel(r.Omschrijving, r.Rekening, r.Onzecode, r.Netto, f.RegelBtw(i)))], null, Creditnota: credit));
            Assert.True(uitkomst.IsGelukt, $"{nummer}: {string.Join(" ", uitkomst.Fouten)}");
        }

        return new Factuur(nummer, datum, partij.Naam, true, -(credit ? -f.Totaal : f.Totaal), gelezen.DocumentId ?? Guid.Empty);
    }

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie("Keten De Spaak B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, await inrichting.Instellingen(wie, id) with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);
        var begin = await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans",
            [new("1100", 50_000m, "Bank"), new("0500", -18_000m, "Aandelenkapitaal"), new("0510", -32_000m, "Overige reserves")]));
        Assert.True(begin.IsGelukt, string.Join("; ", begin.Fouten));
        return id;
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id, Partijdata p, string btw)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: btw)), p.Iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }
}
