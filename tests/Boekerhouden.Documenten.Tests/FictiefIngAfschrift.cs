using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een mutatie op het verzonnen ING-afschrift: boekdatum, naam, type, het bedrag (af negatief) en de regels eronder.</summary>
public sealed record IngRegel(string Datum, string Naam, string Type, decimal Bedrag, IReadOnlyList<string> Details);

/// <summary>
/// Een verzonnen afschrift in de opmaak van een zakelijk ING-afschrift over een periode: kop met periode, begin- en
/// eindsaldo, en per mutatie de boekdatum voluit, naam, type en één bedragkolom met + of -, met regels als
/// "Omschrijving:", "IBAN:" en "Valutadatum:" eronder. Alleen voor de tests.
/// </summary>
public static class FictiefIngAfschrift
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public const string Eigen = "NL20INGB0001234567";

    public static readonly IngRegel[] Mutaties =
    [
        new("26-01-2026", "Kosten Zakelijk Betalingsverkeer", "Diversen", -12.50m, ["Factuurnr. 1000000001", "Betreft IBAN: NL20INGB0001234567", "Periode: 01-12-2025 / 31-12-2025", "Valutadatum: 26-01-2026"]),
        new("03-02-2026", "Kantoorhandel Jansen", "Online bankieren", -199.65m, ["Omschrijving: Factuur 2026-0042", "betaling inkoop", "IBAN: NL36INGB0007654321", "Datum/Tijd: 03-02-2026 10:12:44", "Valutadatum: 03-02-2026"]),
        new("10-02-2026", "J. de Vries", "Online bankieren", 1210m, ["Omschrijving: Factuur 20260001", "IBAN: NL44RABO0123456789", "Valutadatum: 10-02-2026"]),
        new("26-02-2026", "Kosten Zakelijk Betalingsverkeer", "Diversen", -12.75m, ["Factuurnr. 1000000002", "Betreft IBAN: NL20INGB0001234567", "Periode: 01-01-2026 / 31-01-2026", "Valutadatum: 26-02-2026"]),
        new("02-03-2026", "Webwinkel via Betaaldienst B.V.", "iDEAL", -45m, ["Omschrijving: 1234567890", "0030001234567890 W1234567890", "IBAN: NL02ABNA0123456789", "Kenmerk: 02-03-2026 11:39 0030001234567890", "Valutadatum: 02-03-2026"]),
    ];

    public static byte[] Pdf(decimal beginsaldo = 1000m, decimal? eindsaldo = null, int perPagina = 3)
    {
        var eind = eindsaldo ?? beginsaldo + Mutaties.Sum(m => m.Bedrag);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var groepen = Mutaties.Chunk(perPagina).ToList();
        for (var p = 0; p < groepen.Count; p++)
        {
            var pagina = builder.AddPage(PageSize.A4);
            var y = 800.0;
            void Tekst(double x, string tekst, double grootte = 8) => pagina.AddText(tekst, grootte, new PdfPoint(x, y), font);

            Tekst(330, "Afschrift Zakelijke rekening", 11);
            y -= 30;
            Tekst(380, "Periode", 7);
            y -= 10;
            Tekst(380, "01-01-2026 t/m 31-03-2026");
            if (p == 0)
            {
                y -= 20;
                Tekst(70, "Voorbeeld Handel B.V.");
                Tekst(400, "Rekeningnummer", 7);
                y -= 10;
                Tekst(400, "NL20 INGB 0001 2345 67");
                y -= 14;
                Tekst(380, "Beginsaldo (EUR)", 7);
                Tekst(470, "Totaal bij (EUR)", 7);
                y -= 10;
                Tekst(385, Bedrag(beginsaldo));
                Tekst(475, Bedrag(Mutaties.Where(m => m.Bedrag > 0).Sum(m => m.Bedrag)));
                y -= 12;
                Tekst(380, "Eindsaldo (EUR)", 7);
                Tekst(470, "Totaal af (EUR)", 7);
                y -= 10;
                Tekst(385, Bedrag(eind));
                Tekst(475, Bedrag(-Mutaties.Where(m => m.Bedrag < 0).Sum(m => m.Bedrag)));
            }
            else
            {
                y -= 14;
                Tekst(48, "Rekeningnummer", 7);
                y -= 10;
                Tekst(48, "NL20 INGB 0001 2345 67");
            }

            y -= 30;
            Tekst(48, "Geboekt op", 7);
            Tekst(100, "Naam / Omschrijving / Mededeling", 7);
            Tekst(330, "Type", 7);
            Tekst(480, "Bedrag (EUR)", 7);
            foreach (var m in groepen[p])
            {
                y -= 14;
                Tekst(48, m.Datum);
                Tekst(100, m.Naam);
                Tekst(330, m.Type);
                Tekst(490, (m.Bedrag > 0 ? "+" : "-") + Bedrag(Math.Abs(m.Bedrag)));
                foreach (var regel in m.Details)
                {
                    y -= 9;
                    Tekst(100, regel);
                }
            }

            y -= 40;
            Tekst(20, "A-VOORBEELD01", 6);
            y -= 20;
            Tekst(48, $"Dit is een verzonnen afschrift voor de tests.   Pagina {p + 1} van {groepen.Count}", 7);
        }

        return builder.Build();
    }

    private static string Bedrag(decimal bedrag) => (bedrag < 0 ? "-" : "") + Math.Abs(bedrag).ToString("#,0.00", Nl);
}
