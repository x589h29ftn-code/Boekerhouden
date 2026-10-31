using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Export;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Verkoop;
using UglyToad.PdfPig;

namespace Boekerhouden.Administratie.Tests;

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

/// <summary>De lay-out van de verkoopfactuur en de opmaak: HTML, PDF, e-factuur en e-mail.</summary>
public sealed class FactuuropmaakTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private static readonly Partij Afzender = new("Bakkerij Voorbeeld B.V.", new("Dorpsstraat 1", "1234 AB", "Ergens"), "NL123456782B01", "12345678", "info@bakkerij.test", ["NL20INGB0001234567"]);

    private async Task<(Uitvoerder Wie, Guid Id, string Klant)> Administratie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Bakkerij Opmaak");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = "NL123456782B01",
            KvkNummer = "12345678",
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens("Dorpsstraat 1", "1234 AB", "Ergens", null, "info@bakkerij.test"),
        })).IsGelukt);
        var klant = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer("Klant <Test> B.V.", null, null, null,
            new Klantgegevens(true, "Laan 2", "5678 CD", "Elders", Email: "administratie@klant.test", Contactpersoon: "J. Jansen")), null);
        return (wie, id, (await inrichting.Relaties(wie, id)).Single(r => r.Id == klant.Waarde).Code);
    }

    private async Task<Guid> Factuur(Uitvoerder wie, Guid id, string klant, bool definitief = true)
    {
        var verkoop = app.Dienst<VerkoopDienst>();
        var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2026, 3, 1), new(2026, 3, 31), "Werkzaamheden maart",
            [new Verkoopregel("Advies", 10m, 85m, "8000", "V21", "uur"), new Verkoopregel("Broodjes", 20m, 2.5m, "8010", "V9")], "PO-123", "Dank voor de opdracht."));
        if (definitief)
        {
            Assert.True((await verkoop.MaakDefinitief(wie, id, concept.Waarde)).IsGelukt);
        }

        return concept.Waarde;
    }

    [Fact]
    public async Task Zonder_eigen_lay_out_geldt_de_standaard_en_de_eerste_bewaarde_wordt_de_standaard()
    {
        var (wie, id, _) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();

        var standaard = await opmaak.Layout(wie, id);
        Assert.Null(standaard.Id);
        Assert.Equal(Factuurontwerp.Standaard, standaard.Ontwerp);

        var ontwerp = Factuurontwerp.Standaard with { Lettertype = "Georgia", Accentkleur = "#aa3300" };
        var eerste = (await opmaak.Bewaar(wie, id, null, "Huisstijl", ontwerp)).Waarde;
        var tweede = (await opmaak.Bewaar(wie, id, null, "Engels", Factuurontwerp.Standaard)).Waarde;

        Assert.Equal([("Huisstijl", true), ("Engels", false)], (await opmaak.Layouts(wie, id)).Select(l => (l.Naam, l.Standaard)));
        Assert.Equal("Georgia", (await opmaak.Layout(wie, id)).Ontwerp.Lettertype);
        Assert.False((await opmaak.Verwijder(wie, id, eerste)).IsGelukt);
        Assert.True((await opmaak.MaakStandaard(wie, id, tweede)).IsGelukt);
        Assert.True((await opmaak.Verwijder(wie, id, eerste)).IsGelukt);
        Assert.Equal([("Engels", true)], (await opmaak.Layouts(wie, id)).Select(l => (l.Naam, l.Standaard)));
    }

    [Fact]
    public async Task Een_ontwerp_met_onzin_wordt_binnen_de_grenzen_gebracht()
    {
        var (wie, id, _) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        var ontwerp = Factuurontwerp.Standaard.Verplaats("klant", -50m, 999m) with { Accentkleur = "red;}body{display:none", Lettertype = "Comic" };
        ontwerp = ontwerp with { Blokken = [.. ontwerp.Blokken.Where(b => b.Soort != Bloksoort.Voettekst)] };

        var laag = (await opmaak.Bewaar(wie, id, null, "Proef", ontwerp)).Waarde;
        var bewaard = (await opmaak.Layout(wie, id, laag)).Ontwerp;

        Assert.Equal((0m, 292m), (bewaard.Blok("klant")!.X, bewaard.Blok("klant")!.Y));
        Assert.Equal(Factuurontwerp.Standaard.Accentkleur, bewaard.Accentkleur);
        Assert.Equal(Factuurontwerp.Standaard.Lettertype, bewaard.Lettertype);
        Assert.NotNull(bewaard.Blok(Bloksoort.Voettekst));
    }

    [Fact]
    public async Task Een_logo_is_een_png_of_jpeg_tot_1_mb()
    {
        var (wie, id, _) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        var laag = (await opmaak.Bewaar(wie, id, null, "Huisstijl", Factuurontwerp.Standaard)).Waarde;

        Assert.False((await opmaak.ZetLogo(wie, id, laag, Encoding.UTF8.GetBytes("<svg onload=alert(1)>"))).IsGelukt);
        Assert.False((await opmaak.ZetLogo(wie, id, laag, [.. Png, .. new byte[Logo.MaximumGrootte]])).IsGelukt);
        Assert.True((await opmaak.ZetLogo(wie, id, laag, Png)).IsGelukt);
        Assert.Equal("image/png", (await opmaak.Layout(wie, id)).Logo!.Inhoudstype);
        Assert.True((await opmaak.VerwijderLogo(wie, id, laag)).IsGelukt);
        Assert.Null((await opmaak.Layout(wie, id)).Logo);
    }

    [Fact]
    public async Task De_factuur_in_html_heeft_alles_wat_erop_moet_en_escapet_tekst()
    {
        var (wie, id, klant) = await Administratie();
        var factuur = await Factuur(wie, id, klant);
        var b = (await app.Dienst<FactuuropmaakDienst>().Beeld(wie, id, factuur))!.Value;

        var html = FactuurHtml.Document(b.Beeld, b.Ontwerp, new Logo(Png, "image/png"));

        Assert.Contains("20260001", html, StringComparison.Ordinal);
        Assert.Contains("Klant &lt;Test&gt; B.V.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Klant <Test>", html, StringComparison.Ordinal);
        Assert.Contains("t.a.v. J. Jansen", html, StringComparison.Ordinal);
        Assert.Contains("PO-123", html, StringComparison.Ordinal);
        Assert.Contains("Dank voor de opdracht.", html, StringComparison.Ordinal);
        Assert.Contains("NL20 INGB 0001 2345 67", html, StringComparison.Ordinal);
        Assert.Contains("KvK 12345678", html, StringComparison.Ordinal);
        Assert.Contains(Betalingskenmerk.Toon(b.Beeld.Factuur.Betalingskenmerk!), html, StringComparison.Ordinal);
        Assert.Contains("Btw 21% over 850,00", html, StringComparison.Ordinal);
        Assert.Contains("€ 1.083,00", html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Contains("@font-face", html, StringComparison.Ordinal);
        Assert.DoesNotContain("CONCEPT", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Een_concept_heeft_concept_erover_een_creditfactuur_heet_zo_en_vraagt_niet_om_betaling()
    {
        var (wie, id, klant) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        var concept = await Factuur(wie, id, klant, definitief: false);
        var c = (await opmaak.Beeld(wie, id, concept))!.Value;
        Assert.Contains("CONCEPT", FactuurHtml.Document(c.Beeld, c.Ontwerp, c.Logo), StringComparison.Ordinal);

        Assert.True((await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, concept)).IsGelukt);
        var credit = (await app.Dienst<VerkoopDienst>().Crediteer(wie, id, concept)).Waarde;
        Assert.True((await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, credit)).IsGelukt);
        var b = (await opmaak.Beeld(wie, id, credit))!.Value;
        var html = FactuurHtml.Document(b.Beeld, b.Ontwerp, b.Logo);

        Assert.Contains("Creditfactuur", html, StringComparison.Ordinal);
        Assert.Contains("Bij factuur", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Wij verzoeken u", html, StringComparison.Ordinal);
        Assert.Contains("−1.083,00", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task De_ontwerper_ziet_ook_uitgezette_blokken_en_kan_ze_kiezen()
    {
        var beeld = Verkoopbeeld.Voorbeeld(Afzender, BtwCodes.Standaard, new(2026, 10, 31));
        var ontwerp = Factuurontwerp.Standaard.Wijzig("afzender", b => b with { Zichtbaar = false });

        var pagina = FactuurHtml.Pagina(beeld, ontwerp, null, ontwerper: true, gekozen: "klant");
        var document = FactuurHtml.Document(beeld, ontwerp, null);

        Assert.Contains("data-blok=\"afzender\"", pagina, StringComparison.Ordinal);
        Assert.Contains("fp-kies gekozen", pagina, StringComparison.Ordinal);
        Assert.Contains("fp-leeg\">Logo", pagina, StringComparison.Ordinal);
        Assert.DoesNotContain("data-blok", document, StringComparison.Ordinal);
        Assert.DoesNotContain("Dorpsstraat 1<br>", document, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task De_e_factuur_is_er_alleen_van_een_definitieve_factuur()
    {
        var (wie, id, klant) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        var concept = await Factuur(wie, id, klant, definitief: false);
        Assert.False((await opmaak.Ubl(wie, id, concept)).IsGelukt);

        Assert.True((await app.Dienst<VerkoopDienst>().MaakDefinitief(wie, id, concept)).IsGelukt);
        var ubl = await opmaak.Ubl(wie, id, concept);

        Assert.True(ubl.IsGelukt);
        Assert.EndsWith(".xml", ubl.Waarde!.Naam, StringComparison.Ordinal);
        var gelezen = Boekhouding.Herkenning.UblFactuurLezer.Lees(new MemoryStream(ubl.Waarde.Inhoud));
        Assert.Equal(("20260001", 1083m), (gelezen.Factuurnummer, gelezen.TotaalInclusief));
    }

    [Fact]
    public void De_email_is_een_concept_met_de_bijlagen()
    {
        var eml = Encoding.UTF8.GetString(FactuuropmaakDienst.Eml("info@bakkerij.test", "klant@klant.test", "Factuur 20260001 van Bakkerij", "Beste klant,",
            [new Factuurbestand([1, 2, 3], "Factuur 20260001.pdf", "application/pdf"), new Factuurbestand(Encoding.UTF8.GetBytes("<Invoice/>"), "Factuur 20260001.xml", "application/xml")]));

        Assert.Contains("X-Unsent: 1", eml, StringComparison.Ordinal);
        Assert.Contains("To: klant@klant.test", eml, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf", eml, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/xml", eml, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("Beste klant,")), eml, StringComparison.Ordinal);

        // Een adres met een regeleinde (header-injectie) komt er niet in.
        var vals = Encoding.UTF8.GetString(FactuuropmaakDienst.Eml(null, "a@b.test\r\nBcc: x@y.test", "Onderwerp", "", []));
        Assert.DoesNotContain("Bcc:", vals, StringComparison.Ordinal);
        Assert.DoesNotContain("To:", vals, StringComparison.Ordinal);
    }

    [PdfBrowserFact]
    public async Task De_pdf_loopt_door_op_een_volgende_pagina_met_de_voettekst_op_elke_pagina()
    {
        var beeld = Verkoopbeeld.Voorbeeld(Afzender, BtwCodes.Standaard, new(2026, 10, 31));
        beeld = beeld with { Factuur = beeld.Factuur with { Regels = [.. Enumerable.Range(1, 60).Select(i => new Verkoopregel($"Regel {i}", 1m, 10m * i, "8000", "V21"))] } };

        var pdf = await app.Dienst<PdfDienst>().Maak(FactuurHtml.Document(beeld, Factuurontwerp.Standaard, new Logo(Png, "image/png")));

        Assert.True(pdf.IsGelukt, string.Join("; ", pdf.Fouten));
        using var document = PdfDocument.Open(pdf.Waarde!);
        Assert.True(document.NumberOfPages >= 2);
        var paginas = document.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))).ToList();
        Assert.Contains("20260001", paginas[0], StringComparison.Ordinal);
        Assert.All(paginas, p => Assert.Contains("12345678", p, StringComparison.Ordinal));
        Assert.Contains("Regel 60", paginas[^1], StringComparison.Ordinal);
        Assert.Contains("Wij verzoeken u", string.Join(" ", paginas), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_geen_lay_out_wijzigen()
    {
        var (wie, id, klant) = await Administratie();
        var opmaak = app.Dienst<FactuuropmaakDienst>();
        var laag = (await opmaak.Bewaar(wie, id, null, "Huisstijl", Factuurontwerp.Standaard)).Waarde;
        var factuur = await Factuur(wie, id, klant);
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.Layouts(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.Beeld(buiten, id, factuur));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.Ubl(buiten, id, factuur));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.Bewaar(inzien, id, laag, "Anders", Factuurontwerp.Standaard));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.ZetLogo(inzien, id, laag, Png));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.MaakStandaard(inzien, id, laag));
        await Assert.ThrowsAsync<GeenToegangException>(() => opmaak.Verwijder(inzien, id, laag));
        Assert.True((await opmaak.Ubl(inzien, id, factuur)).IsGelukt);

        var ander = await app.Administratie("Andere Zaak");
        Assert.Null(await opmaak.Beeld(wie, ander, factuur));
        Assert.False((await opmaak.ZetLogo(wie, ander, laag, Png)).IsGelukt);
    }
}
