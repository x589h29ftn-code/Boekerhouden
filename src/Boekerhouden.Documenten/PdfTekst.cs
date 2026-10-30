using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Boekerhouden.Documenten;

/// <summary>Een bijlage in een PDF, bijv. de UBL-versie van de factuur.</summary>
public sealed record PdfBijlage(string Naam, byte[] Inhoud);

/// <summary>Een plek op een pagina, in PDF-punten (oorsprong linksonder), zoals pdf.js die kan tekenen.</summary>
public sealed record PdfPlek(int Pagina, double X0, double Y0, double X1, double Y1);

/// <summary>
/// Een woord op een pagina met zijn plek in PDF-punten (oorsprong linksonder): uit de tekstlaag van de PDF of uit de
/// OCR van een scan. Daarmee werken regels, aanwijzen en oplichten voor beide op dezelfde manier.
/// </summary>
public sealed record Tekstwoord(int Pagina, string Tekst, double X0, double Y0, double X1, double Y1)
{
    internal double Breedte => X1 - X0;

    internal double Hoogte => Y1 - Y0;

    internal double Midden => (Y0 + Y1) / 2;
}

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
        return Open(LeesBegrensd(stream), document =>
        {
            var regels = new List<string>();
            foreach (var pagina in document.GetPages())
            {
                regels.AddRange(Regels(Woorden(pagina)));
            }

            var bijlagen = new List<PdfBijlage>();
            if (document.Advanced.TryGetEmbeddedFiles(out var bestanden))
            {
                bijlagen.AddRange(bestanden
                    .Where(b => b.Memory.Length <= MaximumBijlageBytes)
                    .Select(b => new PdfBijlage(b.Name ?? b.FileSpecification ?? "bijlage", b.Memory.ToArray())));
            }

            return new PdfInhoud(document.NumberOfPages, regels, bijlagen);
        });
    }

    /// <summary>Alle woorden van een PDF met hun plek (uit de tekstlaag); leeg bij een scan.</summary>
    public static IReadOnlyList<Tekstwoord> Woorden(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length > MaximumBytes)
        {
            throw new DocumentException($"Het bestand is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        return Open(pdf, document => (IReadOnlyList<Tekstwoord>)[.. document.GetPages().SelectMany(Woorden)]);
    }

    /// <summary>
    /// De tekst in een gebied van een pagina, als regels van boven naar beneden; zo neemt de gebruiker een omschrijving,
    /// bedrag of nummer over door het op de factuur aan te wijzen. Het gebied is in PDF-punten (oorsprong linksonder,
    /// zoals pdf.js het doorgeeft). Een woord telt mee als zijn midden in het gebied ligt; een klik (een heel klein
    /// gebied) pakt het woord waarop geklikt is.
    /// </summary>
    public static IReadOnlyList<string> InGebied(byte[] pdf, int pagina, double x0, double y0, double x1, double y1)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length > MaximumBytes)
        {
            throw new DocumentException($"Het bestand is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        return Open(pdf, document =>
        {
            if (pagina < 1 || pagina > document.NumberOfPages)
            {
                throw new DocumentException($"Pagina {pagina} bestaat niet.");
            }

            return InGebied(Woorden(document.GetPage(pagina)), pagina, x0, y0, x1, y1);
        });
    }

    /// <summary>De tekst in een gebied, uit losse woorden (bijv. de OCR van een scan); zie de andere overload.</summary>
    public static IReadOnlyList<string> InGebied(IEnumerable<Tekstwoord> woorden, int pagina, double x0, double y0, double x1, double y1)
    {
        ArgumentNullException.ThrowIfNull(woorden);
        var (links, rechts, onder, boven) = (Math.Min(x0, x1), Math.Max(x0, x1), Math.Min(y0, y1), Math.Max(y0, y1));
        var klik = rechts - links < 4 && boven - onder < 4;
        var (mx, my) = ((links + rechts) / 2, (onder + boven) / 2);
        return [.. Regels(woorden.Where(w => w.Pagina == pagina && (klik
            ? mx >= w.X0 - 1 && mx <= w.X1 + 1 && my >= w.Y0 - 1 && my <= w.Y1 + 1
            : (w.X0 + w.X1) / 2 >= links && (w.X0 + w.X1) / 2 <= rechts && w.Midden >= onder && w.Midden <= boven)))];
    }

    /// <summary>
    /// Waar de waarden van de velden op de factuur staan: per veld de plekken (hoogstens <paramref name="maximum"/>) waar
    /// een van de vormen staat, als één of enkele opeenvolgende woorden op een regel. Vergelijken zonder spaties,
    /// hoofdletters en leestekens aan de randen (<see cref="Boekhouding.Herkenning.Tekstvormen.Compact"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PdfPlek>> Zoek(byte[] pdf, IReadOnlyDictionary<string, IReadOnlyList<string>> velden, int maximum = 3)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(velden);
        if (pdf.Length > MaximumBytes)
        {
            throw new DocumentException($"Het bestand is groter dan {MaximumBytes / 1024 / 1024} MB.");
        }

        return Zoek(Open(pdf, document => Enumerable.Range(1, document.NumberOfPages).SelectMany(n => Woorden(document.GetPage(n))).ToList()), velden, maximum);
    }

    /// <summary>Waar de waarden van de velden staan, in losse woorden (bijv. de OCR van een scan); zie de andere overload.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PdfPlek>> Zoek(IEnumerable<Tekstwoord> alleWoorden, IReadOnlyDictionary<string, IReadOnlyList<string>> velden, int maximum = 3)
    {
        ArgumentNullException.ThrowIfNull(alleWoorden);
        ArgumentNullException.ThrowIfNull(velden);
        var gezocht = velden.ToDictionary(v => v.Key, v => v.Value.Select(Boekhouding.Herkenning.Tekstvormen.Compact).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal));
        var langste = gezocht.Values.SelectMany(v => v).Select(v => v.Length).DefaultIfEmpty(0).Max();
        var gevonden = velden.Keys.ToDictionary(k => k, _ => new List<PdfPlek>());
        if (langste == 0)
        {
            return gevonden.ToDictionary(v => v.Key, v => (IReadOnlyList<PdfPlek>)v.Value);
        }

        foreach (var perPagina in alleWoorden.GroupBy(w => w.Pagina).OrderBy(g => g.Key))
        {
            foreach (var regel in RegelsMetWoorden(perPagina))
            {
                for (var i = 0; i < regel.Count; i++)
                {
                    var tekst = string.Empty;
                    for (var j = i; j < regel.Count && j < i + 10; j++)
                    {
                        tekst += regel[j].Tekst;
                        var compact = Boekhouding.Herkenning.Tekstvormen.Compact(tekst);
                        if (compact.Length > langste + 2)
                        {
                            break;
                        }

                        foreach (var (veld, vormen) in gezocht)
                        {
                            if (gevonden[veld].Count < maximum && vormen.Contains(compact))
                            {
                                var woorden = regel.Skip(i).Take(j - i + 1).ToList();
                                gevonden[veld].Add(new PdfPlek(perPagina.Key, woorden.Min(w => w.X0), woorden.Min(w => w.Y0), woorden.Max(w => w.X1), woorden.Max(w => w.Y1)));
                            }
                        }
                    }
                }
            }
        }

        return gevonden.ToDictionary(v => v.Key, v => (IReadOnlyList<PdfPlek>)v.Value);
    }

    private static T Open<T>(byte[] bytes, Func<PdfDocument, T> lees)
    {
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

            return lees(document);
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

    /// <summary>De woorden van een pagina uit de tekstlaag (alleen horizontale tekst).</summary>
    internal static IEnumerable<Tekstwoord> Woorden(Page pagina) =>
        pagina.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == TextOrientation.Horizontal)
            .Select(w => new Tekstwoord(pagina.Number, w.Text, w.BoundingBox.Left, w.BoundingBox.Bottom, w.BoundingBox.Right, w.BoundingBox.Top));

    /// <summary>
    /// De woorden als regels: per pagina van boven naar beneden, en binnen een regel met een grotere tussenruimte als er
    /// een kolom tussen zit (zoals de tekstanalyse dat verwacht).
    /// </summary>
    public static IReadOnlyList<string> Regels(IEnumerable<Tekstwoord> alleWoorden)
    {
        ArgumentNullException.ThrowIfNull(alleWoorden);
        var regels = new List<string>();
        foreach (var perPagina in alleWoorden.GroupBy(w => w.Pagina).OrderBy(g => g.Key))
        {
            foreach (var gesorteerd in RegelsMetWoorden(perPagina))
            {
                var tekst = new System.Text.StringBuilder(gesorteerd[0].Tekst);
                for (var i = 1; i < gesorteerd.Count; i++)
                {
                    var vorige = gesorteerd[i - 1];
                    var tekenbreedte = vorige.Breedte / Math.Max(1, vorige.Tekst.Length);
                    var ruimte = gesorteerd[i].X0 - vorige.X1;
                    tekst.Append(ruimte > 3 * tekenbreedte ? "    " : " ").Append(gesorteerd[i].Tekst);
                }

                regels.Add(tekst.ToString());
            }
        }

        return regels;
    }

    /// <summary>De woorden van één pagina gegroepeerd tot regels (van boven naar beneden), elke regel van links naar rechts.</summary>
    private static IEnumerable<List<Tekstwoord>> RegelsMetWoorden(IEnumerable<Tekstwoord> alleWoorden)
    {
        var woorden = alleWoorden.Where(w => !string.IsNullOrWhiteSpace(w.Tekst)).OrderByDescending(w => w.Y0).ToList();
        var regels = new List<List<Tekstwoord>>();
        foreach (var woord in woorden)
        {
            var hoogte = Math.Max(1, woord.Hoogte);
            var regel = regels.FirstOrDefault(r => Math.Abs(r[0].Midden - woord.Midden) < 0.5 * Math.Max(hoogte, r[0].Hoogte));
            if (regel is null)
            {
                regels.Add([woord]);
            }
            else
            {
                regel.Add(woord);
            }
        }

        foreach (var regel in regels.OrderByDescending(r => r.Average(w => w.Midden)))
        {
            yield return regel.OrderBy(w => w.X0).ToList();
        }
    }

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
