using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Herstel na een onderbreking: een bankafschrift inlezen dat halverwege wordt afgebroken (de gebruiker sluit het
/// venster, de verbinding valt weg), op verschillende momenten. Daarna is het afschrift er helemaal of helemaal niet,
/// is elke geboekte mutatie ook echt in het grootboek geboekt, kloppen de vaste controles, en geeft opnieuw inlezen en
/// verwerken precies één keer alles.
/// </summary>
public sealed class HerstelTests(AdministratieFixture app, ITestOutputHelper uitvoer) : IClassFixture<AdministratieFixture>
{
    private const int Facturen = 60;

    [Fact]
    public async Task Een_afgebroken_bankimport_is_heel_of_niet_en_opnieuw_inlezen_herstelt()
    {
        var fouten = new List<string>();
        foreach (var na in new[] { 0, 5, 20, 60, 150, 400, 1_000 })
        {
            var w = await Werkadministratie.Maak(app, $"Herstel {na} B.V.");
            var verkoop = app.Dienst<VerkoopDienst>();
            var mutaties = new List<Bankmutatie>();
            for (var i = 0; i < Facturen; i++)
            {
                var concept = await w.Concept(new DateOnly(2025, 2, 1).AddDays(i % 25), 100m + i);
                var nummer = (await verkoop.MaakDefinitief(w.Wie, w.Id, concept)).Waarde!;
                var bedrag = Math.Round((100m + i) * 1.21m, 2);
                mutaties.Add(new Bankmutatie(new DateOnly(2025, 3, 1).AddDays(i % 28), bedrag, $"Factuur {nummer}", Praktijkset.Fietsplan.Iban, Praktijkset.Fietsplan.Naam));
            }

            for (var i = 0; i < 400; i++)
            {
                mutaties.Add(new Bankmutatie(new DateOnly(2025, 3, 1).AddDays(i % 28), -1.25m - i, $"Pin {i}", null, "Winkel"));
            }

            var afschrift = new Bankafschrift(Werkadministratie.Iban, "1", "EUR", new DateOnly(2025, 3, 1), 10_000m, new DateOnly(2025, 3, 28), 10_000m + mutaties.Sum(m => m.Bedrag), mutaties);
            var bestand = System.Text.Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, "HERSTEL"));
            var bank = app.Dienst<BankDienst>();

            using var stop = new CancellationTokenSource();
            stop.CancelAfter(na);
            string afloop;
            try
            {
                var gelezen = await bank.Lees(w.Wie, w.Id, new MemoryStream(bestand), "maart.mt940", stop.Token);
                afloop = gelezen.IsGelukt ? $"helemaal ingelezen, {gelezen.Waarde!.Automatisch} vanzelf geboekt" : "geweigerd: " + string.Join(" ", gelezen.Fouten);
            }
            catch (OperationCanceledException)
            {
                afloop = "afgebroken";
            }

            var ingelezen = (await bank.Lijst(w.Wie, w.Id, ookGeboekt: true)).Count;
            var geboekt = (await bank.Lijst(w.Wie, w.Id, ookGeboekt: true)).Count(m => m.Status == Banktransactiestatus.Geboekt);
            var inGrootboek = (await app.Dienst<GrootboekDienst>().Posten(w.Wie, w.Id, "BNK")).Count;
            uitvoer.WriteLine($"Afgebroken na {na,5} ms: {afloop}; {ingelezen} mutaties ingelezen, {geboekt} geboekt, {inGrootboek} bankboekingen.");
            if (ingelezen is not 0 && ingelezen != mutaties.Count)
            {
                fouten.Add($"Na {na} ms: {ingelezen} van {mutaties.Count} mutaties ingelezen (half).");
            }

            if (geboekt != inGrootboek)
            {
                fouten.Add($"Na {na} ms: {geboekt} mutaties staan als geboekt, maar er zijn {inGrootboek} bankboekingen.");
            }

            fouten.AddRange(await Invarianten.Controleer(app, w.Wie, w.Id, $"na {na} ms afbreken"));

            // Opnieuw inlezen en verwerken: precies één keer alles.
            var opnieuw = await bank.Lees(w.Wie, w.Id, new MemoryStream(bestand), "maart.mt940");
            if (!opnieuw.IsGelukt)
            {
                fouten.Add($"Na {na} ms: opnieuw inlezen lukt niet: {string.Join(" ", opnieuw.Fouten)}");
            }

            await bank.VerwerkOpnieuw(w.Wie, w.Id);
            var alles = await bank.Lijst(w.Wie, w.Id, ookGeboekt: true);
            var bankboekingen = (await app.Dienst<GrootboekDienst>().Posten(w.Wie, w.Id, "BNK")).Count;
            if (alles.Count != mutaties.Count || alles.Count(m => m.Status == Banktransactiestatus.Geboekt) != bankboekingen)
            {
                fouten.Add($"Na {na} ms en opnieuw: {alles.Count} mutaties (verwacht {mutaties.Count}), {alles.Count(m => m.Status == Banktransactiestatus.Geboekt)} geboekt, {bankboekingen} bankboekingen.");
            }

            var open = (await app.Dienst<OpenpostenDienst>().Debiteuren(w.Wie, w.Id, new DateOnly(2025, 12, 31))).Relaties.SelectMany(r => r.Posten).Count();
            uitvoer.WriteLine($"    opnieuw: {alles.Count} mutaties, {bankboekingen} geboekt, {open} facturen nog open.");
            fouten.AddRange(await Invarianten.Controleer(app, w.Wie, w.Id, $"na {na} ms afbreken en opnieuw"));
        }

        foreach (var f in fouten)
        {
            uitvoer.WriteLine("  - " + f);
        }

        Assert.True(fouten.Count == 0, string.Join("\n", fouten));
    }
}
