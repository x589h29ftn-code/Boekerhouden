using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
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
}
