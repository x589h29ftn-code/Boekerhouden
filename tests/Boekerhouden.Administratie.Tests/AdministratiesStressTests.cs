using System.Diagnostics;
using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Draait alleen met BOEKERHOUDEN_STRESS_ADMINISTRATIES (aantal, bijv. 300) en eventueel BOEKERHOUDEN_STRESS_JAREN (standaard 5).</summary>
internal sealed class AdministratiesStressFactAttribute : FactAttribute
{
    public AdministratiesStressFactAttribute()
    {
        if (Aantal == 0)
        {
            Skip = "Zet BOEKERHOUDEN_STRESS_ADMINISTRATIES op het aantal administraties (bijv. 300) om deze stresstest te draaien.";
        }
    }

    public static int Aantal => Getal("BOEKERHOUDEN_STRESS_ADMINISTRATIES", 0);

    public static int Jaren => Getal("BOEKERHOUDEN_STRESS_JAREN", 5);

    private static int Getal(string naam, int standaard) =>
        Environment.GetEnvironmentVariable(naam) is { Length: > 0 } w && int.TryParse(w, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : standaard;
}

/// <summary>
/// Stresstest van het aantal administraties: een kantoor met honderden klanten, elk met jaren aan boekhouding. De
/// gegevens gaan in bulk de database in (zoals ze na jaren werken zouden staan): leveranciers, per maand 25
/// inkoopfacturen met openstaande posten, 50 bankmutaties met afschrift (betalingen en ontvangsten) en 2
/// memoriaalboekingen, met een kloppende hash-keten. Daarna worden de gewone handelingen gemeten bij de eerste, middelste
/// en laatste klant: overzichten, lijsten, inkoopboek, bank, en een nieuwe factuur inlezen.
/// </summary>
public sealed class AdministratiesStressTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const int Leveranciers = 20;
    private static readonly string[] Kostenrekeningen = ["4300", "4310", "4500", "4010"];

    private sealed class Generator(Guid administratie, string door)
    {
        private long volgnummer;
        private string vorige = Journaalketen.Begin;

        public List<Boeking> Boekingen { get; } = [];

        public Boeking Boek(string dagboek, string boekstuk, DateOnly datum, string omschrijving, params Journaalregel[] regels)
        {
            var boeking = new Boeking
            {
                Id = Guid.NewGuid(),
                AdministratieId = administratie,
                Volgnummer = ++volgnummer,
                Dagboek = dagboek,
                Boekstuk = boekstuk,
                Datum = datum,
                Omschrijving = omschrijving,
                GeboektOp = new DateTimeOffset(datum.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                GeboektDoor = door,
                VorigeHash = vorige,
                Regels = [.. regels.Select((r, i) => new Boekingsregel
                {
                    AdministratieId = administratie,
                    Regelnummer = i + 1,
                    Rekeningnummer = r.Rekeningnummer,
                    Bedrag = r.Bedrag,
                    Omschrijving = r.Omschrijving,
                    BtwCode = r.BtwCode,
                    Relatie = r.Relatie,
                    Factuurnummer = r.Factuurnummer,
                })],
            };
            boeking.Hash = Journaalketen.Hash(boeking.NaarDomein(), boeking.Volgnummer, vorige);
            vorige = boeking.Hash;
            Boekingen.Add(boeking);
            return boeking;
        }
    }

    /// <summary>Vult één administratie met <paramref name="jaren"/> jaar boekhouding, eindigend in 2026.</summary>
    private async Task<int> Vul(Uitvoerder wie, Guid id, int klant, int jaren)
    {
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var relaties = Enumerable.Range(0, Leveranciers).Select(l => new Handelsrelatie
        {
            Id = Guid.NewGuid(),
            AdministratieId = id,
            Code = $"LEV{l:00}",
            Naam = $"Leverancier {l:00} van klant {klant:000} B.V.",
            Ibans = [InkoopStressTests.StressIban(l)],
            Tegenrekening = Kostenrekeningen[l % Kostenrekeningen.Length],
            AangemaaktOp = DateTimeOffset.UnixEpoch,
        }).ToList();
        db.Relaties.AddRange(relaties);

        var g = new Generator(id, "Stresstest");
        var posten = new List<OpenstaandePost>();
        var afschriften = new List<Afschrift>();
        var transacties = new List<Banktransactie>();
        var saldo = 10_000m;
        var eerste = 2026 - jaren + 1;
        for (var jaar = eerste; jaar <= 2026; jaar++)
        {
            var (ink, bnk, mem) = (0, 0, 0);
            for (var maand = 1; maand <= 12; maand++)
            {
                var facturen = new List<(string Relatie, string Nummer, decimal Totaal)>();
                for (var i = 0; i < 25; i++)
                {
                    var l = (i + maand) % Leveranciers;
                    var excl = 50m + (i * 37 + maand * 11 + klant) % 900;
                    var btw = Math.Round(excl * 0.21m, 2, MidpointRounding.AwayFromZero);
                    var nummer = $"{l:00}-{jaar}-{maand:00}{i:00}";
                    var datum = new DateOnly(jaar, maand, 1 + i);
                    var boeking = g.Boek("INK", $"{jaar}-{++ink:0000}", datum, $"Leverancier {l:00} {nummer}",
                        new Journaalregel(relaties[l].Tegenrekening!, excl, null, "I21", relaties[l].Code, nummer),
                        new Journaalregel("1500", btw, "Btw inkoop hoog", "I21", relaties[l].Code, nummer),
                        new Journaalregel("1600", -(excl + btw), null, null, relaties[l].Code, nummer));
                    var open = jaar == 2026 && maand == 12;
                    posten.Add(new OpenstaandePost
                    {
                        Id = Guid.NewGuid(),
                        AdministratieId = id,
                        Soort = Factuursoort.Inkoop,
                        Relatie = relaties[l].Code,
                        Factuurnummer = nummer,
                        Datum = datum,
                        Vervaldatum = datum.AddDays(30),
                        Bedrag = -(excl + btw),
                        Openstaand = open ? -(excl + btw) : 0,
                        BoekingId = boeking.Id,
                    });
                    if (!open)
                    {
                        facturen.Add((relaties[l].Code, nummer, excl + btw));
                    }
                }

                var afschrift = new Afschrift
                {
                    Id = Guid.NewGuid(),
                    AdministratieId = id,
                    Dagboek = "BNK",
                    Iban = "NL91ABNA0417164300",
                    Nummer = $"{jaar}{maand:00}",
                    Begindatum = new DateOnly(jaar, maand, 1),
                    Beginsaldo = saldo,
                    Einddatum = new DateOnly(jaar, maand, 28),
                    Bestandsnaam = $"afschrift-{jaar}-{maand:00}.sta",
                    IngelezenOp = DateTimeOffset.UnixEpoch,
                    IngelezenDoor = "Stresstest",
                };
                var volgorde = 0;
                void Mutatie(DateOnly datum, decimal bedrag, string omschrijving, Boeking boeking, string? tegenNaam) => transacties.Add(new Banktransactie
                {
                    Id = Guid.NewGuid(),
                    AdministratieId = id,
                    AfschriftId = afschrift.Id,
                    Volgorde = ++volgorde,
                    Dagboek = "BNK",
                    Iban = afschrift.Iban,
                    Datum = datum,
                    Bedrag = bedrag,
                    Omschrijving = omschrijving,
                    TegenNaam = tegenNaam,
                    Sleutel = $"{jaar}{maand:00}-{volgorde}",
                    Status = Banktransactiestatus.Geboekt,
                    BoekingId = boeking.Id,
                });

                // Betalingen van de facturen van deze maand (behalve de open van december 2026), en ontvangsten van klanten.
                foreach (var (relatie, nummer, totaal) in facturen)
                {
                    var datum = new DateOnly(jaar, maand, 28);
                    var b = g.Boek("BNK", $"{jaar}-{++bnk:0000}", datum, $"Betaling {nummer}",
                        new Journaalregel("1100", -totaal), new Journaalregel("1600", totaal, null, null, relatie, nummer));
                    Mutatie(datum, -totaal, $"Betaling {nummer}", b, relatie);
                    saldo -= totaal;
                }

                for (var i = 0; i < 25; i++)
                {
                    var datum = new DateOnly(jaar, maand, 1 + i);
                    var excl = 500m + (i * 53 + maand * 7) % 2000;
                    var btw = Math.Round(excl * 0.21m, 2, MidpointRounding.AwayFromZero);
                    var b = g.Boek("BNK", $"{jaar}-{++bnk:0000}", datum, $"Ontvangst klant {i:00}",
                        new Journaalregel("1100", excl + btw), new Journaalregel("8000", -excl, null, "V21"), new Journaalregel("1510", -btw, null, "V21"));
                    Mutatie(datum, excl + btw, $"Ontvangst klant {i:00}", b, $"Klant {i:00}");
                    saldo += excl + btw;
                }

                afschrift.Eindsaldo = saldo;
                afschriften.Add(afschrift);
                for (var i = 0; i < 2; i++)
                {
                    g.Boek("MEM", $"{jaar}-{++mem:0000}", new DateOnly(jaar, maand, 28), "Memoriaal",
                        new Journaalregel("4300", 25m), new Journaalregel("1400", -25m));
                }
            }
        }

        db.Boekingen.AddRange(g.Boekingen);
        db.OpenstaandePosten.AddRange(posten);
        db.Afschriften.AddRange(afschriften);
        db.Banktransacties.AddRange(transacties);
        await db.SaveChangesAsync();
        return g.Boekingen.Count;
    }

    [AdministratiesStressFact]
    public async Task Honderden_administraties_met_jaren_aan_gegevens()
    {
        var (aantal, jaren) = (AdministratiesStressFactAttribute.Aantal, AdministratiesStressFactAttribute.Jaren);
        var wie = await app.Beheerder();
        var administraties = app.Dienst<AdministratieDienst>();

        // 1. Vullen: vier klanten tegelijk.
        var klok = Stopwatch.StartNew();
        var ids = new Guid[aantal];
        var boekingen = 0;
        var poort = new SemaphoreSlim(4);
        await Task.WhenAll(Enumerable.Range(0, aantal).Select(async n =>
        {
            await poort.WaitAsync();
            try
            {
                var uitkomst = await administraties.Maak(new AdministratieInvoer($"Klant {n:000} B.V.", Ondernemingsvorm.BV, null, null), wie);
                ids[n] = uitkomst.Waarde;
                Interlocked.Add(ref boekingen, await Vul(wie, ids[n], n, jaren));
            }
            finally
            {
                poort.Release();
            }
        }));
        uitvoer.WriteLine($"Vullen: {aantal} administraties × {jaren} jaar, {boekingen:N0} boekingen in {klok.Elapsed.TotalSeconds:0} s");
        await using (var db = await app.Dienst<Toegang>().Open(wie, ids[0], Recht.Lezen))
        {
            var grootte = await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync();
            uitvoer.WriteLine($"Database: {grootte / 1024 / 1024:N0} MB");
        }

        // 2. De administraties zelf.
        klok.Restart();
        var lijst = await administraties.Lijst(wie);
        Meet("lijst administraties", klok, $"{lijst.Count}");
        klok.Restart();
        var gezocht = await administraties.Lijst(wie, "Klant 150");
        Meet("zoeken in administraties", klok, $"{gezocht.Count}");

        // 3. Per klant de gewone handelingen; de eerste keer koud, daarna warm.
        foreach (var n in new[] { 0, aantal / 2, aantal - 1 })
        {
            uitvoer.WriteLine($"-- klant {n:000}");
            await Handelingen(wie, ids[n]);
        }

        uitvoer.WriteLine("-- klant 000 nog eens (warm)");
        await Handelingen(wie, ids[0]);
        Assert.Equal(aantal, lijst.Count(a => a.Naam.StartsWith("Klant ", StringComparison.Ordinal)));
    }

    private async Task Handelingen(Uitvoerder wie, Guid id)
    {
        var grootboek = app.Dienst<GrootboekDienst>();
        var inkoop = app.Dienst<InkoopDienst>();
        var bank = app.Dienst<BankDienst>();
        var klok = Stopwatch.StartNew();
        var jaren = await grootboek.Boekjaren(wie, id);
        Meet("boekjaren", klok, string.Join(",", jaren));
        klok.Restart();
        var proef = await grootboek.Proefbalans(wie, id, 2026);
        Meet("proefbalans 2026", klok, $"{proef.Regels.Count} rekeningen");

        // Ter vergelijking: zoals het was, het hele journaal (alle jaren) inlezen en daaruit rekenen.
        klok.Restart();
        var alles = (await grootboek.Posten(wie, id)).OrderBy(p => p.Volgnummer).Select(p => p.Post).ToList();
        var oud = Journaal.Herstel(await grootboek.Schema(wie, id), await grootboek.Dagboeken(wie, id), alles).Proefbalans(2026);
        Meet("  idem, oude manier (alles)", klok, $"{alles.Count:N0} boekingen");
        Assert.Equal(oud.Regels, proef.Regels);
        klok.Restart();
        await grootboek.Jaaroverzicht(wie, id, 2026);
        Meet("balans en W&V 2026", klok);
        klok.Restart();
        await grootboek.Kolommenbalans(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Meet("kolommenbalans 2026", klok);
        klok.Restart();
        var kaart = await grootboek.Kaart(wie, id, "1100", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Meet("grootboekkaart bank 2026", klok);
        klok.Restart();
        var posten = await grootboek.Posten(wie, id, van: new DateOnly(2026, 1, 1), tot: new DateOnly(2026, 12, 31));
        Meet("journaal 2026", klok, $"{posten.Count:N0} boekingen");
        klok.Restart();
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
        Meet("hash-keten controleren", klok);
        klok.Restart();
        var boek = await inkoop.Inkoopboek(wie, id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Meet("inkoopboek 2026", klok, $"{boek.Count} facturen");
        klok.Restart();
        await inkoop.Lijst(wie, id);
        Meet("inkooplijst", klok);
        klok.Restart();
        await inkoop.Historie(wie, id, "LEV03");
        Meet("historie leverancier", klok);
        klok.Restart();
        var mutaties = await bank.Lijst(wie, id, ookGeboekt: true);
        Meet("bankmutaties (ook geboekt)", klok, $"{mutaties.Count:N0}");
        klok.Restart();
        var open = await bank.OpenPosten(wie, id);
        Meet("openstaande posten", klok, $"{open.Count}");
        klok.Restart();
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopStressTests.Factuur(3, 7)), "nieuw.pdf", "application/pdf");
        Meet("nieuwe factuur inlezen", klok, gelezen.Melding.Length > 60 ? gelezen.Melding[..60] : gelezen.Melding);
        klok.Restart();
        await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value);
        Meet("verwerkscherm openen", klok);
        Assert.NotNull(kaart);
    }

    private void Meet(string wat, Stopwatch klok, string? extra = null) =>
        uitvoer.WriteLine($"   {wat,-30} {klok.Elapsed.TotalMilliseconds,8:0} ms  {extra}");
}
