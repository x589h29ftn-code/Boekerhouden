using RapidOcrNet;
using SkiaSharp;

namespace Boekerhouden.Documenten;

/// <summary>
/// Tekstherkenning met de modellen van PaddleOCR (PP-OCRv5, Latijns schrift; Apache-2.0) via RapidOcrNet (Apache-2.0)
/// en ONNX Runtime (MIT), in het eigen proces. Een neuraal netwerk dat eerst de regels zoekt en ze dan als geheel leest:
/// veel beter dan Tesseract op foto's van kassabonnen, letters van een matrixprinter (losse stipjes), kreukels en een
/// beetje scheve regels. De modellen staan naast de app in <c>models/v5</c>; zonder modellen is er geen motor.
/// </summary>
public sealed class PaddleOcr : IOcrMotor, IDisposable
{
    private static readonly string[] Bestanden =
        ["ch_PP-OCRv5_mobile_det.onnx", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx", "latin_PP-OCRv5_rec_mobile_infer.onnx", "ppocrv5_latin_dict.txt"];

    /// <summary>Woorden apart (met hun plek), niet alleen de regels.</summary>
    private static readonly RapidOcrOptions Opties = RapidOcrOptions.Default with { ReturnWordBox = true };

    /// <summary>Eén herkenning tegelijk per motor: het rekenen gebeurt al met meerdere kernen tegelijk.</summary>
    private readonly SemaphoreSlim tegelijk = new(1);

    private const float MinsteZekerheid = 0.6f;

    private readonly RapidOcr ocr;

    private PaddleOcr(RapidOcr ocr) => this.ocr = ocr;

    public bool LeestZacht => true;

    /// <summary>De motor als de modellen er zijn (standaard in <c>models/v5</c> naast de app), anders leeg.</summary>
    public static PaddleOcr? Zoek(string? map = null)
    {
        map ??= Path.Combine(AppContext.BaseDirectory, "models", "v5");
        var paden = Bestanden.Select(b => Path.Combine(map, b)).ToArray();
        if (!paden.All(File.Exists))
        {
            return null;
        }

        var ocr = new RapidOcr();
        try
        {
            ocr.InitModels(paden[0], paden[1], paden[2], paden[3], Math.Clamp(Environment.ProcessorCount, 1, 4));
            return new PaddleOcr(ocr);
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Een kapot model of een ontbrekende ONNX Runtime voor dit platform: dan geen motor.
            ocr.Dispose();
            return null;
        }
    }

    public async Task<OcrUitkomst> LeesAsync(byte[] afbeelding, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(afbeelding);
        using var beeld = Decodeer(afbeelding) ?? throw new DocumentException("De tekstherkenning (OCR) kon deze afbeelding niet lezen.");
        await tegelijk.WaitAsync(annulering).ConfigureAwait(false);
        OcrResult resultaat;
        try
        {
            resultaat = await ocr.DetectAsync(beeld, Opties, annulering).ConfigureAwait(false);
        }
        finally
        {
            tegelijk.Release();
        }

        // Onder de 60% zekerheid is het meestal geen tekst (een streepjescode, een patroon op de achtergrond).
        var woorden = new List<OcrWoord>();
        foreach (var regel in resultaat.TextBlocks ?? [])
        {
            if (regel.WordResults is { Length: > 0 } losse)
            {
                woorden.AddRange(losse.Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Score >= MinsteZekerheid).Select(w => Woord(w.Text, w.BoxPoints, w.Score)));
            }
            else if (!string.IsNullOrWhiteSpace(regel.Text) && (regel.CharScores is not { Length: > 0 } c || c.Average() >= MinsteZekerheid))
            {
                woorden.Add(Woord(regel.Text, regel.BoxPoints, regel.CharScores is { Length: > 0 } s ? s.Average() : 0f));
            }
        }

        return new OcrUitkomst(beeld.Width, beeld.Height, woorden);
    }

    /// <summary>De afbeelding, of leeg als het geen (hele) afbeelding is; SkiaSharp gooit dan soms een uitzondering.</summary>
    private static SKBitmap? Decodeer(byte[] afbeelding)
    {
        try
        {
            return SKBitmap.Decode(afbeelding);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Een woord met de rechthoek om zijn vier hoeken; de zekerheid van 0 tot 100, zoals bij Tesseract.</summary>
    private static OcrWoord Woord(string tekst, SKPointI[] hoeken, float score)
    {
        var (links, boven) = (hoeken.Min(p => p.X), hoeken.Min(p => p.Y));
        return new OcrWoord(tekst.Trim(), links, boven, Math.Max(1, hoeken.Max(p => p.X) - links), Math.Max(1, hoeken.Max(p => p.Y) - boven), Math.Round(score * 100d, 1));
    }

    public void Dispose()
    {
        ocr.Dispose();
        tegelijk.Dispose();
    }
}
