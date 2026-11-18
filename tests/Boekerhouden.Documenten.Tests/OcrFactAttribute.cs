using Boekerhouden.Documenten;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Test die alleen draait als Tesseract (de OCR) te starten is, met Nederlands of Engels. In CI wordt Tesseract
/// geïnstalleerd; lokaal zonder Tesseract worden deze tests overgeslagen.
/// </summary>
internal sealed class OcrFactAttribute : FactAttribute
{
    private static readonly Lazy<TesseractOcr?> Motor = new(() => TesseractOcr.Zoek());

    public OcrFactAttribute()
    {
        if (Motor.Value is null)
        {
            Skip = "Installeer Tesseract (met Nederlands) om de OCR-tests te draaien.";
        }
    }

    public static TesseractOcr Tesseract => Motor.Value ?? throw new InvalidOperationException("Tesseract is niet geïnstalleerd.");
}

/// <summary>Test die alleen draait als de modellen van PaddleOCR naast de tests staan (ze komen mee uit het pakket).</summary>
internal sealed class PaddleFactAttribute : FactAttribute
{
    private static readonly Lazy<PaddleOcr?> Motor = new(() => PaddleOcr.Zoek());

    public PaddleFactAttribute()
    {
        if (Motor.Value is null)
        {
            Skip = "De modellen van PaddleOCR (models/v5) ontbreken of ONNX Runtime start niet op dit platform.";
        }
    }

    public static PaddleOcr Paddle => Motor.Value ?? throw new InvalidOperationException("PaddleOCR is er niet.");
}
