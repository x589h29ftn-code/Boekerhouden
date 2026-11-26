using SkiaSharp;

namespace Boekerhouden.Documenten;

/// <summary>
/// Hoe groot een afbeelding uitgepakt mag zijn: 64 megapixel (ruim een foto van een telefoon met 48 megapixel). Een
/// groter beeld wordt niet gelezen, zodat een klein bestand met een enorm beeld het geheugen niet kan opmaken.
/// </summary>
public static class Afbeeldingsgrens
{
    public const long MaximumPixels = 64_000_000;

    public static bool Past(SKImageInfo info) => info.Width > 0 && info.Height > 0 && (long)info.Width * info.Height <= MaximumPixels;
}
