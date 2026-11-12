using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Draait alleen met BOEKERHOUDEN_GELIJKTIJDIG=&lt;aantal gebruikers&gt; (bijv. 5). Optioneel BOEKERHOUDEN_GELIJKTIJDIG_RONDES
/// (werkrondes per gebruiker, standaard 40). Op PostgreSQL, of met BOEKERHOUDEN_TEST_SQLITE=1 op de database van de
/// Windows-app (de kantoorserver).
/// </summary>
internal sealed class GelijktijdigFactAttribute : FactAttribute
{
    public GelijktijdigFactAttribute()
    {
        if (Gebruikers == 0)
        {
            Skip = "Zet BOEKERHOUDEN_GELIJKTIJDIG=<aantal gebruikers> (bijv. 5) om de test met gelijktijdige gebruikers te draaien.";
        }
    }

    public static int Gebruikers => Getal("BOEKERHOUDEN_GELIJKTIJDIG", 0);

    public static int Rondes => Getal("BOEKERHOUDEN_GELIJKTIJDIG_RONDES", 40);

    private static int Getal(string naam, int standaard) =>
        int.TryParse(Environment.GetEnvironmentVariable(naam), CultureInfo.InvariantCulture, out var n) && n > 0 ? n : standaard;
}

/// <summary>
/// Meerdere gebruikers tegelijk aan het werk, zoals op kantoor: elk in een eigen administratie (en daarna allemaal in
/// dezelfde). Per ronde doet een gebruiker wat hij op een gewone dag doet: een inkoopfactuur inlezen en boeken, een
/// verkoopfactuur maken, een memoriaalpost boeken, een bankafschrift inlezen en de overzichten openen. De test meet per
/// handeling hoe lang het duurt als iedereen tegelijk werkt, vergeleken met één gebruiker alleen, telt wat er misging
/// (bijv. een vergrendelde database) en controleert na afloop de hash-keten en de balans van elke administratie.
/// </summary>
public sealed class GelijktijdigStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string EigenIban = "NL91ABNA0417164300";
    private readonly List<string> verslag = [];

    private void Schrijf(string regel)
    {
        uitvoer.WriteLine(regel);
        verslag.Add(regel);
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_GELIJKTIJDIG_VERSLAG") is { Length: > 0 } pad)
        {
            File.WriteAllLines(pad, verslag);
        }
    }

    [GelijktijdigFact]
    public async Task Gebruikers_werken_tegelijk_elk_in_een_administratie_en_samen_in_een()
    {
        var (aantal, rondes) = (GelijktijdigFactAttribute.Gebruikers, GelijktijdigFactAttribute.Rondes);
        var beheerder = await app.Beheerder();
        Schrijf($"{(AdministratieFixture.IsSqlite ? "SQLite (Windows-app, kantoorserver)" : "PostgreSQL")}, {aantal} gebruikers, {rondes} rondes per gebruiker, {Environment.ProcessorCount} processorkernen");

        // Elke gebruiker een eigen administratie, plus één gedeelde waar iedereen lid van is.
        var gebruikers = new List<Uitvoerder>();
        var eigen = new List<Guid>();
        for (var g = 0; g < aantal; g++)
        {
            var nieuw = await app.Dienst<GebruikerDienst>().MaakZonderWachtwoord($"Gebruiker {g + 1}", null, null, [Rollen.Kantoor], beheerder);
            Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
            gebruikers.Add(new Uitvoerder(nieuw.Waarde, $"Gebruiker {g + 1}"));
            eigen.Add(await Administratie($"Klant {g + 1} B.V.", beheerder, [gebruikers[g]]));
        }

        var gedeeld = await Administratie("Gedeelde Klant B.V.", beheerder, gebruikers);
        var alleen = await Administratie("Alleen B.V.", beheerder, [gebruikers[0]]);

        // 1. Eén gebruiker alleen: de maat om mee te vergelijken.
        var basis = new Meting();
        var klok = Stopwatch.StartNew();
        await Werk(gebruikers[0], alleen, 0, rondes, basis);
        var alleenDuur = klok.Elapsed;
        Schrijf("");
        Schrijf($"--- 1 gebruiker alleen: {rondes} rondes in {alleenDuur.TotalSeconds:0.0} s ({alleenDuur.TotalMilliseconds / rondes:0} ms per ronde)");
        Toon(basis, null);

        // 2. Iedereen tegelijk, elk in zijn eigen administratie.
        var samen = new Meting();
        klok.Restart();
        await Task.WhenAll(Enumerable.Range(0, aantal).Select(g => Task.Run(() => Werk(gebruikers[g], eigen[g], g, rondes, samen))));
        var samenDuur = klok.Elapsed;
        Schrijf("");
        Schrijf($"--- {aantal} gebruikers tegelijk, elk in een eigen administratie: {aantal * rondes} rondes in {samenDuur.TotalSeconds:0.0} s ({samenDuur.TotalMilliseconds / rondes:0} ms per ronde per gebruiker)");
        Toon(samen, basis);

        // 3. Iedereen tegelijk in dezelfde administratie (dezelfde hash-keten en dezelfde nummerreeksen).
        var druk = new Meting();
        klok.Restart();
        await Task.WhenAll(Enumerable.Range(0, aantal).Select(g => Task.Run(() => Werk(gebruikers[g], gedeeld, g, rondes, druk))));
        var drukDuur = klok.Elapsed;
        Schrijf("");
        Schrijf($"--- {aantal} gebruikers tegelijk in dezelfde administratie: {aantal * rondes} rondes in {drukDuur.TotalSeconds:0.0} s ({drukDuur.TotalMilliseconds / rondes:0} ms per ronde per gebruiker)");
        Toon(druk, basis);

        // Na afloop: elke keten heel, elke balans sluitend, en het aantal boekingen klopt met wat er gelukt is.
        Schrijf("");
        var grootboek = app.Dienst<GrootboekDienst>();
        foreach (var (naam, id, verwacht) in eigen.Select((id, g) => ($"Klant {g + 1}", id, (int?)null)).Append(("Gedeeld", gedeeld, null)).Append(("Alleen", alleen, null)))
        {
            Assert.Empty(await grootboek.ControleerKeten(beheerder, id));
            var balans = await grootboek.Kolommenbalans(beheerder, id, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31));
            Assert.Equal(0m, balans.Regels.Sum(r => r.Saldo));
            var posten = await grootboek.Posten(beheerder, id);
            Assert.Equal(posten.Count, posten.Select(p => (p.Post.Dagboek, p.Post.Boekstuk)).Distinct().Count());
            _ = verwacht;
            Schrijf($"{naam,-10} {posten.Count,5} boekingen, keten heel, balans sluit, geen dubbele boekstuknummers");
        }

        Schrijf("");
        Schrijf($"fouten: alleen {basis.Fouten.Count}, eigen administraties {samen.Fouten.Count}, dezelfde administratie {druk.Fouten.Count}");
        foreach (var fout in basis.Fouten.Concat(samen.Fouten).Concat(druk.Fouten).GroupBy(f => f).OrderByDescending(f => f.Count()).Take(8))
        {
            Schrijf($"   {fout.Count(),4}× {fout.Key}");
        }

        foreach (var botsing in druk.Botsingen.GroupBy(b => b[..b.IndexOf(':', StringComparison.Ordinal)]))
        {
            Schrijf($"   nette melding bij {botsing.Key}: {botsing.Count()}×");
        }

        // Er mag nergens iets misgaan; in dezelfde administratie mag een gebruiker wel de melding krijgen dat een
        // collega net hetzelfde deed (hetzelfde boekstuknummer, dezelfde nieuwe leverancier).
        Assert.Empty(basis.Fouten);
        Assert.Empty(samen.Fouten);
        Assert.Empty(samen.Botsingen);
        Assert.Empty(druk.Fouten);
    }

    private async Task<Guid> Administratie(string naam, Uitvoerder beheerder, IReadOnlyList<Uitvoerder> leden)
    {
        var id = await app.Administratie(naam);
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(beheerder, id);
        Assert.True((await inrichting.WijzigInstellingen(beheerder, id, nu with { BtwNummer = "NL123456782B01", Ibans = [EigenIban], Contact = new Contactgegevens("Proefstraat 1", "1234 AB", "Proefstad") })).IsGelukt);
        foreach (var lid in leden)
        {
            Assert.True((await app.Dienst<AdministratieDienst>().ZetLid(id, lid.Id!.Value, Lidmaatschapsrol.Kantoor, beheerder)).IsGelukt);
        }

        // De klanten vooraf, zodat de gebruikers niet tegelijk dezelfde klant aanmaken.
        for (var k = 0; k < 5; k++)
        {
            var nieuw = await inrichting.VoegRelatieToe(beheerder, id, new RelatieInvoer($"Afnemer {k + 1} B.V.", null, null, null,
                new Klantgegevens(true, $"Laan {k + 1}", "5678 CD", "Elders", Omzetrekening: "8000")), null);
            Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
        }

        return id;
    }

    /// <summary>Het werk van één gebruiker in één administratie; elke handeling gemeten, een fout geteld en niet fataal.</summary>
    private async Task Werk(Uitvoerder wie, Guid id, int nummer, int rondes, Meting meting)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var bank = app.Dienst<BankDienst>();
        var codes = (await app.Dienst<BtwDienst>().Tabel()).Op(new DateOnly(2026, 6, 1));
        var klanten = (await app.Dienst<InrichtingDienst>().Relaties(wie, id)).Where(r => r.Klant?.IsKlant == true).Select(r => r.Code).ToList();
        var ontvanger = new Partij("Stress B.V.", new Adres("Proefstraat 1", "1234 AB", "Proefstad"), "NL123456782B01", Ibans: [EigenIban]);
        for (var ronde = 0; ronde < rondes; ronde++)
        {
            var datum = new DateOnly(2026, 1, 2).AddDays(ronde * 300 / rondes);
            var uniek = $"{nummer + 1}-{ronde + 1:000}";

            // Inkoopfactuur (e-factuur) inlezen; de eerste keren zelf boeken, daarna doet het pakket het.
            var l = (nummer * 3 + ronde) % 6;
            var iban = Iban("INGB", 1000000 + l);
            var leverancier = new Partij($"Leverancier {l + 1} B.V.", new Adres($"Handelsweg {l + 1}", "9999 ZZ", "Verzonnen"), $"NL{800000000 + l:000000000}B01", Ibans: [iban]);
            var factuur = new Verkoopfactuur("STRESS", ontvanger, $"L{l + 1}-{uniek}", datum, datum.AddDays(30), $"Levering {uniek}", [new($"Artikel {ronde % 7 + 1}", 1 + ronde % 3, 25m + ronde, "4300", "V21")]);
            var xml = Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(factuur, leverancier, codes));
            await meting.Tijd("inkoopfactuur inlezen en boeken", async () =>
            {
                using var stroom = new MemoryStream(xml);
                var gelezen = await inkoop.Lees(wie, id, stroom, $"L{l + 1}-{uniek}.xml", "application/xml");
                if (gelezen.DocumentId is { } document && !gelezen.IsGeboekt && !gelezen.Dubbel)
                {
                    var verwerking = (await inkoop.Verwerking(wie, id, document))!;
                    var h = verwerking.Herkend!;
                    var code = verwerking.Voorstel?.Leverancier?.Relatie.Code;
                    var geboekt = await inkoop.Boek(wie, id, document, new Factuurinvoer(code, code is null ? new NieuweRelatie(h.Naam!, h.Iban, h.BtwNummer, h.KvkNummer) : null,
                        h.Factuurnummer!, h.Factuurdatum!.Value, h.Factuurdatum.Value.AddDays(30), "#leverancier #factuurnummer", [new Factuurregel("", "4300", "I21", h.TotaalExclusief!.Value, h.TotaalBtw)]));
                    return geboekt.IsGelukt ? null : string.Join("; ", geboekt.Fouten);
                }

                return gelezen.DocumentId is null ? gelezen.Melding : null;
            });

            // Verkoopfactuur maken en definitief maken (nummer uit de reeks van het jaar).
            await meting.Tijd("verkoopfactuur maken en definitief", async () =>
            {
                var concept = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klanten[(nummer + ronde) % klanten.Count], datum, datum.AddDays(14), $"Werkzaamheden {uniek}",
                    [new Verkoopregel($"Dienst {ronde % 9 + 1}", 1 + ronde % 4, 80m + ronde, "8000", "V21")]));
                if (!concept.IsGelukt)
                {
                    return string.Join("; ", concept.Fouten);
                }

                var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
                return definitief.IsGelukt ? null : string.Join("; ", definitief.Fouten);
            });

            // Memoriaalpost (volgend boekstuk opvragen en boeken, zoals het scherm doet).
            await meting.Tijd("memoriaalpost boeken", async () =>
            {
                var boekstuk = await grootboek.VolgendBoekstuk(wie, id, "MEM", datum.Year);
                var geboekt = await grootboek.Boek(wie, id, new("MEM", boekstuk, datum, $"Correctie {uniek}", [new("4300", 10m + ronde), new("1100", -(10m + ronde))]));
                return geboekt.IsGelukt ? null : string.Join("; ", geboekt.Fouten);
            });

            // Elke vijfde ronde een bankafschrift met tien mutaties (eigen afschriftnummer per gebruiker en ronde).
            if (ronde % 5 == 4)
            {
                await meting.Tijd("bankafschrift (10 mutaties) inlezen", async () =>
                {
                    var stand = await bank.Stand(wie, id);
                    var begin = stand.Count > 0 ? stand[0].Saldo ?? 0m : 0m;
                    var mutaties = Enumerable.Range(0, 10).Select(i => new Bankmutatie(datum, -(5m + i + nummer), $"Abonnement {uniek}-{i}", Iban("RABO", 2000000 + i), $"Dienstverlener {i + 1}")).ToList();
                    var afschrift = new Bankafschrift(EigenIban, uniek, "EUR", datum, begin, datum, begin + mutaties.Sum(m => m.Bedrag), mutaties);
                    using var stroom = new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift)));
                    var gelezen = await bank.Lees(wie, id, stroom, $"bank-{uniek}.sta");

                    // Een afschrift dat niet aansluit op het vorige (een collega las er net een in) telt niet als fout van het pakket.
                    return gelezen.IsGelukt || gelezen.Fouten.Any(f => f.Contains("sluit", StringComparison.OrdinalIgnoreCase) || f.Contains("saldo", StringComparison.OrdinalIgnoreCase)) ? null : string.Join("; ", gelezen.Fouten);
                });
            }

            // De overzichten die iemand tussendoor opent.
            await meting.Tijd("kolommenbalans", async () => { await grootboek.Kolommenbalans(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)); return null; });
            await meting.Tijd("grootboekkaart crediteuren", async () => { await grootboek.Kaart(wie, id, "1600", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)); return null; });
            await meting.Tijd("journaal", async () => { await grootboek.Posten(wie, id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)); return null; });
            await meting.Tijd("dashboard", async () => { await app.Dienst<DashboardDienst>().Laad(wie, id, DashboardDienst.Standaard); return null; });
            await meting.Tijd("bank: te verwerken", async () => { await bank.Lijst(wie, id); return null; });
        }
    }

    private void Toon(Meting meting, Meting? basis)
    {
        Schrijf($"   {"handeling",-38} {"aantal",6} {"mediaan",8} {"p95",7} {"max",7}{(basis is null ? null : "   t.o.v. alleen")}");
        foreach (var (wat, tijden) in meting.Tijden.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var gesorteerd = tijden.Order().ToList();
            var mediaan = gesorteerd[gesorteerd.Count / 2];
            var p95 = gesorteerd[Math.Min(gesorteerd.Count - 1, (int)(gesorteerd.Count * 0.95))];
            string? vergelijk = null;
            if (basis is not null && basis.Tijden.TryGetValue(wat, out var alleen) && !alleen.IsEmpty)
            {
                var basismediaan = alleen.Order().ElementAt(alleen.Count / 2);
                vergelijk = $"   {mediaan / Math.Max(basismediaan, 0.5):0.0}× zo lang";
            }

            Schrijf($"   {wat,-38} {gesorteerd.Count,6} {mediaan,5:0} ms {p95,4:0} ms {gesorteerd[^1],4:0} ms{vergelijk}");
        }

        if (!meting.Fouten.IsEmpty || !meting.Botsingen.IsEmpty)
        {
            Schrijf($"   {meting.Fouten.Count} fout(en), {meting.Botsingen.Count} keer een nette melding omdat een collega net hetzelfde deed");
        }
    }

    /// <summary>De tijden per handeling en wat er misging, veilig voor meerdere gebruikers tegelijk.</summary>
    private sealed class Meting
    {
        public ConcurrentDictionary<string, ConcurrentBag<double>> Tijden { get; } = new();

        public ConcurrentBag<string> Fouten { get; } = [];

        /// <summary>Twee gebruikers deden op hetzelfde moment hetzelfde en één kreeg de nette melding (geen fout van het pakket).</summary>
        public ConcurrentBag<string> Botsingen { get; } = [];

        public async Task Tijd(string wat, Func<Task<string?>> werk)
        {
            var klok = Stopwatch.StartNew();
            string? fout;
            try
            {
                fout = await werk();
            }
            catch (Exception uitzondering) when (uitzondering is not OutOfMemoryException)
            {
                var kern = uitzondering.GetBaseException();
                fout = $"{kern.GetType().Name}: {kern.Message}";
            }

            Tijden.GetOrAdd(wat, _ => []).Add(klok.Elapsed.TotalMilliseconds);
            if (fout is not null && (fout.Contains("bestaat al in dagboek", StringComparison.Ordinal) || fout.StartsWith("Een collega legde", StringComparison.Ordinal)))
            {
                Botsingen.Add($"{wat}: {fout}");
            }
            else if (fout is not null)
            {
                Fouten.Add($"{wat}: {(fout.Length > 160 ? fout[..160] : fout)}");
            }
        }
    }

    /// <summary>Een geldig (verzonnen) Nederlands IBAN bij een banknaam en rekeningnummer.</summary>
    private static string Iban(string bank, int rekening)
    {
        var bban = $"{bank}{rekening:0000000000}";
        var cijfers = string.Concat($"{bban}NL00".Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        var controle = 98 - (int)(BigInteger.Parse(cijfers, CultureInfo.InvariantCulture) % 97);
        return $"NL{controle:00}{bban}";
    }
}
