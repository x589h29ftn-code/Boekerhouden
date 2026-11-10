using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Documenten;
using Boekerhouden.Documenten.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Het administratiepakket met een nagebootste tekstherkenning: de n-de afbeelding geeft de n-de tekst.</summary>
public sealed class StapelFixture : AdministratieFixture
{
    public NepOcr Ocr { get; } = new();

    public NepScanner Scanner { get; } = new();

    protected override void Diensten(IServiceCollection diensten)
    {
        // De echte tekstherkenning (als die er is) eruit: de dienst neemt de eerste motor.
        foreach (var echt in diensten.Where(d => d.ServiceType == typeof(IOcrMotor)).ToList())
        {
            diensten.Remove(echt);
        }

        diensten.AddSingleton<IOcrMotor>(Ocr);
        diensten.AddSingleton<IVirusscanner>(Scanner);
    }

    /// <summary>Een virusscanner die op commando onbereikbaar is vanaf het n-de bestand.</summary>
    public sealed class NepScanner : IVirusscanner
    {
        /// <summary>Zoveel bestanden gaan nog goed; daarna is de scanner onbereikbaar. Leeg = altijd bereikbaar.</summary>
        public int? NogGoed { get; set; }

        public Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default)
        {
            if (NogGoed is { } n)
            {
                NogGoed = n - 1;
                if (n <= 0)
                {
                    return Task.FromResult(new Scanresultaat(Scanuitslag.NietGescand, "De virusscanner is niet bereikbaar."));
                }
            }

            return Task.FromResult(new Scanresultaat(Scanuitslag.Schoon, null));
        }
    }

    public sealed class NepOcr : IOcrMotor
    {
        private int gelezen;

        /// <summary>Per afbeelding (in volgorde) de regels die de tekstherkenning "vindt".</summary>
        public List<string[]> Teksten { get; } = [];

        public int Gelezen => gelezen;

        public Task<OcrUitkomst> LeesAsync(byte[] afbeelding, CancellationToken annulering = default)
        {
            var index = Interlocked.Increment(ref gelezen) - 1;
            var regels = index < Teksten.Count ? Teksten[index] : [];
            var woorden = regels.SelectMany((regel, r) => regel.Split(' ').Select((w, i) => new OcrWoord(w, 50 + (i * 130), 60 + (r * 40), 120, 30, 95))).ToList();
            return Task.FromResult(new OcrUitkomst(1240, 1754, woorden));
        }
    }
}

/// <summary>
/// Een stapel splitsen: één PDF met veel facturen wordt met een voorstel in losse facturen geknipt en elk stuk gaat door
/// de gewone weg van inlezen bij inkoop of verkoop.
/// </summary>
public sealed class StapelTests(StapelFixture app) : IClassFixture<StapelFixture>
{
    private async Task<int> Aantal<T>(Guid id)
        where T : class, IVanAdministratie
    {
        await using var db = await app.Dienst<IDbContextFactory<AdministratieDbContext>>().CreateDbContextAsync();
        return await db.Set<T>().IgnoreQueryFilters().CountAsync(e => e.AdministratieId == id);
    }

    [Fact]
    public async Task Een_stapel_inkoopfacturen_wordt_volgens_het_voorstel_losse_facturen_bij_inkoop()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Inkoop B.V.");
        var dienst = app.Dienst<StapelDienst>();

        var pdf = FictieveStapel.Pdf();
        var gelezen = await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(pdf), "inkoop 2026.pdf");

        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        var stapel = (await dienst.Open(wie, id, gelezen.Waarde))!;
        Assert.Equal((FictieveStapel.AantalPaginas, Stapelstatus.Open, 0), (stapel.Kop.Paginas, stapel.Kop.Status, stapel.Kop.TeLezen));
        Assert.Equal(FictieveStapel.Beginpaginas, stapel.Paginas.Where(p => p.Begin && !p.Weg).Select(p => p.Nummer));
        Assert.Equal([FictieveStapel.LegePagina], stapel.Paginas.Where(p => p.Weg).Select(p => p.Nummer));
        Assert.Equal(FictieveStapel.Inkoop.Select(f => f.Totaalpaginas), stapel.Groepen.Select(g => g.Count));
        Assert.Single(await dienst.Lijst(wie, id, Factuursoort.Inkoop));
        Assert.Empty(await dienst.Lijst(wie, id, Factuursoort.Verkoop));
        Assert.Equal(pdf, await dienst.Inhoud(wie, id, gelezen.Waarde));

        var verslag = await dienst.Verwerk(wie, id, gelezen.Waarde);

        Assert.True(verslag.IsGelukt, string.Join("; ", verslag.Fouten));
        Assert.Equal((FictieveStapel.Inkoop.Length, 0, 0), (verslag.Waarde!.Facturen, verslag.Waarde.Dubbel, verslag.Waarde.Mislukt));
        var documenten = await app.Dienst<InkoopDienst>().Lijst(wie, id);
        Assert.Equal(FictieveStapel.Inkoop.Length, documenten.Count);
        Assert.Equal(FictieveStapel.Inkoop.Select(f => f.Nummer).Order(), documenten.Select(d => d.Factuurnummer).Order());
        Assert.Contains(documenten, d => d.Bestandsnaam == "inkoop 2026 p. 2-4.pdf" && d.Leverancier == "Groothandel De Linde B.V." && d.Totaal == 1512.50m);
        Assert.Contains(documenten, d => d.Bestandsnaam == "inkoop 2026 p. 1.pdf");

        // De stapel blijft bewaard maar is klaar; nog een keer splitsen of dezelfde stapel opnieuw aanleveren kan niet.
        var klaar = (await dienst.Open(wie, id, gelezen.Waarde))!.Kop;
        Assert.Equal((Stapelstatus.Verwerkt, FictieveStapel.Inkoop.Length), (klaar.Status, klaar.Facturen));
        Assert.Empty(await dienst.Lijst(wie, id, Factuursoort.Inkoop));
        Assert.False((await dienst.Verwerk(wie, id, gelezen.Waarde)).IsGelukt);
        Assert.Contains("al gesplitst", (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(pdf), "inkoop 2026.pdf")).Fouten[0], StringComparison.Ordinal);
        Assert.False((await dienst.Verwijder(wie, id, gelezen.Waarde)).IsGelukt);
        var log = await app.Dienst<AuditDienst>().Recent();
        Assert.Contains(log, r => r.Actie == AuditActies.StapelIngelezen && r.Onderwerp == "inkoop 2026.pdf");
        Assert.Contains(log, r => r.Actie == AuditActies.StapelGesplitst && r.Details!.StartsWith($"inkoop: {FictieveStapel.Inkoop.Length} facturen uit {FictieveStapel.AantalPaginas} pagina's, 1 weggelaten", StringComparison.Ordinal));
    }

    [Fact]
    public async Task De_gebruiker_past_de_knippen_aan_en_die_bepalen_de_facturen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Knippen B.V.");
        var dienst = app.Dienst<StapelDienst>();
        var stapel = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(FictieveStapel.Pdf()), "stapel.pdf")).Waarde;

        // Alleen de eerste twee facturen: de rest weglaten, en de factuur van drie pagina's na de eerste pagina doorknippen.
        var weg = Enumerable.Range(5, FictieveStapel.AantalPaginas - 4).ToList();
        Assert.True((await dienst.Bewaar(wie, id, stapel, [1, 2, 3], weg)).IsGelukt);
        Assert.False((await dienst.Bewaar(wie, id, stapel, [999], [])).IsGelukt);

        var open = (await dienst.Open(wie, id, stapel))!;
        Assert.Equal([[1], [2], [3, 4]], open.Groepen);
        var verslag = await dienst.Verwerk(wie, id, stapel);

        Assert.Equal(3, verslag.Waarde!.Facturen);
        Assert.Equal(["stapel p. 1.pdf", "stapel p. 2.pdf", "stapel p. 3-4.pdf"], (await app.Dienst<InkoopDienst>().Lijst(wie, id)).Select(d => d.Bestandsnaam).Order());

        // Alles weglaten kan niet; een open stapel kan weg.
        var tweede = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(FictieveStapel.Pdf(FictieveStapel.Inkoop[..2])), "tweede.pdf")).Waarde;
        Assert.True((await dienst.Bewaar(wie, id, tweede, [1], [1, 2, 3, 4])).IsGelukt);
        Assert.Contains("geen pagina", (await dienst.Verwerk(wie, id, tweede)).Fouten[0], StringComparison.Ordinal);
        Assert.True((await dienst.Verwijder(wie, id, tweede)).IsGelukt);
        Assert.Null(await dienst.Open(wie, id, tweede));
        Assert.Equal(1, await Aantal<StapelInhoud>(id));
    }

    [Fact]
    public async Task Een_stapel_verkoopfacturen_komt_als_ingelezen_facturen_bij_verkoop()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Verkoop B.V.");
        var dienst = app.Dienst<StapelDienst>();

        var stapel = (await dienst.Lees(wie, id, Factuursoort.Verkoop, new MemoryStream(FictieveStapel.Pdf()), "verkoop.pdf")).Waarde;
        var verslag = await dienst.Verwerk(wie, id, stapel);

        Assert.True(verslag.IsGelukt, string.Join("; ", verslag.Fouten));
        Assert.Equal((FictieveStapel.Inkoop.Length, 0), (verslag.Waarde!.Facturen, verslag.Waarde.Mislukt));
        Assert.Equal(FictieveStapel.Inkoop.Length, await Aantal<Verkoopdocument>(id));
        Assert.Equal(FictieveStapel.Inkoop.Length, await Aantal<Verkoopbestand>(id));
        Assert.Equal(0, await Aantal<Document>(id));
    }

    [Fact]
    public async Task Een_gescande_stapel_wordt_een_keer_gelezen_en_de_tekst_gaat_mee_naar_de_facturen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Scan B.V.");
        var dienst = app.Dienst<StapelDienst>();
        var bouwer = new PdfDocumentBuilder();
        for (var i = 0; i < 3; i++)
        {
            bouwer.AddPage(PageSize.A4).AddPng(Scan(), new PdfRectangle(0, 0, 595, 842));
        }

        var stapel = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(bouwer.Build()), "scan.pdf")).Waarde;

        // Zonder tekst: elke pagina een eigen factuur, met twijfel.
        var voor = (await dienst.Open(wie, id, stapel))!;
        Assert.Equal((3, true), (voor.Kop.TeLezen, voor.Tekstherkenning));
        Assert.Equal([true, true, true], voor.Paginas.Select(p => p.Begin));
        Assert.Equal([false, true, true], voor.Paginas.Select(p => p.Twijfel));

        var begin = app.Ocr.Gelezen;
        app.Ocr.Teksten.AddRange(Enumerable.Repeat(Array.Empty<string>(), begin));
        app.Ocr.Teksten.Add(["Drukkerij Inkt V.O.F.", "IBAN NL44RABO0123456789", "Factuur", "Factuurnummer: SC-1", "Factuurdatum: 01-03-2026", "Drukwerk 100,00", "Pagina 1 van 2"]);
        app.Ocr.Teksten.Add(["Drukkerij Inkt V.O.F.", "Factuurnummer: SC-1", "Subtotaal 100,00", "BTW 21% 21,00", "Totaal te betalen 121,00", "Pagina 2 van 2"]);
        app.Ocr.Teksten.Add(["Kantoorhal B.V.", "IBAN NL86INGB0002445588", "Factuur", "Factuurnummer: SC-2", "Factuurdatum: 02-03-2026", "Subtotaal 50,00", "BTW 21% 10,50", "Totaal te betalen 60,50"]);
        var voortgang = new List<(int, int)>();

        Assert.True((await dienst.LeesScans(wie, id, stapel, new Meld(voortgang))).IsGelukt);

        Assert.Equal([(1, 3), (2, 3), (3, 3)], voortgang);
        var na = (await dienst.Open(wie, id, stapel))!;
        Assert.Equal(0, na.Kop.TeLezen);
        Assert.True(na.Paginas.Select(p => p.Begin).SequenceEqual([true, false, true]), string.Join(" | ", na.Paginas.Select(p => $"{p.Nummer}:{p.Begin}:{p.Leeg}:{p.Reden}:{p.Factuurnummer}")));
        Assert.Equal(["SC-1", "SC-1", "SC-2"], na.Paginas.Select(p => p.Factuurnummer));
        Assert.All(na.Paginas, p => Assert.False(p.Twijfel));

        // Bij het splitsen wordt niet opnieuw gelezen: de tekst gaat mee.
        var verslag = await dienst.Verwerk(wie, id, stapel);

        Assert.Equal(2, verslag.Waarde!.Facturen);
        Assert.Equal(begin + 3, app.Ocr.Gelezen);
        var documenten = await app.Dienst<InkoopDienst>().Lijst(wie, id);
        Assert.Equal([("SC-1", 121.00m), ("SC-2", 60.50m)], documenten.OrderBy(d => d.Factuurnummer).Select(d => (d.Factuurnummer!, d.Totaal!.Value)));
    }

    [Fact]
    public async Task Knippen_van_de_gebruiker_blijven_staan_als_de_tekstherkenning_later_klaar_is()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Eigen Knippen B.V.");
        var dienst = app.Dienst<StapelDienst>();
        var bouwer = new PdfDocumentBuilder();
        for (var i = 0; i < 2; i++)
        {
            bouwer.AddPage(PageSize.A4).AddPng(Scan(), new PdfRectangle(0, 0, 595, 842));
        }

        var stapel = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(bouwer.Build()), "eigen.pdf")).Waarde;
        Assert.True((await dienst.Bewaar(wie, id, stapel, [1], [])).IsGelukt);

        Assert.True((await dienst.LeesScans(wie, id, stapel)).IsGelukt);

        Assert.Equal([true, false], (await dienst.Open(wie, id, stapel))!.Paginas.Select(p => p.Begin));
    }

    [Fact]
    public async Task Valt_de_virusscanner_halverwege_weg_dan_blijft_de_stapel_open_en_gaat_de_rest_de_volgende_keer()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Halverwege B.V.");
        var dienst = app.Dienst<StapelDienst>();
        var stapel = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(FictieveStapel.Pdf()), "half.pdf")).Waarde;

        app.Scanner.NogGoed = 2;
        Stapelverslag eerste;
        try
        {
            eerste = (await dienst.Verwerk(wie, id, stapel)).Waarde!;
        }
        finally
        {
            app.Scanner.NogGoed = null;
        }

        Assert.Equal((FictieveStapel.Inkoop.Length, FictieveStapel.Inkoop.Length - 2), (eerste.Facturen, eerste.Mislukt));
        Assert.Equal(Stapelstatus.Open, (await dienst.Open(wie, id, stapel))!.Kop.Status);
        Assert.Equal(2, (await app.Dienst<InkoopDienst>().Lijst(wie, id)).Count);

        var tweede = (await dienst.Verwerk(wie, id, stapel)).Waarde!;

        Assert.Equal((0, 0), (tweede.Mislukt, tweede.Dubbel));
        Assert.Equal(Stapelstatus.Verwerkt, (await dienst.Open(wie, id, stapel))!.Kop.Status);
        Assert.Equal(FictieveStapel.Inkoop.Length, (await app.Dienst<InkoopDienst>().Lijst(wie, id)).Count);
    }

    [Fact]
    public async Task Alleen_wie_mag_invoeren_splitst_een_stapel_en_een_verkeerd_bestand_wordt_geweigerd()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Stapel Rechten B.V.");
        var dienst = app.Dienst<StapelDienst>();
        var inzien = await app.Gebruiker("Ilse Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var buiten = await app.Gebruiker("Bas Buiten");
        var stapel = (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(FictieveStapel.Pdf()), "rechten.pdf")).Waarde;

        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees(inzien, id, Factuursoort.Inkoop, new MemoryStream(FictieveStapel.Pdf()), "x.pdf"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Bewaar(inzien, id, stapel, [1], []));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwerk(inzien, id, stapel));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(inzien, id, stapel));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.LeesScans(inzien, id, stapel));
        Assert.NotNull(await dienst.Open(inzien, id, stapel));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Open(buiten, id, stapel));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Inhoud(buiten, id, stapel));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lijst(buiten, id, Factuursoort.Inkoop));

        // Een stapel van een andere administratie is niet te openen via deze.
        var ander = await app.Administratie("Stapel Ander B.V.");
        Assert.Null(await dienst.Open(wie, ander, stapel));
        Assert.Null(await dienst.Inhoud(wie, ander, stapel));
        Assert.False((await dienst.Verwerk(wie, ander, stapel)).IsGelukt);

        Assert.Contains("geen PDF", (await dienst.Lees(wie, id, Factuursoort.Inkoop, new MemoryStream("gewoon tekst"u8.ToArray()), "tekst.pdf")).Fouten[0], StringComparison.Ordinal);
    }

    private static byte[] Scan() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "scan-kantoorvakhandel.png"));

    private sealed class Meld(List<(int, int)> lijst) : IProgress<(int Gedaan, int Totaal)>
    {
        public void Report((int Gedaan, int Totaal) value) => lijst.Add(value);
    }
}
