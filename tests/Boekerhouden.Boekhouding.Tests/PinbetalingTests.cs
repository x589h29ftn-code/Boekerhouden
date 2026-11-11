using System.Globalization;
using System.Text;
using Boekerhouden.Boekhouding.Afletteren;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Een pinbetaling op het afschrift hoort bij een ingelezen bon: geen factuurnummer en geen IBAN, wel het bedrag, de dag
/// en de naam van de winkel in de omschrijving van de bank.
/// </summary>
public sealed class PinbetalingTests
{
    private static readonly Relatie[] Winkels =
    [
        new("ZOET", "Bakkerij Zoet", []),
        new("ROOM", "Banketbakker Roomhart", []),
        new("BRUG", "Tankstation De Brug", []),
        new("KIOSK", "Kiosk Perron 1", []),
    ];

    private static OpenPost Bon(string relatie, string nummer, decimal bedrag, DateOnly datum) =>
        new(Guid.NewGuid(), Factuursoort.Inkoop, relatie, nummer, datum, datum.AddDays(30), -bedrag, -bedrag);

    private static Bankmutatie Pin(DateOnly datum, decimal bedrag, string omschrijving, string? naam = null) => new(datum, -bedrag, omschrijving, null, naam);

    [Theory]
    [InlineData("Bakkerij Zoet", "BEA, Betaalpas CCV*BAKKERIJ ZOET,PAS012 NR:4SF001, 11.11.26/10.19 SNEEK", true)]
    [InlineData("Banketbakker Roomhart", "Zettle_*Banketb Roomha Joure", true)]
    [InlineData("Brandstof Noord", "BRANDSTOFNOORD FRANEKER NLD", true)]
    [InlineData("Tankstation De Brug", "DE BRUG TANKSTATION DRACHTEN", true)]
    [InlineData("Albertus Hein Supermarkt", "ALBERTUS HEIN 1337 LEEUWARDEN", true)]
    [InlineData("Albertus Hein Supermarkt", "ALBERTUS 1337 LEEUWARDEN", true)]
    [InlineData("Bouwhal Gamar", "Gamar 0412 ASSEN", true)]
    [InlineData("Lunchroom De Kade", "SumUp *Lunchroom De Kad", true)]
    [InlineData("Lunchroom De Kade", "LUNCHROOM HET PLEIN", false)]
    [InlineData("Bakkerij Zoet", "BAKKERIJ KORST LEMMER", false)]
    [InlineData("Kiosk Perron 1", "SERVEX BV ZWOLLE", false)]
    [InlineData("De Bakkerij", "DE BAKKERIJ SNEEK", true)]
    [InlineData("De Bakkerij", "BAKKERIJ ZOET SNEEK", true)]
    [InlineData("Spar", "SPAR HEEG", true)]
    [InlineData("Spar", "SPARTA SPORT", false)]
    [InlineData("", "BAKKERIJ", false)]
    public void De_naam_van_de_winkel_in_de_omschrijving_van_de_bank(string winkel, string omschrijving, bool verwacht) =>
        Assert.Equal(verwacht, Pinbetaling.NaamKomtVoor(winkel, omschrijving));

    [Theory]
    [InlineData("Pasvolgnr: 003 11-11-2026 10:19 Transactie: A1B2", "2026-11-11")]
    [InlineData("BEA, Betaalpas X,PAS012 NR:4SF001, 11.11.26/10.19 SNEEK", "2026-11-11")]
    [InlineData("Betaalautomaat 10:19 pasnr. 012 X SNEEK", null)]
    [InlineData("Term 2026-11-10 22:15", "2026-11-10")]
    [InlineData("factuur 01-02-2025 oud", null)]
    public void De_dag_van_de_betaling_uit_de_omschrijving(string omschrijving, string? verwacht) =>
        Assert.Equal(verwacht is null ? null : DateOnly.Parse(verwacht, CultureInfo.InvariantCulture), Pinbetaling.Betaaldag(new Bankmutatie(new DateOnly(2026, 11, 12), -5m, omschrijving)));

    [Fact]
    public void Bedrag_datum_en_naam_is_zeker_en_zonder_naam_een_voorstel()
    {
        var dag = new DateOnly(2026, 11, 11);
        var gebak = Bon("ZOET", "40852548", 25.95m, dag);
        var kiosk = Bon("KIOSK", "146487", 13.85m, dag);
        OpenPost[] posten = [gebak, kiosk, Bon("ROOM", "61630446", 25.95m, dag)];

        var metNaam = Afletteraar.Zoek(Pin(dag.AddDays(1), 25.95m, "BEA, Betaalpas CCV*BAKKERIJ ZOET,PAS012 NR:4SF001, 11.11.26/10.19 SNEEK"), posten, Winkels);
        var zonderNaam = Afletteraar.Zoek(Pin(dag, 13.85m, "Pasvolgnr: 003 11-11-2026 08:02", "SERVEX BV ZWOLLE"), posten, Winkels);

        Assert.Equal(gebak, Assert.Single(metNaam.Automatisch!.Posten));
        Assert.Equal("Pinbetaling: bedrag, datum en naam", metNaam.Kandidaten[0].Reden);
        Assert.Null(zonderNaam.Automatisch);
        Assert.Equal((kiosk, "Bedrag en datum"), (zonderNaam.Kandidaten[0].Posten[0], zonderNaam.Kandidaten[0].Reden));
    }

    [Fact]
    public void Een_bon_van_een_andere_dag_of_een_ander_bedrag_wordt_nooit_vanzelf_afgeletterd()
    {
        var dag = new DateOnly(2026, 11, 11);
        OpenPost[] posten = [Bon("ZOET", "1", 25.95m, dag)];

        // Drie weken later dezelfde winkel en hetzelfde bedrag: een andere aankoop. Alleen het bedrag blijft als voorstel.
        var later = Afletteraar.Zoek(Pin(dag.AddDays(21), 25.95m, "BAKKERIJ ZOET SNEEK 02-12-2026 09:00"), posten, Winkels);
        var eerder = Afletteraar.Zoek(Pin(dag.AddDays(-1), 25.95m, "BAKKERIJ ZOET SNEEK"), posten, Winkels);
        var anderBedrag = Afletteraar.Zoek(Pin(dag, 25.90m, "BAKKERIJ ZOET SNEEK 11-11-2026 10:19"), posten, Winkels);

        Assert.Null(later.Automatisch);
        Assert.Equal("Alleen bedrag", Assert.Single(later.Kandidaten).Reden);
        Assert.Null(eerder.Automatisch);
        Assert.Null(anderBedrag.Automatisch);
        Assert.Empty(anderBedrag.Kandidaten);
    }

    [Fact]
    public void Twee_bonnen_van_dezelfde_winkel_met_hetzelfde_bedrag_alleen_zeker_op_de_dag_zelf()
    {
        var maandag = new DateOnly(2026, 11, 9);
        var eerste = Bon("BRUG", "A", 60m, maandag);
        var tweede = Bon("BRUG", "B", 60m, maandag.AddDays(2));
        OpenPost[] posten = [eerste, tweede];

        // De omschrijving noemt de dag: dan is het die bon. Zonder dag (alleen de boekdatum, beide binnen het venster) niet.
        var metDag = Afletteraar.Zoek(Pin(maandag.AddDays(3), 60m, "DE BRUG TANKSTATION Pasvolgnr 003 11-11-2026 17:40"), posten, Winkels);
        var zonderDag = Afletteraar.Zoek(Pin(maandag.AddDays(3), 60m, "DE BRUG TANKSTATION DRACHTEN"), posten, Winkels);

        Assert.Equal(tweede, Assert.Single(metDag.Automatisch!.Posten));
        Assert.Null(zonderDag.Automatisch);
        Assert.Equal([tweede, eerste], zonderDag.Kandidaten.Select(k => k.Posten[0]));
    }

    [Fact]
    public void Een_overboeking_met_het_iban_van_een_ander_gaat_niet_als_pinbetaling()
    {
        var dag = new DateOnly(2026, 11, 11);
        Relatie[] relaties = [new("ZOET", "Bakkerij Zoet", ["NL20INGB0001234567"])];
        OpenPost[] posten = [Bon("ZOET", "1", 25.95m, dag)];

        // Naam, bedrag en dag kloppen, maar het geld gaat naar een ander IBAN dan wij van de bakker kennen.
        var advies = Afletteraar.Zoek(new Bankmutatie(dag, -25.95m, "Bakkerij Zoet 11-11-2026", "NL91ABNA0417164300", "Bakkerij Zoet"), posten, relaties);

        Assert.Null(advies.Automatisch);
    }

    /// <summary>Een winkel: de naam op de bon en hoe de bank hem noemt.</summary>
    private sealed record Winkel(string Code, string Bonnaam, string Banknaam, string Plaats, decimal Van, decimal Tot, bool NaamOpAfschrift = true);

    private static readonly Winkel[] Alle =
    [
        new("PAXO", "Klusmarkt Paxo", "KLUSMARKT PAXO", "ZWOLLE", 4, 180),
        new("GAMAR", "Bouwhal Gamar", "Gamar 0412", "ASSEN", 4, 180),
        new("MOER", "IJzerhandel De Moer", "DE MOER IJZERHANDEL", "SNEEK", 3, 90),
        new("HEIN", "Albertus Hein Supermarkt", "ALBERTUS HEIN 1337", "LEEUWARDEN", 3, 95),
        new("JUMPER", "Supermarkt De Jumper", "JUMPER", "HEERENVEEN", 3, 95),
        new("HOEKJE", "Buurtsuper Het Hoekje", "HET HOEKJE", "BOLSWARD", 2, 40),
        new("ZOET", "Bakkerij Zoet", "CCV*BAKKERIJ ZOET", "SNEEK", 2, 30),
        new("ROOM", "Banketbakker Roomhart", "Zettle_*Banketb Roomha", "JOURE", 2, 30),
        new("BRUG", "Tankstation De Brug", "DE BRUG TANKSTATION", "DRACHTEN", 20, 110),
        new("NOORD", "Brandstof Noord", "BRANDSTOFNOORD", "FRANEKER", 20, 110),
        new("KADE", "Lunchroom De Kade", "SumUp *Lunchroom De Kad", "HARLINGEN", 5, 60),
        new("ANKER", "Eetcafe Het Anker", "HET ANKER", "STAVOREN", 5, 80),
        new("KRUID", "Drogisterij Kruidhof", "KRUIDHOF 2211", "DOKKUM", 2, 45),
        new("PARK", "Parkeergarage Centrum", "PARKEREN CENTRUM", "GRONINGEN", 2, 18),

        // De bank noemt de exploitant, niet de naam op de bon: alleen bedrag en datum blijven over.
        new("KIOSK", "Kiosk Perron 1", "SERVEX BV", "ZWOLLE", 2, 20, NaamOpAfschrift: false),
        new("PIZZA", "Pizzeria Bella", "HORECA GROEP NOORD BV", "SNEEK", 10, 70, NaamOpAfschrift: false),
    ];

    /// <summary>Hoe vier banken een pinbetaling omschrijven: met of zonder de dag, de naam in de omschrijving of als tegenpartij.</summary>
    private static Bankmutatie Pinregel(Random r, Winkel w, DateOnly dag, decimal bedrag)
    {
        var tijd = $"{r.Next(7, 22):00}:{r.Next(0, 60):00}";
        var terminal = $"{r.Next(100000, 999999)}";
        return r.Next(4) switch
        {
            0 => new(dag.AddDays(r.Next(0, 3)), -bedrag, $"BEA, Betaalpas {w.Banknaam},PAS012 NR:{terminal}, {dag:dd.MM.yy}/{tijd.Replace(':', '.')} {w.Plaats}"),
            1 => new(dag.AddDays(r.Next(0, 3)), -bedrag, $"Pasvolgnr: 003 {dag:dd-MM-yyyy} {tijd} Transactie: {terminal} Term: T{r.Next(1000, 9999)}", null, $"{w.Banknaam} {w.Plaats}"),
            2 => new(dag.AddDays(r.Next(0, 2)), -bedrag, $"Betaalautomaat {tijd} pasnr. 012 {w.Banknaam} {w.Plaats}"),
            _ => new(dag.AddDays(r.Next(0, 3)), -bedrag, $"{w.Banknaam} {w.Plaats} NLD Apple Pay", null, w.Banknaam),
        };
    }

    /// <summary>
    /// Vijftig willekeurige bonnen (vast zaadje) van zestien winkels over drie maanden, met opzet dubbele bedragen, tegen
    /// een afschrift met de pinbetalingen (in de vorm van vier banken) en afleiders: pinbetalingen zonder bon bij dezelfde
    /// winkels, hetzelfde bedrag bij een onbekende winkel, dezelfde winkel en hetzelfde bedrag weken later. Verwerkt
    /// zoals het bankscherm: op volgorde, en wat zeker is wordt meteen afgeletterd. Nooit een verkeerde bon vanzelf.
    /// </summary>
    [Fact]
    public void Vijftig_willekeurige_bonnen_tegen_het_bankafschrift()
    {
        var r = new Random(20261111);
        var relaties = Alle.Select(w => new Relatie(w.Code, w.Bonnaam, [])).ToList();
        var bonnen = new List<(OpenPost Post, Winkel Winkel)>();
        var begin = new DateOnly(2026, 1, 5);
        for (var i = 0; i < 50; i++)
        {
            var w = Alle[r.Next(Alle.Length)];
            var dag = begin.AddDays(r.Next(0, 85));

            // Een op de vijf bonnen krijgt het bedrag van een eerdere bon (vaste bedragen: parkeren, koffie, tanken).
            var bedrag = i > 5 && r.Next(5) == 0 ? -bonnen[r.Next(bonnen.Count)].Post.Bedrag : Math.Round(w.Van + ((decimal)r.NextDouble() * (w.Tot - w.Van)), 2);
            bonnen.Add((Bon(w.Code, $"{r.Next(100000, 999999)}", bedrag, dag), w));
        }

        // 46 bonnen zijn gepind, 4 contant betaald (geen regel op het afschrift).
        var regels = new List<(Bankmutatie Mutatie, Guid? Hoort)>();
        foreach (var (post, winkel) in bonnen.Take(46))
        {
            regels.Add((Pinregel(r, winkel, post.Datum, -post.Bedrag), post.Id));
        }

        // Afleiders: 30 pinbetalingen zonder bon.
        for (var i = 0; i < 30; i++)
        {
            var (post, winkel) = bonnen[r.Next(bonnen.Count)];
            regels.Add((i % 3) switch
            {
                0 => (Pinregel(r, winkel, post.Datum.AddDays(r.Next(14, 40)), -post.Bedrag), null),
                1 => (new Bankmutatie(post.Datum, post.Bedrag, $"Betaalautomaat 12:00 pasnr. 012 WARENHUIS HEMO {winkel.Plaats}"), null),
                _ => (Pinregel(r, winkel, post.Datum.AddDays(r.Next(-3, 4)), Math.Round(-post.Bedrag + 0.5m + r.Next(1, 30), 2)), (Guid?)null),
            });
        }

        var open = bonnen.Select(b => b.Post).ToList();
        var (juist, fout, voorstelBovenaan, voorstelLager, gemist, afleiderVanzelf, afleiderVoorstel) = (0, 0, 0, 0, 0, 0, 0);
        var verslag = new StringBuilder();
        foreach (var (mutatie, hoort) in regels.OrderBy(x => x.Mutatie.Datum).ThenBy(x => x.Mutatie.Omschrijving, StringComparer.Ordinal))
        {
            var advies = Afletteraar.Zoek(mutatie, open, relaties);
            var vanzelf = advies.Automatisch?.Posten is [var enige] ? enige.Id : (Guid?)null;
            var uitkomst = (hoort, vanzelf) switch
            {
                (null, null) when advies.Kandidaten.Count == 0 => "afleider: niets",
                (null, null) => "afleider: voorstel",
                (null, _) => "AFLEIDER VANZELF",
                (_, _) when vanzelf == hoort => "vanzelf goed",
                (_, not null) => "VANZELF FOUT",
                _ when advies.Kandidaten is [{ Posten: [var eerste] }, ..] && eerste.Id == hoort => "voorstel bovenaan",
                _ when advies.Kandidaten.Any(k => k.Posten.Any(p => p.Id == hoort)) => "voorstel lager",
                _ => "gemist",
            };
            _ = uitkomst switch
            {
                "vanzelf goed" => juist++,
                "VANZELF FOUT" => fout++,
                "voorstel bovenaan" => voorstelBovenaan++,
                "voorstel lager" => voorstelLager++,
                "gemist" => gemist++,
                "AFLEIDER VANZELF" => afleiderVanzelf++,
                "afleider: voorstel" => afleiderVoorstel++,
                _ => 0,
            };
            verslag.AppendLine(CultureInfo.InvariantCulture, $"{mutatie.Datum:dd-MM} {-mutatie.Bedrag,8:0.00}  {uitkomst,-18} {(advies.Kandidaten.Count > 0 ? advies.Kandidaten[0].Reden : null),-45} {mutatie.TegenNaam} {mutatie.Omschrijving}");

            // Zoals het bankscherm: wat zeker is, wordt meteen afgeletterd.
            if (vanzelf is { } id)
            {
                open = [.. open.Select(p => p.Id == id ? p with { Openstaand = 0m } : p)];
            }
        }

        var metNaam = bonnen.Take(46).Count(b => b.Winkel.NaamOpAfschrift);
        verslag.AppendLine(CultureInfo.InvariantCulture, $"\n46 gepinde bonnen ({metNaam} met de winkelnaam op het afschrift): vanzelf goed {juist}, vanzelf fout {fout}, voorstel bovenaan {voorstelBovenaan}, voorstel lager {voorstelLager}, gemist {gemist}");
        verslag.AppendLine(CultureInfo.InvariantCulture, $"30 afleiders: vanzelf (fout) {afleiderVanzelf}, als voorstel {afleiderVoorstel}");
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_BONNENMAP") is { Length: > 0 } map && Directory.Exists(map))
        {
            File.WriteAllText(Path.Combine(map, "verslag-bank.txt"), verslag.ToString());
        }

        // Nooit een verkeerde bon of een betaling zonder bon vanzelf; met de naam op het afschrift vrijwel alles vanzelf;
        // de rest staat als voorstel klaar.
        Assert.True(fout == 0 && afleiderVanzelf == 0, verslag.ToString());
        Assert.True(juist >= metNaam - 3, verslag.ToString());
        Assert.True(gemist == 0, verslag.ToString());
    }
}
