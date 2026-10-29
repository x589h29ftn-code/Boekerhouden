using Boekerhouden.Boekhouding.Herkenning;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

public sealed class PdfHerkenningTests
{
    /// <summary>Een verzonnen webwinkelfactuur met kolommen, zoals een echte PDF die opmaakt.</summary>
    internal static byte[] Webwinkelfactuur()
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 800.0;
        void Regel(params (double X, string Tekst)[] delen)
        {
            foreach (var (x, tekst) in delen)
            {
                pagina.AddText(tekst, 10, new PdfPoint(x, y), font);
            }

            y -= 14;
        }

        Regel((40, "Telewinkel"));
        Regel((40, "Voorbeeldhandel B.V."));
        Regel((40, "Postbus 123"), (350, "Pieter Particulier"));
        Regel((40, "IBAN NL20INGB0001234567"), (350, "Laan 1"));
        Regel((40, "BTW NL800000006B01"));
        Regel((40, "KvK 87654321"));
        Regel((40, "Factuur"));
        Regel((40, "Factuurnummer:"), (160, "TW-2026-0042"));
        Regel((40, "Factuurdatum:"), (160, "14-02-2026"));
        Regel((40, "Omschrijving"), (300, "Prijs per stuk"), (400, "Aantal"), (470, "Prijs totaal"));
        Regel((300, "incl. BTW"), (470, "incl. BTW"));
        Regel((40, "Smartphone X 128GB Blauw"), (300, "EUR 605,00"), (400, "1"), (470, "EUR 605,00"));
        Regel((40, "Hoesje Smartphone X"), (300, "EUR 24,20"), (400, "1"), (470, "EUR 24,20"));
        Regel((300, "Totaal inclusief BTW:"), (470, "EUR 629,20"));
        Regel((300, "BTW (21%):"), (470, "EUR 109,20"));
        return builder.Build();
    }

    [Fact]
    public void Tekst_van_een_pdf_staat_per_regel_met_kolommen_op_een_regel()
    {
        var pdf = PdfTekst.Lees(new MemoryStream(Webwinkelfactuur()));

        Assert.Equal(1, pdf.Paginas);
        Assert.False(pdf.IsScan);
        Assert.Contains(pdf.Regels, r => r.StartsWith("Smartphone X 128GB Blauw", StringComparison.Ordinal) && r.EndsWith("605,00", StringComparison.Ordinal));
        Assert.Contains(pdf.Regels, r => r.StartsWith("Totaal inclusief BTW:", StringComparison.Ordinal) && r.EndsWith("629,20", StringComparison.Ordinal));
    }

    [Fact]
    public void Tekst_uit_een_aangewezen_gebied()
    {
        var pdf = Webwinkelfactuur();

        Assert.Equal(["Smartphone X 128GB Blauw"], PdfTekst.InGebied(pdf, 1, 35, 640, 290, 656));
        Assert.Equal(["Smartphone X 128GB Blauw", "Hoesje Smartphone X"], PdfTekst.InGebied(pdf, 1, 290, 628, 35, 656));
        Assert.Equal(["EUR 629,20"], PdfTekst.InGebied(pdf, 1, 465, 615, 560, 625));
        Assert.Equal(["TW-2026-0042"], PdfTekst.InGebied(pdf, 1, 170, 704, 171, 705));
        Assert.Empty(PdfTekst.InGebied(pdf, 1, 0, 0, 30, 30));
        Assert.Throws<DocumentException>(() => PdfTekst.InGebied(pdf, 2, 0, 0, 10, 10));
        Assert.Throws<DocumentException>(() => PdfTekst.InGebied("geen pdf"u8.ToArray(), 1, 0, 0, 10, 10));
    }

    [Theory]
    [InlineData("%PDF-1.7 ...", ".pdf")]
    [InlineData("\uFEFF<?xml version=\"1.0\"?><Invoice/>", ".xml")]
    [InlineData("  <Invoice xmlns=\"urn:x\"/>", ".xml")]
    [InlineData("MZ\u0090\u0000 programma", null)]
    [InlineData("<html><script>alert(1)</script></html>", null)]
    [InlineData("<!DOCTYPE html><p>x</p>", null)]
    public void Alleen_pdf_en_xml_mogen_worden_geopend(string inhoud, string? extensie) =>
        Assert.Equal(extensie, Bestandssoort.Extensie(System.Text.Encoding.UTF8.GetBytes(inhoud)));

    [Fact]
    public async Task Pdf_factuur_wordt_door_het_pakket_herkend()
    {
        var herkenning = Factuurherkenning.Standaard(new EigenGegevens([], [], ["Bakkerij Voorbeeld"]));

        var resultaat = await herkenning.HerkenAsync(new MemoryStream(Webwinkelfactuur()), "factuur.pdf");

        var factuur = Assert.IsType<HerkendeFactuur>(resultaat.Factuur);
        Assert.Equal(Herkenningsbron.Tekst, factuur.Bron);
        Assert.Equal("Voorbeeldhandel B.V.", factuur.Naam);
        Assert.Equal("NL800000006B01", factuur.BtwNummer);
        Assert.Equal("87654321", factuur.KvkNummer);
        Assert.Equal("NL20INGB0001234567", factuur.Iban);
        Assert.Equal("TW-2026-0042", factuur.Factuurnummer);
        Assert.Equal(new DateOnly(2026, 2, 14), factuur.Factuurdatum);
        Assert.Equal(629.20m, factuur.TotaalInclusief);
        Assert.Equal(520.00m, factuur.TotaalExclusief);
        Assert.Equal(new Btwregel(21m, null, 109.20m), Assert.Single(factuur.Btwregels));
        Assert.Equal("Smartphone X 128GB Blauw e.a.", factuur.Omschrijving);
        Assert.False(factuur.OpEigenNaam);
        Assert.Empty(factuur.Controleer());
    }

    [Fact]
    public async Task E_factuur_als_xml_bestand()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "belnet-2026-0411.xml"));

        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen).HerkenAsync(stream, "belnet.xml");

        Assert.Equal(Herkenningsbron.EFactuur, resultaat.Factuur?.Bron);
    }

    [Fact]
    public async Task Geen_pdf_een_onbekend_type_en_een_scan_geven_een_melding()
    {
        var herkenning = Factuurherkenning.Standaard(EigenGegevens.Geen);

        var nep = await herkenning.HerkenAsync(new MemoryStream("geen pdf"u8.ToArray()), "factuur.pdf");
        var foto = await herkenning.HerkenAsync(new MemoryStream([1, 2, 3]), "bon.jpg");
        var leeg = new PdfDocumentBuilder();
        leeg.AddPage(PageSize.A4);
        var scan = await herkenning.HerkenAsync(new MemoryStream(leeg.Build()), "scan.pdf");

        Assert.Equal("Het bestand is geen PDF.", nep.Melding);
        Assert.StartsWith("Dit soort bestand wordt (nog) niet herkend", foto.Melding, StringComparison.Ordinal);
        Assert.StartsWith("Geen tekst gevonden", scan.Melding, StringComparison.Ordinal);
        Assert.Null(scan.Factuur);
    }

    [Fact]
    public void Te_groot_bestand_wordt_geweigerd_zonder_alles_in_te_lezen()
    {
        var fout = Assert.Throws<DocumentException>(() => PdfTekst.Lees(new NulStream(PdfTekst.MaximumBytes + 1)));

        Assert.Contains("groter dan 20 MB", fout.Message, StringComparison.Ordinal);
    }

    /// <summary>Een stroom van nullen van een opgegeven lengte, zonder die in het geheugen te hebben.</summary>
    private sealed class NulStream(long lengte) : Stream
    {
        private long positie;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => lengte;

        public override long Position { get => positie; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var aantal = (int)Math.Min(count, lengte - positie);
            Array.Clear(buffer, offset, aantal);
            positie += aantal;
            return aantal;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
