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
}
