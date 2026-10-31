using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een maand inkoop en bank zoals in de praktijk (verzonnen): vijftien facturen van drie leveranciers, een afschrift
/// met betalingen op factuurnummer, bankkosten en parkeren zonder factuur, een deelbetaling in twee delen en een factuur
/// die per abuis twee keer betaald is. Daarna komt het te veel betaalde terug.
/// </summary>
public sealed class BankpraktijkTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private const string Telecom = "NL04INGB0005100208";
    private const string Kabel = "NL15ABNA0004300106";
    private const string Kantoor = "NL15RABO0003300404";

    private static readonly (string Naam, string Iban, string Nummer, int Dag, decimal Exclusief, decimal Btw)[] Facturen =
    [
        ("Telecom Noord B.V.", Telecom, "9100234501", 1, 82.64m, 17.35m),
        ("Telecom Noord B.V.", Telecom, "9100234502", 8, 82.64m, 17.35m),
        ("Telecom Noord B.V.", Telecom, "9100234503", 15, 82.64m, 17.35m),
        ("Telecom Noord B.V.", Telecom, "9100234504", 12, 826.45m, 173.55m),
        ("Telecom Noord B.V.", Telecom, "9100234505", 22, 1239.67m, 260.33m),
        ("Kabelnet Zuid B.V.", Kabel, "KZ-2026-100234", 5, 57.85m, 12.15m),
        ("Kabelnet Zuid B.V.", Kabel, "KZ-2026-100288", 12, 57.85m, 12.15m),
        ("Kabelnet Zuid B.V.", Kabel, "KZ-2026-100341", 19, 57.85m, 12.15m),
        ("Kabelnet Zuid B.V.", Kabel, "KZ-2026-100302", 14, 495.87m, 104.13m),
        ("Kabelnet Zuid B.V.", Kabel, "KZ-2026-100377", 26, 743.80m, 156.20m),
        ("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00412", 6, 123.97m, 26.03m),
        ("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00419", 13, 123.97m, 26.03m),
        ("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00431", 20, 123.97m, 26.03m),
        ("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00425", 7, 1652.89m, 347.11m),
        ("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00440", 27, 2479.34m, 520.66m),
    ];

    private static MemoryStream Afschrift(string tekst) => new(Encoding.Latin1.GetBytes(tekst.ReplaceLineEndings("\r\n")));

    private const string Oktober = """
        :20:TEST20261030
        :25:NL31RABO0001234501
        :28C:00001/001
        :60F:C261001EUR25000,00
        :61:2610011001D24,50NCHGNONREF
        :86:Kosten zakelijk betaalpakket oktober 2026
        :61:2610021002D99,99NTRF9100234501
        :86:NL04INGB0005100208 Telecom Noord B.V.
        Factuur 9100234501
        :61:2610061006D70,00NTRFKZ2026100234
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100234
        :61:2610071007D150,00NTRFKF202600412
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00412
        :61:2610081008D2000,00NTRFKF202600425
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00425
        :61:2610081008D14,80NMSCNONREF
        :86:BEA   NR:QP4821   08.10.26/10.12 Parkeergarage Centrum,PAS114
        Parkeerkosten
        :61:2610091009D99,99NTRF9100234502
        :86:NL04INGB0005100208 Telecom Noord B.V.
        Factuur 9100234502
        :61:2610131013D1000,00NTRF9100234504
        :86:NL04INGB0005100208 Telecom Noord B.V.
        Factuur 9100234504
        :61:2610131013D70,00NTRFKZ2026100288
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100288
        :61:2610141014D150,00NTRFKF202600419
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00419
        :61:2610151015D600,00NTRFKZ2026100302
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100302
        :61:2610161016D99,99NTRF9100234503
        :86:NL04INGB0005100208 Telecom Noord B.V.
        Factuur 9100234503
        :61:2610161016D600,00NTRFKZ2026100302
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100302
        :61:2610161016D9,20NMSCNONREF
        :86:BEA   NR:QP5530   16.10.26/14.47 Parkeren Stad,PAS114
        Parkeerkosten
        :61:2610201020D70,00NTRFKZ2026100341
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100341
        :61:2610201020D31,45NDDTNONREF
        :86:SEPA Incasso algemeen doorlopend
        Incassant: NL53ZZZ301740750000
        Naam: Parkeerapp B.V.
        Omschrijving: Parkeerkosten september 2026
        :61:2610211021D150,00NTRFKF202600431
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00431
        :61:2610231023D1500,00NTRF9100234505
        :86:NL04INGB0005100208 Telecom Noord B.V.
        Factuur 9100234505
        :61:2610231023D11,30NMSCNONREF
        :86:BEA   NR:QP6012   23.10.26/09.31 Parkeergarage Centrum,PAS114
        Parkeerkosten
        :61:2610271027D900,00NTRFKZ2026100377
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Factuur KZ-2026-100377
        :61:2610281028D1800,00NTRFKF202600440
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00440 deelbetaling 1 van 2
        :61:2610281028D6,75NCHGNONREF
        :86:Kosten SEPA transacties oktober 2026
        :61:2610301030D1200,00NTRFKF202600440
        :86:NL15RABO0003300404 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00440 restbetaling (deel 2 van 2)
        :62F:C261030EUR14342,03
        -
        """;

    private const string November = """
        :20:TEST20261105
        :25:NL31RABO0001234501
        :28C:00002/001
        :60F:C261030EUR14342,03
        :61:2611051105C600,00NTRFKZ2026100302
        :86:NL15ABNA0004300106 Kabelnet Zuid B.V.
        Terugbetaling dubbel betaald KZ-2026-100302
        :62F:C261105EUR14942,03
        -
        """;

    [Fact]
    public async Task Deelbetaling_lettert_af_en_een_dubbele_betaling_wordt_te_vorderen_op_de_factuur()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Testbedrijf Bank B.V.");
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        foreach (var f in Facturen)
        {
            var datum = new DateOnly(2026, 10, f.Dag);
            var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(f.Nummer, datum.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))), $"{f.Nummer}.xml", null);
            var code = (await inrichting.Relaties(wie, id)).FirstOrDefault(r => r.Naam == f.Naam)?.Code;
            var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(f.Naam, f.Iban, null, null) : null,
                f.Nummer, datum, datum.AddDays(14), $"Factuur {f.Nummer}", [new Factuurregel("", f.Naam.StartsWith("Kantoor", StringComparison.Ordinal) ? "4300" : "4310", "I21", f.Exclusief, f.Btw)]));
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        }

        var bank = app.Dienst<BankDienst>();
        var crediteuren = app.Dienst<OpenpostenDienst>();
        Assert.Equal(9_959.97m, (await crediteuren.Crediteuren(wie, id, new(2026, 10, 31))).Openstaand);

        var oktober = await bank.Lees(wie, id, Afschrift(Oktober), "oktober.sta");

        Assert.True(oktober.IsGelukt, string.Join("; ", oktober.Fouten));
        Assert.Equal((23, 16), (oktober.Waarde!.Nieuw, oktober.Waarde.Automatisch));
        var teVerwerken = await bank.Lijst(wie, id);
        Assert.Equal(7, teVerwerken.Count);
        var dubbel = Assert.Single(teVerwerken, m => m.Bedrag == -600m);
        Assert.Equal(("Kabelnet Zuid B.V.", Kabel), (dubbel.TegenNaam, dubbel.TegenIban));
        Assert.Contains("dubbele betaling", dubbel.Melding, StringComparison.Ordinal);
        Assert.All(teVerwerken.Where(m => m.Id != dubbel.Id), m => Assert.True(m.Bedrag > -50m));

        // Op de deelbetaling na staat niets meer open; KF-2026-00440 is in twee delen betaald.
        var halverwege = await crediteuren.Crediteuren(wie, id, new(2026, 10, 29));
        var deel = Assert.Single(halverwege.Relaties.SelectMany(r => r.Posten));
        Assert.Equal(("KF-2026-00440", 1800m, 1200m), (deel.Factuurnummer, deel.Betaald, deel.Openstaand));
        Assert.Empty((await crediteuren.Crediteuren(wie, id, new(2026, 10, 30))).Relaties);

        // De dubbele betaling bevestigen: te vorderen op dezelfde factuur.
        var voorstel = (await bank.Verwerking(wie, id, dubbel.Id))!.Voorstel.Afletteren.Kandidaten.Single();
        Assert.True(voorstel.IsAlBetaald);
        Assert.True((await bank.Afletteren(wie, id, dubbel.Id, [voorstel.Posten[0].Id])).IsGelukt);

        var eind = await crediteuren.Crediteuren(wie, id, new(2026, 10, 31));
        var tevorderen = Assert.Single(Assert.Single(eind.Relaties).Posten);
        Assert.Equal(("Kabelnet Zuid B.V.", "KZ-2026-100302", 600m, 1200m, -600m), (eind.Relaties[0].Naam, tevorderen.Factuurnummer, tevorderen.Factuurbedrag, tevorderen.Betaald, tevorderen.Openstaand));
        var grootboek = app.Dienst<GrootboekDienst>();
        var proef = await grootboek.Proefbalans(wie, id, 2026);
        Assert.Equal((600m, 0m), (Saldo(proef, "1600"), Saldo(proef, "2000")));
        // Bank: alles betaald (25.000 - 14.342,03) behalve de zes kosten zonder factuur (98,00), die nog te verwerken zijn.
        Assert.Equal(-(25_000m - 14_342.03m - 98m), Saldo(proef, "1100"));

        // De leverancier betaalt terug: automatisch afgeletterd, niets meer open.
        var november = await bank.Lees(wie, id, Afschrift(November), "november.sta");
        Assert.Equal(1, november.Waarde!.Automatisch);
        Assert.Empty((await crediteuren.Crediteuren(wie, id, new(2026, 11, 30))).Relaties);
        Assert.Empty(await bank.OpenPosten(wie, id));
        Assert.Equal(0m, Saldo(await grootboek.Proefbalans(wie, id, 2026), "1600"));
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }

    private const string Lastig = """
        :20:TEST20261029
        :25:NL31RABO0001234501
        :28C:00001/001
        :60F:C261001EUR20000,00
        :61:2610061006D544,50NTRFNONREF
        :86:NL47INGB0002200104 Technisch Bureau Noord B.V.
        BETALINGSKENM.: 4410123456789012
        :61:2610091009D968,00NTRFNONREF
        :86:NL21INGB0002500108 Kantoorservice Zuid B.V.
        Factuur KS-2026-2201 minus creditnota KS-CN-2026-0033
        :61:2610121012D633,44NTRFNONREF
        :86:NL39RABO0002400103 Bouwstoffen Noord B.V.
        Fact BN-2026-1041 1042 1043
        :61:2610121012D2371,60NTRFNONREF
        :86:NL56RABO0002600102 Machinehandel Midden B.V.
        MM-2026-0780 incl 2% korting
        :61:2610161016D400,00NTRFNONREF
        :86:NL96RABO0007700903 Kantoorhuis Fryslan B.V.
        Factuur KF-2026-00455
        :62F:C261016EUR15082,46
        -
        """;

    [Fact]
    public async Task Sombetaling_creditnota_korting_en_een_ander_iban()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Testbedrijf Lastig B.V.");
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        Assert.True((await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Kantoorhuis Fryslân B.V.", null, null, "4300"), Kantoor)).IsGelukt);
        async Task Boek(string naam, string iban, string nummer, int dag, decimal exclusief, bool creditnota = false, string? kenmerk = null)
        {
            var datum = new DateOnly(2026, 10, dag);
            var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(nummer, datum.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))), $"{nummer}.xml", null);
            var code = (await inrichting.Relaties(wie, id)).FirstOrDefault(r => r.Naam == naam)?.Code;
            var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(naam, iban, null, null) : null,
                nummer, datum, datum.AddDays(14), $"Factuur {nummer}", [new Factuurregel("", "4300", "I21", exclusief, null)], kenmerk, Creditnota: creditnota));
            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        }

        await Boek("Technisch Bureau Noord B.V.", "NL47INGB0002200104", "TN-26-0555", 5, 450m, kenmerk: "4410 1234 5678 9012");
        await Boek("Kantoorservice Zuid B.V.", "NL21INGB0002500108", "KS-2026-2201", 1, 1000m);
        await Boek("Kantoorservice Zuid B.V.", "NL21INGB0002500108", "KS-CN-2026-0033", 8, 200m, creditnota: true);
        await Boek("Bouwstoffen Noord B.V.", "NL39RABO0002400103", "BN-2026-1041", 7, 310m);
        await Boek("Bouwstoffen Noord B.V.", "NL39RABO0002400103", "BN-2026-1042", 8, 125.50m);
        await Boek("Bouwstoffen Noord B.V.", "NL39RABO0002400103", "BN-2026-1043", 9, 88m);
        await Boek("Machinehandel Midden B.V.", "NL56RABO0002600102", "MM-2026-0780", 5, 2000m);
        await Boek("Kantoorhuis Fryslân B.V.", Kantoor, "KF-2026-00455", 15, 330.58m);

        var bank = app.Dienst<BankDienst>();
        var gelezenAfschrift = await bank.Lees(wie, id, Afschrift(Lastig), "lastig.sta");

        // Kenmerk, factuur min creditnota en de sombetaling met de nummers gaan automatisch; korting en het andere IBAN niet.
        Assert.Equal(3, gelezenAfschrift.Waarde!.Automatisch);
        var rest = await bank.Lijst(wie, id);
        Assert.Equal([-2371.60m, -400m], rest.Select(m => m.Bedrag).OrderBy(b => b));
        var korting = rest.Single(m => m.Bedrag == -2371.60m);
        var kandidaat = Assert.Single((await bank.Verwerking(wie, id, korting.Id))!.Voorstel.Afletteren.Kandidaten);
        Assert.True(kandidaat.Korting);
        Assert.False((await bank.Afletteren(wie, id, korting.Id, [kandidaat.Posten[0].Id], new Verschilafboeking("1600", "I21"))).IsGelukt);
        Assert.True((await bank.Afletteren(wie, id, korting.Id, [kandidaat.Posten[0].Id], new Verschilafboeking("4300", "I21", "Betalingskorting"))).IsGelukt);
        var ander = rest.Single(m => m.Bedrag == -400m);
        Assert.Equal(Boekhouding.Afletteren.Zekerheid.Voorstel, Assert.Single((await bank.Verwerking(wie, id, ander.Id))!.Voorstel.Afletteren.Kandidaten).Zekerheid);

        var open = await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, new(2026, 10, 31));
        Assert.Equal([("KF-2026-00455", 400m)], open.Relaties.SelectMany(r => r.Posten).Select(p => (p.Factuurnummer, p.Openstaand)));
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((-(20_000m - 15_082.46m - 400m), -400m), (Saldo(proef, "1100"), Saldo(proef, "1600")));
    }

    private static decimal Saldo(Boekhouding.Grootboek.Proefbalans proef, string rekening) =>
        proef.Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;
}
