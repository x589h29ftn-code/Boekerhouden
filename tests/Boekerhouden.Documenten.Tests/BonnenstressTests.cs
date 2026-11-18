using System.Globalization;
using System.Text;
using System.Text.Json;
using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Stresstest van de bonherkenning: de 25 verzonnen telefoonfoto's uit <c>scripts/bonnen/maak-bonnen.py</c> (bouwmarkt,
/// supermarkt, bakkerij, tankstation, horeca; scheef, wazig, schaduw, gemengde tarieven) gaan door de hele keten
/// (foto → scan → tekstherkenning → analyse) en worden per veld vergeleken met wat er op de bon staat. Draait alleen als
/// <c>BOEKERHOUDEN_BONNENMAP</c> naar de map met de bonnen wijst (ze staan niet in git) en Tesseract er is; het verslag
/// komt als <c>verslag.txt</c> in die map.
/// </summary>
public sealed class BonnenstressTests
{
    private static string? Map => Environment.GetEnvironmentVariable("BOEKERHOUDEN_BONNENMAP") is { Length: > 0 } map && File.Exists(Path.Combine(map, "verwacht.json")) ? map : null;

    [OcrFact]
    public async Task Vijfentwintig_bonnen_door_de_hele_keten()
    {
        if (Map is not { } map)
        {
            return;
        }

        var nl = CultureInfo.GetCultureInfo("nl-NL");
        // De motor die de app ook kiest (PaddleOCR als de modellen er zijn); BOEKERHOUDEN_OCR=tesseract voor Tesseract.
        IOcrMotor motor = Environment.GetEnvironmentVariable("BOEKERHOUDEN_OCR") == "tesseract" ? OcrFactAttribute.Tesseract : PaddleOcr.Zoek() ?? (IOcrMotor)OcrFactAttribute.Tesseract;
        var herkenning = Factuurherkenning.Standaard(EigenGegevens.Geen, ocr: new OcrLezer(motor));
        var verslag = new StringBuilder();
        var goed = new Dictionary<string, int> { ["naam"] = 0, ["datum"] = 0, ["totaal"] = 0, ["btw"] = 0, ["nummer"] = 0 };
        var aantal = 0;
        var helemaal = 0;
        using var verwacht = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(map, "verwacht.json")));
        foreach (var v in verwacht.RootElement.EnumerateArray())
        {
            aantal++;
            var bestand = v.GetProperty("bestand").GetString()!;
            var foto = await File.ReadAllBytesAsync(Path.Combine(map, bestand));
            var resultaat = await herkenning.HerkenAsync(new MemoryStream(foto), bestand, "image/jpeg");
            var f = resultaat.Factuur ?? new HerkendeFactuur(Herkenningsbron.Ocr);
            var naam = v.GetProperty("naam").GetString()!;
            var datum = DateOnly.ParseExact(v.GetProperty("datum").GetString()!, "dd-MM-yyyy", CultureInfo.InvariantCulture);
            var totaal = v.GetProperty("totaal").GetDecimal();
            var nummer = v.GetProperty("nummer").ValueKind == JsonValueKind.Null ? null : v.GetProperty("nummer").GetString();
            var btw = v.GetProperty("btw").EnumerateArray()
                .Select(b => (Tarief: b.GetProperty("tarief").GetDecimal(), Btw: b.GetProperty("btw").GetDecimal()))
                .OrderBy(b => b.Tarief).ToList();

            static string Kaal(string? tekst) => new((tekst ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            var fouten = new List<string>();
            void Toets(string veld, bool klopt, string gevonden, string hoortTeZijn)
            {
                if (klopt)
                {
                    goed[veld]++;
                }
                else
                {
                    fouten.Add($"{veld}: '{gevonden}' moet '{hoortTeZijn}' zijn");
                }
            }

            Toets("naam", Kaal(f.Naam) == Kaal(naam), f.Naam ?? "", naam);
            Toets("datum", f.Factuurdatum == datum, f.Factuurdatum?.ToString("dd-MM-yyyy", nl) ?? "", datum.ToString("dd-MM-yyyy", nl));
            Toets("totaal", f.TotaalInclusief == totaal, f.TotaalInclusief?.ToString("N2", nl) ?? "", totaal.ToString("N2", nl));
            var gevondenBtw = f.Btwregels.Select(b => (Tarief: b.Percentage, b.Btw)).OrderBy(b => b.Tarief).ToList();
            Toets("btw", gevondenBtw.SequenceEqual(btw),
                string.Join(" + ", gevondenBtw.Select(b => $"{b.Tarief:0.#}%: {b.Btw.ToString("N2", nl)}")), string.Join(" + ", btw.Select(b => $"{b.Tarief:0.#}%: {b.Btw.ToString("N2", nl)}")));
            if (nummer is not null)
            {
                Toets("nummer", f.Factuurnummer == nummer, f.Factuurnummer ?? "", nummer);
            }
            else
            {
                goed["nummer"]++;
            }

            helemaal += fouten.Count == 0 ? 1 : 0;
            verslag.AppendLine(nl, $"{bestand}  {v.GetProperty("soort").GetString(),-12} {naam,-26} [{v.GetProperty("gebrek").GetString()}]  {(fouten.Count == 0 ? "goed" : "FOUT")}");
            foreach (var fout in fouten)
            {
                verslag.AppendLine(nl, $"      {fout}");
            }

            if (fouten.Count > 0 && resultaat.Ocr is { } ocr)
            {
                await File.WriteAllTextAsync(Path.Combine(map, Path.ChangeExtension(bestand, ".txt")), string.Join('\n', PdfTekst.Regels(ocr.Woorden)));
            }
        }

        verslag.AppendLine();
        verslag.AppendLine(nl, $"Helemaal goed: {helemaal} van {aantal}");
        foreach (var (veld, n) in goed)
        {
            verslag.AppendLine(nl, $"  {veld,-7} {n} van {aantal}");
        }

        await File.WriteAllTextAsync(Path.Combine(map, "verslag.txt"), verslag.ToString());

        // De ondergrens waar we nu op staan; omhoog zetten als de herkenning beter wordt.
        // De stand van 13 november 2026: 25 van 25 helemaal goed; van de 10 ordnerbonnen (maak-ordnerbonnen.py) 7, de
        // andere missen de datum (onder een ring) of het bonnummer. Speling, omdat de tekstherkenning per versie iets
        // anders leest.
        var ordner = verwacht.RootElement.EnumerateArray().Any(v => v.GetProperty("bestand").GetString()!.StartsWith("ordner", StringComparison.Ordinal));
        var (speling, spelingBedragen) = ordner ? (4, 2) : (2, 2);
        Assert.True(helemaal >= aantal - speling, $"helemaal goed: {helemaal} van {aantal}; zie verslag.txt");
        Assert.True(goed["totaal"] >= aantal - spelingBedragen, $"totaal goed bij {goed["totaal"]} van {aantal}; zie verslag.txt");
        Assert.True(goed["btw"] >= aantal - spelingBedragen, $"btw goed bij {goed["btw"]} van {aantal}; zie verslag.txt");
    }
}
