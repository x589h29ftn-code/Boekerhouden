namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Een foto van een bonnetje wordt een scan (grijs, belichting vlak, contrast omhoog) vóór de tekstherkenning: met de
/// verzonnen foto van Bouwmarkt De Hamer, waarop een schaduw over het totaal valt.
/// </summary>
public sealed class FotoverbeteringTests
{
    internal static byte[] Bon() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "foto-bon-bouwmarkt.jpg"));

    [Fact]
    public void Een_foto_wordt_een_jpeg_in_grijstinten_met_wit_papier_en_zwarte_inkt()
    {
        var beter = Fotoverbetering.Verbeter(Bon())!;

        Assert.Equal("jpeg", OcrLezer.Afbeeldingssoort(beter));
        using var beeld = SkiaSharp.SKBitmap.Decode(beter);
        Assert.Equal((1000, 1750), (beeld.Width, beeld.Height));

        // Midden op het papier in de schaduw (onderaan) is het nu wit; een letter van TOTAAL is donker.
        var papier = beeld.GetPixel(500, 1480);
        Assert.True(papier.Red > 235 && papier.Red == papier.Green && papier.Green == papier.Blue, $"papier in de schaduw: {papier}");
        var donkerste = Enumerable.Range(150, 300).Min(x => Enumerable.Range(830, 60).Min(y => beeld.GetPixel(x, y).Red));
        Assert.True(donkerste < 40, $"inkt van het totaal: {donkerste}");
    }

    [Fact]
    public void Geen_afbeelding_blijft_zoals_het_is()
    {
        Assert.Null(Fotoverbetering.Verbeter("geen foto"u8.ToArray()));
        Assert.Null(Fotoverbetering.Verbeter([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]));
    }

    [OcrFact]
    public async Task Met_de_verbetering_leest_de_tekstherkenning_ook_het_totaal_in_de_schaduw()
    {
        var motor = OcrFactAttribute.Tesseract;
        static string Tekst(OcrUitkomst ocr) => string.Join(' ', ocr.Woorden.Where(w => w.Zekerheid > 0).Select(w => w.Tekst));

        var ruw = Tekst(await motor.LeesAsync(Bon()));
        var beter = Tekst(await motor.LeesAsync(Fotoverbetering.Verbeter(Bon())!));

        Assert.DoesNotContain("TOTAAL", ruw, StringComparison.Ordinal);
        Assert.Contains("TOTAAL", beter, StringComparison.Ordinal);
        Assert.Contains("80,15", beter, StringComparison.Ordinal);
        Assert.Contains("13,91", beter, StringComparison.Ordinal);
    }
}
