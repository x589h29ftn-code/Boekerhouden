using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Documenten;
using Boekerhouden.Documenten.Tests;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Verzonnen PDF's met een lastige opmaak: een specificatie met eigen totalen op pagina 2, een naam die alleen in het
/// logo staat, en totalen zonder bekend label die de gebruiker één keer aanwijst.
/// </summary>
public sealed class InkoopOpmaakTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private sealed class Pagina(PdfPageBuilder pagina, PdfDocumentBuilder.AddedFont font)
    {
        private double y = 800;

        public void Regel(params (double X, string Tekst)[] kolommen)
        {
            foreach (var (x, tekst) in kolommen)
            {
                pagina.AddText(tekst, 9, new PdfPoint(x, y), font);
            }

            y -= 14;
        }
    }

    private static byte[] Pdf(params Action<Pagina>[] paginas)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var vul in paginas)
        {
            vul(new Pagina(builder.AddPage(PageSize.A4), font));
        }

        return builder.Build();
    }

    /// <summary>Een abonnementsfactuur met op pagina 2 een specificatie met kolommen "Prijs" en "Totaal".</summary>
    private static byte[] Abonnement(string nummer, decimal documenten) => Pdf(
        p =>
        {
            p.Regel((380, "Tel:"), (430, "+31 30 000 0000"));
            p.Regel((380, "Email:"), (430, "sales@softwarehuis-delta.nl"));
            p.Regel((40, "Kantoor Voorbeeld"), (380, "IBAN:"), (430, "NL91 ABNA 0417 1643 00"));
            p.Regel((40, "Dorpsstraat 7"), (380, "BTW:"), (430, "NL800000006B01"));
            p.Regel((40, "8601 AB Sneek"));
            p.Regel((40, "FACTUUR"));
            p.Regel((40, "Factuurnummer:"), (120, nummer));
            p.Regel((40, "Factuurdatum:"), (120, "01-10-2026"));
            p.Regel((40, "Betreft:"), (120, "Verleende diensten in periode 01-09-2026 t/m 30-09-2026"));
            p.Regel((40, "Omschrijving"), (300, "Aantal"), (400, "Prijs"), (480, "Bedrag"));
            p.Regel((40, "Standaard abonnement"), (300, "1"), (400, "EUR 10,00"), (480, "EUR 10,00"));
            p.Regel((40, "Documenten"), (300, "100"), (400, "EUR 0,18"), (480, $"EUR {documenten:0.00}".Replace('.', ',')));
            var excl = 10m + documenten;
            var btw = Math.Round(excl * 0.21m, 2, MidpointRounding.AwayFromZero);
            p.Regel((380, "Totaal (excl. BTW)"), (480, $"EUR{excl:0.00}".Replace('.', ',')));
            p.Regel((380, "BTW 21 %"), (480, $"EUR{btw:0.00}".Replace('.', ',')));
            p.Regel((380, "Totaal (incl. BTW)"), (480, $"EUR{excl + btw:0.00}".Replace('.', ',')));
            p.Regel((150, "Softwarehuis Delta B.V. | Havenstraat 10 | 3811 AB Amersfoort"));
        },
        p =>
        {
            p.Regel((150, "SPECIFICATIE Kantoor Voorbeeld"));
            p.Regel((40, "Documenten per administratie"), (400, "Prijs"), (480, "Totaal"));
            p.Regel((40, "22"), (80, "Documenten"), (400, "EUR 0,18"), (480, "EUR 3,96"));
        });

    [Fact]
    public async Task Totalen_van_pagina_een_en_niet_van_de_specificatie()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();

        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Abonnement("202617576", 18m)), "abonnement.pdf", "application/pdf");

        var h = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!.Herkend!;
        Assert.Equal(("Softwarehuis Delta B.V.", "202617576"), (h.Naam, h.Factuurnummer));
        Assert.Equal((28m, 5.88m, 33.88m), (h.TotaalExclusief, h.TotaalBtw, h.TotaalInclusief));
    }

    /// <summary>De naam staat alleen in het logo; de klant (met rechtsvorm) staat boven "t.a.v.".</summary>
    private static byte[] Logo() => Pdf(p =>
    {
        p.Regel((380, "Van Sminialaan 2 - 8731 BE Wommels"));
        p.Regel((380, "Telefoon 0612-345678"));
        p.Regel((380, "Mail info@q4-adviseurs.nl"));
        p.Regel((380, "BTW NL001145148B88"));
        p.Regel((40, "Faktuur"));
        p.Regel((300, "Markt Voorbeeld B.V."));
        p.Regel((300, "t.a.v. de heren J. Jansen en P. Pietersen"));
        p.Regel((40, "Faktuur"), (120, "2026-10-01"));
        p.Regel((40, "Datum:"), (120, "28 oktober 2026"));
        p.Regel((40, "Zie onderstaande"), (160, "EUR"), (200, "454,79"), (260, "Excl. BTW"));
        p.Regel((40, "BTW 21%"), (200, "95,51"));
        p.Regel((40, "Faktuurbedrag"), (160, "EUR"), (200, "550,30"), (260, "Incl. BTW"));
    });

    [Fact]
    public async Task Naam_in_het_logo_komt_uit_het_maildomein()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();

        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Logo()), "q4.pdf", "application/pdf");

        var h = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!.Herkend!;
        Assert.Equal(("Q4 Adviseurs", "2026-10-01", new DateOnly(2026, 10, 28)), (h.Naam, h.Factuurnummer, h.Factuurdatum));
        Assert.Equal((454.79m, 550.30m), (h.TotaalExclusief, h.TotaalInclusief));
    }

    /// <summary>
    /// Een totaal met een label dat het pakket niet kent ("Eindbedrag"), zonder btw-regel: de btw (21%) staat alleen in de
    /// tekst onderaan, zonder bedrag. Daar valt niets na te rekenen.
    /// </summary>
    private static byte[] Werkplaats(string nummer, decimal netto) => Pdf(p =>
    {
        var btw = Math.Round(netto * 0.21m, 2, MidpointRounding.AwayFromZero);
        p.Regel((40, "Werkplaats Oost B.V."));
        p.Regel((40, "IBAN NL20 INGB 0001 2345 67"));
        p.Regel((40, $"Factuurnummer: {nummer}"));
        p.Regel((40, "Factuurdatum: 01-05-2026"));
        p.Regel((40, "Onderhoud bus"), (300, "Netto"), (480, $"{netto:0.00}".Replace('.', ',')));
        p.Regel((300, "Eindbedrag"), (480, $"{netto + btw:0.00}".Replace('.', ',')));
        p.Regel((40, "Alle bedragen zijn inclusief 21 procent omzetbelasting."));
    });

    [Fact]
    public async Task Aangewezen_totaal_onthoudt_het_pakket_bij_de_volgende_factuur()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();
        var eerste = await inkoop.Lees(wie, id, new MemoryStream(Werkplaats("W-1", 200m)), "w1.pdf", "application/pdf");
        Assert.Null((await inkoop.Verwerking(wie, id, eerste.DocumentId!.Value))!.Herkend!.TotaalInclusief);

        // De gebruiker wijst het eindbedrag aan (regel 6: y = 800 - 5 × 14 = 730).
        var plek = new Zoekgebied(Factuurveld.Totaal, 1, 290, 726, 560, 740);
        Assert.Equal(["Eindbedrag    242,00"], await inkoop.TekstInGebied(wie, id, eerste.DocumentId.Value, plek.Pagina, plek.X0, plek.Y0, plek.X1, plek.Y1));
        var boeken = await inkoop.Boek(wie, id, eerste.DocumentId.Value, new Factuurinvoer(null, new("Werkplaats Oost B.V.", "NL20INGB0001234567", null, null),
            "W-1", new(2026, 5, 1), new(2026, 5, 31), "Onderhoud", [new Factuurregel("", "4500", "I21", 200m, 42m)], Aanwijzingen: [plek]));
        Assert.True(boeken.IsGelukt, string.Join("; ", boeken.Fouten));

        var tweede = await inkoop.Lees(wie, id, new MemoryStream(Werkplaats("W-2", 300m)), "w2.pdf", "application/pdf");

        var volgende = (await inkoop.Verwerking(wie, id, tweede.DocumentId!.Value))!;
        Assert.Equal(363m, volgende.Herkend!.TotaalInclusief);
    }

    /// <summary>Het nummer staat achter "Ref." (geen bekend label) rechtsboven; de rest is gewoon.</summary>
    private static byte[] Bouwmarkt(string nummer, string datum, decimal netto) => Pdf(p =>
    {
        var btw = Math.Round(netto * 0.21m, 2, MidpointRounding.AwayFromZero);
        p.Regel((40, "Bouwmarkt Noord B.V."), (380, "Ref."), (420, nummer));
        p.Regel((40, "IBAN NL20 INGB 0001 2345 67"));
        p.Regel((40, $"Factuurdatum: {datum}"));
        p.Regel((40, "Omschrijving"), (480, "Bedrag"));
        p.Regel((40, "Schroeven en pluggen"), (480, $"{netto:0.00}".Replace('.', ',')));
        p.Regel((300, "Subtotaal"), (480, $"{netto:0.00}".Replace('.', ',')));
        p.Regel((300, "BTW 21%"), (480, $"{btw:0.00}".Replace('.', ',')));
        p.Regel((300, "Te betalen"), (480, $"{netto + btw:0.00}".Replace('.', ',')));
    });

    [Fact]
    public async Task Na_het_boeken_leert_het_pakket_zelf_waar_het_nummer_staat()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();
        var eerste = await inkoop.Lees(wie, id, new MemoryStream(Bouwmarkt("BM-77", "01-05-2026", 50m)), "bm77.pdf", "application/pdf");
        Assert.Null((await inkoop.Verwerking(wie, id, eerste.DocumentId!.Value))!.Herkend!.Factuurnummer);

        // De gebruiker typt het nummer over (zonder aan te wijzen) en boekt.
        var boeken = await inkoop.Boek(wie, id, eerste.DocumentId.Value, new Factuurinvoer(null, new("Bouwmarkt Noord B.V.", "NL20INGB0001234567", null, null),
            "BM-77", new(2026, 5, 1), new(2026, 5, 31), "Materiaal", [new Factuurregel("", "4500", "I21", 50m, 10.50m)]));
        Assert.True(boeken.IsGelukt, string.Join("; ", boeken.Fouten));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ZoektemplateBijgewerkt && r.Details?.Contains("zelf geleerd: Factuurnummer", StringComparison.Ordinal) == true);

        var tweede = await inkoop.Lees(wie, id, new MemoryStream(Bouwmarkt("BM-1043", "01-06-2026", 75m)), "bm1043.pdf", "application/pdf");

        var volgende = (await inkoop.Verwerking(wie, id, tweede.DocumentId!.Value))!;
        Assert.Equal(("BM-1043", 90.75m), (volgende.Herkend!.Factuurnummer, volgende.Herkend.TotaalInclusief));
    }

    [Fact]
    public async Task Overzicht_van_wat_het_pakket_bij_een_leverancier_geleerd_heeft_en_vergeten()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Bouwmarkt("BM-5", "01-05-2026", 50m)), "bm5.pdf", "application/pdf");
        Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(null, new("Bouwmarkt Noord B.V.", "NL20INGB0001234567", null, null),
            "BM-5", new(2026, 5, 1), new(2026, 5, 31), "Materiaal", [new Factuurregel("", "4500", "I21", 50m, 10.50m)]))).IsGelukt);
        var code = (await inrichting.Relaties(wie, id)).Single(r => r.Naam == "Bouwmarkt Noord B.V.").Code;

        var geleerd = (await inrichting.Geleerd(wie, id, code))!;

        Assert.Equal(("4500", "I21", 1), (geleerd.Inkoop!.Keuze.Rekeningnummer, geleerd.Inkoop.Keuze.BtwCode, geleerd.Inkoop.OpRij));
        Assert.Equal(60.50m, Assert.Single(geleerd.Bevestigingen).Bevestiging.Bedrag);
        Assert.Contains(geleerd.Plekken, p => p.Veld == Factuurveld.Factuurnummer && !p.Voorrang);
        Assert.Null(await inrichting.Geleerd(wie, id, "ONBEKEND"));

        var inzien = await app.Gebruiker("InzienGeleerd");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        Assert.NotNull(await inrichting.Geleerd(inzien, id, code));
        await Assert.ThrowsAsync<GeenToegangException>(() => inrichting.VergeetGeleerd(inzien, id, code));
        await Assert.ThrowsAsync<GeenToegangException>(() => inrichting.VergeetPlek(inzien, id, code, Factuurveld.Factuurnummer));

        Assert.True((await inrichting.VergeetPlek(wie, id, code, Factuurveld.Factuurnummer)).IsGelukt);
        Assert.True((await inrichting.VergeetGeleerd(wie, id, code)).IsGelukt);
        var leeg = (await inrichting.Geleerd(wie, id, code))!;
        Assert.Null(leeg.Inkoop);
        Assert.DoesNotContain(leeg.Plekken, p => p.Veld == Factuurveld.Factuurnummer);
        Assert.False((await inrichting.VergeetGeleerd(wie, id, code)).IsGelukt);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.GeleerdVergeten && r.Onderwerp == code);
    }

    [Fact]
    public async Task Eigen_grens_bij_een_leverancier_houdt_de_factuur_ter_controle()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Kantoor Voorbeeld");
        var inkoop = app.Dienst<InkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Bouwmarkt("BM-50", "01-05-2026", 50m)), "bm50.pdf", "application/pdf");
        Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, new Factuurinvoer(null, new("Bouwmarkt Noord B.V.", "NL20INGB0001234567", null, null),
            "BM-50", new(2026, 5, 1), new(2026, 5, 31), "Materiaal", [new Factuurregel("", "4500", "I21", 50m, 10.50m)]))).IsGelukt);
        var code = (await inrichting.Relaties(wie, id)).Single(r => r.Naam == "Bouwmarkt Noord B.V.").Code;

        var inzien = await app.Gebruiker("InzienControle");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => inrichting.ZetControle(inzien, id, code, 100m, null));
        Assert.False((await inrichting.ZetControle(wie, id, code, -1m, null)).IsGelukt);
        Assert.False((await inrichting.ZetControle(wie, id, "ONBEKEND", 100m, null)).IsGelukt);

        Assert.True((await inrichting.ZetControle(wie, id, code, 500m, 50m)).IsGelukt);
        var relatie = (await inrichting.Geleerd(wie, id, code))!.Relatie;
        Assert.Equal((500m, 50m), (relatie.ControleVanaf, relatie.ControleAfwijking));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ControleIngesteld && r.Onderwerp == code && r.Details == "geen eigen grens → vanaf 500.00, afwijking 50%");

        // 60,50 eerder; 96,80 is meer dan 50% hoger.
        var hoger = await inkoop.Lees(wie, id, new MemoryStream(Bouwmarkt("BM-51", "01-06-2026", 80m)), "bm51.pdf", "application/pdf");
        var voorstel = (await inkoop.Verwerking(wie, id, hoger.DocumentId!.Value))!.Voorstel!;
        Assert.Contains("Het bedrag wijkt meer dan 50% af van de vorige factuur van Bouwmarkt Noord B.V. (€ 60,50).", voorstel.Belemmeringen);
        Assert.False(voorstel.Automatisch);

        Assert.True((await inrichting.ZetControle(wie, id, code, null, null)).IsGelukt);
        var nu = (await inkoop.Verwerking(wie, id, hoger.DocumentId.Value))!.Voorstel!;
        Assert.DoesNotContain(nu.Belemmeringen, b => b.StartsWith("Het bedrag wijkt", StringComparison.Ordinal));
    }

    [OcrFact]
    public async Task Foto_van_een_bon_wordt_met_ocr_herkend_en_als_pdf_bewaard()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        var inkoop = app.Dienst<InkoopDienst>();
        var foto = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "scan-kantoorvakhandel.png"));

        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(foto), "bon.png", "image/png");

        var verwerking = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!;
        Assert.Equal(("KV-2026-0815", 484m, Herkenningsbron.Ocr), (verwerking.Herkend!.Factuurnummer, verwerking.Herkend.TotaalInclusief, verwerking.Herkend.Bron));
        var bewaard = (await inkoop.Inhoud(wie, id, gelezen.DocumentId.Value))!.Value;
        Assert.Equal((".pdf", "application/pdf", "bon.pdf"), (Bestandssoort.Extensie(bewaard.Inhoud), bewaard.Inhoudstype, bewaard.Bestandsnaam));

        // Aanwijzen en oplichten werken op de woorden van de OCR.
        var plek = Assert.Single((await inkoop.Vindplaatsen(wie, id, gelezen.DocumentId.Value, new Dictionary<string, IReadOnlyList<string>> { ["nummer"] = Tekstvormen.Tekst("KV-2026-0815") }))["nummer"]);
        Assert.Equal(["KV-2026-0815"], await inkoop.TekstInGebied(wie, id, gelezen.DocumentId.Value, 1, plek.X0 - 1, plek.Y0 - 1, plek.X1 + 1, plek.Y1 + 1));

        // Dezelfde foto nog eens: herkend als al ingelezen (op de inhoud van het origineel).
        Assert.Contains("al ingelezen", (await inkoop.Lees(wie, id, new MemoryStream(foto), "bon (2).png", "image/png")).Melding, StringComparison.Ordinal);
    }
}
