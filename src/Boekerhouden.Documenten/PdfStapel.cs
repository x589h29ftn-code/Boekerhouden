using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten;

/// <summary>De tekst van één pagina van een stapel.</summary>
/// <param name="Woorden">De woorden uit de tekstlaag met hun plek.</param>
/// <param name="Scan">Geen tekst maar wel een afbeelding: een scan, te lezen met tekstherkenning.</param>
public sealed record Stapelpaginatekst(int Pagina, IReadOnlyList<Tekstwoord> Woorden, bool Scan)
{
    public IReadOnlyList<string> Regels => PdfTekst.Regels(Woorden);
}

/// <summary>
/// Een stapel: één PDF met veel facturen achter elkaar (bijv. een gescande jaaradministratie). Ruimere grenzen dan een
/// losse factuur; de tekst per pagina (voor het voorstel waar te knippen) en het splitsen in losse PDF's, waarbij de
/// pagina's ongewijzigd worden overgenomen.
/// </summary>
public static class PdfStapel
{
    public const long MaximumBytes = 300L * 1024 * 1024;
    public const int MaximumPaginas = 500;

    /// <summary>De tekst per pagina; gooit een <see cref="DocumentException"/> als het geen leesbare PDF is of te groot.</summary>
    public static IReadOnlyList<Stapelpaginatekst> Paginas(byte[] pdf) =>
        Open(pdf, document =>
        {
            var lijst = new List<Stapelpaginatekst>(document.NumberOfPages);
            foreach (var pagina in document.GetPages())
            {
                var woorden = PdfTekst.Woorden(pagina).ToList();
                lijst.Add(new Stapelpaginatekst(pagina.Number, woorden, woorden.Count == 0 && pagina.GetImages().Any()));
            }

            return (IReadOnlyList<Stapelpaginatekst>)lijst;
        });

    /// <summary>
    /// Per groep pagina's (1 = de eerste) een eigen PDF, in de volgorde van de groepen. Een pagina mag in hoogstens
    /// één groep zitten.
    /// </summary>
    public static IReadOnlyList<byte[]> Splits(byte[] pdf, IReadOnlyList<IReadOnlyList<int>> groepen)
    {
        ArgumentNullException.ThrowIfNull(groepen);
        return Open(pdf, document =>
        {
            var alle = groepen.SelectMany(g => g).ToList();
            if (alle.Count != alle.Distinct().Count() || alle.Any(p => p < 1 || p > document.NumberOfPages))
            {
                throw new DocumentException("De indeling van de stapel klopt niet: een pagina staat er dubbel in of bestaat niet.");
            }

            var delen = new List<byte[]>(groepen.Count);
            foreach (var groep in groepen.Where(g => g.Count > 0))
            {
                using var bouwer = new PdfDocumentBuilder();
                foreach (var pagina in groep)
                {
                    bouwer.AddPage(document, pagina);
                }

                delen.Add(bouwer.Build());
            }

            return (IReadOnlyList<byte[]>)delen;
        });
    }

    private static T Open<T>(byte[] pdf, Func<PdfDocument, T> lees)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length > MaximumBytes)
        {
            throw new DocumentException($"De stapel is groter dan {MaximumBytes / 1024 / 1024} MB; lever hem in delen aan.");
        }

        if (pdf.AsSpan(0, Math.Min(pdf.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            throw new DocumentException("Het bestand is geen PDF.");
        }

        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            if (document.NumberOfPages > MaximumPaginas)
            {
                throw new DocumentException($"De stapel heeft {document.NumberOfPages} pagina's; hoogstens {MaximumPaginas} per keer. Lever hem in delen aan.");
            }

            return lees(document);
        }
        catch (DocumentException)
        {
            throw;
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            throw new DocumentException("De PDF is niet te lezen (beschadigd of beveiligd met een wachtwoord).", fout);
        }
    }
}
