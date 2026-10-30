using System.Diagnostics;
using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Draait alleen met BOEKERHOUDEN_STRESS=1 (of een aantal facturen per leverancier, bijv. 50).</summary>
internal sealed class StressFactAttribute : FactAttribute
{
    public StressFactAttribute()
    {
        if (Aantal == 0)
        {
            Skip = "Zet BOEKERHOUDEN_STRESS=1 (of het aantal facturen per leverancier) om de stresstest te draaien.";
        }
    }

    /// <summary>Facturen per leverancier; 1 betekent de standaard (30).</summary>
    public static int Aantal => Environment.GetEnvironmentVariable("BOEKERHOUDEN_STRESS") is { Length: > 0 } waarde
        && int.TryParse(waarde, CultureInfo.InvariantCulture, out var n) && n > 0 ? (n == 1 ? 30 : n) : 0;
}

/// <summary>
/// Stresstest van het inkoopboek: tien verzonnen leveranciers met elk een reeks maandfacturen (PDF), in bulk ingelezen,
/// een paar zelf geboekt, de rest via Opnieuw herkennen, dan de overzichten; en een tweede administratie waarin vier
/// gebruikers tegelijk inlezen (automatisch boeken in de hash-keten). Meet de tijden en controleert de keten.
/// </summary>
public sealed class InkoopStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly string[] Leveranciers =
    [
        "Telecom Noord B.V.", "Energie Oost B.V.", "Software Delta B.V.", "Schoonmaak Glans B.V.", "Drukkerij Pers B.V.",
        "Lease Auto B.V.", "Verzekeraar Zeker N.V.", "Kantoorvakhandel Pen B.V.", "Webhosting Wolk B.V.", "Koffie Service B.V.",
    ];

    /// <summary>Een maandfactuur als PDF, in de opmaak van de leverancier (de kolommen schuiven per leverancier).</summary>
    internal static byte[] Factuur(int leverancier, int maand)
    {
        var netto = 40m + leverancier * 7.5m + (maand % 3);
        var btw = Math.Round(netto * 0.21m, 2, MidpointRounding.AwayFromZero);
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 800.0;
        var kolom = 360 + leverancier * 8;
        void Regel(string links, string? rechts = null)
        {
            pagina.AddText(links, 9, new PdfPoint(40, y), font);
            if (rechts is not null)
            {
                pagina.AddText(rechts, 9, new PdfPoint(kolom, y), font);
            }

            y -= 14;
        }

        var datum = new DateOnly(2026, 1, 1).AddMonths(maand - 1).AddDays(leverancier);
        Regel(Leveranciers[leverancier], "Test BV");
        Regel($"Stationsweg {leverancier + 1}", "Industrieweg 5");
        Regel($"97{leverancier:00} AB Groningen", "1234 AB Amersfoort");
        Regel($"IBAN {StressIban(leverancier)}");
        Regel($"KvK {10000000 + leverancier * 1111}");
        Regel($"Factuurnummer: {leverancier:00}-{2026}-{maand:0000}");
        Regel($"Factuurdatum: {datum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        Regel("Omschrijving", "Bedrag");
        Regel($"Dienstverlening {datum.ToString("MMMM yyyy", Nl)}", netto.ToString("N2", Nl));
        Regel("Subtotaal", $"EUR {netto.ToString("N2", Nl)}");
        Regel("BTW 21%", $"EUR {btw.ToString("N2", Nl)}");
        Regel("Totaal", $"EUR {(netto + btw).ToString("N2", Nl)}");
        return builder.Build();
    }

    /// <summary>Een geldig IBAN per leverancier (verzonnen, met kloppend controlegetal).</summary>
    internal static string StressIban(int leverancier)
    {
        var bban = $"INGB{(1000000 + leverancier * 13579):0000000000}";
        var getal = string.Concat((bban + "NL00").Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        var rest = getal.Aggregate(0, (r, c) => (r * 10 + (c - '0')) % 97);
        return $"NL{98 - rest:00}{bban}";
    }

    private void Meld(string wat, Stopwatch klok, int aantal = 1) =>
        uitvoer.WriteLine($"{wat,-48} {klok.Elapsed.TotalSeconds,8:0.00} s{(aantal > 1 ? $"  ({klok.Elapsed.TotalMilliseconds / aantal:0} ms per stuk)" : null)}");

    [StressFact]
    public async Task Honderden_facturen_inlezen_boeken_en_overzichten()
    {
        var perLeverancier = StressFactAttribute.Aantal;
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        var inkoop = app.Dienst<InkoopDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();

        // 1. Alles in bulk inlezen, maand voor maand (zoals een stapel van een jaar).
        var tijden = new List<double>();
        var klok = Stopwatch.StartNew();
        var documenten = new Dictionary<(int, int), Guid>();
        for (var maand = 1; maand <= perLeverancier; maand++)
        {
            for (var l = 0; l < Leveranciers.Length; l++)
            {
                var stuk = Stopwatch.StartNew();
                var uitkomst = await inkoop.Lees(wie, id, new MemoryStream(Factuur(l, maand)), $"{l}-{maand}.pdf", "application/pdf");
                tijden.Add(stuk.Elapsed.TotalMilliseconds);
                documenten[(l, maand)] = uitkomst.DocumentId ?? throw new InvalidOperationException(uitkomst.Melding);
            }
        }

        var aantal = documenten.Count;
        Meld($"1. {aantal} facturen inlezen", klok, aantal);
        tijden.Sort();
        uitvoer.WriteLine($"   inlezen: mediaan {tijden[tijden.Count / 2]:0} ms, p95 {tijden[(int)(tijden.Count * 0.95)]:0} ms, max {tijden[^1]:0} ms; eerste 10 gem. {tijden.Take(10).Average():0} ms");

        // 2. De lijst en het verwerkscherm met een volle werkvoorraad.
        klok.Restart();
        var lijst = await inkoop.Lijst(wie, id, Documentstatus.TeVerwerken);
        Meld($"2. lijst Te verwerken ({lijst.Count})", klok);
        Assert.Equal(aantal, lijst.Count);
        klok.Restart();
        for (var i = 0; i < 20; i++)
        {
            Assert.NotNull(await inkoop.Verwerking(wie, id, lijst[i].Id));
        }

        Meld("   verwerkscherm openen (20 keer)", klok, 20);

        // 3. Per leverancier de eerste twee facturen zelf boeken (de drempel), met het voorstel.
        klok.Restart();
        for (var l = 0; l < Leveranciers.Length; l++)
        {
            for (var maand = 1; maand <= 2; maand++)
            {
                var document = documenten[(l, maand)];
                var h = (await inkoop.Verwerking(wie, id, document))!.Herkend!;
                var code = (await inkoop.Verwerking(wie, id, document))!.Voorstel?.Leverancier?.Relatie.Code;
                var invoer = new Factuurinvoer(code, code is null ? new NieuweRelatie(h.Naam!, h.Iban, h.BtwNummer, h.KvkNummer) : null,
                    h.Factuurnummer!, h.Factuurdatum!.Value, h.Factuurdatum.Value.AddDays(14), "#leverancier #maand",
                    [new Factuurregel("", "4300", "I21", h.TotaalExclusief!.Value, h.TotaalBtw)]);
                var geboekt = await inkoop.Boek(wie, id, document, invoer);
                Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
            }
        }

        Meld($"3. {Leveranciers.Length * 2} facturen zelf boeken", klok, Leveranciers.Length * 2);

        // 4. Opnieuw herkennen: de rest gaat vanzelf.
        klok.Restart();
        var standen = new List<(int Gedaan, int Totaal)>();
        var opnieuw = await inkoop.VerwerkOpnieuw(wie, id, new SynchroneVoortgang(standen.Add));
        Meld($"4. opnieuw herkennen ({opnieuw.Bekeken} bekeken, {opnieuw.Geboekt} geboekt)", klok, Math.Max(1, opnieuw.Bekeken));
        var rest = await inkoop.Lijst(wie, id, Documentstatus.TeVerwerken);
        uitvoer.WriteLine($"   nog te verwerken: {rest.Count}{(rest.Count > 0 ? $" — bijv. {rest[0].Melding}" : null)}");

        // 5. Overzichten met alle boekingen.
        klok.Restart();
        var proef = await grootboek.Proefbalans(wie, id, 2026);
        Meld("5. proefbalans", klok);
        klok.Restart();
        await grootboek.Kolommenbalans(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Meld("   kolommenbalans", klok);
        klok.Restart();
        var kaart = await grootboek.Kaart(wie, id, "4300", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Meld("   grootboekkaart 4300", klok);
        klok.Restart();
        var alle = await inkoop.Lijst(wie, id);
        Meld($"   inkooplijst alles ({alle.Count})", klok);
        klok.Restart();
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Meld("   hash-keten controleren", klok);
        var posten = await grootboek.Posten(wie, id, "INK");
        uitvoer.WriteLine($"   {posten.Count} inkoopboekingen; saldo 4300 {proef.Regels.Single(r => r.Rekening.Nummer == "4300").Saldo:N2}");

        Assert.Equal(aantal - rest.Count, posten.Count);
        Assert.Equal((opnieuw.Bekeken - 1, opnieuw.Bekeken), standen[^1]);
        Assert.True(rest.Count < aantal / 10, $"Te veel bleef liggen: {rest.Count} van {aantal}.");
        Assert.NotNull(kaart);
    }

    [StressFact]
    public async Task Vier_gebruikers_lezen_tegelijk_in_en_de_keten_blijft_heel()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        var inkoop = app.Dienst<InkoopDienst>();

        // Eerst leren: per leverancier twee facturen zelf boeken; daarna boekt het pakket de rest bij het inlezen.
        for (var l = 0; l < 4; l++)
        {
            for (var maand = 1; maand <= 2; maand++)
            {
                var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Factuur(l, maand)), $"{l}-{maand}.pdf", "application/pdf");
                var verwerking = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!;
                var h = verwerking.Herkend!;
                var code = verwerking.Voorstel?.Leverancier?.Relatie.Code;
                Assert.True((await inkoop.Boek(wie, id, gelezen.DocumentId.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(h.Naam!, h.Iban, null, h.KvkNummer) : null,
                    h.Factuurnummer!, h.Factuurdatum!.Value, h.Factuurdatum.Value.AddDays(14), "x", [new Factuurregel("", "4300", "I21", h.TotaalExclusief!.Value, h.TotaalBtw)]))).IsGelukt);
            }
        }

        var klok = Stopwatch.StartNew();
        var fouten = new System.Collections.Concurrent.ConcurrentBag<string>();
        var uitkomsten = new System.Collections.Concurrent.ConcurrentBag<Inleesuitkomst>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(l => Task.Run(async () =>
        {
            for (var maand = 3; maand <= 3 + 15; maand++)
            {
                try
                {
                    uitkomsten.Add(await inkoop.Lees(wie, id, new MemoryStream(Factuur(l, maand)), $"{l}-{maand}.pdf", "application/pdf"));
                }
                catch (Exception fout)
                {
                    fouten.Add($"{l}-{maand}: {fout.GetType().Name}: {fout.Message}");
                }
            }
        })));
        Meld($"4 × 16 facturen tegelijk inlezen", klok, 64);
        uitvoer.WriteLine($"   automatisch geboekt: {uitkomsten.Count(u => u.Status == Documentstatus.Geboekt)} van {uitkomsten.Count}; fouten: {fouten.Count}");
        foreach (var fout in fouten.Take(5))
        {
            uitvoer.WriteLine($"   {fout}");
        }

        Assert.Empty(fouten);
        Assert.Empty(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, id));
        var boekstukken = (await app.Dienst<GrootboekDienst>().Posten(wie, id, "INK")).Select(p => p.Post.Boekstuk).ToList();
        Assert.Equal(boekstukken.Count, boekstukken.Distinct().Count());
    }
}

/// <summary>Voortgang zonder synchronisatiecontext: meteen doorgeven (Progress&lt;T&gt; zou later melden).</summary>
internal sealed class SynchroneVoortgang(Action<(int Gedaan, int Totaal)> melding) : IProgress<(int Gedaan, int Totaal)>
{
    public void Report((int Gedaan, int Totaal) value) => melding(value);
}
