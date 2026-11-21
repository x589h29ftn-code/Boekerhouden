using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Tests.Praktijk;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Een e-factuur is altijd te bekijken: de meegestuurde PDF, en anders de gegevens uit de e-factuur zelf.</summary>
public sealed class EfactuurbeeldTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Zonder_meegestuurde_pdf_geen_beeld_en_met_pdf_die_pdf()
    {
        var w = await Werkadministratie.Maak(app, "Efactuur Beeld B.V.");
        var inkoop = app.Dienst<InkoopDienst>();

        var zonder = await w.InkoopDocument("EB-1", new DateOnly(2026, 3, 2), 100m);
        Assert.False((await inkoop.Verwerking(w.Wie, w.Id, zonder))!.HeeftBeeld);
        Assert.Null(await inkoop.Beeld(w.Wie, w.Id, zonder));

        // Dezelfde soort e-factuur met een PDF als bijlage.
        var ubl = Encoding.UTF8.GetString((await inkoop.Inhoud(w.Wie, w.Id, zonder))!.Value.Inhoud);
        var pdf = "%PDF-1.4 de factuur"u8.ToArray();
        var metPdf = ubl.Replace("<cbc:ID>EB-1</cbc:ID>", $"""<cbc:ID>EB-2</cbc:ID><cac:AdditionalDocumentReference><cbc:ID>EB-2</cbc:ID><cac:Attachment><cbc:EmbeddedDocumentBinaryObject mimeCode="application/pdf" filename="EB-2.pdf">{Convert.ToBase64String(pdf)}</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>""", StringComparison.Ordinal);
        Assert.NotEqual(ubl, metPdf);
        var gelezen = await inkoop.Lees(w.Wie, w.Id, new MemoryStream(Encoding.UTF8.GetBytes(metPdf)), "EB-2.xml", null);
        var met = gelezen.DocumentId!.Value;
        Assert.True((await inkoop.Verwerking(w.Wie, w.Id, met))!.HeeftBeeld);
        Assert.Equal(pdf, await inkoop.Beeld(w.Wie, w.Id, met));
    }
}
