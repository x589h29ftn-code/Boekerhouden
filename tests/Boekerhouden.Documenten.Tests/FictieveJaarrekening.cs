using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een verzonnen jaarrekening als PDF, voor de beginbalans. Alleen voor de tests.</summary>
public static class FictieveJaarrekening
{
    /// <summary>
    /// Een verzonnen jaarrekening: een inhoudsopgave die de balans noemt, dan de balans met twee kolommen per jaar
    /// (posten links, subtotalen rechts) en een paginavoet.
    /// </summary>
    public static byte[] Pdf(string liquide = "1.250")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 0.0;
        PdfPageBuilder pagina = null!;
        void Nieuw()
        {
            pagina = builder.AddPage(PageSize.A4);
            y = 800;
        }

        void Regel(params (double X, string Tekst)[] delen)
        {
            foreach (var (x, tekst) in delen)
            {
                pagina.AddText(tekst, 9, new PdfPoint(x, y), font);
            }

            y -= 14;
        }

        Nieuw();
        Regel((60, "Voorbeeld Holding B.V., te Sneek"));
        Regel((60, "INHOUDSOPGAVE"));
        Regel((60, "1"), (80, "Balans per 31 december 2025"), (400, "4"));
        Regel((60, "2"), (80, "Winst-en-verliesrekening over 2025"), (400, "6"));
        Nieuw();
        Regel((60, "1"), (80, "BALANS PER 31 DECEMBER 2025"));
        Regel((60, "ACTIVA"));
        Regel((60, "(Na resultaatbestemming)"), (250, "31 dec 2025"), (400, "31 dec 2024"));
        Regel((60, "Vaste activa"));
        Regel((60, "Materiële vaste activa"));
        Regel((60, "Inventaris"), (200, "4.000"), (340, "5.000"));
        Regel((270, "4.000"), (410, "5.000"));
        Regel((60, "Vlottende activa"));
        Regel((60, "Vorderingen"));
        Regel((60, "Debiteuren"), (200, "2.500"), (340, "1.000"));
        Regel((60, "Rekening-courant directie"), (200, "750"), (340, "-"));
        Regel((270, "3.250"), (410, "1.000"));
        Regel((60, "Liquide middelen"), (270, liquide), (410, "3.000"));
        Regel((60, "Totaal"), (270, "8.500"), (410, "9.000"));
        Regel((200, "4"), (300, "Samenstellingsverklaring afgegeven"));
        Nieuw();
        Regel((60, "PASSIVA"));
        Regel((60, "(Na resultaatbestemming)"), (250, "31 dec 2025"), (400, "31 dec 2024"));
        Regel((60, "Eigen vermogen"));
        Regel((60, "Aandelenkapitaal"), (200, "1.000"), (340, "1.000"));
        Regel((60, "Overige reserves"), (200, "3.300"), (340, "2.800"));
        Regel((270, "4.300"), (410, "3.800"));
        Regel((60, "Langlopende schulden"));
        Regel((60, "Overige schulden"), (200, "2.000"), (340, "2.500"));
        Regel((60, "Kortlopende schulden"));
        Regel((60, "Crediteuren"), (200, "1.700"), (340, "2.200"));
        Regel((60, "Overlopende passiva"), (200, "500"), (340, "500"));
        Regel((270, "2.200"), (410, "2.700"));
        Regel((60, "Totaal"), (270, "8.500"), (410, "9.000"));
        return builder.Build();
    }
}
