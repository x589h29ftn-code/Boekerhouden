using System.Diagnostics;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Hoeveel proefgegevens: maanden memoriaal (vanaf januari van het vorige jaar), boekingen per maand, bankmutaties en inkoopfacturen.</summary>
public sealed record Proefomvang(int Maanden = 24, int PerMaand = 20, int Mutaties = 300, int Facturen = 40)
{
    public static Proefomvang Klein { get; } = new(3, 3, 12, 3);
}

/// <summary>Wat de proef aanmaakte en hoe lang elk deel duurde (in milliseconden).</summary>
public sealed record Proefuitkomst(Guid Administratie, int Boekingen, int Mutaties, int Facturen, IReadOnlyList<(string Stap, long Ms)> Tijden);

/// <summary>
/// Proefgegevens voor de stresstest van de Windows-app (<c>--proef</c>) en de tests: één administratie met een
/// beginbalans, twee jaar memoriaal, een bankafschrift met honderden mutaties (als MT940 ingelezen, met afletteren en
/// regels) en een stapel inkoopfacturen (UBL) in Te verwerken. Alles via de gewone diensten, dus met dezelfde controles
/// en dezelfde database als bij echt werk. Alleen verzonnen namen en bedragen.
/// </summary>
public static class Proefgegevens
{
    /// <summary>Het IBAN van de eigen bankrekening in de proef (een geldig, verzonnen nummer).</summary>
    public const string EigenIban = "NL91ABNA0417164300";

    public static async Task<Proefuitkomst> Vul(IServiceProvider diensten, Uitvoerder wie, Proefomvang omvang, int jaar)
    {
        ArgumentNullException.ThrowIfNull(diensten);
        ArgumentNullException.ThrowIfNull(omvang);
        var tijden = new List<(string, long)>();
        var klok = Stopwatch.StartNew();
        long Rond(string stap)
        {
            var ms = klok.ElapsedMilliseconds;
            tijden.Add((stap, ms));
            klok.Restart();
            return ms;
        }

        var administratie = await diensten.GetRequiredService<AdministratieDienst>()
            .Maak(new AdministratieInvoer("Proef B.V.", Ondernemingsvorm.BV, null, null), wie);
        var id = administratie.Waarde;
        var grootboek = diensten.GetRequiredService<GrootboekDienst>();
        Controleer(await grootboek.Boek(wie, id, new("MEM", $"{jaar - 1}-0001", new(jaar - 1, 1, 1), "Beginbalans", [new("1100", 1234.56m), new("0500", -1234.56m)])));
        Rond("administratie en beginbalans");

        // Memoriaal: elke maand kosten en omzet, met wisselende bedragen.
        var boekingen = 1;
        var nummers = new Dictionary<int, int> { [jaar - 1] = 1 };
        (string Rekening, string Naam)[] soorten = [("4300", "Kantoorbenodigdheden"), ("4310", "Telefoon en internet"), ("8000", "Omzet")];
        for (var m = 0; m < omvang.Maanden; m++)
        {
            var maand = new DateOnly(jaar - 1, 1, 1).AddMonths(m);
            for (var i = 0; i < omvang.PerMaand; i++)
            {
                var datum = new DateOnly(maand.Year, maand.Month, Math.Min(1 + (i * 27 / Math.Max(omvang.PerMaand, 1)), 28));
                var (rekening, naam) = soorten[i % soorten.Length];
                var bedrag = 10m + ((m * 37 + i * 13) % 500) + 0.25m * (i % 4);
                var nummer = nummers[datum.Year] = nummers.GetValueOrDefault(datum.Year) + 1;
                var post = rekening == "8000"
                    ? new Journaalpost("MEM", $"{datum.Year}-{nummer:0000}", datum, $"{naam} {datum:MM-yyyy} {i + 1}", [new("1100", bedrag), new(rekening, -bedrag)])
                    : new Journaalpost("MEM", $"{datum.Year}-{nummer:0000}", datum, $"{naam} {datum:MM-yyyy} {i + 1}", [new(rekening, bedrag), new("1100", -bedrag)]);
                Controleer(await grootboek.Boek(wie, id, post));
                boekingen++;
            }
        }

        // Vaste activa: een aanschaf op inventaris in januari van het vorige jaar, met de afschrijvingen van dat jaar.
        Controleer(await grootboek.Boek(wie, id, new("MEM", $"{jaar - 1}-9001", new(jaar - 1, 1, 15), "Aanschaf", [new("0200", 2400m, "Server"), new("1100", -2400m)])));
        var activa = diensten.GetRequiredService<ActivaDienst>();
        var voorstel = (await activa.Kandidaten(wie, id)).Single().Voorstel with { Looptijd = 36 };
        Controleer(await activa.Bewaar(wie, id, null, voorstel));
        Controleer(await activa.Afschrijven(wie, id, new DateOnly(jaar - 1, 12, 31), Boekhouding.Activa.Afschrijvingsfrequentie.Maand));
        Rond($"{boekingen} memoriaalboekingen");

        // Bank: één afschrift over het jaar, als MT940 ingelezen.
        var mutaties = Enumerable.Range(0, omvang.Mutaties).Select(i =>
        {
            var datum = new DateOnly(jaar, 1, 1).AddDays(i * 360 / Math.Max(omvang.Mutaties, 1));
            var bedrag = i % 3 == 0 ? 100m + i : -(15m + (i * 7 % 300) + 0.99m);
            return new Bankmutatie(datum, bedrag, bedrag > 0 ? $"Betaling factuur {jaar}{i:0000}" : $"Abonnement {i % 12 + 1} nota {i:00000}",
                $"NL{(i % 89) + 10:00}INGB000{1000000 + i % 50:0000000}", bedrag > 0 ? $"Klant {i % 25 + 1}" : $"Leverancier {i % 15 + 1}");
        }).ToList();
        var afschrift = new Bankafschrift(EigenIban, "1", "EUR", new(jaar, 1, 1), 5000m, new(jaar, 12, 31), 5000m + mutaties.Sum(m => m.Bedrag), mutaties);
        var bank = diensten.GetRequiredService<BankDienst>();
        using (var stroom = new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift))))
        {
            var gelezen = await bank.Lees(wie, id, stroom, "proef.sta");
            if (!gelezen.IsGelukt)
            {
                throw new InvalidOperationException("Het proefafschrift is niet ingelezen: " + string.Join(" ", gelezen.Fouten));
            }
        }

        Rond($"bankafschrift met {mutaties.Count} mutaties");

        // Inkoop: e-facturen (UBL) van verzonnen leveranciers in Te verwerken.
        var codes = (await diensten.GetRequiredService<BtwDienst>().Tabel()).Op(new DateOnly(jaar, 6, 1));
        var inkoop = diensten.GetRequiredService<InkoopDienst>();
        var ontvanger = new Partij("Proef B.V.", new Adres("Proefstraat 1", "1234 AB", "Proefstad"));
        for (var i = 0; i < omvang.Facturen; i++)
        {
            var datum = new DateOnly(jaar, 1 + i % 12, 1 + i % 27);
            var leverancier = new Partij($"Leverancier {i % 15 + 1} B.V.", new Adres($"Handelsweg {i % 15 + 1}", "9999 ZZ", "Verzonnen"),
                $"NL{800000000 + i % 15:000000000}B01", Ibans: [$"NL{(i % 15) + 10:00}INGB000{1000000 + i % 15:0000000}"]);
            var factuur = new Verkoopfactuur("PROEF", ontvanger, $"F{jaar}-{i + 1:0000}", datum, datum.AddDays(30), $"Levering {i + 1}",
                [new($"Artikel {i % 7 + 1}", 1 + i % 3, 25m + i, "4300", "V21")]);
            var xml = UblFactuurSchrijver.Schrijf(factuur, leverancier, codes);
            using var stroom = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            await inkoop.Lees(wie, id, stroom, $"factuur-{i + 1:000}.xml", "application/xml");
        }

        Rond($"{omvang.Facturen} inkoopfacturen ingelezen");
        return new Proefuitkomst(id, boekingen, mutaties.Count, omvang.Facturen, tijden);
    }

    private static void Controleer<T>(Uitkomst<T> uitkomst)
    {
        if (!uitkomst.IsGelukt)
        {
            throw new InvalidOperationException("Een proefboeking is mislukt: " + string.Join(" ", uitkomst.Fouten));
        }
    }
}
