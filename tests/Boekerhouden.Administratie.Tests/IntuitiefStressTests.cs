using System.Diagnostics;
using System.Text;
using Boekerhouden.Administratie.Components.Gedeeld;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Tests.Praktijk;
using Boekerhouden.Documenten;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Stresstest van de onderdelen die de gebruiker bij de hand nemen: overal slepen (herkennen en inlezen) op alle
/// documenten van de praktijkset, op rommel en kwaadaardige bestanden en met veel bestanden tegelijk; en Te doen, Aan
/// de slag, periode afsluiten en zoeken met acties op een volle administratie.
/// </summary>
public sealed class IntuitiefStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static readonly Eigengegevens Eigen = new(Praktijkset.Eigen.Naam, Praktijkset.Eigen.Kvk, Praktijkset.Eigen.BtwNummer,
        [Praktijkset.Eigen.Iban, Praktijkset.SpaarIban]);

    private async Task<Guid> Inrichten(Uitvoerder wie)
    {
        var id = await app.Administratie(Praktijkset.Eigen.Naam);
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [e.Iban, Praktijkset.SpaarIban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.WijzigBank(wie, id, "BNK", "Betaalrekening", e.Iban, "1100")).IsGelukt);
        Assert.True((await bank.VoegBankToe(wie, id, "SPA", "Spaarrekening", Praktijkset.SpaarIban, "1110")).IsGelukt);
        return id;
    }

    [Fact]
    public void Herkent_alle_documenten_van_de_praktijkset()
    {
        var bestanden = Praktijkset.Maak().Bestanden();
        var fout = new List<string>();
        var klok = Stopwatch.StartNew();
        foreach (var b in bestanden)
        {
            var verwacht = b.Map switch { "bank" => Inleessoort.Bank, "verkoop" => Inleessoort.Verkoop, _ => Inleessoort.Inkoop };
            var soort = InleesDienst.Herken(b.Naam, b.Inhoud, Eigen, PdfTekst.Woorden);
            if (soort != verwacht)
            {
                fout.Add($"{b.Map}/{b.Naam}: {soort}");
            }
        }

        uitvoer.WriteLine($"{bestanden.Count} bestanden in {klok.ElapsedMilliseconds} ms, {fout.Count} anders herkend:");
        fout.ForEach(uitvoer.WriteLine);
        Assert.Empty(fout);
    }

    [Fact]
    public async Task Rommel_en_kwaadaardige_bestanden_geven_een_melding_en_geen_fout()
    {
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var inlezen = app.Dienst<InleesDienst>();
        var willekeurig = new byte[200_000];
        new Random(7).NextBytes(willekeurig);
        var lachen = """
            <?xml version="1.0"?>
            <!DOCTYPE lol [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
            <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">]>
            <Invoice><cac:AccountingSupplierParty xmlns:cac="x">&lol3;</cac:AccountingSupplierParty></Invoice>
            """;
        var gevallen = new (string Naam, byte[] Inhoud)[]
        {
            ("leeg.pdf", []),
            ("leeg.xml", []),
            ("willekeurig.pdf", willekeurig),
            ("kapot.pdf", [.. "%PDF-1.7\n"u8.ToArray(), .. willekeurig.Take(5000)]),
            ("miljard-lachen.xml", Encoding.UTF8.GetBytes(lachen)),
            ("half.xml", "<Invoice><cac:AccountingSupplierParty"u8.ToArray()),
            ("afgekapt.mt940", ":20:X\n:25:NL20INGB0001234567\n:28C:1\n:60F:C260101EUR1"u8.ToArray()),
            ("geen-idee.docx", willekeurig.Take(10_000).ToArray()),
            ("groot.txt", Encoding.UTF8.GetBytes(new string('a', 5_000_000))),
            ("naam met ../pad.pdf", [.. "%PDF-1.4\n"u8.ToArray()]),
        };

        var klok = Stopwatch.StartNew();
        foreach (var (naam, inhoud) in gevallen)
        {
            var uitkomst = await inlezen.Lees(wie, id, inhoud, naam, null);
            uitvoer.WriteLine($"{naam}: {uitkomst.Soort}, {(uitkomst.Gelukt ? "ingelezen" : "niet")}{(uitkomst.Nakijken ? ", nakijken" : null)}: {uitkomst.Melding}");
            Assert.False(string.IsNullOrWhiteSpace(uitkomst.Melding));

            // Rommel is nooit een herkende factuur: niet ingelezen, of ingelezen om na te kijken.
            Assert.True(!uitkomst.Gelukt || uitkomst.Nakijken, $"{naam} telt als gewone factuur: {uitkomst.Melding}");
        }

        uitvoer.WriteLine($"{gevallen.Length} gevallen in {klok.ElapsedMilliseconds} ms");
        Assert.True(klok.ElapsedMilliseconds < 60_000);

        // Een afschrift dat niet bij deze administratie hoort, en dezelfde factuur twee keer.
        var vreemd = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "abn-2026-03.sta"));
        Assert.False((await inlezen.Lees(wie, id, vreemd, "abn-2026-03.sta", null)).Gelukt);
        var factuur = Praktijkset.Maak().Bestanden().First(b => b.Map == "inkoop" && b.Naam.EndsWith(".pdf", StringComparison.Ordinal));
        Assert.True((await inlezen.Lees(wie, id, factuur.Inhoud, factuur.Naam, null)).Gelukt);
        var nogeens = await inlezen.Lees(wie, id, factuur.Inhoud, factuur.Naam, null);
        Assert.Equal((Inleessoort.Inkoop, false), (nogeens.Soort, nogeens.Gelukt));
        uitvoer.WriteLine($"dubbel: {nogeens.Melding}");
    }

    [Fact]
    public async Task Veel_documenten_tegelijk_en_daarna_de_nieuwe_onderdelen_op_een_volle_administratie()
    {
        var wie = await app.Beheerder();
        var id = await Inrichten(wie);
        var inlezen = app.Dienst<InleesDienst>();
        var bestanden = Praktijkset.Maak().Bestanden();

        // Alles door elkaar, acht tegelijk, zoals een map die op de pagina wordt losgelaten.
        var klok = Stopwatch.StartNew();
        var uitkomsten = new List<Overalingelezen>();
        foreach (var groep in bestanden.Chunk(8))
        {
            uitkomsten.AddRange(await Task.WhenAll(groep.Select(b => inlezen.Lees(wie, id, b.Inhoud, b.Naam, null))));
        }

        var duur = klok.ElapsedMilliseconds;
        uitvoer.WriteLine($"{bestanden.Count} bestanden in {duur} ms ({duur / bestanden.Count} ms per bestand)");
        uitvoer.WriteLine(Overalinlezen.Samenvatting(uitkomsten));
        foreach (var soort in Enum.GetValues<Inleessoort>())
        {
            uitvoer.WriteLine($"{soort}: {uitkomsten.Count(u => u.Soort == soort)} ({uitkomsten.Count(u => u.Soort == soort && u.Gelukt)} ingelezen)");
        }

        Assert.Equal(bestanden.Count(b => b.Map == "bank"), uitkomsten.Count(u => u.Soort == Inleessoort.Bank));

        // Tegelijk ingelezen afschriften die elkaar overlappen: geen fout, de overlap telt als dubbel. Het bankboek
        // heeft precies de mutaties van de praktijkset (elk één keer).
        Assert.All(uitkomsten.Where(u => u.Soort == Inleessoort.Bank), u => Assert.True(u.Gelukt, u.Melding));
        var bank = app.Dienst<BankDienst>();
        var naElkaar = await Inrichten(wie);
        foreach (var b in bestanden.Where(b => b.Map == "bank"))
        {
            Assert.True((await bank.Lees(wie, naElkaar, new MemoryStream(b.Inhoud), b.Naam)).IsGelukt);
        }

        var parallel = (await bank.Lijst(wie, id)).Select(m => (m.Datum, m.Bedrag, m.Omschrijving)).Order().ToList();
        var sequentieel = (await bank.Lijst(wie, naElkaar)).Select(m => (m.Datum, m.Bedrag, m.Omschrijving)).Order().ToList();
        uitvoer.WriteLine($"bankmutaties: tegelijk {parallel.Count}, na elkaar {sequentieel.Count}");
        Assert.Equal(sequentieel, parallel);
        Assert.Equal(bestanden.Count(b => b.Map == "verkoop"), uitkomsten.Count(u => u.Soort == Inleessoort.Verkoop));
        Assert.All(uitkomsten.Where(u => u.Soort == Inleessoort.Verkoop), u => Assert.True(u.Gelukt, u.Melding));

        // De nieuwe onderdelen op deze volle administratie: snel genoeg om op elke pagina te laden.
        async Task<T> Meet<T>(string wat, Func<Task<T>> doe, int grens)
        {
            var k = Stopwatch.StartNew();
            var uit = await doe();
            uitvoer.WriteLine($"{wat}: {k.ElapsedMilliseconds} ms");
            Assert.True(k.ElapsedMilliseconds < grens, $"{wat} duurde {k.ElapsedMilliseconds} ms");
            return uit;
        }

        var aantallen = await Meet("aantallen zijbalk", () => app.Dienst<KantooroverzichtDienst>().Aantallen(wie, id), 2000);
        uitvoer.WriteLine($"te verwerken: inkoop {aantallen.Inkoop}, verkoop {aantallen.Verkoop}, bank {aantallen.Bank}");
        Assert.True(aantallen.Bank > 0);
        var stappen = await Meet("afsluitstappen maart", () => app.Dienst<AfsluitDienst>().Stappen(wie, id, new DateOnly(2026, 3, 31)), 5000);
        stappen.ToList().ForEach(s => uitvoer.WriteLine($"  {(s.Klaar ? "x" : " ")} {s.Naam} {s.Nog}"));
        Assert.False(stappen.Single(s => s.Naam.StartsWith("Bank", StringComparison.Ordinal)).Klaar);
        var start = await Meet("aan de slag", () => app.Dienst<DashboardDienst>().Startstappen(wie, id), 2000);
        uitvoer.WriteLine($"aan de slag: {start.Count} stappen");
        var zoeken = app.Dienst<ZoekDienst>();
        foreach (var tekst in new[] { "afschrift", "btw kwartaal", "nieuwe factuur", "periode", new string('x', 10_000), "'; drop table boekingen; --", "%_\\", "ÿéèçøæ 日本 🚲", "   ", "a" })
        {
            var gevonden = await Meet($"zoeken '{(tekst.Length > 20 ? tekst[..20] + "…" : tekst)}'", () => zoeken.Zoek(wie, tekst, id), 3000);
            Assert.NotNull(gevonden);
        }

        Assert.Equal("Bankafschrift inlezen", (await zoeken.Zoek(wie, "afschrift", id))[0].Titel);
        Assert.NotNull(VolgendeTaak.Volgende(aantallen, "/inkoop"));
    }
}
