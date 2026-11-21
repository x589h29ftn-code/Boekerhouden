using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Elke weg om een bestand in te lezen krijgt dezelfde rotte bestanden: leeg, één byte, willekeurige bytes, een PDF die
/// halverwege ophoudt, XML met een DTD (entiteitenbom), heel diep geneste XML, één heel lange regel, UTF-16, een
/// zip, een afgebroken afschrift en een afgebroken e-factuur. Geen enkele mag het pakket laten vastlopen of een
/// onverwachte fout geven; er mag niets geboekt worden en de administratie moet daarna nog kloppen.
/// </summary>
public sealed class InleesRobuustheidTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private static IEnumerable<(string Naam, string Bestand, byte[] Inhoud)> Rotte()
    {
        var toeval = new Random(17);
        byte[] Willekeurig(int n)
        {
            var b = new byte[n];
            toeval.NextBytes(b);
            return b;
        }

        var bom = """<?xml version="1.0"?><!DOCTYPE l [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"><!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;"><!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;"><!ENTITY e "&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;"><!ENTITY f "&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;">]><Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">&f;</Invoice>""";
        var diep = new StringBuilder("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\">");
        for (var i = 0; i < 20_000; i++)
        {
            diep.Append("<a>");
        }

        var mt940 = ":20:AF1\r\n:25:NL20INGB0001234567\r\n:28C:1\r\n:60F:C250101EUR1000,00\r\n:61:2501020102D50,00NTRFNONREF\r\n:86:Afgebroken";
        var ubl = """<?xml version="1.0"?><Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"><cbc:ID>F-1</cbc:ID><cbc:IssueDate>2026-13-45</cbc:IssueDate><cac:Leg""";
        yield return ("leeg", "leeg.pdf", []);
        yield return ("één byte", "a.pdf", [0x25]);
        yield return ("willekeurig", "rommel.pdf", Willekeurig(64 * 1024));
        yield return ("afgebroken pdf", "half.pdf", [.. "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj << /Type /Pa"u8.ToArray(), .. Willekeurig(500)]);
        yield return ("entiteitenbom", "bom.xml", Encoding.UTF8.GetBytes(bom));
        yield return ("diep genest", "diep.xml", Encoding.UTF8.GetBytes(diep.ToString()));
        yield return ("lange regel", "lang.mt940", Encoding.ASCII.GetBytes(new string('9', 3_000_000)));
        yield return ("utf-16", "tekst.xml", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<Invoice>geen ubl</Invoice>")).ToArray());
        yield return ("zip", "archief.xml", [.. "PK\x03\x04"u8.ToArray(), .. Willekeurig(2_000)]);
        yield return ("afgebroken afschrift", "afschrift.mt940", Encoding.Latin1.GetBytes(mt940));
        yield return ("afgebroken e-factuur", "factuur.xml", Encoding.UTF8.GetBytes(ubl));
        yield return ("png zonder beeld", "foto.png", [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. Willekeurig(1_000)]);
        yield return ("te groot", "groot.pdf", [.. "%PDF-1.4\n"u8.ToArray(), .. new byte[(21 * 1024 * 1024) - 9]]);
    }

    [Fact]
    public async Task Elke_inleesweg_weigert_rotte_bestanden_netjes_en_boekt_niets()
    {
        var w = await Werkadministratie.Maak(app, "Rotte Bestanden B.V.");
        var (wie, id) = (w.Wie, w.Id);
        var grootboek = app.Dienst<GrootboekDienst>();
        var voor = (await grootboek.Posten(wie, id)).Count;
        var fouten = new List<string>();
        var wegen = new (string Naam, Func<byte[], string, Task<string>> Lees)[]
        {
            ("inkoop", async (b, n) => (await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(b), n, null)).Melding),
            ("verkoop inlezen", async (b, n) => (await app.Dienst<VerkoopimportDienst>().Lees(wie, id, new MemoryStream(b), n, null, null)).Melding),
            ("bank", async (b, n) => Tekst(await app.Dienst<BankDienst>().Lees(wie, id, new MemoryStream(b), n))),
            ("loonjournaal", async (b, n) => Tekst(await app.Dienst<LoonjournaalDienst>().Lees(wie, id, new MemoryStream(b), n))),
            ("beginbalans", async (b, n) => Tekst(await app.Dienst<BeginbalansDienst>().Lees(wie, id, new MemoryStream(b), n))),
            ("activastaat", async (b, n) => Tekst(await app.Dienst<ActivastaatDienst>().Lees(wie, id, new MemoryStream(b), n))),
            ("stapel", async (b, n) => Tekst(await app.Dienst<StapelDienst>().Lees(wie, id, Factuursoort.Inkoop, new MemoryStream(b), n))),
            ("overal slepen", async (b, n) => (await app.Dienst<InleesDienst>().Lees(wie, id, b, n, null)).Melding),
            ("auditfile", async (b, n) => Tekst(await app.Dienst<AuditfileDienst>().LeesIn(wie, new MemoryStream(b), Path.ChangeExtension(n, ".xaf"), Ondernemingsvorm.BV))),
        };

        foreach (var (soort, bestand, inhoud) in Rotte())
        {
            foreach (var (weg, lees) in wegen)
            {
                var klok = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var melding = await lees(inhoud, bestand).WaitAsync(stop.Token);
                    uitvoer.WriteLine($"{weg,-16} {soort,-22} {klok.ElapsedMilliseconds,6} ms  {Kort(melding)}");
                    if (klok.Elapsed > TimeSpan.FromSeconds(20))
                    {
                        fouten.Add($"{weg} met {soort}: duurde {klok.Elapsed.TotalSeconds:0} s.");
                    }
                }
                catch (Exception fout) when (fout is not OutOfMemoryException)
                {
                    fouten.Add($"{weg} met {soort}: {fout.GetType().Name}: {Kort(fout.Message)}");
                }
            }
        }

        // Een leeg bestand wordt nergens bewaard (niets om na te kijken).
        if ((await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream([]), "nog-eens-leeg.pdf", null)).DocumentId is not null)
        {
            fouten.Add("Inkoop bewaart een leeg bestand.");
        }

        if ((await app.Dienst<VerkoopimportDienst>().Lees(wie, id, new MemoryStream([]), "nog-eens-leeg.pdf", null, null)).Id is not null)
        {
            fouten.Add("Verkoop inlezen bewaart een leeg bestand.");
        }

        // Niets geboekt (alleen de beginbalans van de werkadministratie), en een auditfile werd geen administratie.
        var na = (await grootboek.Posten(wie, id)).Count;
        if (na != voor)
        {
            fouten.Add($"Er is geboekt: {na - voor} boekingen erbij.");
        }

        fouten.AddRange(await Invarianten.Controleer(app, wie, id, "na de rotte bestanden"));
        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }

    private static string Tekst<T>(Uitkomst<T> uitkomst) => uitkomst.IsGelukt ? "gelukt" : string.Join(" ", uitkomst.Fouten);

    private static string Kort(string tekst) => tekst.Length > 110 ? tekst[..110] + "…" : tekst;
}
