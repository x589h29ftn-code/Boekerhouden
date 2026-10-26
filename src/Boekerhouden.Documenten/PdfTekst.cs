using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Boekerhouden.Documenten;

/// <summary>Een bijlage in een PDF, bijv. de UBL-versie van de factuur.</summary>
public sealed record PdfBijlage(string Naam, byte[] Inhoud);

/// <summary>De tekst van een PDF als regels (per pagina van boven naar beneden), en de ingesloten bestanden.</summary>
public sealed record PdfInhoud(int Paginas, IReadOnlyList<string> Regels, IReadOnlyList<PdfBijlage> Bijlagen)
{
    public string Tekst => string.Join('\n', Regels);

    /// <summary>Geen tekstlaag: een scan of foto, alleen met OCR te lezen.</summary>
    public bool IsScan => Regels.Count == 0;
}

/// <summary>
/// Haalt de tekst uit een PDF met de woorden op hun plek: woorden op dezelfde hoogte vormen één regel, van links naar
/// rechts, met een grotere tussenruimte als er een kolom tussen zit. Zo blijft "Totaal ... € 24,99" één regel, zoals de
/// tekstanalyse dat verwacht. Grenzen tegen kwaadaardige of te grote bestanden: grootte, aantal pagina's en bijlagen.
/// </summary>
public static class PdfTekst
{
    public const long MaximumBytes = 20 * 1024 * 1024;
    public const int MaximumPaginas = 50;
    public const long MaximumBijlageBytes = 5 * 1024 * 1024;

    public static PdfInhoud Lees(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = LeesBegrensd(stream);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            throw new DocumentException("Het bestand is geen PDF.");
        }

        try
        {
            using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            if (document.NumberOfPages > MaximumPaginas)
            {
                throw new DocumentException($"De PDF heeft {document.NumberOfPages} pagina's; een factuur mag er hoogstens {MaximumPaginas} hebben.");
            }

            var regels = new List<string>();
            foreach (var pagina in document.GetPages())
            {
                regels.AddRange(Regels(pagina));
            }

            var bijlagen = new List<PdfBijlage>();
            if (document.Advanced.TryGetEmbeddedFiles(out var bestanden))
            {
                bijlagen.AddRange(bestanden
                    .Where(b => b.Memory.Length <= MaximumBijlageBytes)
                    .Select(b => new PdfBijlage(b.Name ?? b.FileSpecification ?? "bijlage", b.Memory.ToArray())));
            }

            return new PdfInhoud(document.NumberOfPages, regels, bijlagen);
        }
        catch (DocumentException)
        {
            throw;
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Versleuteld, beschadigd of een constructie die de lezer niet kent: nooit de app laten vallen.
            throw new DocumentException("De PDF is niet te lezen (beschadigd of beveiligd met een wachtwoord).", fout);
        }
    }

    /// <summary>De woorden van een pagina, gegroepeerd tot regels van boven naar beneden.</summary>
    internal static IEnumerable<string> Regels(Page pagina)
    {
        var woorden = pagina.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == TextOrientation.Horizontal)
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();
        var regels = new List<List<Word>>();
        foreach (var woord in woorden)
        {
            var hoogte = Math.Max(1, woord.BoundingBox.Height);
            var regel = regels.FirstOrDefault(r => Math.Abs(Midden(r[0]) - Midden(woord)) < 0.5 * Math.Max(hoogte, r[0].BoundingBox.Height));
            if (regel is null)
            {
                regels.Add([woord]);
            }
            else
            {
                regel.Add(woord);
            }
        }

        foreach (var regel in regels.OrderByDescending(r => r.Average(Midden)))
        {
            var gesorteerd = regel.OrderBy(w => w.BoundingBox.Left).ToList();
            var tekst = new System.Text.StringBuilder(gesorteerd[0].Text);
            for (var i = 1; i < gesorteerd.Count; i++)
            {
                var vorige = gesorteerd[i - 1];
                var tekenbreedte = vorige.BoundingBox.Width / Math.Max(1, vorige.Text.Length);
                var ruimte = gesorteerd[i].BoundingBox.Left - vorige.BoundingBox.Right;
                tekst.Append(ruimte > 3 * tekenbreedte ? "    " : " ").Append(gesorteerd[i].Text);
            }

            yield return tekst.ToString();
        }
    }

    private static double Midden(Word woord) => (woord.BoundingBox.Top + woord.BoundingBox.Bottom) / 2;

    private static byte[] LeesBegrensd(Stream stream)
    {
        using var kopie = new MemoryStream();
        var buffer = new byte[81920];
        int gelezen;
        while ((gelezen = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (kopie.Length + gelezen > MaximumBytes)
            {
                throw new DocumentException($"Het bestand is groter dan {MaximumBytes / 1024 / 1024} MB.");
            }

            kopie.Write(buffer, 0, gelezen);
        }

        return kopie.ToArray();
    }
}

/// <summary>Een document dat niet gelezen kan of mag worden, met een melding voor de gebruiker.</summary>
public sealed class DocumentException : Exception
{
    public DocumentException()
        : base("Het document is niet te lezen.")
    {
    }

    public DocumentException(string melding)
        : base(melding)
    {
    }

    public DocumentException(string melding, Exception binnen)
        : base(melding, binnen)
    {
    }
}
