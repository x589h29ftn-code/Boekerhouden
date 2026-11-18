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
using Npgsql;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Draait alleen met BOEKERHOUDEN_OMVANG=&lt;aantal administraties&gt; (bijv. 300), op PostgreSQL. Optioneel:
/// BOEKERHOUDEN_OMVANG_JAREN (standaard 5) en BOEKERHOUDEN_OMVANG_PERJAAR (facturen én bankmutaties per jaar, standaard 500).
/// </summary>
internal sealed class OmvangFactAttribute : FactAttribute
{
    public OmvangFactAttribute()
    {
        if (Administraties == 0)
        {
            Skip = "Zet BOEKERHOUDEN_OMVANG=<aantal administraties> (bijv. 300) om de omvangtest te draaien.";
        }
        else if (AdministratieFixture.IsSqlite)
        {
            Skip = "De omvangtest kopieert administraties met SQL van PostgreSQL.";
        }
    }

    public static int Administraties => Getal("BOEKERHOUDEN_OMVANG", 0);

    public static int Jaren => Getal("BOEKERHOUDEN_OMVANG_JAREN", 5);

    public static int PerJaar => Getal("BOEKERHOUDEN_OMVANG_PERJAAR", 500);

    private static int Getal(string naam, int standaard) =>
        int.TryParse(Environment.GetEnvironmentVariable(naam), CultureInfo.InvariantCulture, out var n) && n > 0 ? n : standaard;
}

/// <summary>
/// Hoeveel administraties en jaren kan het pakket aan? Eén administratie wordt via de gewone diensten gevuld (per jaar
/// 60% inkoopfacturen als e-factuur ingelezen en geboekt, 40% verkoopfacturen gemaakt en definitief, en evenveel
/// bankmutaties als MT940 die de facturen betalen en worden afgeletterd), en daarna met SQL gekopieerd tot het gevraagde
/// aantal administraties. Bij 1, 30, 100 en het volle aantal administraties meet de test dezelfde schermen (kolommenbalans,
/// grootboekkaart, journaal, inkoop- en verkoopboek, bank, btw, dashboard, zoeken, de werklijst van het kantoor) en een
/// paar handelingen (boeken, bank inlezen, factuur inlezen), zodat te zien is wat er trager wordt als de database groeit.
/// </summary>
public sealed class OmvangStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const string EigenIban = "NL91ABNA0417164300";
    private const int Leveranciers = 15;
    private const int Klanten = 10;
    private static readonly Partij Eigen = new("Omvang Handel B.V.", new Adres("Proefstraat 1", "1234 AB", "Proefstad"), "NL123456782B01", Ibans: [EigenIban]);
    private readonly List<string> verslag = [];

    private void Schrijf(string regel)
    {
        uitvoer.WriteLine(regel);
        verslag.Add(regel);
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_OMVANG_VERSLAG") is { Length: > 0 } pad)
        {
            File.WriteAllLines(pad, verslag);
        }
    }

    [OmvangFact]
    public async Task Honderden_administraties_met_jaren_aan_facturen_en_bankmutaties()
    {
        var (aantal, jaren, perJaar) = (OmvangFactAttribute.Administraties, OmvangFactAttribute.Jaren, OmvangFactAttribute.PerJaar);
        var laatste = 2026;
        var wie = await app.Beheerder();
        var model = await app.Administratie("Omvang Handel B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, model);
        Assert.True((await inrichting.WijzigInstellingen(wie, model, nu with { BtwNummer = "NL123456782B01", Ibans = [EigenIban], Contact = new Contactgegevens("Proefstraat 1", "1234 AB", "Proefstad") })).IsGelukt);

        // 1. Eén administratie vullen via de gewone diensten.
        var klok = Stopwatch.StartNew();
        var saldo = 25000m;
        for (var jaar = laatste - jaren + 1; jaar <= laatste; jaar++)
        {
            var jaarklok = Stopwatch.StartNew();
            var (betalingen, zelf) = await Facturen(wie, model, jaar, perJaar);
            var facturen = jaarklok.Elapsed;
            jaarklok.Restart();
            saldo = await Bank(wie, model, jaar, saldo, betalingen);
            Schrijf($"jaar {jaar}: {betalingen.Count} facturen in {facturen.TotalSeconds:0.0} s ({facturen.TotalMilliseconds / betalingen.Count:0} ms per stuk, {zelf} zelf geboekt), " +
                $"{betalingen.Count} bankmutaties in {jaarklok.Elapsed.TotalSeconds:0.0} s");
        }

        Schrijf($"model gevuld in {klok.Elapsed.TotalSeconds:0} s");
        Assert.Empty(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, model));
        Schrijf("");
        await Meet(wie, model, model, laatste, 1);

        // 2. Kopiëren tot het gevraagde aantal, met metingen onderweg.
        var kopieen = new List<Guid>();
        foreach (var doel in new[] { 30, 100, aantal }.Where(d => d <= aantal).Distinct().Order())
        {
            klok.Restart();
            while (kopieen.Count + 1 < doel)
            {
                kopieen.Add(await Kopieer(model, kopieen.Count + 2));
            }

            await Sql("ANALYZE");
            Schrijf($"gekopieerd tot {doel} administraties in {klok.Elapsed.TotalSeconds:0} s");
            await Meet(wie, model, kopieen[^1], laatste, doel);
        }
    }

    /// <summary>De facturen van een jaar: inkoop als e-factuur ingelezen (zelf geboekt tot het pakket het overneemt), verkoop gemaakt en definitief.</summary>
    private async Task<(List<Bankmutatie> Betalingen, int Zelf)> Facturen(Uitvoerder wie, Guid id, int jaar, int perJaar)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var inrichting = app.Dienst<InrichtingDienst>();
        var codes = (await app.Dienst<BtwDienst>().Tabel()).Op(new DateOnly(jaar, 6, 1));
        var betalingen = new List<Bankmutatie>();
        var zelf = 0;
        var klanten = (await inrichting.Relaties(wie, id)).Where(r => r.Klant?.IsKlant == true).Select(r => r.Code).ToList();
        for (var k = klanten.Count; k < Klanten; k++)
        {
            var nieuw = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer($"Klant {k + 1} B.V.", null, null, null,
                new Klantgegevens(true, $"Laan {k + 1}", "5678 CD", "Elders", Omzetrekening: "8000")), null);
            Assert.True(nieuw.IsGelukt, string.Join("; ", nieuw.Fouten));
            klanten.Add((await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code);
        }

        for (var i = 0; i < perJaar; i++)
        {
            var datum = new DateOnly(jaar, 1, 1).AddDays(i * 330 / perJaar);
            if (i % 5 < 3)
            {
                // Inkoop: e-factuur van een van de leveranciers.
                var l = i % Leveranciers;
                var iban = Iban("INGB", 1000000 + l);
                var leverancier = new Partij($"Leverancier {l + 1} B.V.", new Adres($"Handelsweg {l + 1}", "9999 ZZ", "Verzonnen"), $"NL{800000000 + l:000000000}B01", Ibans: [iban]);
                var nummer = $"L{l + 1:00}-{jaar}-{i + 1:0000}";
                var factuur = new Verkoopfactuur("OMVANG", Eigen, nummer, datum, datum.AddDays(30), $"Levering {i + 1}",
                    [new($"Artikel {i % 7 + 1}", 1 + i % 3, 25m + i % 200, "4300", "V21")]);
                using var stroom = new MemoryStream(Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(factuur, leverancier, codes)));
                var gelezen = await inkoop.Lees(wie, id, stroom, $"{nummer}.xml", "application/xml");
                Assert.NotNull(gelezen.DocumentId);
                if (!gelezen.IsGeboekt)
                {
                    zelf++;
                    var verwerking = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!;
                    var h = verwerking.Herkend!;
                    var code = verwerking.Voorstel?.Leverancier?.Relatie.Code;
                    var geboekt = await inkoop.Boek(wie, id, gelezen.DocumentId.Value, new Factuurinvoer(code, code is null ? new NieuweRelatie(h.Naam!, h.Iban, h.BtwNummer, h.KvkNummer) : null,
                        h.Factuurnummer!, h.Factuurdatum!.Value, h.Factuurdatum.Value.AddDays(30), "#leverancier #factuurnummer",
                        [new Factuurregel("", "4300", "I21", h.TotaalExclusief!.Value, h.TotaalBtw)]));
                    Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
                }

                var totaal = decimal.Round(factuur.TotaalExclusief * 1.21m, 2);
                betalingen.Add(new Bankmutatie(datum.AddDays(20), -totaal, $"Factuur {nummer}", iban, leverancier.Naam));
            }
            else
            {
                // Verkoop: een factuur maken en definitief maken.
                var k = i % Klanten;
                var invoer = new Verkoopinvoer(klanten[k], datum, datum.AddDays(14), $"Werkzaamheden {datum:MM-yyyy}", [new Verkoopregel($"Dienst {i % 9 + 1}", 1 + i % 4, 80m + i % 150, "8000", "V21")]);
                var concept = await verkoop.Bewaar(wie, id, null, invoer);
                Assert.True(concept.IsGelukt, string.Join("; ", concept.Fouten));
                var definitief = await verkoop.MaakDefinitief(wie, id, concept.Waarde);
                Assert.True(definitief.IsGelukt, string.Join("; ", definitief.Fouten));
                var totaal = decimal.Round(invoer.Regels.Sum(r => r.Aantal * r.Prijs) * 1.21m, 2);
                betalingen.Add(new Bankmutatie(datum.AddDays(12), totaal, $"Betaling factuur {definitief.Waarde}", Iban("RABO", 2000000 + k), $"Klant {k + 1} B.V."));
            }
        }

        return (betalingen, zelf);
    }

    /// <summary>Het afschrift van een jaar met de betaling van elke factuur; wat niet vanzelf is afgeletterd gaat op de vraagposten.</summary>
    private async Task<decimal> Bank(Uitvoerder wie, Guid id, int jaar, decimal begin, List<Bankmutatie> betalingen)
    {
        var bank = app.Dienst<BankDienst>();
        var mutaties = betalingen.OrderBy(b => b.Datum).ToList();
        var eind = begin + mutaties.Sum(m => m.Bedrag);
        var afschrift = new Bankafschrift(EigenIban, jaar.ToString(CultureInfo.InvariantCulture), "EUR", new(jaar, 1, 1), begin, mutaties[^1].Datum, eind, mutaties);
        using var stroom = new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift)));
        var gelezen = await bank.Lees(wie, id, stroom, $"bank-{jaar}.sta");
        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        var open = (await bank.Lijst(wie, id)).Where(m => m.Status == Banktransactiestatus.TeVerwerken).Select(m => m.Id).ToList();
        if (open.Count > 0)
        {
            var (vraagposten, _) = await bank.Vraagposten(wie, id);
            var rest = await bank.OpRekening(wie, id, open, vraagposten ?? "2390", null);
            Assert.True(rest.IsGelukt, string.Join("; ", rest.Fouten));
        }

        Schrijf($"   bank {jaar}: {gelezen.Waarde!.Nieuw} ingelezen, {gelezen.Waarde.Automatisch} vanzelf afgeletterd, {open.Count} op de vraagposten");
        return eind;
    }

    /// <summary>De schermen en handelingen meten: lezen in de modeladministratie, schrijven in <paramref name="werk"/>.</summary>
    private async Task Meet(Uitvoerder wie, Guid model, Guid werk, int jaar, int administraties)
    {
        var grootboek = app.Dienst<GrootboekDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        var verkoop = app.Dienst<VerkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var (van, tot) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
        var alles = new DateOnly(jaar - OmvangFactAttribute.Jaren + 1, 1, 1);
        var grootte = await Getal("select pg_database_size(current_database())");
        var regels = await Getal("select count(*) from \"Boekingsregels\"");
        var boekingen = await Getal("select count(*) from \"Boekingen\"");
        Schrijf($"--- {administraties} administratie(s): {boekingen:N0} boekingen, {regels:N0} boekingsregels, database {grootte / 1024.0 / 1024:N0} MB");

        async Task Tijd(string wat, Func<Task<object?>> werkje)
        {
            // Twee keer: de eerste keer warmt de database en de querycache op, de tweede is wat de gebruiker merkt.
            var klok = Stopwatch.StartNew();
            await werkje();
            var koud = klok.ElapsedMilliseconds;
            klok.Restart();
            var uitkomst = await werkje();
            Schrijf($"   {wat,-46} {klok.ElapsedMilliseconds,6} ms   (eerste keer {koud,6} ms){(uitkomst is null ? null : $"   {uitkomst}")}");
        }

        await Tijd("lijst van administraties", async () => $"{(await app.Dienst<AdministratieDienst>().Lijst(wie)).Count} administraties");
        await Tijd("werklijst van het kantoor (alle adm.)", async () => $"{(await app.Dienst<KantooroverzichtDienst>().Stand(wie)).Count} standen");
        await Tijd("dashboard", async () => (await app.Dienst<DashboardDienst>().Laad(wie, model, DashboardDienst.Standaard)).GetType().Name.Length > 0 ? null : null);
        await Tijd($"kolommenbalans {jaar}", async () => $"{(await grootboek.Kolommenbalans(wie, model, van, tot)).Regels.Count} rekeningen");
        await Tijd($"balans en W&V {jaar}", async () => { await grootboek.Jaaroverzicht(wie, model, jaar); return null; });
        await Tijd($"grootboekkaart crediteuren {jaar}", async () => $"{(await grootboek.Kaart(wie, model, "1600", van, tot)).Mutaties.Count} regels");
        await Tijd("grootboekkaart crediteuren, alle jaren", async () => $"{(await grootboek.Kaart(wie, model, "1600", alles, tot)).Mutaties.Count} regels");
        await Tijd($"journaal {jaar}", async () => $"{(await grootboek.Posten(wie, model, null, van, tot)).Count} boekingen");
        await Tijd($"inkoopboek {jaar}", async () => $"{(await inkoop.Inkoopboek(wie, model, van, tot)).Count} facturen");
        await Tijd("inkoop: lijst van alle documenten", async () => $"{(await inkoop.Lijst(wie, model)).Count} documenten");
        await Tijd($"verkoop: lijst {jaar}", async () => $"{(await verkoop.Lijst(wie, model, van, tot)).Count} facturen");
        await Tijd("bank: alle mutaties (ook geboekt)", async () => $"{(await bank.Lijst(wie, model, ookGeboekt: true)).Count} mutaties");
        await Tijd("openstaande posten", async () => $"{(await bank.OpenPosten(wie, model)).Count} posten");
        await Tijd($"btw-aangifte {jaar}", async () => { await app.Dienst<BtwaangifteDienst>().Jaar(wie, model, jaar); return null; });
        await Tijd("zoeken in de administratie", async () => $"{(await app.Dienst<ZoekDienst>().Zoek(wie, "L07-2025-0127", model)).Count} treffers");
        await Tijd("zoeken over alle administraties", async () => $"{(await app.Dienst<ZoekDienst>().Zoek(wie, "Omvang Handel B.V. 2", null)).Count} treffers");
        await Tijd("hash-keten controleren (alle jaren)", async () => $"{(await grootboek.ControleerKeten(wie, werk)).Count} fouten");

        // Handelingen: een memoriaalboeking, een afschrift met 50 mutaties en een inkoopfactuur, in een (gekopieerde) administratie.
        var volg = administraties;
        var klokje = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            var boekstuk = await grootboek.VolgendBoekstuk(wie, werk, "MEM", jaar);
            var geboekt = await grootboek.Boek(wie, werk, new("MEM", boekstuk, new(jaar, 12, 1 + i), $"Stresstest {volg} {i}", [new("4300", 10m + i), new("1100", -(10m + i))]));
            Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));
        }

        Schrijf($"   {"20 memoriaalboekingen",-46} {klokje.ElapsedMilliseconds / 20,6} ms per boeking");
        var stand = (await bank.Stand(wie, werk)) is [var eerste, ..] ? eerste : null;
        var mutaties = Enumerable.Range(0, 50).Select(i => new Bankmutatie(new DateOnly(jaar + 1, 1, 2).AddDays(i % 25), -(12m + i), $"Abonnement {volg}-{i}", Iban("INGB", 1000000 + i % Leveranciers), $"Leverancier {i % Leveranciers + 1} B.V.")).ToList();
        var begin = await Decimaal($"select \"Eindsaldo\" from \"Afschriften\" where \"AdministratieId\" = '{werk}' order by \"Einddatum\" desc limit 1");
        var afschrift = new Bankafschrift(EigenIban, $"N{volg}", "EUR", new(jaar + 1, 1, 1), begin, new(jaar + 1, 1, 31), begin + mutaties.Sum(m => m.Bedrag), mutaties);
        klokje.Restart();
        using (var stroom = new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift))))
        {
            var gelezen = await bank.Lees(wie, werk, stroom, $"nieuw-{volg}.sta");
            Schrijf($"   {"afschrift met 50 mutaties inlezen",-46} {klokje.ElapsedMilliseconds,6} ms   {(gelezen.IsGelukt ? $"{gelezen.Waarde!.Nieuw} nieuw" : string.Join("; ", gelezen.Fouten))}{(stand is null ? " (geen stand)" : null)}");
        }

        var codes = (await app.Dienst<BtwDienst>().Tabel()).Op(new DateOnly(jaar, 6, 1));
        var leverancier = new Partij("Leverancier 3 B.V.", new Adres("Handelsweg 3", "9999 ZZ", "Verzonnen"), $"NL{800000002:000000000}B01", Ibans: [Iban("INGB", 1000002)]);
        var factuur = new Verkoopfactuur("OMVANG", Eigen, $"NIEUW-{volg}", new(jaar, 12, 15), new(jaar + 1, 1, 14), "Levering", [new("Artikel", 1, 100m, "4300", "V21")]);
        klokje.Restart();
        using (var stroom = new MemoryStream(Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(factuur, leverancier, codes))))
        {
            var gelezen = await inkoop.Lees(wie, werk, stroom, $"nieuw-{volg}.xml", "application/xml");
            Schrijf($"   {"inkoopfactuur inlezen (en vanzelf boeken)",-46} {klokje.ElapsedMilliseconds,6} ms   {gelezen.Status}");
        }

        Schrijf("");
    }

    /// <summary>Kopieert alle gegevens van een administratie naar een nieuwe, met nieuwe sleutels (alleen PostgreSQL).</summary>
    private async Task<Guid> Kopieer(Guid model, int volgnummer)
    {
        kopieerplan ??= await Kopieerplan();
        var nieuw = Guid.NewGuid();
        await using var verbinding = new NpgsqlConnection(app.Verbinding);
        await verbinding.OpenAsync();
        await using var transactie = await verbinding.BeginTransactionAsync();
        foreach (var sql in kopieerplan)
        {
            await using var opdracht = new NpgsqlCommand(sql, verbinding, transactie);
            opdracht.CommandTimeout = 600;
            opdracht.Parameters.AddWithValue("model", model);
            opdracht.Parameters.AddWithValue("nieuw", nieuw);
            opdracht.Parameters.AddWithValue("n", volgnummer.ToString(CultureInfo.InvariantCulture));
            await opdracht.ExecuteNonQueryAsync();
        }

        await transactie.CommitAsync();
        return nieuw;
    }

    private List<string>? kopieerplan;

    /// <summary>
    /// Per tabel met een administratie één INSERT … SELECT, in de volgorde van de verwijzingen. Sleutels en verwijzingen
    /// (uuid) krijgen een nieuwe waarde die van de oude en het volgnummer afhangt, zodat verwijzingen blijven kloppen;
    /// verwijzingen naar gebruikers blijven staan; oplopende nummers (identity) maakt de database zelf.
    /// </summary>
    private async Task<List<string>> Kopieerplan()
    {
        var kolommen = new Dictionary<string, List<(string Naam, string Type, bool Identity)>>();
        var ouders = new Dictionary<string, HashSet<string>>();
        await using var verbinding = new NpgsqlConnection(app.Verbinding);
        await verbinding.OpenAsync();
        await using (var opdracht = new NpgsqlCommand(
            "select table_name, column_name, udt_name, is_identity = 'YES' from information_schema.columns where table_schema = 'public' order by table_name, ordinal_position", verbinding))
        await using (var lezer = await opdracht.ExecuteReaderAsync())
        {
            while (await lezer.ReadAsync())
            {
                if (!kolommen.TryGetValue(lezer.GetString(0), out var lijst))
                {
                    kolommen[lezer.GetString(0)] = lijst = [];
                }

                lijst.Add((lezer.GetString(1), lezer.GetString(2), lezer.GetBoolean(3)));
            }
        }

        await using (var opdracht = new NpgsqlCommand("select c.relname, p.relname from pg_constraint k join pg_class c on c.oid = k.conrelid join pg_class p on p.oid = k.confrelid where k.contype = 'f'", verbinding))
        await using (var lezer = await opdracht.ExecuteReaderAsync())
        {
            while (await lezer.ReadAsync())
            {
                if (!ouders.TryGetValue(lezer.GetString(0), out var set))
                {
                    ouders[lezer.GetString(0)] = set = [];
                }

                set.Add(lezer.GetString(1));
            }
        }

        var tabellen = kolommen.Where(t => t.Value.Any(k => k.Naam == "AdministratieId") && t.Key != "VerwijderdeAdministraties").Select(t => t.Key).ToList();
        var volgorde = new List<string>();
        while (tabellen.Count > 0)
        {
            var klaar = tabellen.Where(t => !ouders.GetValueOrDefault(t, []).Any(o => o != t && tabellen.Contains(o))).ToList();
            Assert.NotEmpty(klaar);
            volgorde.AddRange(klaar);
            tabellen.RemoveAll(klaar.Contains);
        }

        string[] gebruikers = ["GebruikerId", "GeboektDoorId"];
        var plan = new List<string>
        {
            Insert("Administraties", kolommen["Administraties"], k => k.Naam == "Id" ? "@nieuw" : k.Naam == "Naam" ? "\"Naam\" || ' ' || @n" : $"\"{k.Naam}\"", "\"Id\" = @model"),
        };
        plan.AddRange(volgorde.Select(t => Insert(t, kolommen[t],
            k => k.Naam == "AdministratieId" ? "@nieuw" : k.Type == "uuid" && !gebruikers.Contains(k.Naam) ? $"md5(\"{k.Naam}\"::text || @n)::uuid" : $"\"{k.Naam}\"", "\"AdministratieId\" = @model")));
        return plan;

        static string Insert(string tabel, List<(string Naam, string Type, bool Identity)> kolommen, Func<(string Naam, string Type, bool Identity), string> waarde, string waar)
        {
            var mee = kolommen.Where(k => !k.Identity).ToList();
            return $"insert into \"{tabel}\" ({string.Join(", ", mee.Select(k => $"\"{k.Naam}\""))}) select {string.Join(", ", mee.Select(waarde))} from \"{tabel}\" where {waar}";
        }
    }

    private async Task Sql(string sql)
    {
        await using var verbinding = new NpgsqlConnection(app.Verbinding);
        await verbinding.OpenAsync();
        await using var opdracht = new NpgsqlCommand(sql, verbinding);
        opdracht.CommandTimeout = 1200;
        await opdracht.ExecuteNonQueryAsync();
    }

    private async Task<long> Getal(string sql)
    {
        await using var verbinding = new NpgsqlConnection(app.Verbinding);
        await verbinding.OpenAsync();
        await using var opdracht = new NpgsqlCommand(sql, verbinding);
        return Convert.ToInt64(await opdracht.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<decimal> Decimaal(string sql)
    {
        await using var verbinding = new NpgsqlConnection(app.Verbinding);
        await verbinding.OpenAsync();
        await using var opdracht = new NpgsqlCommand(sql, verbinding);
        return Convert.ToDecimal(await opdracht.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
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
