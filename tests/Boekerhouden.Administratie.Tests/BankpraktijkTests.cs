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

    private static decimal Saldo(Boekhouding.Grootboek.Proefbalans proef, string rekening) =>
        proef.Regels.FirstOrDefault(r => r.Rekening.Nummer == rekening)?.Saldo ?? 0m;
}
