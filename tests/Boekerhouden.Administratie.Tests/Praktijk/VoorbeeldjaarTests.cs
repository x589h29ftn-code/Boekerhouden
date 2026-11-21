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
/// Het voorbeeldjaar: een verzonnen BV met een vast jaar aan boekingen (2025), waarvan de uitkomst met de hand is
/// uitgerekend en in <c>docs/administratie/voorbeeldjaar-2025.md</c> staat om door een accountant na te rekenen. Alles
/// gaat langs dezelfde weg als op het scherm: verkoopfacturen definitief maken en crediteren, inkoopfacturen als
/// e-factuur inlezen en boeken, afschriften inlezen en verwerken, het vaste actief en de afschrijving, de btw-aangifte
/// per kwartaal vastleggen en betalen. Daarna moeten de balans, de winst-en-verliesrekening, elke rubriek van de vier
/// aangiften, de openstaande posten en de vennootschapsbelasting tot op de cent gelijk zijn aan de berekening.
/// </summary>
public sealed class VoorbeeldjaarTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>De verwachte saldi op 31-12-2025 (debet positief), met de hand uitgerekend (zie de documentatie).</summary>
    private static readonly Dictionary<string, decimal> Saldi = new()
    {
        ["0200"] = 6_000.00m,     // werkbank
        ["0210"] = -900.00m,      // 9 maanden x 100
        ["0500"] = -18_000.00m,
        ["0510"] = -12_000.00m,
        ["1100"] = 24_800.00m,
        ["1300"] = 10_453.98m,    // 1.419,98 + 1.471,50 + 7.562,50
        ["1500"] = 4_568.40m,     // voorbelasting 1.816,80 + 1.680,00 + 1.050,00 + 21,60
        ["1510"] = -5_860.00m,    // verschuldigd 5.025,00, betaald 1.330, ontvangen 1.236 en 929
        ["1520"] = -2_000.00m,
        ["1600"] = -2_392.40m,    // 130,80 + 2.000,00 + 261,60
        ["1800"] = 0m,
        ["4000"] = 8_000.00m,
        ["4010"] = 1_500.00m,
        ["4310"] = 600.00m,
        ["4410"] = 300.00m,
        ["4800"] = 900.00m,
        ["4900"] = 2_360.00m,     // 120 + 2.000 + 240
        ["7000"] = 13_000.00m,
        ["8000"] = -1_350.00m,    // 900 - 900 (credit) + 1.350
        ["8010"] = -30_149.98m,   // 12.500 + 1.500 - 1.500 + 4.000 + 1.999,98 + 5.400 + 6.250
        ["9000"] = 170.00m,       // 4 x 42,50
    };

    /// <summary>Per kwartaal: per rubriek grondslag en btw, de voorbelasting, en afgerond (in hele euro's) te betalen.</summary>
    private static readonly (string Rubriek, decimal Grondslag, decimal Btw)[][] Rubrieken =
    [
        [("1a", 14_000.00m, 2_940.00m), ("1b", 900.00m, 81.00m), ("4b", 600.00m, 126.00m)],
        [("1a", 499.98m, 105.00m), ("1b", -900.00m, -81.00m), ("2a", 2_000.00m, 420.00m), ("3b", 4_000.00m, 0m)],
        [("1b", 1_350.00m, 121.50m), ("3a", 5_400.00m, 0m)],
        [("1a", 6_250.00m, 1_312.50m)],
    ];

    private static readonly decimal[] Voorbelasting = [1_816.80m, 1_680.00m, 1_050.00m, 21.60m];
    private static readonly decimal[] TeBetalen = [1_330.20m, -1_236.00m, -928.50m, 1_290.90m];
    private static readonly decimal[] AfgerondTeBetalen = [1_330m, -1_236m, -929m, 1_290m];

    [Fact]
    public async Task Het_voorbeeldjaar_klopt_tot_op_de_cent()
    {
        var verschillen = new List<string>();
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var verkoop = app.Dienst<VerkoopDienst>();
        var btw = app.Dienst<BtwaangifteDienst>();
        var activa = app.Dienst<ActivaDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var fietsplan = await Klant(wie, id, Praktijkset.Fietsplan, "V21");
        var hotel = await Klant(wie, id, Praktijkset.Hotel, "V21");
        var muller = await Klant(wie, id, Praktijkset.Muller, "VEU");
        var velo = await Klant(wie, id, Praktijkset.Velo, "VEX");
        var nummers = new Dictionary<string, string>();
        var saldo = 30_000m;

        async Task<Guid> Verkoop(string naam, string klant, DateOnly datum, params Verkoopregel[] regels)
        {
            var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, datum, datum.AddDays(14), naam, regels));
            Assert.True(concept.IsGelukt, string.Join(" ", concept.Fouten));
            var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
            Assert.True(definitief.IsGelukt, $"{naam}: {string.Join(" ", definitief.Fouten)}");
            nummers[naam] = definitief.Waarde!;
            return concept.Waarde;
        }

        async Task Afschrift(int nummer, DateOnly van, DateOnly tot, params Bankmutatie[] mutaties)
        {
            var a = new Bankafschrift(Iban, $"{nummer}", "EUR", van, saldo, tot, saldo + mutaties.Sum(m => m.Bedrag), mutaties);
            saldo = a.Eindsaldo;
            var bank = app.Dienst<BankDienst>();
            var gelezen = await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(a, $"VB{nummer}"))), $"afschrift {nummer}.mt940");
            Assert.True(gelezen.IsGelukt, string.Join(" ", gelezen.Fouten));
            foreach (var m in await bank.Lijst(wie, id))
            {
                if (await Verwerk(wie, id, m) is { } fout)
                {
                    verschillen.Add($"Bank {m.Datum} {m.Bedrag} {m.Omschrijving}: {fout}");
                }
            }
        }

        async Task Kwartaal(int kwartaal)
        {
            var van = new DateOnly(2025, (kwartaal * 3) - 2, 1);
            var vastgelegd = await btw.Vastleggen(wie, id, van, van.AddMonths(3).AddDays(-1));
            if (!vastgelegd.IsGelukt)
            {
                verschillen.Add($"Btw {kwartaal}e kwartaal vastleggen: {string.Join(" ", vastgelegd.Fouten)}");
            }
        }

        async Task Afschrijven(DateOnly tot)
        {
            var uitkomst = await activa.Afschrijven(wie, id, tot, Afschrijvingsfrequentie.Kwartaal);
            if (!uitkomst.IsGelukt)
            {
                verschillen.Add($"Afschrijven tot {tot}: {string.Join(" ", uitkomst.Fouten)}");
            }
        }

        // ---- 1e kwartaal
        await Verkoop("V1", fietsplan, new(2025, 1, 15), new Verkoopregel("Fietsen", 10, 1_250m, "8010", "V21"));
        var v2 = await Verkoop("V2", hotel, new(2025, 2, 10), new Verkoopregel("Onderhoud", 20, 45m, "8000", "V9"), new Verkoopregel("Fiets", 1, 1_500m, "8010", "V21"));
        await Inkoop(wie, id, verschillen, Praktijkset.Groothandel, "GH-2025-001", new(2025, 1, 6), new Regeldata("Fietsen", 1, 8_000m, "V21", "7000", "I21"));
        await Inkoop(wie, id, verschillen, Praktijkset.Koffie, "KC-118", new(2025, 3, 3), new Regeldata("Koffie", 1, 120m, "V9", "4900", "I9"));
        await Inkoop(wie, id, verschillen, Praktijkset.Cloud, "WK-501", new(2025, 3, 20), new Regeldata("Kassasoftware", 1, 600m, "VVL", "4310", "IEU"));
        await Afschrift(1, new(2025, 1, 1), new(2025, 3, 31),
            new Bankmutatie(new(2025, 1, 20), -9_680m, "Factuur GH-2025-001", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam),
            new Bankmutatie(new(2025, 1, 31), 15_125m, $"Factuur {nummers["V1"]}", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
            new Bankmutatie(new(2025, 3, 28), -600m, "Factuur WK-501", Praktijkset.Cloud.Iban, Praktijkset.Cloud.Naam),
            new Bankmutatie(new(2025, 3, 31), -42.50m, "Kosten zakelijk betalingsverkeer 1e kwartaal"));
        await Kwartaal(1);

        // ---- 2e kwartaal: de creditfactuur bij V2 (uit het 1e kwartaal), een deelbetaling, verlegde btw en de werkbank
        await Verkoop("V3", muller, new(2025, 4, 5), new Verkoopregel("Fietsen", 4, 1_000m, "8010", "VEU"));
        await Verkoop("V4", fietsplan, new(2025, 5, 12), new Verkoopregel("E-bike", 2, 999.99m, "8010", "V21"));
        var credit = await verkoop.Crediteer(wie, id, v2);
        Assert.True(credit.IsGelukt, string.Join(" ", credit.Fouten));
        var creditdatum = new DateOnly(2025, 5, 15);
        Assert.True((await verkoop.Bewaar(wie, id, credit.Waarde, new Verkoopinvoer(hotel, creditdatum, creditdatum, $"Creditfactuur bij factuur {nummers["V2"]}",
            [new Verkoopregel("Onderhoud", -20, 45m, "8000", "V9"), new Verkoopregel("Fiets", -1, 1_500m, "8010", "V21")]))).IsGelukt);
        var creditnummer = await verkoop.MaakDefinitief(wie, id, credit.Waarde);
        Assert.True(creditnummer.IsGelukt, string.Join(" ", creditnummer.Fouten));
        await Inkoop(wie, id, verschillen, Praktijkset.Groothandel, "GH-2025-014", new(2025, 4, 10), new Regeldata("Werkbank", 1, 6_000m, "V21", "0200", "I21"));
        await Inkoop(wie, id, verschillen, Praktijkset.Hamer, "H-25-040", new(2025, 6, 15), new Regeldata("Verbouwing werkplaats", 1, 2_000m, "VVL", "4900", "IVL"));
        var werkbank = await activa.Bewaar(wie, id, null, new ActivumInvoer("Werkbank", new(2025, 4, 10), 6_000m, 0m, new(2025, 4, 1), 60,
            Afschrijvingsmethode.Lineair, "0200", "0210", "4800"));
        Assert.True(werkbank.IsGelukt, string.Join(" ", werkbank.Fouten));
        await Afschrift(2, new(2025, 4, 1), new(2025, 6, 30),
            new Bankmutatie(new(2025, 4, 25), -1_330m, "Omzetbelasting 1e kwartaal 2025", Belastingherkenning.BelastingdienstIban, "Belastingdienst"),
            new Bankmutatie(new(2025, 4, 30), -7_260m, "Factuur GH-2025-014", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam),
            new Bankmutatie(new(2025, 5, 20), 4_000m, $"Factuur {nummers["V3"]}", Praktijkset.Muller.Iban, Praktijkset.Muller.Naam),
            new Bankmutatie(new(2025, 6, 5), 1_000m, $"Factuur {nummers["V4"]} deel", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam),
            new Bankmutatie(new(2025, 6, 20), -300m, "Lunch relaties", null, "Restaurant De Gast"),
            new Bankmutatie(new(2025, 6, 30), -42.50m, "Kosten zakelijk betalingsverkeer 2e kwartaal"));
        await Afschrijven(new(2025, 6, 30));
        await Kwartaal(2);

        // ---- 3e kwartaal: uitvoer buiten de EU, 9% en de teruggaaf van het 2e kwartaal
        await Verkoop("V5", velo, new(2025, 8, 15), new Verkoopregel("E-bike Trekking", 3, 1_800m, "8010", "VEX"));
        await Verkoop("V6", hotel, new(2025, 9, 20), new Verkoopregel("Onderhoud", 30, 45m, "8000", "V9"));
        await Inkoop(wie, id, verschillen, Praktijkset.Groothandel, "GH-2025-031", new(2025, 7, 1), new Regeldata("Fietsen", 1, 5_000m, "V21", "7000", "I21"));
        await Afschrift(3, new(2025, 7, 1), new(2025, 9, 30),
            new Bankmutatie(new(2025, 7, 20), 1_236m, "Omzetbelasting 2e kwartaal 2025 teruggaaf", Belastingherkenning.BelastingdienstIban, "Belastingdienst"),
            new Bankmutatie(new(2025, 7, 31), -6_050m, "Factuur GH-2025-031", Praktijkset.Groothandel.Iban, Praktijkset.Groothandel.Naam),
            new Bankmutatie(new(2025, 9, 1), 5_400m, $"Factuur {nummers["V5"]}", Praktijkset.Velo.Iban, Praktijkset.Velo.Naam),
            new Bankmutatie(new(2025, 9, 30), -42.50m, "Kosten zakelijk betalingsverkeer 3e kwartaal"));
        await Afschrijven(new(2025, 9, 30));
        await Kwartaal(3);

        // ---- 4e kwartaal: het loon, facturen die aan het eind openstaan
        await Verkoop("V7", fietsplan, new(2025, 11, 10), new Verkoopregel("Fietsen", 5, 1_250m, "8010", "V21"));
        await Inkoop(wie, id, verschillen, Praktijkset.Koffie, "KC-170", new(2025, 12, 15), new Regeldata("Koffie", 1, 240m, "V9", "4900", "I9"));
        var loon = await grootboek.Boek(wie, id, new Journaalpost("MEM", "2025-0100", new(2025, 12, 31), "Loonjournaal 2025",
            [new("4000", 8_000m, "Brutolonen"), new("4010", 1_500m, "Sociale lasten"), new("1520", -2_000m, "Loonheffing"), new("1800", -7_500m, "Nettolonen")]));
        Assert.True(loon.IsGelukt, string.Join(" ", loon.Fouten));
        await Afschrift(4, new(2025, 10, 1), new(2025, 12, 31),
            new Bankmutatie(new(2025, 10, 20), 929m, "Omzetbelasting 3e kwartaal 2025 teruggaaf", Belastingherkenning.BelastingdienstIban, "Belastingdienst"),
            new Bankmutatie(new(2025, 12, 31), -7_500m, "Nettolonen 2025", null, "Personeel"),
            new Bankmutatie(new(2025, 12, 31), -42.50m, "Kosten zakelijk betalingsverkeer 4e kwartaal"));
        await Afschrijven(new(2025, 12, 31));
        await Kwartaal(4);

        // ---- de saldi op 31-12-2025
        var kb = await grootboek.Kolommenbalans(wie, id, new(2025, 1, 1), new(2025, 12, 31));
        var werkelijk = kb.Regels.Where(r => r.Saldo != 0).ToDictionary(r => r.Rekening.Nummer, r => r.Saldo);
        foreach (var nummer in Saldi.Keys.Union(werkelijk.Keys).Order(StringComparer.Ordinal))
        {
            Controleer(verschillen, $"Saldo {nummer}", werkelijk.GetValueOrDefault(nummer), Saldi.GetValueOrDefault(nummer));
        }

        Controleer(verschillen, "Resultaat 2025", -kb.Regels.Where(r => !r.Rekening.IsBalans).Sum(r => r.Saldo), 4_669.98m);

        // ---- de vier aangiften
        var jaar = (await btw.Jaar(wie, id, 2025)).Jaar;
        for (var k = 0; k < 4; k++)
        {
            var periode = jaar.Perioden[k];
            var berekend = periode.Berekend;
            var naam = $"Btw {k + 1}e kwartaal";
            foreach (var r in Btwaangifterubrieken())
            {
                var verwacht = Rubrieken[k].FirstOrDefault(x => x.Rubriek == r);
                Controleer(verschillen, $"{naam} {r} grondslag", berekend.Rubriek(r).Grondslag, verwacht.Grondslag);
                Controleer(verschillen, $"{naam} {r} btw", berekend.Rubriek(r).Btw, verwacht.Btw);
            }

            Controleer(verschillen, $"{naam} 5b voorbelasting", berekend.Voorbelasting, Voorbelasting[k]);
            Controleer(verschillen, $"{naam} te betalen", berekend.TeBetalen, TeBetalen[k]);
            Controleer(verschillen, $"{naam} vastgelegd (afgerond)", periode.Vastgelegd?.AfgerondTeBetalen, AfgerondTeBetalen[k]);
        }

        // ---- de openstaande posten op 31-12-2025
        var openposten = app.Dienst<OpenpostenDienst>();
        var debiteuren = (await openposten.Debiteuren(wie, id, new(2025, 12, 31))).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
        Vergelijk(verschillen, "Debiteuren", debiteuren, new Dictionary<string, decimal> { [nummers["V4"]] = 1_419.98m, [nummers["V6"]] = 1_471.50m, [nummers["V7"]] = 7_562.50m });
        var crediteuren = (await openposten.Crediteuren(wie, id, new(2025, 12, 31))).Relaties.SelectMany(r => r.Posten).ToDictionary(p => p.Factuurnummer, p => p.Openstaand);
        Vergelijk(verschillen, "Crediteuren", crediteuren, new Dictionary<string, decimal> { ["KC-118"] = 130.80m, ["H-25-040"] = 2_000m, ["KC-170"] = 261.60m });

        // ---- de vennootschapsbelasting: 4.669,98 + 79,50 (26,5% van 300 representatie) - 1.680 (KIA 28% van 6.000)
        //      = 3.069,48; afgerond op 5 euro 3.065; 19% = 582,35, naar beneden 582
        var vpb = (await app.Dienst<VpbDienst>().Positie(wie, id, 2025))!.Positie;
        Controleer(verschillen, "Vpb belastbaar bedrag", vpb.Belastbaar, 3_069.48m);
        Controleer(verschillen, "Vpb", vpb.Vennootschapsbelasting, 582m);
        Controleer(verschillen, "Vpb nog te betalen", vpb.TeBetalen, 582m);

        // ---- de activastaat
        var staat = (await activa.Activastaat(wie, id, 2025)).Single(g => g.Activarekening == "0200").Totaal;
        Controleer(verschillen, "Activastaat boekwaarde eind", staat.BoekwaardeEind, 5_100m);

        verschillen.AddRange(await Invarianten.Controleer(app, wie, id, "het voorbeeldjaar"));
        verschillen.AddRange(await Samenhang.Controleer(app, wie, id, "het voorbeeldjaar"));
        foreach (var v in verschillen)
        {
            uitvoer.WriteLine("  - " + v);
        }

        Assert.True(verschillen.Count == 0, string.Join("\n", verschillen));
    }

    private static IEnumerable<string> Btwaangifterubrieken() => Boekhouding.Btw.Btwaangifte.Volgorde;

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie("Voorbeeld Rijwielen B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@voorbeeld.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);
        var begin = await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2025-0001", new(2025, 1, 1), "Beginbalans 2025",
            [new("1100", 30_000m, "Bank"), new("0500", -18_000m, "Aandelenkapitaal"), new("0510", -12_000m, "Overige reserves")]));
        Assert.True(begin.IsGelukt, string.Join("; ", begin.Fouten));
        return id;
    }

    private async Task<string> Klant(Uitvoerder wie, Guid id, Partijdata p, string btwcode)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var uitkomst = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8010", Verkoopbtwcode: btwcode)), p.Iban);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        return (await inrichting.Relaties(wie, id)).Single(r => r.Id == uitkomst.Waarde).Code;
    }

    /// <summary>Een inkoopfactuur als e-factuur inlezen en boeken zoals op het scherm.</summary>
    private async Task Inkoop(Uitvoerder wie, Guid id, List<string> verschillen, Partijdata partij, string nummer, DateOnly datum, Regeldata regel)
    {
        var f = new Factuurdata(true, partij, nummer, datum, datum.AddDays(14), regel.Omschrijving, [regel], Formaat.Ubl, Betaalwijze.Nummer);
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Praktijkset.Document(f, partij, Praktijkset.Eigen)), f.Bestandsnaam, null);
        if (gelezen.IsGeboekt)
        {
            return;
        }

        if (gelezen.DocumentId is not { } document)
        {
            verschillen.Add($"Inkoop {nummer}: niet ingelezen: {gelezen.Melding}");
            return;
        }

        var code = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).FirstOrDefault(r => r.Naam == partij.Naam)?.Code;
        var uitkomst = await inkoop.Boek(wie, id, document, new Factuurinvoer(code, code is null ? new NieuweRelatie(partij.Naam, partij.Iban, partij.BtwNummer, partij.Kvk) : null,
            nummer, datum, f.Vervaldatum, f.Omschrijving, [new Factuurregel(regel.Omschrijving, regel.Rekening, regel.Onzecode, regel.Netto, f.RegelBtw(0))], null));
        if (!uitkomst.IsGelukt)
        {
            verschillen.Add($"Inkoop {nummer}: boeken lukt niet: {string.Join(" ", uitkomst.Fouten)}");
        }
    }

    /// <summary>Een mutatie die blijft staan: btw, lonen, representatie en bankkosten op rekening, anders afletteren op het genoemde nummer.</summary>
    private async Task<string?> Verwerk(Uitvoerder wie, Guid id, BankmutatieRegel m)
    {
        var bank = app.Dienst<BankDienst>();
        string? rekening = m.Omschrijving switch
        {
            _ when m.Omschrijving.StartsWith("Omzetbelasting", StringComparison.Ordinal) => "1510",
            _ when m.Omschrijving.StartsWith("Kosten zakelijk", StringComparison.Ordinal) => "9000",
            _ when m.Omschrijving.StartsWith("Lunch", StringComparison.Ordinal) => "4410",
            _ when m.Omschrijving.StartsWith("Nettolonen", StringComparison.Ordinal) => "1800",
            _ => null,
        };
        if (rekening is not null)
        {
            var geboekt = await bank.OpRekening(wie, id, m.Id, rekening, null, null);
            return geboekt.IsGelukt ? null : string.Join(" ", geboekt.Fouten);
        }

        var nummer = m.Omschrijving.Replace("Factuur ", "", StringComparison.Ordinal).Replace(" deel", "", StringComparison.Ordinal).Trim();
        var post = (await bank.OpenPosten(wie, id)).FirstOrDefault(p => p.Factuurnummer == nummer);
        if (post is null)
        {
            return $"factuur {nummer} staat niet open";
        }

        var uitkomst = await bank.Afletteren(wie, id, m.Id, [post.Id], null);
        return uitkomst.IsGelukt ? null : string.Join(" ", uitkomst.Fouten);
    }

    private static void Controleer(List<string> verschillen, string wat, decimal? werkelijk, decimal verwacht)
    {
        if (werkelijk != verwacht)
        {
            verschillen.Add(string.Create(Nl, $"{wat}: {werkelijk:N2} in plaats van {verwacht:N2}."));
        }
    }

    private static void Vergelijk(List<string> verschillen, string wat, Dictionary<string, decimal> werkelijk, Dictionary<string, decimal> verwacht)
    {
        foreach (var nummer in werkelijk.Keys.Union(verwacht.Keys))
        {
            Controleer(verschillen, $"{wat} {nummer} open", werkelijk.GetValueOrDefault(nummer), verwacht.GetValueOrDefault(nummer));
        }
    }
}
