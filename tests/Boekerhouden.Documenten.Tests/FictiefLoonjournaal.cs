using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een verzonnen cumulatief loonjournaal uit een salarispakket als PDF (A4 liggend). Alleen voor de tests.</summary>
public static class FictiefLoonjournaal
{
    /// <summary>
    /// Links de debetrekeningen met bedragen per kostenplaats, rechts de creditrekeningen; een rekening rechts staat
    /// soms op dezelfde regel als een bedrag links. Onderaan de totalen (<paramref name="totaal"/> = wat er staat).
    /// </summary>
    public static byte[] Pdf(string totaal = "10.000,00")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var pagina = builder.AddPage(842, 595);
        var y = 560.0;
        void Regel(params (double X, string Tekst)[] delen)
        {
            foreach (var (x, tekst) in delen)
            {
                pagina.AddText(tekst, 8, new PdfPoint(x, y), font);
            }

            y -= 13;
        }

        Regel((30, "Cumulatief"), (80, "journaal"));
        Regel((30, "Werkgever"), (90, "Voorbeeld"), (135, "Bouw"), (160, "B.V."), (300, "Groepsindeling"), (380, "Alle"), (600, "Periode"), (640, "vanaf"), (700, "2026-1"));
        Regel((30, "Clientnummer"), (90, "12345"), (300, "Groepcode"), (380, "0"), (600, "Periode"), (640, "tot"), (660, "en"), (675, "met"), (700, "2026-3"));
        Regel((150, "Kostenplaats"), (220, "Kostendrager"), (360, "Debet"), (600, "Kostenplaats"), (680, "Credit"));
        Regel((30, "4000"), (55, "Brutolonen"), (440, "1500"), (465, "Af"), (475, "te"), (485, "dragen"), (515, "loonheffing"));
        Regel((150, "DIRECT"), (350, "5.000,00"), (760, "2.500,00"));
        Regel((150, "INDIRECT"), (350, "2.000,00"));
        Regel((30, "4010"), (55, "Sociale"), (90, "lasten"), (440, "2100"), (465, "Netto"), (495, "loon"));
        Regel((150, "DIRECT"), (350, "3.000,00"), (760, "7.500,00"));
        Regel((30, "Totaal"), (350, totaal), (440, "Totaal"), (760, totaal));
        Regel((30, "Cumulatief"), (80, "journaal"), (760, "pagina"), (790, "1"), (800, "van"), (815, "1"));
        return builder.Build();
    }

    /// <summary>De opbouw van een ander salarispakket: één rekening per regel.</summary>
    public enum Lijstsoort
    {
        /// <summary>Kolommen Debet en Credit; het bedrag staat onder de kop waar het hoort.</summary>
        Kolommen,

        /// <summary>Eén kolom Bedrag met D of C erachter.</summary>
        DebetCredit,

        /// <summary>Eén kolom Bedrag; credit met een min erachter, geen kolomkoppen.</summary>
        Teken,
    }

    /// <summary>Een verzonnen loonjournaal als lijst: per regel nummer, naam en bedrag; met een nieuwe rekening 4062 als <paramref name="nieuw"/>.</summary>
    public static byte[] Lijst(Lijstsoort soort, bool nieuw = false)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var pagina = builder.AddPage(PageSize.A4);
        var y = 800.0;
        void Regel(params (double X, string Tekst)[] delen)
        {
            foreach (var (x, tekst) in delen)
            {
                pagina.AddText(tekst, 9, new PdfPoint(x, y), font);
            }

            y -= 14;
        }

        void Rekening(string nummer, string naam, string bedrag, bool credit)
        {
            var delen = new List<(double, string)> { (40, nummer) };
            var x = 80.0;
            foreach (var woord in naam.Split(' '))
            {
                delen.Add((x, woord));
                x += (woord.Length * 5) + 4;
            }

            delen.AddRange(soort switch
            {
                Lijstsoort.Kolommen => [(credit ? 470 : 380, bedrag)],
                Lijstsoort.DebetCredit => [(400, bedrag), (450, credit ? "C" : "D")],
                _ => [(400, credit ? bedrag + "-" : bedrag)],
            });
            Regel([.. delen]);
        }

        Regel((40, "Journaalpost"), (110, "lonen"), (150, "maart"), (185, "2026"));
        Regel((40, "Voorbeeld"), (95, "Advies"), (135, "B.V."));
        if (soort != Lijstsoort.Teken)
        {
            Regel((40, "Rekening"), (80, "Omschrijving"), soort == Lijstsoort.Kolommen ? (385, "Debet") : (400, "Bedrag"), soort == Lijstsoort.Kolommen ? (475, "Credit") : (450, "D/C"));
        }

        Rekening("4000", "Brutolonen", "6.000,00", credit: false);
        Rekening("4010", "Sociale lasten", "1.000,00", credit: false);
        if (nieuw)
        {
            Rekening("4062", "Reiskostenvergoeding via verloning", "200,00", credit: false);
        }

        Rekening("1500", "Af te dragen loonheffing", "2.500,00", credit: true);
        Rekening("2100", "Netto loon", nieuw ? "4.700,00" : "4.500,00", credit: true);
        var totaal = nieuw ? "7.200,00" : "7.000,00";
        Regel(soort == Lijstsoort.Kolommen ? [(80, "Totaal"), (380, totaal), (470, totaal)] : [(80, "Totaal"), (400, totaal)]);
        return builder.Build();
    }

    /// <summary>Een rekening voor een gegenereerd journaal: bedrag als tekst zoals in de PDF (bijv. "-1.250,00"), debet of credit.</summary>
    public sealed record Rekening(string Nummer, string Naam, string Bedrag, bool Credit);

    /// <summary>
    /// Een gegenereerd journaal over zoveel pagina's als nodig (40 regels per pagina): als lijst (kolommen Debet en
    /// Credit) of, met <paramref name="tweeKolommen"/>, debet links en credit rechts. Onderaan de totalen zoals opgegeven.
    /// </summary>
    public static byte[] Maak(IReadOnlyList<Rekening> rekeningen, string totaalDebet, string totaalCredit, bool tweeKolommen = false)
    {
        ArgumentNullException.ThrowIfNull(rekeningen);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder pagina = null!;
        var y = 0.0;
        void Nieuw()
        {
            pagina = builder.AddPage(842, 595);
            y = 560;
            Regel((30, "Loonjournaal"), (110, "periode"), (160, "2026-1"));
            Regel(tweeKolommen ? [(30, "Rekening"), (360, "Debet"), (440, "Rekening"), (760, "Credit")] : [(30, "Rekening"), (80, "Omschrijving"), (385, "Debet"), (475, "Credit")]);
        }

        void Regel(params (double X, string Tekst)[] delen)
        {
            foreach (var (x, tekst) in delen)
            {
                pagina.AddText(tekst, 8, new PdfPoint(x, y), font);
            }

            y -= 12;
        }

        IEnumerable<(double, string)> Naam(double x, string naam) => naam.Split(' ').Select((w, i) => (x + naam.Split(' ').Take(i).Sum(v => (v.Length * 4.6) + 3), w));

        Nieuw();
        if (tweeKolommen)
        {
            var debet = rekeningen.Where(r => !r.Credit).ToList();
            var credit = rekeningen.Where(r => r.Credit).ToList();
            for (var i = 0; i < Math.Max(debet.Count, credit.Count); i++)
            {
                if (y < 60)
                {
                    Nieuw();
                }

                var kop = new List<(double, string)>();
                var bedrag = new List<(double, string)>();
                if (i < debet.Count)
                {
                    kop.Add((30, debet[i].Nummer));
                    kop.AddRange(Naam(70, debet[i].Naam));
                    bedrag.Add((350, debet[i].Bedrag));
                }

                if (i < credit.Count)
                {
                    kop.Add((440, credit[i].Nummer));
                    kop.AddRange(Naam(480, credit[i].Naam));
                    bedrag.Add((760, credit[i].Bedrag));
                }

                Regel([.. kop]);
                Regel([.. bedrag]);
            }

            Regel((30, "Totaal"), (350, totaalDebet), (440, "Totaal"), (760, totaalCredit));
        }
        else
        {
            foreach (var r in rekeningen)
            {
                if (y < 60)
                {
                    Nieuw();
                }

                Regel([(30, r.Nummer), .. Naam(80, r.Naam), (r.Credit ? 470 : 380, r.Bedrag)]);
            }

            Regel((80, "Totaal"), (380, totaalDebet), (470, totaalCredit));
        }

        return builder.Build();
    }
}
