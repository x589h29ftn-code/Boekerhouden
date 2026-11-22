using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// De vraag uit de praktijk: afschriften zijn ingelezen tot en met 30 september, en het volgende bestand loopt van 28
/// september tot en met 30 oktober. Niets mag dubbel in de boeken komen, wat echt nieuw is moet erbij, en na het
/// verwerken is het saldo van de bank in het grootboek gelijk aan het eindsaldo van het laatste afschrift. In hetzelfde
/// formaat (MT940), in een ander formaat (CAMT.053), met een boeking die de bank later nog op 30 september zette, met
/// twee gelijke pinbetalingen op één dag, in omgekeerde volgorde, en hetzelfde bestand twee keer.
/// </summary>
public sealed class BankafschriftOverlapTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const decimal Begin = 10_000m;

    /// <summary>Juli t/m oktober 2026: per maand een paar mutaties, en eind september iets op 28, 29 (twee keer hetzelfde) en 30.</summary>
    private static readonly Bankmutatie[] Mutaties =
    [
        new(new(2026, 7, 3), 1210m, "Factuur 20260101", "NL44RABO0123456789", "J. de Vries"),
        new(new(2026, 7, 15), -250m, "Huur juli", "NL91ABNA0417164300", "Verhuur B.V."),
        new(new(2026, 7, 28), -24.99m, "Abonnement juli", "NL63TRIO0212345678", "Belnet B.V."),
        new(new(2026, 8, 4), 605m, "Factuur 20260102", "NL44RABO0123456789", "J. de Vries"),
        new(new(2026, 8, 15), -250m, "Huur augustus", "NL91ABNA0417164300", "Verhuur B.V."),
        new(new(2026, 8, 30), -12.40m, "Pinbetaling tankstation", null, null),
        new(new(2026, 9, 2), 2420m, "Factuur 20260103", "NL44RABO0123456789", "J. de Vries"),
        new(new(2026, 9, 15), -250m, "Huur september", "NL91ABNA0417164300", "Verhuur B.V."),
        new(new(2026, 9, 28), -24.99m, "Abonnement september", "NL63TRIO0212345678", "Belnet B.V."),
        new(new(2026, 9, 29), -3.50m, "Pinbetaling koffie", null, null),
        new(new(2026, 9, 29), -3.50m, "Pinbetaling koffie", null, null),
        new(new(2026, 9, 30), -18.15m, "Bankkosten september", null, "ING Bank"),
        new(new(2026, 10, 1), 1815m, "Factuur 20260104", "NL44RABO0123456789", "J. de Vries"),
        new(new(2026, 10, 15), -250m, "Huur oktober", "NL91ABNA0417164300", "Verhuur B.V."),
        new(new(2026, 10, 28), -24.99m, "Abonnement oktober", "NL63TRIO0212345678", "Belnet B.V."),
        new(new(2026, 10, 30), -7.25m, "Pinbetaling lunch", null, null),
    ];

    /// <summary>Een boeking die de bank pas later nog op 30 september zette: niet in het eerste bestand, wel in het tweede.</summary>
    private static readonly Bankmutatie Laat = new(new(2026, 9, 30), 99m, "Terugboeking borg", "NL12BUNQ2020123456", "Opslag Noord");

    private static readonly DateOnly Overlapbegin = new(2026, 9, 28);

    private static Bankafschrift Afschrift(DateOnly van, DateOnly tot, IEnumerable<Bankmutatie>? extra = null)
    {
        var alle = Mutaties.Concat(extra ?? []).ToList();
        var begin = Begin + alle.Where(m => m.Datum < van).Sum(m => m.Bedrag);
        var mutaties = alle.Where(m => m.Datum >= van && m.Datum <= tot).OrderBy(m => m.Datum).ToList();
        return new Bankafschrift(Werkadministratie.Iban, $"{van:MMdd}", "EUR", van.AddDays(-1), begin, tot, begin + mutaties.Sum(m => m.Bedrag), mutaties);
    }

    private static MemoryStream Mt940(Bankafschrift a) => new(Encoding.UTF8.GetBytes(Mt940Schrijver.Schrijf(a)));

    /// <summary>
    /// Hetzelfde afschrift als CAMT.053 (een ander formaat, zoals een andere export het geeft), met de omschrijving zoals
    /// zo'n export hem vaak schrijft: in hoofdletters met de naam van de tegenpartij erbij.
    /// </summary>
    private static MemoryStream Camt(Bankafschrift a)
    {
        XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02";
        static string Getal(decimal b) => Math.Abs(b).ToString("0.00", CultureInfo.InvariantCulture);
        XElement Saldo(string code, decimal bedrag, DateOnly dag) =>
            new(ns + "Bal", new XElement(ns + "Tp", new XElement(ns + "CdOrPrtry", new XElement(ns + "Cd", code))),
                new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), Getal(bedrag)),
                new XElement(ns + "CdtDbtInd", bedrag < 0 ? "DBIT" : "CRDT"),
                new XElement(ns + "Dt", new XElement(ns + "Dt", dag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        var entries = a.Mutaties.Select(m => new XElement(ns + "Ntry",
            new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), Getal(m.Bedrag)),
            new XElement(ns + "CdtDbtInd", m.Bedrag < 0 ? "DBIT" : "CRDT"),
            new XElement(ns + "Sts", "BOOK"),
            new XElement(ns + "BookgDt", new XElement(ns + "Dt", m.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            new XElement(ns + "ValDt", new XElement(ns + "Dt", m.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            new XElement(ns + "NtryDtls", new XElement(ns + "TxDtls",
                m.TegenNaam is null && m.TegenIban is null ? null : new XElement(ns + "RltdPties",
                    m.TegenNaam is null ? null : new XElement(ns + (m.Bedrag > 0 ? "Dbtr" : "Cdtr"), new XElement(ns + "Nm", m.TegenNaam)),
                    m.TegenIban is null ? null : new XElement(ns + (m.Bedrag > 0 ? "DbtrAcct" : "CdtrAcct"), new XElement(ns + "Id", new XElement(ns + "IBAN", m.TegenIban)))),
                new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", $"{m.TegenNaam} {m.Omschrijving}".Trim().ToUpperInvariant()))))));
        var document = new XDocument(new XElement(ns + "Document", new XElement(ns + "BkToCstmrStmt",
            new XElement(ns + "GrpHdr", new XElement(ns + "MsgId", $"TEST{a.Einddatum:yyyyMMdd}"), new XElement(ns + "CreDtTm", $"{a.Einddatum:yyyy-MM-dd}T23:59:00")),
            new XElement(ns + "Stmt",
                new XElement(ns + "Id", $"{a.Begindatum:yyyyMMdd}-{a.Einddatum:yyyyMMdd}"),
                new XElement(ns + "CreDtTm", $"{a.Einddatum:yyyy-MM-dd}T23:59:00"),
                new XElement(ns + "Acct", new XElement(ns + "Id", new XElement(ns + "IBAN", a.Iban)), new XElement(ns + "Ccy", "EUR")),
                Saldo("OPBD", a.Beginsaldo, a.Begindatum!.Value.AddDays(1)),
                Saldo("CLBD", a.Eindsaldo, a.Einddatum!.Value),
                entries))));
        return new MemoryStream(Encoding.UTF8.GetBytes(document.ToString()));
    }

    /// <summary>De drie maandafschriften t/m 30 september (MT940).</summary>
    private static async Task TotEnMetSeptember(BankDienst bank, Werkadministratie w, IEnumerable<Bankmutatie>? extra = null)
    {
        foreach (var (van, tot, naam) in new[] { (new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "juli.mt940"), (new(2026, 8, 1), new(2026, 8, 31), "augustus.mt940"), (new(2026, 9, 1), new(2026, 9, 30), "september.mt940") })
        {
            var gelezen = await bank.Lees(w.Wie, w.Id, Mt940(Afschrift(van, tot, extra)), naam);
            Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        }
    }

    /// <summary>Alles wat nog te verwerken is op een rekening boeken (wat mogelijk dubbel is eerst weg), dan het grootboek tegen het eindsaldo.</summary>
    private async Task<(decimal Grootboek, int Mutaties)> VerwerkAlles(BankDienst bank, Werkadministratie w)
    {
        var dubbel = (await bank.Lijst(w.Wie, w.Id)).Where(m => m.MogelijkDubbel).Select(m => m.Id).ToList();
        if (dubbel.Count > 0)
        {
            Assert.True((await bank.Verwijder(w.Wie, w.Id, dubbel)).IsGelukt);
        }

        var open = (await bank.Lijst(w.Wie, w.Id)).Where(m => m.Status == Banktransactiestatus.TeVerwerken).Select(m => m.Id).ToList();
        if (open.Count > 0)
        {
            var geboekt = await bank.OpRekening(w.Wie, w.Id, open, "4900", null);
            Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        }

        var koba = await app.Dienst<GrootboekDienst>().Kolommenbalans(w.Wie, w.Id, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31));
        return (koba.Regels.Single(r => r.Rekening.Nummer == "1100").Saldo, (await bank.Lijst(w.Wie, w.Id, ookGeboekt: true)).Count);
    }

    private static decimal Eindsaldo(IEnumerable<Bankmutatie>? extra = null) => Begin + Mutaties.Concat(extra ?? []).Sum(m => m.Bedrag);

    [Fact]
    public async Task Hetzelfde_formaat_de_overlap_wordt_overgeslagen_en_oktober_komt_erbij()
    {
        var w = await Werkadministratie.Maak(app, "Overlap MT940 B.V.");
        var bank = app.Dienst<BankDienst>();
        await TotEnMetSeptember(bank, w);

        var oktober = (await bank.Lees(w.Wie, w.Id, Mt940(Afschrift(Overlapbegin, new(2026, 10, 30))), "28-09-tm-30-10.mt940")).Waarde!;
        uitvoer.WriteLine($"MT940: nieuw {oktober.Nieuw}, al ingelezen {oktober.Dubbel}, mogelijk dubbel {oktober.MogelijkDubbel}; {string.Join(" | ", oktober.Meldingen)}");

        // 28, 29 (twee keer) en 30 september waren er al; oktober is nieuw.
        Assert.Equal((4, 4, 0), (oktober.Nieuw, oktober.Dubbel, oktober.MogelijkDubbel));
        Assert.Contains(oktober.Meldingen, m => m.Contains("overlapt met september.mt940", StringComparison.Ordinal) && m.Contains("4 al ingelezen", StringComparison.Ordinal));
        Assert.DoesNotContain(oktober.Meldingen, m => m.Contains("sluit niet aan", StringComparison.Ordinal) || m.Contains("mist", StringComparison.Ordinal));
        Assert.Equal(2, (await bank.Lijst(w.Wie, w.Id, ookGeboekt: true)).Count(m => m.Bedrag == -3.50m));

        var (grootboek, aantal) = await VerwerkAlles(bank, w);
        Assert.Equal((Eindsaldo(), Mutaties.Length), (grootboek, aantal));
    }

    [Fact]
    public async Task Een_boeking_die_de_bank_later_nog_op_30_september_zette_komt_er_wel_bij()
    {
        var w = await Werkadministratie.Maak(app, "Overlap Laat B.V.");
        var bank = app.Dienst<BankDienst>();
        await TotEnMetSeptember(bank, w);

        var oktober = (await bank.Lees(w.Wie, w.Id, Mt940(Afschrift(Overlapbegin, new(2026, 10, 30), [Laat])), "28-09-tm-30-10.mt940")).Waarde!;

        Assert.Equal((5, 4, 0), (oktober.Nieuw, oktober.Dubbel, oktober.MogelijkDubbel));
        Assert.Contains(await bank.Lijst(w.Wie, w.Id), m => m.Bedrag == 99m && m.Datum == new DateOnly(2026, 9, 30));
        var (grootboek, aantal) = await VerwerkAlles(bank, w);
        Assert.Equal((Eindsaldo([Laat]), Mutaties.Length + 1), (grootboek, aantal));
    }

    [Fact]
    public async Task Een_ander_formaat_de_overlap_is_mogelijk_dubbel_en_wordt_niet_vanzelf_geboekt()
    {
        var w = await Werkadministratie.Maak(app, "Overlap CAMT B.V.");
        var bank = app.Dienst<BankDienst>();
        await TotEnMetSeptember(bank, w);

        var oktober = (await bank.Lees(w.Wie, w.Id, Camt(Afschrift(Overlapbegin, new(2026, 10, 30))), "28-09-tm-30-10.xml")).Waarde!;
        uitvoer.WriteLine($"CAMT: nieuw {oktober.Nieuw}, al ingelezen {oktober.Dubbel}, mogelijk dubbel {oktober.MogelijkDubbel}; {string.Join(" | ", oktober.Meldingen)}");

        // De vier van eind september herkent het pakket (al ingelezen of mogelijk dubbel); oktober komt gewoon binnen.
        Assert.Equal((0, 4), (oktober.Dubbel, oktober.MogelijkDubbel));
        Assert.Equal(4, oktober.Nieuw - oktober.MogelijkDubbel);
        Assert.Contains(oktober.Meldingen, m => m.EndsWith("4 mogelijk dubbel.", StringComparison.Ordinal));
        Assert.Equal(2, (await bank.Lijst(w.Wie, w.Id)).Count(m => m.MogelijkDubbel && m.Bedrag == -3.50m));
        var lijst = await bank.Lijst(w.Wie, w.Id, ookGeboekt: true);
        Assert.All(lijst.Where(m => m.MogelijkDubbel), m => Assert.Equal(Banktransactiestatus.TeVerwerken, m.Status));
        Assert.All(lijst.Where(m => m.MogelijkDubbel), m => Assert.True(m.Datum >= Overlapbegin && m.Datum <= new DateOnly(2026, 9, 30)));

        // Opnieuw herkennen boekt de mogelijk dubbele niet; na opruimen en verwerken klopt de bank precies.
        await bank.VerwerkOpnieuw(w.Wie, w.Id);
        Assert.Equal(oktober.MogelijkDubbel, (await bank.Lijst(w.Wie, w.Id)).Count(m => m.MogelijkDubbel));
        var (grootboek, aantal) = await VerwerkAlles(bank, w);
        Assert.Equal((Eindsaldo(), Mutaties.Length), (grootboek, aantal));
    }

    [Fact]
    public async Task Omgekeerde_volgorde_en_hetzelfde_bestand_twee_keer()
    {
        var w = await Werkadministratie.Maak(app, "Overlap Omgekeerd B.V.");
        var bank = app.Dienst<BankDienst>();

        // Eerst het bestand van 28 september t/m 30 oktober, daarna pas juli t/m september.
        Assert.Equal(8, (await bank.Lees(w.Wie, w.Id, Mt940(Afschrift(Overlapbegin, new(2026, 10, 30))), "28-09-tm-30-10.mt940")).Waarde!.Nieuw);
        await TotEnMetSeptember(bank, w);
        Assert.Equal(Mutaties.Length, (await bank.Lijst(w.Wie, w.Id, ookGeboekt: true)).Count);

        // Hetzelfde bestand nog eens: alles al ingelezen.
        var nogEens = (await bank.Lees(w.Wie, w.Id, Mt940(Afschrift(Overlapbegin, new(2026, 10, 30))), "28-09-tm-30-10 (1).mt940")).Waarde!;
        Assert.Equal((0, 8), (nogEens.Nieuw, nogEens.Dubbel));

        var (grootboek, aantal) = await VerwerkAlles(bank, w);
        Assert.Equal((Eindsaldo(), Mutaties.Length), (grootboek, aantal));
    }
}
