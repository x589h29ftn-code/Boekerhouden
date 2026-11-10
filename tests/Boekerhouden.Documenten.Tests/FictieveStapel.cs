using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Een verzonnen stapel: inkoopfacturen van verschillende leveranciers achter elkaar in één PDF, van één tot vijf
/// pagina's, met en zonder paginanummering, een leeg blad ertussen en een pagina algemene voorwaarden.
/// </summary>
internal static class FictieveStapel
{
    /// <summary>Een factuur in de stapel.</summary>
    /// <param name="Nummering">"Pagina 1 van 3" op elke pagina.</param>
    /// <param name="Voorwaarden">Een laatste pagina met alleen algemene voorwaarden.</param>
    internal sealed record Factuur(string Leverancier, string Iban, string Btw, string Nummer, string Datum, int Paginas, decimal Exclusief, bool Nummering = true, bool Voorwaarden = false)
    {
        public int Totaalpaginas => Paginas + (Voorwaarden ? 1 : 0);
    }

    internal static readonly Factuur[] Inkoop =
    [
        new("Belnet B.V.", "NL91ABNA0417164300", "NL001234567B01", "BN-2026-0411", "01-04-2026", 1, 20.65m),
        new("Groothandel De Linde B.V.", "NL18RABO0123459876", "NL002345678B01", "GL-88012", "03-04-2026", 3, 1250.00m),
        new("Energie Noord N.V.", "NL69INGB0123456789", "NL003456789B01", "EN-2026-55120", "07-04-2026", 2, 310.40m, Nummering: false),
        new("Groothandel De Linde B.V.", "NL18RABO0123459876", "NL002345678B01", "GL-88047", "12-04-2026", 1, 96.00m),
        new("Drukkerij Inkt V.O.F.", "NL44RABO0123456789", "NL004567890B01", "2026-0193", "15-04-2026", 5, 2480.00m),
        new("Kantoorhal B.V.", "NL86INGB0002445588", "NL005678901B01", "KH-7741", "20-04-2026", 1, 54.90m, Nummering: false, Voorwaarden: true),
    ];

    /// <summary>De pagina (1 = de eerste) waarop elke factuur begint, met een leeg blad na de tweede factuur.</summary>
    internal static IReadOnlyList<int> Beginpaginas
    {
        get
        {
            var begin = new List<int>();
            var pagina = 1;
            for (var i = 0; i < Inkoop.Length; i++)
            {
                begin.Add(pagina);
                pagina += Inkoop[i].Totaalpaginas + (i == LeegNa ? 1 : 0);
            }

            return begin;
        }
    }

    /// <summary>Na deze factuur (index) zit een leeg blad.</summary>
    internal const int LeegNa = 1;

    /// <summary>Het nummer van de lege pagina.</summary>
    internal static int LegePagina => Beginpaginas[LeegNa + 1] - 1;

    internal static int AantalPaginas => Inkoop.Sum(f => f.Totaalpaginas) + 1;

    internal static byte[] Pdf() => Pdf(Inkoop, LeegNa);

    internal static byte[] Pdf(IReadOnlyList<Factuur> facturen, int? leegNa = null)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < facturen.Count; i++)
        {
            var f = facturen[i];
            var perPagina = Math.Round(f.Exclusief / f.Paginas, 2);
            for (var p = 1; p <= f.Paginas; p++)
            {
                var pagina = builder.AddPage(PageSize.A4);
                var y = 800.0;
                void Regel(params (double X, string Tekst)[] delen)
                {
                    foreach (var (x, tekst) in delen)
                    {
                        pagina.AddText(tekst, 10, new PdfPoint(x, y), font);
                    }

                    y -= 14;
                }

                Regel((40, f.Leverancier));
                if (p == 1)
                {
                    Regel((40, "Dorpsstraat 1"), (350, "Voorbeeld Handel B.V."));
                    Regel((40, "1234 AB Ergens"), (350, "Laan 2"));
                    Regel((40, $"IBAN {f.Iban}"));
                    Regel((40, $"BTW {f.Btw}"));
                    Regel((40, "Factuur"));
                    Regel((40, "Factuurnummer:"), (160, f.Nummer));
                    Regel((40, "Factuurdatum:"), (160, f.Datum));
                }
                else if (f.Nummering)
                {
                    Regel((40, "Factuurnummer:"), (160, f.Nummer));
                }
                else
                {
                    Regel((40, "Specificatie van uw verbruik"));
                }

                Regel((40, "Omschrijving"), (470, "Bedrag"));
                for (var r = 1; r <= 6; r++)
                {
                    Regel((40, $"Levering {p}.{r}"), (470, Bedrag(Math.Round(perPagina / 6, 2))));
                }

                if (p == f.Paginas)
                {
                    var btw = Math.Round(f.Exclusief * 0.21m, 2);
                    Regel((300, "Subtotaal"), (470, Bedrag(f.Exclusief)));
                    Regel((300, "BTW 21%"), (470, Bedrag(btw)));
                    Regel((300, "Totaal te betalen"), (470, Bedrag(f.Exclusief + btw)));
                }

                if (f.Nummering)
                {
                    pagina.AddText($"Pagina {p} van {f.Paginas}", 9, new PdfPoint(260, 40), font);
                }
            }

            if (f.Voorwaarden)
            {
                var pagina = builder.AddPage(PageSize.A4);
                var y = 800.0;
                foreach (var regel in new[] { "Algemene voorwaarden", "Op al onze leveringen zijn deze voorwaarden van toepassing.", "Betaling binnen de termijn die op de voorzijde staat.", "Klachten meldt u schriftelijk binnen acht dagen." })
                {
                    pagina.AddText(regel, 9, new PdfPoint(40, y), font);
                    y -= 13;
                }
            }

            if (i == leegNa)
            {
                builder.AddPage(PageSize.A4);
            }
        }

        return builder.Build();
    }

    private static string Bedrag(decimal bedrag) => bedrag.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));
}
