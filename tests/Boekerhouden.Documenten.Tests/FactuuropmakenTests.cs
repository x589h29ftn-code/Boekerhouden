using System.Globalization;
using Boekerhouden.Boekhouding.Herkenning;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit.Abstractions;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// Herkenning over veel verschillende factuuropmaken (verzonnen): Nederlands en Engels, zes vormen van een datum,
/// vijf vormen van een bedrag ("1.234,56", "1234,56", "€ 1.234,56", "EUR 1234,56", "1,234.56"), labels naast of boven
/// de waarde, één of twee btw-tarieven, een ordernummer en een vervaldatum als stoorzender. Per veld wordt geteld hoe
/// vaak het goed, leeg of fout is; fout is erger dan leeg (leeg vult de gebruiker in, fout kan ongemerkt geboekt
/// worden). De test faalt als een totaal of datum vaker fout dan leeg is, of als minder dan de helft goed is.
/// </summary>
public sealed class FactuuropmakenTests(ITestOutputHelper uitvoer)
{
    private sealed record Opmaak(bool Engels, int Datumvorm, int Bedragvorm, bool Boven, bool TweeTarieven, bool Stoorzenders);

    private sealed record Waarheid(string Nummer, DateOnly Datum, decimal Inclusief, decimal Btw, string Iban, string BtwNummer);

    private static readonly string[] Kritiek = ["totaal", "factuurdatum"];

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

    private static readonly (string Naam, string Iban, string Btw)[] Leveranciers =
    [
        ("Rijwielgroothandel Noord B.V.", "NL18RABO0123459876", GeldigBtw(1)),
        ("Energie Oost B.V.", "NL69INGB0123456789", GeldigBtw(2)),
        ("Kantoorhal B.V.", "NL86INGB0002445588", GeldigBtw(3)),
        ("Cloud Services Ltd", "NL91ABNA0417164300", GeldigBtw(4)),
    ];

    [Fact]
    public void Velden_over_veel_opmaken()
    {
        var toeval = new Random(5);
        var tellingen = new Dictionary<string, (int Goed, int Leeg, int Fout)>();
        var voorbeelden = new List<string>();
        var legen = new List<string>();
        void Tel(string veld, object? gevonden, object verwacht, Opmaak opmaak)
        {
            var (g, l, f) = tellingen.GetValueOrDefault(veld);
            if (gevonden is null)
            {
                tellingen[veld] = (g, l + 1, f);
                legen.Add($"{veld} {(opmaak.Engels ? "EN" : "NL")} {(opmaak.Boven ? "boven" : "naast")}");
            }
            else if (Equals(gevonden, verwacht))
            {
                tellingen[veld] = (g + 1, l, f);
            }
            else
            {
                tellingen[veld] = (g, l, f + 1);
                if (voorbeelden.Count(v => v.StartsWith(veld, StringComparison.Ordinal)) < 4)
                {
                    voorbeelden.Add($"{veld}: {gevonden} in plaats van {verwacht} ({opmaak})");
                }
            }
        }

        var i = 0;
        foreach (var engels in new[] { false, true })
        {
            foreach (var datumvorm in Enumerable.Range(0, 6))
            {
                foreach (var bedragvorm in Enumerable.Range(0, 5))
                {
                    foreach (var boven in new[] { false, true })
                    {
                        var opmaak = new Opmaak(engels, datumvorm, bedragvorm, boven, toeval.Next(2) == 0, toeval.Next(2) == 0);
                        var (naam, iban, btw) = Leveranciers[i++ % Leveranciers.Length];
                        var exclusief21 = Math.Round((decimal)(toeval.NextDouble() * 3_000 + 20), 2);
                        var exclusief9 = opmaak.TweeTarieven ? Math.Round((decimal)(toeval.NextDouble() * 500 + 5), 2) : 0m;
                        var btw21 = Math.Round(exclusief21 * 0.21m, 2, MidpointRounding.AwayFromZero);
                        var btw9 = Math.Round(exclusief9 * 0.09m, 2, MidpointRounding.AwayFromZero);
                        var waarheid = new Waarheid($"{(engels ? "INV" : "F")}-2025-{1000 + i}", new DateOnly(2025, 1 + toeval.Next(12), 1 + toeval.Next(28)),
                            exclusief21 + exclusief9 + btw21 + btw9, btw21 + btw9, iban, btw);
                        var pdf = Maak(opmaak, naam, waarheid, exclusief21, exclusief9, btw21, btw9);
                        var tekst = PdfTekst.Lees(new MemoryStream(pdf)).Tekst;
                        var h = Factuurtekst.Analyseer(tekst, eigenIbans: ["NL20INGB0001234567"], eigenBtwNummers: ["NL123456782B01"], eigenNamen: ["Fietsenhandel De Spaak B.V."]);
                        Tel("factuurnummer", h.Factuurnummer, waarheid.Nummer, opmaak);
                        Tel("factuurdatum", h.Factuurdatum, waarheid.Datum, opmaak);
                        Tel("totaal", h.TotaalInclusief, waarheid.Inclusief, opmaak);
                        Tel("btw", h.Btw is null ? null : h.TotaalBtw, waarheid.Btw, opmaak);
                        Tel("iban", h.Iban, waarheid.Iban, opmaak);
                        Tel("btw-nummer", h.BtwNummer, waarheid.BtwNummer, opmaak);
                    }
                }
            }
        }

        foreach (var (veld, (goed, leeg, fout)) in tellingen)
        {
            uitvoer.WriteLine($"{veld,-14} goed {goed,3}  leeg {leeg,3}  fout {fout,3}  ({100.0 * goed / (goed + leeg + fout):0}% goed)");
        }

        foreach (var g in legen.GroupBy(x => x).OrderByDescending(g => g.Count()))
        {
            uitvoer.WriteLine($"  leeg {g.Count(),3}x: {g.Key}");
        }

        foreach (var v in voorbeelden)
        {
            uitvoer.WriteLine("  " + v);
        }

        Assert.All(Kritiek, veld => Assert.True(tellingen[veld].Fout <= tellingen[veld].Leeg, $"{veld}: vaker fout ({tellingen[veld].Fout}) dan leeg ({tellingen[veld].Leeg})"));
        Assert.All(tellingen, t => Assert.True(t.Value.Goed * 2 >= t.Value.Goed + t.Value.Leeg + t.Value.Fout, $"{t.Key}: minder dan de helft goed"));
    }

    /// <summary>Een verzonnen btw-nummer dat de controle doorstaat.</summary>
    private static string GeldigBtw(int start)
    {
        for (var n = start * 1_000_000; ; n++)
        {
            var nummer = $"NL{n:000000000}B01";
            if (Boekhouding.Relaties.BtwNummer.IsGeldig(nummer))
            {
                return nummer;
            }
        }
    }

    private static string Datum(DateOnly d, Opmaak o) => o.Datumvorm switch
    {
        0 => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        1 => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        2 => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        3 => o.Engels ? d.ToString("d MMMM yyyy", En) : d.ToString("d MMMM yyyy", Nl),
        4 => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => o.Engels ? d.ToString("MMMM d, yyyy", En) : d.ToString("d MMM yyyy", Nl).Replace(".", "", StringComparison.Ordinal),
    };

    private static string Bedrag(decimal b, Opmaak o) => o.Bedragvorm switch
    {
        0 => b.ToString("#,##0.00", Nl),
        1 => b.ToString("0.00", Nl),
        2 => "€ " + b.ToString("#,##0.00", Nl),
        3 => "EUR " + b.ToString("0.00", Nl),
        _ => o.Engels ? "€" + b.ToString("#,##0.00", En) : b.ToString("#,##0.00", Nl),
    };

    private static byte[] Maak(Opmaak o, string naam, Waarheid w, decimal ex21, decimal ex9, decimal btw21, decimal btw9)
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        // Met een lettertype dat het euroteken kent (DejaVu, als het er is); anders "EUR" in plaats van "€".
        var map = "/usr/share/fonts/truetype/dejavu/";
        var metEuro = File.Exists(map + "DejaVuSans.ttf") && File.Exists(map + "DejaVuSans-Bold.ttf");
        var font = metEuro ? builder.AddTrueTypeFont(File.ReadAllBytes(map + "DejaVuSans.ttf")) : builder.AddStandard14Font(Standard14Font.Helvetica);
        var vet = metEuro ? builder.AddTrueTypeFont(File.ReadAllBytes(map + "DejaVuSans-Bold.ttf")) : builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var y = 800.0;
        void Regel(double x, string tekst, bool zwaar = false, bool volgende = true)
        {
            pagina.AddText(metEuro ? tekst : tekst.Replace("€", "EUR ", StringComparison.Ordinal), 10, new PdfPoint(x, y), zwaar ? vet : font);
            if (volgende)
            {
                y -= 15;
            }
        }

        void Veld(string label, string waarde)
        {
            if (o.Boven)
            {
                Regel(320, label, zwaar: true);
                Regel(320, waarde);
            }
            else
            {
                Regel(320, label, volgende: false);
                Regel(440, waarde);
            }
        }

        var e = o.Engels;
        Regel(50, naam, zwaar: true);
        Regel(50, "Industrieweg 12");
        Regel(50, "1234 AB Ergens");
        Regel(50, $"{(e ? "VAT number" : "Btw-nummer")}: {w.BtwNummer}");
        Regel(50, "KvK 12345678");
        y -= 10;
        Regel(50, "Fietsenhandel De Spaak B.V.");
        Regel(50, "Dorpsstraat 1");
        Regel(50, "1234 AB Ergens");
        y = 740;
        Regel(320, e ? "INVOICE" : "FACTUUR", zwaar: true);
        Veld(e ? "Invoice number" : "Factuurnummer", w.Nummer);
        Veld(e ? "Invoice date" : "Factuurdatum", Datum(w.Datum, o));
        if (o.Stoorzenders)
        {
            Veld(e ? "Order number" : "Ordernummer", "4500123987");
            Veld(e ? "Due date" : "Vervaldatum", Datum(w.Datum.AddDays(30), o));
        }

        y = 560;
        Regel(50, e ? "Description" : "Omschrijving", zwaar: true, volgende: false);
        Regel(400, e ? "Amount" : "Bedrag", zwaar: true);
        Regel(50, e ? "Bicycles and parts" : "Fietsen en onderdelen", volgende: false);
        Regel(400, Bedrag(ex21, o));
        if (ex9 > 0)
        {
            Regel(50, e ? "Repairs" : "Reparaties", volgende: false);
            Regel(400, Bedrag(ex9, o));
        }

        y -= 10;
        Regel(300, e ? "Subtotal" : "Totaal excl. btw", volgende: false);
        Regel(400, Bedrag(ex21 + ex9, o));
        Regel(300, e ? "VAT 21%" : "Btw 21%", volgende: false);
        Regel(400, Bedrag(btw21, o));
        if (ex9 > 0)
        {
            Regel(300, e ? "VAT 9%" : "Btw 9%", volgende: false);
            Regel(400, Bedrag(btw9, o));
        }

        Regel(300, e ? "Total" : "Totaal incl. btw", zwaar: true, volgende: false);
        Regel(400, Bedrag(w.Inclusief, o), zwaar: true);
        y = 120;
        Regel(50, e ? $"Please pay within 30 days to {w.Iban} stating the invoice number." : $"Graag binnen 30 dagen betalen op {w.Iban} onder vermelding van het factuurnummer.");
        Regel(50, "Tel. 050-1234567  info@leverancier.test");
        return builder.Build();
    }
}
