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
