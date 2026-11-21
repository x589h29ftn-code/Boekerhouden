using System.Globalization;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Herkenning;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Elke export teruggelezen en vergeleken met wat de app zelf toont: de kolommenbalans en de grootboekkaart(en) als
/// Excel en PDF, de jaarvergelijking, de activastaat, de openstaande posten als PDF, en een verkoopfactuur als PDF,
/// e-factuur (UBL, opnieuw ingelezen) en e-mail. Een export mag geen ander getal geven dan het scherm.
/// </summary>
public sealed class ExportSamenhangTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    [Fact]
    public async Task Elke_export_geeft_dezelfde_getallen_als_het_scherm()
    {
        var fouten = new List<string>();
        var w = await Werkadministratie.Maak(app, "Export Samenhang B.V.");
        var (wie, id) = (w.Wie, w.Id);
        var verkoop = app.Dienst<VerkoopDienst>();
        var nummers = new List<string>();
        Guid laatste = default;
        foreach (var (datum, bedrag) in new[] { (new DateOnly(2025, 3, 3), 1_234.56m), (new DateOnly(2025, 6, 9), 99.99m), (new DateOnly(2025, 11, 30), 12_000m) })
        {
            laatste = await w.Concept(datum, bedrag);
            nummers.Add((await verkoop.MaakDefinitief(wie, id, laatste)).Waarde!);
        }

        foreach (var (nummer, datum, bedrag) in new[] { ("EX-1", new DateOnly(2025, 2, 2), 450.10m), ("EX-2", new DateOnly(2025, 8, 8), 3_210m) })
        {
            Assert.True((await w.BoekInkoop(await w.InkoopDocument(nummer, datum, bedrag), nummer, datum, bedrag)).IsGelukt);
        }

        var activa = app.Dienst<ActivaDienst>();
        Assert.True((await activa.Bewaar(wie, id, null, new ActivumInvoer("Bestelbus", new(2025, 1, 15), 24_000m, 4_000m, new(2025, 1, 1), 60,
            Afschrijvingsmethode.Lineair, "0300", "0310", "4810"))).IsGelukt);
        Assert.True((await activa.Afschrijven(wie, id, new(2025, 12, 31), Afschrijvingsfrequentie.Maand)).IsGelukt);

        var (van, tot) = (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var grootboek = app.Dienst<GrootboekDienst>();
        var overzicht = app.Dienst<OverzichtDienst>();
        var kb = await grootboek.Kolommenbalans(wie, id, van, tot);
        var naam = (await app.Dienst<AdministratieDienst>().Lijst(wie)).Single(a => a.Id == id).Naam;
        var metSaldo = kb.Regels.Where(r => r.Saldo != 0).ToList();

        // ---- kolommenbalans
        var excel = Cellen(await overzicht.KolommenbalansExcel(wie, id, van, tot));
        foreach (var r in metSaldo)
        {
            if (!excel.Any(rij => rij.Any(c => c.Tekst.StartsWith(r.Rekening.Nummer, StringComparison.Ordinal)) && rij.Any(c => c.Getal == Math.Abs(r.Saldo))))
            {
                fouten.Add($"Kolommenbalans Excel: geen regel {r.Rekening.Nummer} met {Math.Abs(r.Saldo)}.");
            }
        }

        if (!excel.SelectMany(r => r).Any(c => c.Getal == Math.Abs(kb.Resultaat)))
        {
            fouten.Add($"Kolommenbalans Excel: het resultaat {kb.Resultaat} staat er niet in.");
        }

        var pdf = Tekst(Moet(await overzicht.KolommenbalansPdf(wie, id, van, tot)));
        Bevat(fouten, "Kolommenbalans PDF", pdf, [naam, .. metSaldo.Select(r => r.Rekening.Nummer), Weergave.Bedrag(Math.Abs(kb.Resultaat))]);

        // ---- grootboekkaart (bank) en alle kaarten
        var kaart = await grootboek.Kaart(wie, id, "1100", van, tot);
        var kaartExcel = Cellen(await overzicht.KaartExcel(wie, id, "1100", van, tot));
        if (!kaartExcel.SelectMany(r => r).Any(c => c.Getal == kaart.Eindsaldo))
        {
            fouten.Add($"Grootboekkaart Excel: eindsaldo {kaart.Eindsaldo} ontbreekt.");
        }

        Bevat(fouten, "Grootboekkaart PDF", Tekst(Moet(await overzicht.KaartPdf(wie, id, "1100", van, tot))), [naam, Weergave.Bedrag(kaart.Eindsaldo)]);
        var kaarten = await grootboek.Kaarten(wie, id, van, tot);
        var kaartenExcel = Cellen(await overzicht.KaartenExcel(wie, id, van, tot));
        foreach (var k in kaarten.Where(k => k.Mutaties.Count > 0))
        {
            if (!kaartenExcel.SelectMany(r => r).Any(c => c.Tekst.StartsWith(k.Rekening.Nummer + " ", StringComparison.Ordinal)))
            {
                fouten.Add($"Grootboekkaarten Excel: rekening {k.Rekening.Nummer} ontbreekt.");
            }

            if (!kaartenExcel.SelectMany(r => r).Any(c => c.Getal == k.Eindsaldo))
            {
                fouten.Add($"Grootboekkaarten Excel: eindsaldo {k.Eindsaldo} van {k.Rekening.Nummer} ontbreekt.");
            }
        }

        var kaartenPdf = Tekst(Moet(await overzicht.KaartenPdf(wie, id, van, tot)));
        Bevat(fouten, "Grootboekkaarten PDF", kaartenPdf, [.. kaarten.Where(k => k.Mutaties.Count > 0).Select(k => k.Rekening.Nummer)]);

        // ---- jaarvergelijking
        var vergelijking = Cellen(await overzicht.VergelijkingExcel(wie, id, van, tot));
        Bevat(fouten, "Jaarvergelijking Excel", string.Join(" ", vergelijking.SelectMany(r => r).Select(c => c.Tekst)), [.. metSaldo.Select(r => r.Rekening.Nummer)]);
        Bevat(fouten, "Jaarvergelijking PDF", Tekst(Moet(await overzicht.VergelijkingPdf(wie, id, van, tot))), [naam]);

        // ---- activastaat: boekwaarde 24.000 - 12 x (20.000 / 60) = 20.000
        var staat = (await activa.Activastaat(wie, id, 2025)).Single(g => g.Activarekening == "0300").Totaal;
        Assert.Equal(20_000m, staat.BoekwaardeEind);
        if (!Cellen(await overzicht.ActivastaatExcel(wie, id, 2025)).SelectMany(r => r).Any(c => c.Getal == staat.BoekwaardeEind))
        {
            fouten.Add("Activastaat Excel: de boekwaarde ontbreekt.");
        }

        Bevat(fouten, "Activastaat PDF", Tekst(Moet(await overzicht.ActivastaatPdf(wie, id, 2025))), ["Bestelbus", Weergave.Bedrag(staat.BoekwaardeEind)]);

        // ---- openstaande posten
        var debiteuren = await app.Dienst<OpenpostenDienst>().Debiteuren(wie, id, tot);
        var debiteurenPdf = Tekst(Moet(await app.Dienst<OpenpostenDienst>().DebiteurenPdf(wie, id, tot)));
        Bevat(fouten, "Debiteuren PDF", debiteurenPdf, [.. debiteuren.Relaties.SelectMany(r => r.Posten).Select(p => p.Factuurnummer), Weergave.Bedrag(debiteuren.Relaties.Sum(r => r.Openstaand))]);
        var crediteuren = await app.Dienst<OpenpostenDienst>().Crediteuren(wie, id, tot);
        Bevat(fouten, "Crediteuren PDF", Tekst(Moet(await app.Dienst<OpenpostenDienst>().CrediteurenPdf(wie, id, tot))),
            [.. crediteuren.Relaties.SelectMany(r => r.Posten).Select(p => p.Factuurnummer), Weergave.Bedrag(crediteuren.Relaties.Sum(r => r.Openstaand))]);

        // ---- een verkoopfactuur als PDF, e-factuur en e-mail: 12.000 + 21% = 14.520
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        Bevat(fouten, "Verkoopfactuur PDF", Tekst(Moet(await opmaak.Pdf(wie, id, laatste)).Inhoud), [nummers[^1], "14.520,00"]);
        var ubl = Moet(await opmaak.Ubl(wie, id, laatste));
        var terug = UblFactuurLezer.Lees(new MemoryStream(ubl.Inhoud));
        if ((terug.Factuurnummer, terug.TotaalExclusief, terug.TotaalInclusief, terug.TotaalBtw) != (nummers[^1], 12_000m, 14_520m, 2_520m))
        {
            fouten.Add($"E-factuur opnieuw ingelezen: {terug.Factuurnummer} {terug.TotaalExclusief} {terug.TotaalInclusief} {terug.TotaalBtw}.");
        }

        var mail = Mime(Encoding.UTF8.GetString(Moet(await opmaak.Email(wie, id, laatste)).Inhoud));
        Bevat(fouten, "E-mail", mail, ["application/pdf", $"Factuur {nummers[^1]}", ".pdf", ".xml", "14.520,00"]);

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }

    private static T Moet<T>(Uitkomst<T> uitkomst)
    {
        Assert.True(uitkomst.IsGelukt, string.Join(" ", uitkomst.Fouten));
        return uitkomst.Waarde!;
    }

    private static string Tekst(Factuurbestand bestand) => Tekst(bestand.Inhoud);

    /// <summary>Of de bedragen in een PDF te lezen zijn (pdftotext); zonder alleen de teksten zonder cijfers.</summary>
    private static bool volledig;

    /// <summary>
    /// De tekst van alle pagina's van een PDF. Met pdftotext als dat er is: in een Linux-container zet Chromium een
    /// variabel lettertype (Inter) om naar Type 3-lettertypen, waar PdfPig de cijfers niet uit haalt (op Windows met
    /// Segoe UI speelt dat niet).
    /// </summary>
    private static string Tekst(byte[] pdf)
    {
        try
        {
            var pad = Path.GetTempFileName();
            File.WriteAllBytes(pad, pdf);
            using var proces = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pdftotext", ["-layout", pad, "-"]) { RedirectStandardOutput = true, UseShellExecute = false })!;
            var tekst = proces.StandardOutput.ReadToEnd();
            proces.WaitForExit();
            File.Delete(pad);
            if (proces.ExitCode == 0)
            {
                volledig = true;
                return tekst;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Geen pdftotext op deze machine.
        }

        volledig = false;
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    /// <summary>Een e-mail leesbaar: de gecodeerde woorden (onderwerp, bijlagenamen) en de tekst in base64 uitgepakt.</summary>
    private static string Mime(string eml)
    {
        var leesbaar = System.Text.RegularExpressions.Regex.Replace(eml, @"=\?utf-8\?B\?([A-Za-z0-9+/=]+)\?=",
            m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tekst = System.Text.RegularExpressions.Regex.Match(eml, @"Content-Type: text/plain[^\n]*\r?\nContent-Transfer-Encoding: base64\r?\n\r?\n([A-Za-z0-9+/=\r\n]+)");
        return tekst.Success ? leesbaar + Encoding.UTF8.GetString(Convert.FromBase64String(tekst.Groups[1].Value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal))) : leesbaar;
    }

    private sealed record Cel(string Tekst, decimal? Getal);

    /// <summary>Alle cellen van alle bladen, per rij: de tekst en (als het een getal is) het getal.</summary>
    private static List<List<Cel>> Cellen(byte[] xlsx)
    {
        using var boek = new XLWorkbook(new MemoryStream(xlsx));
        return
        [
            .. boek.Worksheets.SelectMany(b => b.RowsUsed()).Select(r => r.CellsUsed().Select(c =>
                new Cel(c.GetFormattedString(Nl), c.DataType == XLDataType.Number ? Math.Round((decimal)c.GetDouble(), 2) : null)).ToList()),
        ];
    }

    private static void Bevat(List<string> fouten, string wat, string tekst, IEnumerable<string> verwacht)
    {
        var teLezen = wat.Contains("PDF", StringComparison.Ordinal) && !volledig ? verwacht.Where(v => !v.Any(char.IsDigit)) : verwacht;
        foreach (var v in teLezen.Where(v => !tekst.Contains(v, StringComparison.Ordinal)))
        {
            fouten.Add($"{wat}: \"{v}\" staat er niet in.");
        }
    }
}
