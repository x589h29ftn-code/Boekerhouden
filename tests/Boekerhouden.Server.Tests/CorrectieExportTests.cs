using System.Net;
using Boekerhouden.Data;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Boekerhouden.Server.Tests;

/// <summary>Test die alleen draait als er een browser is om PDF's mee te maken (Edge, Chrome of Chromium).</summary>
internal sealed class PdfBrowserFactAttribute : FactAttribute
{
    public PdfBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("Pdf__Browser") is not { Length: > 0 } && PdfDienst.ZoekBrowser() is null)
        {
            Skip = "Geen Edge, Chrome of Chromium gevonden; stel Pdf__Browser in om de PDF-test te draaien.";
        }
    }
}

public sealed class CorrectieExportTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private CorrectieDienst Correcties => server.Dienst<CorrectieDienst>();

    /// <summary>Een dossier met drie boekingen: een correctie, een herrubricering en een al verwerkte correctie.</summary>
    private async Task<(Guid DossierId, string Klantnummer)> DossierMetCorrecties(string omschrijving = "Extra afschrijving inventaris")
    {
        await server.MetRgs();
        var klantnummer = TestGegevens.Klantnummer();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(klantnummer, "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, null), Tester)).Waarde;
        Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, 2025, TestGegevens.Fictief("bv-voorbeeld-bakkerij", "saldibalans-2025.csv"), "2025.csv", Tester)).IsGelukt);
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var r = (await server.Dienst<RubriceerDienst>().Overzicht(dossierId, metVoorstellen: false))!.Rekeningen.ToDictionary(x => x.Nummer, x => x.Id);
        await server.Dienst<RubriceerDienst>().Koppel(klantId, [r["4800"]], "WAfsAmvBei", false, Tester);

        await Correcties.Bewaar(dossierId, null, new CorrectieInvoer(omschrijving, CorrectieSoort.Correctie, false, false,
            [new(r["4800"], 2_340m, "tweede halfjaar"), new(r["0210"], -2_340m)]), Tester);
        await Correcties.Bewaar(dossierId, null, new CorrectieInvoer("Kas naar bank", CorrectieSoort.Herrubricering, false, false,
            [new(r["1100"], 100.25m), new(r["1000"], -100.25m)]), Tester);
        await Correcties.Bewaar(dossierId, null, new CorrectieInvoer("Al geboekt door de klant", CorrectieSoort.Correctie, false, true,
            [new(r["4300"], 50m), new(r["1000"], -50m)]), Tester);
        return (dossierId, klantnummer);
    }

    private async Task<ExportVerzoek> Verzoek(Guid dossierId, bool alleenTeVerwerken = false) =>
        new((await Correcties.Overzicht(dossierId))!, alleenTeVerwerken, "Test Gebruiker", new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Excel_bevat_elke_regel_met_exacte_bedragen_en_het_overzicht()
    {
        var (dossierId, klantnummer) = await DossierMetCorrecties();

        using var boek = new XLWorkbook(new MemoryStream(CorrectieExport.Excel(await Verzoek(dossierId))));

        var blad = boek.Worksheet("Correcties");
        Assert.Equal("Correcties Jaarrekening 2025", blad.Cell(1, 1).GetString());
        Assert.StartsWith(klantnummer, blad.Cell(2, 1).GetString(), StringComparison.Ordinal);
        var tabel = blad.Table("TabelCorrectieregels");
        var regels = tabel.DataRange.Rows().ToList();
        Assert.Equal(6, regels.Count);
        Assert.Equal("4800", regels[0].Cell(4).GetString());
        Assert.Equal("tweede halfjaar", regels[0].Cell(6).GetString());
        Assert.True(regels[1].Cell(6).IsEmpty());
        Assert.Equal(2_340m, regels[0].Cell(7).GetValue<decimal>());
        Assert.Equal(100.25m, regels[3].Cell(8).GetValue<decimal>());
        Assert.Equal("Herrubricering", regels[2].Cell(3).GetString());
        Assert.True(tabel.ShowTotalsRow);

        var overzicht = boek.Worksheet("Overzicht").Table("TabelCorrecties").DataRange.Rows().ToList();
        Assert.Equal(3, overzicht.Count);
        Assert.Equal(-2_340m, overzicht[0].Cell(7).GetValue<decimal>());
        Assert.Equal("ja", overzicht[2].Cell(5).GetString());
    }

    [Fact]
    public async Task Alleen_te_verwerken_laat_herrubriceringen_en_verwerkte_correcties_weg()
    {
        var (dossierId, klantnummer) = await DossierMetCorrecties();
        var verzoek = await Verzoek(dossierId, alleenTeVerwerken: true);

        Assert.Equal("Extra afschrijving inventaris", Assert.Single(verzoek.Boekingen).Omschrijving);
        Assert.Equal($"Correcties {klantnummer} 2025 te verwerken.xlsx", verzoek.Bestandsnaam("xlsx"));
        Assert.Contains("alleen correcties die de klant nog moet verwerken", CorrectieExport.Html(verzoek), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_is_veilig_en_toont_bedragen_met_centen()
    {
        var (dossierId, _) = await DossierMetCorrecties("<script>alert('x')</script> & co");

        var html = CorrectieExport.Html(await Verzoek(dossierId));

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; co", html, StringComparison.Ordinal);
        Assert.Contains("2.340,00", html, StringComparison.Ordinal);
        Assert.Contains("100,25", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
    }

    [PdfBrowserFact]
    public async Task Pdf_wordt_gemaakt_met_de_browser()
    {
        var (dossierId, _) = await DossierMetCorrecties();

        var uitkomst = await server.Dienst<PdfDienst>().Maak(CorrectieExport.Html(await Verzoek(dossierId)));

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal("%PDF-"u8.ToArray(), uitkomst.Waarde![..5]);
        Assert.True(uitkomst.Waarde.Length > 2_000);
    }

    [Fact]
    public async Task Download_vraagt_om_inloggen_en_komt_in_het_wijzigingslog()
    {
        var (dossierId, klantnummer) = await DossierMetCorrecties();
        await server.Beheerder();
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var adres = new Uri(ExportEindpunten.Adres(dossierId, "xlsx", alleenTeVerwerken: false), UriKind.Relative);

        var anoniem = await client.GetAsync(adres);
        Assert.Equal(HttpStatusCode.Redirect, anoniem.StatusCode);
        Assert.StartsWith("/inloggen", anoniem.Doel(), StringComparison.Ordinal);

        await Aanmelden.LogIn(client, "beheer@kantoor.test", TestGegevens.Wachtwoord, "/");
        var antwoord = await client.GetAsync(adres);

        Assert.Equal(HttpStatusCode.OK, antwoord.StatusCode);
        Assert.Equal(ExportEindpunten.Xlsx, antwoord.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"Correcties {klantnummer} 2025.xlsx", antwoord.Content.Headers.ContentDisposition?.FileNameStar);
        using var boek = new XLWorkbook(await antwoord.Content.ReadAsStreamAsync());
        Assert.Equal(6, boek.Worksheet("Correcties").Table("TabelCorrectieregels").DataRange.RowCount());
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.CorrectiesGeexporteerd),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "Excel, 3 boeking(en)");

        var onbekend = await client.GetAsync(new Uri(ExportEindpunten.Adres(Guid.NewGuid(), "xlsx", false), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, onbekend.StatusCode);
    }
}
