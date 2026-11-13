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
    [PaddleFact]
    public async Task De_neurale_tekstherkenning_leest_de_bon_ook_in_de_schaduw()
    {
        var motor = PaddleFactAttribute.Paddle;
        var (_, zacht) = Fotoverbetering.VerbeterMetZacht(Bon())!.Value;

        var tekst = string.Join(' ', (await motor.LeesAsync(zacht)).Woorden.Select(w => w.Tekst));

        Assert.Contains("TOTAAL", tekst, StringComparison.Ordinal);
        Assert.Contains("80,15", tekst, StringComparison.Ordinal);
        Assert.Contains("13,91", tekst, StringComparison.Ordinal);
    }

    [PaddleFact]
    public async Task Een_foto_door_de_lezer_bewaart_de_scan_met_de_woorden_van_de_zachte_versie()
    {
        var laag = await new OcrLezer(PaddleFactAttribute.Paddle).LeesAsync(Bon());

        Assert.NotNull(laag);
        Assert.Contains(laag.Woorden, w => w.Tekst == "TOTAAL");
        Assert.NotNull(laag.Pdf);
    }

    [Fact]
    public void De_zachte_versie_heeft_dezelfde_maat_als_de_scan()
    {
        var (scan, zacht) = Fotoverbetering.VerbeterMetZacht(Bon())!.Value;
        using var a = SkiaSharp.SKBitmap.Decode(scan);
        using var b = SkiaSharp.SKBitmap.Decode(zacht);

        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));

        // Zacht: het papier licht maar de grijstinten blijven (geen hard zwart-wit).
        Assert.True(Enumerable.Range(0, 200).Count(i => b.GetPixel(100 + i, 860).Red is > 60 and < 200) > Enumerable.Range(0, 200).Count(i => a.GetPixel(100 + i, 860).Red is > 60 and < 200));
    }

    /// <summary>Een wit vel met regels "tekst" (donkere balkjes) van de gegeven kolommen, om de rij zo veel verschoven.</summary>
    private static void Regels(byte[] vlak, int breedte, int van, int tot, int verschuiving, int elke = 1)
    {
        for (var (y, n) = (200 + verschuiving, 0); y < 1400; y += 34, n++)
        {
            if (n % elke != 0)
            {
                continue;
            }

            // Woorden: balkjes met een spatie ertussen, de regel niet steeds even lang.
            var eind = tot - ((n * 37) % Math.Max(1, (tot - van) / 3));
            for (var x = van; x < eind; x++)
            {
                if ((x - van) % 50 < 42)
                {
                    for (var r = 0; r < 12; r++)
                    {
                        vlak[((y + r) * breedte) + x] = 30;
                    }
                }
            }
        }
    }

    [Fact]
    public void Een_bon_in_een_ordner_wordt_uitgesneden_met_zijn_bedragen_en_zonder_het_vel_ernaast()
    {
        const int Breedte = 1200, Hoogte = 1600;
        var vlak = Enumerable.Repeat((byte)255, Breedte * Hoogte).ToArray();
        Regels(vlak, Breedte, 300, 640, 0);
        Regels(vlak, Breedte, 760, 830, 0, elke: 2);

        // De rand van de bon, een fractie scheef, en het vel ernaast met eigen regels.
        for (var y = 150; y < 1450; y++)
        {
            var x = 880 + ((y - 150) / 200);
            vlak[(y * Breedte) + x] = vlak[(y * Breedte) + x + 1] = 120;
        }

        Regels(vlak, Breedte, 960, 1180, 17);

        var (van, tot) = Fotoverbetering.Uitsnede(vlak, Breedte, Hoogte);

        Assert.InRange(van, 240, 300);
        Assert.InRange(tot, 830, 960);
    }

    [Fact]
    public void Een_volle_pagina_wordt_niet_uitgesneden()
    {
        const int Breedte = 1200, Hoogte = 1600;
        var vlak = Enumerable.Repeat((byte)255, Breedte * Hoogte).ToArray();
        Regels(vlak, Breedte, 100, 1100, 0);

        Assert.Equal((0, Breedte), Fotoverbetering.Uitsnede(vlak, Breedte, Hoogte));
    }

    [Fact]
    public void Een_ring_van_de_ordner_verdwijnt_en_de_tekst_blijft()
    {
        const int Breedte = 1200, Hoogte = 1600;
        var vlak = Enumerable.Repeat((byte)255, Breedte * Hoogte).ToArray();
        Regels(vlak, Breedte, 500, 1000, 0);

        // Een halve ring van 18 pixels dik links van de tekst.
        for (var y = 0; y < Hoogte; y++)
        {
            for (var x = 0; x < 450; x++)
            {
                var afstand = Math.Sqrt(Math.Pow(x - 220, 2) + Math.Pow((y - 700) * 1.6, 2));
                if (y < 700 && afstand is > 180 and < 198)
                {
                    vlak[(y * Breedte) + x] = 25;
                }
            }
        }

        int Tekst() => Enumerable.Range(0, Hoogte).Sum(y => Enumerable.Range(500, 500).Count(x => vlak[(y * Breedte) + x] < 100));
        var tekst = Tekst();
        Fotoverbetering.WisDik(vlak, Breedte, Hoogte);

        Assert.DoesNotContain(Enumerable.Range(0, Hoogte).SelectMany(y => Enumerable.Range(0, 450).Select(x => vlak[(y * Breedte) + x])), p => p < 200);
        Assert.Equal(tekst, Tekst());
    }
}
