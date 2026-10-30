using Boekerhouden.Boekhouding.Herkenning;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Tekstherkenning (OCR) van scans en foto's, met de verzonnen scan van Kantoorvakhandel Pen.</summary>
public sealed class OcrTests
{
    internal static byte[] Scan() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "scan-kantoorvakhandel.png"));

    /// <summary>Een "gescande" PDF: alleen de afbeelding, geen tekstlaag.</summary>
    internal static byte[] GescandePdf()
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        pagina.AddPng(Scan(), new PdfRectangle(0, 0, 595, 842));
        return builder.Build();
    }

    [Fact]
    public void Uitvoer_van_tesseract_lezen()
    {
        const string tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
            + "1\t1\t0\t0\t0\t0\t0\t0\t1240\t1754\t-1\t\n"
            + "5\t1\t1\t1\t1\t1\t90\t460\t220\t30\t96.5\tFactuurnummer:\n"
            + "5\t1\t1\t1\t1\t2\t330\t460\t200\t30\t91\tKV-2026-0815\n";

        var uitkomst = TesseractOcr.LeesTsv(tsv);

        Assert.Equal((1240, 1754), (uitkomst.Breedte, uitkomst.Hoogte));
        Assert.Equal(new OcrWoord("KV-2026-0815", 330, 460, 200, 30, 91), uitkomst.Woorden[1]);
    }

    [Fact]
    public void Pixels_worden_pdf_punten_op_de_plek_van_de_afbeelding()
    {
        var ocr = new OcrUitkomst(1000, 2000, [new OcrWoord("Totaal", 100, 200, 300, 50, 90), new OcrWoord("ruis", 0, 0, 1, 1, -1)]);

        var woord = Assert.Single(OcrLezer.NaarPunten(2, ocr, new PdfPlek(2, 50, 100, 550, 1100)));

        // x: 50 + 100 × 0,5; y (van onder): 1100 − (200 + 50) × 0,5.
        Assert.Equal(new Tekstwoord(2, "Totaal", 100, 975, 250, 1000), woord);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, "png")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, null)]
    public void Afbeelding_op_inhoud_herkennen(byte[] inhoud, string? soort) => Assert.Equal(soort, OcrLezer.Afbeeldingssoort(inhoud));

    [OcrFact]
    public async Task Foto_van_een_factuur_wordt_herkend_en_een_pdf()
    {
        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen, ocr: new OcrLezer(OcrFactAttribute.Tesseract))
            .HerkenAsync(new MemoryStream(Scan()), "bon.png", "image/png");

        var f = resultaat.Factuur!;
        Assert.Equal(Herkenningsbron.Ocr, f.Bron);
        Assert.Equal(("Kantoorvakhandel Pen B.V.", "KV-2026-0815", new DateOnly(2026, 5, 12)), (f.Naam, f.Factuurnummer, f.Factuurdatum));
        Assert.Equal((400m, 84m, 484m), (f.TotaalExclusief, f.TotaalBtw, f.TotaalInclusief));
        Assert.Equal(("NL20INGB0001234567", "NL800000006B01"), (f.Iban, f.BtwNummer));
        Assert.NotNull(resultaat.Ocr!.Pdf);
        Assert.Contains("Bureaustoel", string.Join(" ", PdfTekst.InGebied(resultaat.Ocr.Woorden, 1, 40, 0, 400, 842)), StringComparison.Ordinal);
    }

    [OcrFact]
    public async Task Gescande_pdf_zonder_tekst_gaat_door_de_ocr()
    {
        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen, ocr: new OcrLezer(OcrFactAttribute.Tesseract))
            .HerkenAsync(new MemoryStream(GescandePdf()), "scan.pdf", "application/pdf");

        Assert.Equal(("KV-2026-0815", 484m), (resultaat.Factuur!.Factuurnummer, resultaat.Factuur.TotaalInclusief));
        Assert.Null(resultaat.Ocr!.Pdf);
        var totaal = Assert.Single(PdfTekst.Zoek(resultaat.Ocr.Woorden, new Dictionary<string, IReadOnlyList<string>> { ["totaal"] = Tekstvormen.Bedrag(484m) })["totaal"]);
        Assert.InRange(totaal.X0, 400, 595);
    }

    [Fact]
    public async Task Zonder_ocr_blijft_een_foto_liggen_met_een_melding()
    {
        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen).HerkenAsync(new MemoryStream(Scan()), "bon.png", "image/png");

        Assert.Null(resultaat.Factuur);
        Assert.Contains("tekstherkenning (Tesseract)", resultaat.Melding, StringComparison.Ordinal);
    }
}
