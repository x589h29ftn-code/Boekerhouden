using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een verzonnen mva-staat als PDF zoals een samenstelprogramma hem drukt: kop over drie regels, bedragen rechts uitgelijnd.</summary>
public sealed class PdfActivastaatTests
{
    // Breedte van Helvetica per teken (in duizendsten van de lettergrootte), voor rechts uitlijnen.
    private static double Breedte(string tekst, double grootte) =>
        tekst.Sum(c => c switch { '.' or ',' => 278, '-' => 333, '%' => 889, >= '0' and <= '9' => 556, _ => 556 }) * grootte / 1000;

    internal static byte[] Staat(string totaalAanschaf = "28.766")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var pagina = builder.AddPage(PageSize.A4, isPortrait: false);
        var y = 540.0;
        double[] rechts = [330, 390, 450, 510, 570, 630, 690, 750, 800];
        void Regel(string? omschrijving, string? datum, params string?[] bedragen)
        {
            if (omschrijving is not null)
            {
                pagina.AddText(omschrijving, 8, new PdfPoint(40, y), font);
            }

            if (datum is not null)
            {
                pagina.AddText(datum, 8, new PdfPoint(200, y), font);
            }

            for (var i = 0; i < bedragen.Length; i++)
            {
                if (bedragen[i] is { } b)
                {
                    pagina.AddText(b, 8, new PdfPoint(rechts[i] - Breedte(b, 8), y), font);
                }
            }

            y -= 12;
        }

        void Kop(params string[] teksten)
        {
            for (var i = 0; i < teksten.Length; i++)
            {
                if (teksten[i].Length > 0)
                {
                    pagina.AddText(teksten[i], 7, new PdfPoint(rechts[i] - Breedte(teksten[i], 7), y), font);
                }
            }

            y -= 10;
        }

        pagina.AddText("Bakkerij De Korst B.V. te Ergens", 10, new PdfPoint(40, y), font);
        y -= 24;
        pagina.AddText("Omschrijving", 7, new PdfPoint(40, y - 20), font);
        pagina.AddText("Datum", 7, new PdfPoint(200, y - 20), font);
        Kop("Aanschaf-", "Inves-", "Desinves-", "Aanschaf-", "Afschrij-", "Afschrij-", "Afschrij-", "Boekwaarde", "Residu-");
        Kop("fingen tot", "teringen", "teringen", "fingen t/m", "vingen tot", "vingen", "vingen t/m", "per", "waarde");
        Kop("01-01-2025", "2025", "2025", "31-12-2025", "01-01-2025", "2025", "31-12-2025", "31-12-2025", "");
        y -= 6;
        Regel("Inventaris", null);
        Regel("Oven", "11-02-2022", "3.000", null, null, "3.000", "1.450", "600", "2.050", "950", null);
        Regel("Kassa", null, "800", null, null, "800", "800", null, "800", "-", null);
        Regel("Schilderij", null, "966", null, null, "966", null, null, null, "966", "966");
        Regel(null, null, "4.766", null, null, "4.766", "2.250", "600", "2.850", "1.916", "966");
        Regel("Vervoermiddelen", null);
        Regel("Bestelbus", "01-07-2024", null, "24.000", null, "24.000", null, "2.200", "2.200", "21.800", "2.000");
        Regel(null, null, null, "24.000", null, "24.000", null, "2.200", "2.200", "21.800", "2.000");
        Regel("Totaal vaste activa", null, "4.766", "24.000", null, totaalAanschaf, "2.250", "2.800", "5.050", "23.716", "2.966");
        return builder.Build();
    }

    [Fact]
    public void Een_gedrukte_staat_met_kop_over_drie_regels()
    {
        var staat = PdfActivastaat.Lees(Staat());

        Assert.True(staat.Sluit, string.Join(" | ", staat.Signalen));
        Assert.Equal(2025, staat.Jaar);
        Assert.Equal([("Inventaris", "Oven", 3000m, 2050m, 950m), ("Inventaris", "Kassa", 800m, 800m, 0m), ("Inventaris", "Schilderij", 966m, 0m, 966m), ("Vervoermiddelen", "Bestelbus", 24000m, 2200m, 21800m)],
            staat.Regels.Select(r => (r.Groep, r.Omschrijving, r.AanschafEind, r.AfschrijvingEind, r.Boekwaarde)));
        Assert.Equal((new DateOnly(2022, 2, 11), 2000m), (staat.Regels.First().Datum!.Value, staat.Regels.Last().Restwaarde));
    }

    [Fact]
    public void Een_eindtotaal_dat_niet_klopt_valt_op() =>
        Assert.Contains(PdfActivastaat.Lees(Staat(totaalAanschaf: "28.800")).Signalen, s => s.Tekst.StartsWith("Totaal van de staat: aanschafwaarde", StringComparison.Ordinal));
}
