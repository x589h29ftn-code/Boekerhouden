using SkiaSharp;

namespace Boekerhouden.Documenten;

/// <summary>
/// Maakt van een foto (van een bonnetje of factuur) een scan: rechtop gezet volgens de camera (EXIF), verkleind tot
/// een werkbare maat, in grijstinten, met de belichting vlakgetrokken (schaduw en vergeeld papier worden wit) en het
/// contrast verhoogd (inkt wordt zwart), en rechtgezet als de foto scheef is genomen (tot een graad of tien), zodat de
/// regels van de tekst weer horizontaal lopen. Dat leest beter, voor de tekstherkenning en voor de mens, en geeft een
/// kleinere PDF. Er wordt niet bijgesneden.
/// </summary>
public static class Fotoverbetering
{
    /// <summary>De lange zijde wordt hoogstens zo groot (ruim genoeg voor kleine lettertjes op een A4).</summary>
    public const int MaximumZijde = 2600;

    /// <summary>De grootte van een blok waarover de achtergrond (het papier) wordt geschat.</summary>
    private const int Blok = 48;

    /// <summary>Onder deze helderheid (na het vlaktrekken) is het inkt, erboven papier; ertussen loopt het geleidelijk.</summary>
    private const int Zwart = 90;
    private const int Wit = 205;

    /// <summary>De verbeterde foto als JPEG in grijstinten; leeg als het bestand geen leesbare afbeelding is.</summary>
    public static byte[]? Verbeter(byte[] foto)
    {
        ArgumentNullException.ThrowIfNull(foto);
        try
        {
            using var rechtop = Rechtop(foto);
            if (rechtop is null)
            {
                return null;
            }

            var (breedte, hoogte) = (rechtop.Width, rechtop.Height);
            var grijs = Grijs(rechtop);
            var papier = Papier(grijs, breedte, hoogte);
            var uit = new byte[grijs.Length];
            for (var y = 0; y < hoogte; y++)
            {
                for (var x = 0; x < breedte; x++)
                {
                    var i = (y * breedte) + x;

                    // Vlaktrekken: de helderheid ten opzichte van het papier op die plek.
                    var vlak = Math.Min(255, grijs[i] * 255 / Math.Max(Achtergrond(papier, x, y), 40));
                    uit[i] = (byte)Math.Clamp((vlak - Zwart) * 255 / (Wit - Zwart), 0, 255);
                }
            }

            WisVlakken(uit, breedte, hoogte);
            using var resultaat = new SKBitmap(new SKImageInfo(breedte, hoogte, SKColorType.Gray8, SKAlphaType.Opaque));
            System.Runtime.InteropServices.Marshal.Copy(uit, 0, resultaat.GetPixels(), uit.Length);
            var hoek = Scheefstand(uit, breedte, hoogte);
            using var recht = Math.Abs(hoek) < 0.3 ? null : Gedraaid(resultaat, -hoek);
            using var beeld = SKImage.FromBitmap(recht ?? resultaat);
            using var jpeg = beeld.Encode(SKEncodedImageFormat.Jpeg, 82);
            return jpeg?.ToArray();
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Een beschadigde of onbekende afbeelding: de foto blijft zoals hij is.
            return null;
        }
    }

    /// <summary>
    /// Maakt grote donkere vlakken wit: de tafel naast het papier, een vinger, een harde schaduw. Een vlak is waar een
    /// heel vierkant van een flinke maat donker is; letters en lijnen zijn dunner en blijven dus staan, ook als ze het
    /// vlak raken. De rand van het vlak (de overgang naar het papier) gaat mee.
    /// </summary>
    internal static void WisVlakken(byte[] grijs, int breedte, int hoogte)
    {
        var maat = Math.Max(20, Math.Max(breedte, hoogte) / 70);
        var rand = maat / 2;

        // Per pixel het grootste donkere vierkant dat daar rechtsonder eindigt; vanaf de maat is het de kern van een vlak.
        var vorige = new int[breedte + 1];
        var deze = new int[breedte + 1];
        var breed = new bool[grijs.Length];
        var gevonden = false;
        for (var y = 0; y < hoogte; y++)
        {
            for (var x = 0; x < breedte; x++)
            {
                deze[x + 1] = grijs[(y * breedte) + x] < 128 ? 1 + Math.Min(deze[x], Math.Min(vorige[x], vorige[x + 1])) : 0;
                if (deze[x + 1] >= maat)
                {
                    // Het hele vierkant (naar links) plus de rand, eerst alleen in deze rij.
                    var van = Math.Max(0, x - maat + 1 - rand);
                    Array.Fill(breed, true, (y * breedte) + van, Math.Min(breedte - 1, x + rand) - van + 1);
                    gevonden = true;
                }
            }

            (vorige, deze) = (deze, vorige);
        }

        if (!gevonden)
        {
            return;
        }

        // Dan per kolom: het vierkant naar boven plus de rand, en de rand naar onder.
        for (var x = 0; x < breedte; x++)
        {
            var onder = 0;
            for (var y = 0; y < hoogte; y++)
            {
                if (breed[(y * breedte) + x])
                {
                    for (var v = Math.Max(0, y - maat + 1 - rand); v < y; v++)
                    {
                        grijs[(v * breedte) + x] = 255;
                    }

                    onder = rand + 1;
                }

                if (onder > 0)
                {
                    grijs[(y * breedte) + x] = 255;
                    onder--;
                }
            }
        }
    }

    /// <summary>
    /// De hoek (in graden, met de klok mee positief) waaronder de tekstregels lopen: de hoek waarbij de inkt per rij het
    /// scherpst in regels en tussenruimte uiteenvalt (de grootste spreiding van de rijsommen), gezocht op een verkleind
    /// beeld tussen -10 en +10 graden.
    /// </summary>
    internal static double Scheefstand(byte[] grijs, int breedte, int hoogte)
    {
        var stap = Math.Max(1, Math.Max(breedte, hoogte) / 700);
        var punten = new List<(int X, int Y)>();
        for (var y = 0; y < hoogte; y += stap)
        {
            for (var x = 0; x < breedte; x += stap)
            {
                // Alleen bovenranden van inkt (donker met licht er vlak boven): grote donkere vlakken (de tafel naast
                // het papier) tellen dan alleen met hun rand mee.
                if (grijs[(y * breedte) + x] < 128 && y >= 2 * stap && grijs[((y - (2 * stap)) * breedte) + x] >= 128)
                {
                    punten.Add((x / stap, y / stap));
                }
            }
        }

        var (b, h) = (breedte / stap, hoogte / stap);
        if (punten.Count < 200 || punten.Count > b * h / 2)
        {
            return 0;
        }

        double Score(double graden)
        {
            var (sin, cos) = Math.SinCos(graden * Math.PI / 180);
            var rijen = new int[h + (2 * b) + 4];
            foreach (var (x, y) in punten)
            {
                rijen[(int)((y * cos) - (x * sin)) + b]++;
            }

            // Som van kwadraten: het hoogst als de inkt in weinig rijen samenvalt.
            return rijen.Sum(r => (double)r * r);
        }

        var (beste, hoogste) = (0.0, Score(0));
        for (var graden = -10.0; graden <= 10.0; graden += 0.5)
        {
            var score = Score(graden);
            if (score > hoogste)
            {
                (beste, hoogste) = (graden, score);
            }
        }

        // Alleen rechtzetten als het duidelijk beter is dan niets doen.
        return hoogste > Score(0) * 1.1 ? beste : 0;
    }

    /// <summary>Het beeld gedraaid om zijn midden, op een wit vlak van dezelfde maat.</summary>
    private static SKBitmap Gedraaid(SKBitmap bron, double graden)
    {
        var doel = new SKBitmap(new SKImageInfo(bron.Width, bron.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(doel);
        canvas.Clear(SKColors.White);
        canvas.RotateDegrees((float)graden, bron.Width / 2f, bron.Height / 2f);
        using var beeld = SKImage.FromBitmap(bron);
        canvas.DrawImage(beeld, new SKRect(0, 0, bron.Width, bron.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return doel;
    }

    /// <summary>De foto rechtop (zoals de camera hem bedoelde) en hoogstens <see cref="MaximumZijde"/> groot.</summary>
    private static SKBitmap? Rechtop(byte[] foto)
    {
        using var gegevens = SKData.CreateCopy(foto);
        using var codec = SKCodec.Create(gegevens);
        if (codec is null)
        {
            return null;
        }

        using var bron = SKBitmap.Decode(codec);
        if (bron is null || bron.Width < 8 || bron.Height < 8)
        {
            return null;
        }

        var oorsprong = codec.EncodedOrigin;
        var kwartslag = oorsprong is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var schaal = Math.Min(1f, MaximumZijde / (float)Math.Max(bron.Width, bron.Height));
        var (b, h) = ((int)Math.Round(bron.Width * schaal), (int)Math.Round(bron.Height * schaal));
        var doel = new SKBitmap(new SKImageInfo(kwartslag ? h : b, kwartslag ? b : h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(doel);
        canvas.Clear(SKColors.White);
        switch (oorsprong)
        {
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, b / 2f, h / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, b);
                canvas.RotateDegrees(270);
                break;
        }

        using var beeld = SKImage.FromBitmap(bron);
        canvas.DrawImage(beeld, new SKRect(0, 0, b, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return doel;
    }

    private static byte[] Grijs(SKBitmap beeld)
    {
        var pixels = beeld.GetPixelSpan();
        var grijs = new byte[beeld.Width * beeld.Height];
        for (var i = 0; i < grijs.Length; i++)
        {
            var p = i * 4;
            grijs[i] = (byte)(((pixels[p] * 299) + (pixels[p + 1] * 587) + (pixels[p + 2] * 114)) / 1000);
        }

        return grijs;
    }

    /// <summary>
    /// Per blok de helderheid van het papier: een hoge percentielwaarde (tekst beslaat maar een klein deel van een blok),
    /// zodat schaduw en verloop per plek worden gevolgd.
    /// </summary>
    private static int[,] Papier(byte[] grijs, int breedte, int hoogte)
    {
        var (kolommen, rijen) = (((breedte - 1) / Blok) + 1, ((hoogte - 1) / Blok) + 1);
        var papier = new int[rijen, kolommen];
        var telling = new int[256];
        for (var r = 0; r < rijen; r++)
        {
            for (var k = 0; k < kolommen; k++)
            {
                Array.Clear(telling);
                var aantal = 0;
                for (var y = r * Blok; y < Math.Min(hoogte, (r + 1) * Blok); y++)
                {
                    for (var x = k * Blok; x < Math.Min(breedte, (k + 1) * Blok); x++)
                    {
                        telling[grijs[(y * breedte) + x]]++;
                        aantal++;
                    }
                }

                // Het 80e percentiel van boven af tellen.
                var over = aantal / 5;
                var waarde = 255;
                while (waarde > 0 && (over -= telling[waarde]) >= 0)
                {
                    waarde--;
                }

                papier[r, k] = waarde;
            }
        }

        return papier;
    }

    /// <summary>De achtergrond op een punt: tussen de middens van de blokken geïnterpoleerd, zodat er geen randen ontstaan.</summary>
    private static int Achtergrond(int[,] papier, int x, int y)
    {
        var (rijen, kolommen) = (papier.GetLength(0), papier.GetLength(1));
        var fx = Math.Clamp(((x + 0.5) / Blok) - 0.5, 0, kolommen - 1);
        var fy = Math.Clamp(((y + 0.5) / Blok) - 0.5, 0, rijen - 1);
        var (k0, r0) = ((int)fx, (int)fy);
        var (k1, r1) = (Math.Min(k0 + 1, kolommen - 1), Math.Min(r0 + 1, rijen - 1));
        var (dx, dy) = (fx - k0, fy - r0);
        var boven = (papier[r0, k0] * (1 - dx)) + (papier[r0, k1] * dx);
        var onder = (papier[r1, k0] * (1 - dx)) + (papier[r1, k1] * dx);
        return (int)((boven * (1 - dy)) + (onder * dy));
    }
}
