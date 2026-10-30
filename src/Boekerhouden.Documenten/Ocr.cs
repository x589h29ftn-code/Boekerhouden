using System.Diagnostics;
using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten;

/// <summary>Een woord dat de OCR vond, in pixels van de afbeelding (oorsprong linksboven), met de zekerheid (0–100).</summary>
public sealed record OcrWoord(string Tekst, int Links, int Boven, int Breedte, int Hoogte, double Zekerheid);

/// <summary>Wat de OCR in een afbeelding vond: de maat van de afbeelding en de woorden.</summary>
public sealed record OcrUitkomst(int Breedte, int Hoogte, IReadOnlyList<OcrWoord> Woorden);

/// <summary>Tekstherkenning van een afbeelding (een scan of foto).</summary>
public interface IOcrMotor
{
    Task<OcrUitkomst> LeesAsync(byte[] afbeelding, CancellationToken annulering = default);
}

/// <summary>
/// Tesseract (Apache-2.0) als eigen proces: de afbeelding gaat via stdin erin, de woorden met hun plek komen als TSV
/// terug. Zonder shell, met een tijdslimiet, en het proces wordt bij overschrijding gestopt. Talen: Nederlands en
/// Engels, voor zover geïnstalleerd.
/// </summary>
public sealed class TesseractOcr : IOcrMotor
{
    public static readonly TimeSpan Tijdslimiet = TimeSpan.FromSeconds(90);

    private static readonly string[] Talen = ["nld", "eng"];

    private readonly string programma;
    private readonly string? talen;

    private TesseractOcr(string programma, string? talen)
    {
        this.programma = programma;
        this.talen = talen;
    }

    /// <summary>De plekken waar Tesseract meestal staat, na het pad uit de instellingen en het zoekpad.</summary>
    private static IEnumerable<string> Kandidaten(string? ingesteld)
    {
        if (!string.IsNullOrWhiteSpace(ingesteld))
        {
            yield return ingesteld;
        }

        yield return "tesseract";
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tesseract.exe");
        }
    }

    /// <summary>Tesseract als hij te starten is (met de beschikbare talen), anders leeg: dan is er geen OCR.</summary>
    public static TesseractOcr? Zoek(string? ingesteld = null)
    {
        foreach (var kandidaat in Kandidaten(ingesteld))
        {
            try
            {
                var (code, uitvoer) = Draai(kandidaat, ["--list-langs"], null, TimeSpan.FromSeconds(15), CancellationToken.None).GetAwaiter().GetResult();
                if (code != 0)
                {
                    continue;
                }

                var aanwezig = uitvoer.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var talen = string.Join('+', Talen.Where(aanwezig.Contains));
                return new TesseractOcr(kandidaat, talen.Length > 0 ? talen : null);
            }
            catch (Exception fout) when (fout is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException or IOException)
            {
                // Niet geïnstalleerd op deze plek: de volgende proberen.
            }
        }

        return null;
    }

    public async Task<OcrUitkomst> LeesAsync(byte[] afbeelding, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(afbeelding);
        List<string> argumenten = ["stdin", "stdout", "--psm", "3"];
        if (talen is not null)
        {
            argumenten.InsertRange(2, ["-l", talen]);
        }

        argumenten.Add("tsv");
        var (code, tsv) = await Draai(programma, argumenten, afbeelding, Tijdslimiet, annulering).ConfigureAwait(false);
        if (code != 0)
        {
            throw new DocumentException("De tekstherkenning (OCR) kon deze afbeelding niet lezen.");
        }

        return LeesTsv(tsv);
    }

    /// <summary>De uitvoer van Tesseract (TSV): niveau 1 geeft de maat van de afbeelding, niveau 5 de woorden.</summary>
    public static OcrUitkomst LeesTsv(string tsv)
    {
        ArgumentNullException.ThrowIfNull(tsv);
        var (breedte, hoogte) = (0, 0);
        var woorden = new List<OcrWoord>();
        foreach (var regel in tsv.Split('\n').Skip(1))
        {
            var k = regel.TrimEnd('\r').Split('\t');
            if (k.Length < 12 || !int.TryParse(k[0], CultureInfo.InvariantCulture, out var niveau))
            {
                continue;
            }

            int Getal(int i) => int.TryParse(k[i], CultureInfo.InvariantCulture, out var g) ? g : 0;
            if (niveau == 1)
            {
                (breedte, hoogte) = (Math.Max(breedte, Getal(8)), Math.Max(hoogte, Getal(9)));
            }
            else if (niveau == 5 && k[11].Trim() is { Length: > 0 } tekst)
            {
                woorden.Add(new OcrWoord(tekst, Getal(6), Getal(7), Getal(8), Getal(9),
                    double.TryParse(k[10], CultureInfo.InvariantCulture, out var zeker) ? zeker : 0));
            }
        }

        return new OcrUitkomst(breedte, hoogte, woorden);
    }

    private static async Task<(int Code, string Uitvoer)> Draai(string programma, IEnumerable<string> argumenten, byte[]? invoer, TimeSpan limiet, CancellationToken annulering)
    {
        var start = new ProcessStartInfo(programma)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in argumenten)
        {
            start.ArgumentList.Add(argument);
        }

        using var proces = Process.Start(start) ?? throw new InvalidOperationException($"{programma} start niet.");
        using var tijd = CancellationTokenSource.CreateLinkedTokenSource(annulering);
        tijd.CancelAfter(limiet);
        try
        {
            var uitvoer = proces.StandardOutput.ReadToEndAsync(tijd.Token);
            var fouten = proces.StandardError.ReadToEndAsync(tijd.Token);
            if (invoer is not null)
            {
                await proces.StandardInput.BaseStream.WriteAsync(invoer, tijd.Token).ConfigureAwait(false);
            }

            proces.StandardInput.Close();
            await proces.WaitForExitAsync(tijd.Token).ConfigureAwait(false);
            await fouten.ConfigureAwait(false);
            return (proces.ExitCode, await uitvoer.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!annulering.IsCancellationRequested)
        {
            proces.Kill(entireProcessTree: true);
            throw new TimeoutException($"De tekstherkenning duurde langer dan {limiet.TotalSeconds:0} seconden.");
        }
    }
}

/// <summary>Wat de OCR van een scan of foto opleverde: de woorden op hun plek, en bij een foto de PDF om te bewaren.</summary>
/// <param name="Pdf">Bij een foto (JPEG of PNG): een PDF met de foto als pagina, zodat de viewer en het aanwijzen werken.</param>
public sealed record Ocrlaag(IReadOnlyList<Tekstwoord> Woorden, byte[]? Pdf);

/// <summary>
/// Scans en foto's lezen met OCR. Een foto wordt een PDF van één pagina (A4-breed, de foto erin); van een gescande PDF
/// gaat per pagina de grootste afbeelding door de OCR. De woorden komen in PDF-punten op de plek van de afbeelding, zodat
/// regels, aanwijzen en oplichten werken als bij een PDF met tekst.
/// </summary>
public sealed class OcrLezer(IOcrMotor motor)
{
    /// <summary>Breedte van A4 in PDF-punten.</summary>
    private const double A4Breedte = 595;

    /// <summary>JPEG of PNG, op de inhoud bepaald (niet op de naam); anders leeg.</summary>
    public static string? Afbeeldingssoort(ReadOnlySpan<byte> inhoud) =>
        inhoud.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "jpeg"
        : inhoud.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']) ? "png"
        : null;

    public async Task<Ocrlaag?> LeesAsync(byte[] inhoud, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        if (Afbeeldingssoort(inhoud) is { } soort)
        {
            var ocr = await motor.LeesAsync(inhoud, annulering).ConfigureAwait(false);
            if (ocr.Breedte <= 0 || ocr.Hoogte <= 0)
            {
                return null;
            }

            var plek = new PdfPlek(1, 0, 0, A4Breedte, A4Breedte * ocr.Hoogte / ocr.Breedte);
            return new Ocrlaag([.. NaarPunten(1, ocr, plek)], NaarPdf(inhoud, soort, plek));
        }

        var woorden = new List<Tekstwoord>();
        foreach (var (pagina, afbeelding, plek) in Afbeeldingen(inhoud))
        {
            woorden.AddRange(NaarPunten(pagina, await motor.LeesAsync(afbeelding, annulering).ConfigureAwait(false), plek));
        }

        return woorden.Count == 0 ? null : new Ocrlaag(woorden, null);
    }

    /// <summary>De OCR-woorden van een afbeelding in PDF-punten, op de plek waar de afbeelding op de pagina staat.</summary>
    public static IEnumerable<Tekstwoord> NaarPunten(int pagina, OcrUitkomst ocr, PdfPlek plek)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(plek);
        if (ocr.Breedte <= 0 || ocr.Hoogte <= 0)
        {
            yield break;
        }

        var (sx, sy) = ((plek.X1 - plek.X0) / ocr.Breedte, (plek.Y1 - plek.Y0) / ocr.Hoogte);
        foreach (var w in ocr.Woorden.Where(w => w.Zekerheid > 0))
        {
            yield return new Tekstwoord(pagina, w.Tekst,
                plek.X0 + w.Links * sx, plek.Y1 - (w.Boven + w.Hoogte) * sy,
                plek.X0 + (w.Links + w.Breedte) * sx, plek.Y1 - w.Boven * sy);
        }
    }

    private static byte[] NaarPdf(byte[] afbeelding, string soort, PdfPlek plek)
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(plek.X1, plek.Y1);
        var vlak = new PdfRectangle(0, 0, plek.X1, plek.Y1);
        if (soort == "jpeg")
        {
            pagina.AddJpeg(afbeelding, vlak);
        }
        else
        {
            pagina.AddPng(afbeelding, vlak);
        }

        return builder.Build();
    }

    /// <summary>
    /// Per pagina van een PDF zonder tekst de grootste afbeelding (de scan) met zijn plek: JPEG zoals hij erin staat,
    /// andere soorten als PNG. Een afbeelding die niet te lezen is, slaat de OCR over.
    /// </summary>
    public static IReadOnlyList<(int Pagina, byte[] Afbeelding, PdfPlek Plek)> Afbeeldingen(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length > PdfTekst.MaximumBytes || pdf.AsSpan(0, Math.Min(pdf.Length, 1024)).IndexOf("%PDF-"u8) < 0)
        {
            return [];
        }

        var lijst = new List<(int, byte[], PdfPlek)>();
        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            foreach (var pagina in document.GetPages().Take(PdfTekst.MaximumPaginas))
            {
                if (pagina.GetImages().MaxBy(i => i.BoundingBox.Width * i.BoundingBox.Height) is not { } beeld)
                {
                    continue;
                }

                var b = beeld.BoundingBox;
                var plek = new PdfPlek(pagina.Number, b.Left, b.Bottom, b.Right, b.Top);
                var ruw = beeld.RawBytes.ToArray();
                if (Afbeeldingssoort(ruw) == "jpeg")
                {
                    lijst.Add((pagina.Number, ruw, plek));
                }
                else if (beeld.TryGetPng(out var png))
                {
                    lijst.Add((pagina.Number, png, plek));
                }
            }
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Beschadigd of onbekend: dan geen OCR.
            return [];
        }

        return lijst;
    }
}
