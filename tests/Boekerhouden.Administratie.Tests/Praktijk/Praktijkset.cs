using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>Een leverancier, klant of de eigen onderneming (verzonnen).</summary>
internal sealed record Partijdata(string Naam, string Iban, string? BtwNummer, string? Kvk, string Land, string Straat, string Postcode, string Plaats)
{
    public Partij Partij => new(Naam, new Adres(Straat, Postcode, Plaats, Land), BtwNummer, Kvk, Ibans: [Iban]);
}

/// <summary>
/// Een factuurregel: zoals op het papier (aantal, prijs, de code van de afzender) en hoe wij hem boeken (rekening en
/// onze btw-code: bij inkoop de I-codes, bij verkoop dezelfde V-code).
/// </summary>
internal sealed record Regeldata(string Omschrijving, decimal Aantal, decimal Prijs, string Papiercode, string Rekening, string Onzecode)
{
    public decimal Netto => Math.Round(Aantal * Prijs, 2, MidpointRounding.AwayFromZero);
}

internal enum Formaat
{
    Ubl,
    Pdf,
}

/// <summary>Hoe een factuur betaald wordt (of niet); bepaalt de bankmutaties en wat er aan het eind open staat.</summary>
internal enum Betaalwijze
{
    Kenmerk,
    Nummer,
    Som,
    Deel,
    Dubbel,
    MinCredit,
    Verschil,
    Korting,
    AnderIban,
    Incasso,
    IncassoStorno,
    Verzamel,
    Verrekend,
    Terugbetaald,
    Open,
}

/// <summary>Een in- of verkoopfactuur. Bedragen zoals op het papier: positief, ook bij een creditnota.</summary>
internal sealed record Factuurdata(
    bool Inkoop,
    Partijdata Partij,
    string Nummer,
    DateOnly Datum,
    DateOnly Vervaldatum,
    string Omschrijving,
    IReadOnlyList<Regeldata> Regels,
    Formaat Formaat,
    Betaalwijze Betaling,
    bool Credit = false,
    string? CreditVoor = null,
    string? Kenmerk = null,
    int Ronde = 0)
{
    /// <summary>De factuur zoals de afzender hem schrijft (credit: negatieve aantallen).</summary>
    public Verkoopfactuur Papier(string relatie) =>
        new(relatie, Partij.Partij, Nummer, Datum, Vervaldatum, Omschrijving,
            [.. Regels.Select(r => new Verkoopregel(r.Omschrijving, Credit ? -r.Aantal : r.Aantal, r.Prijs, r.Rekening, r.Papiercode))], Kenmerk, CreditVoor);

    public decimal Exclusief => Regels.Sum(r => r.Netto);

    /// <summary>Btw per papiercode over de som van de regels, zoals de afzender rekent.</summary>
    public IReadOnlyList<(string Code, decimal Grondslag, decimal Btw)> Btw =>
        [.. Regels.GroupBy(r => r.Papiercode).Select(g => (g.Key, g.Sum(r => r.Netto), Math.Round(g.Sum(r => r.Netto) * Praktijkset.Percentage(g.Key) / 100m, 2, MidpointRounding.AwayFromZero)))];

    public decimal BtwTotaal => Btw.Sum(b => b.Btw);

    public decimal Totaal => Exclusief + BtwTotaal;

    /// <summary>Met teken zoals in de administratie: een creditnota negatief.</summary>
    public decimal Getekend => Credit ? -Totaal : Totaal;

    /// <summary>
    /// De btw per regel, zo verdeeld dat de som per code klopt met de factuur (de laatste regel van een code krijgt het
    /// afrondingsverschil).
    /// </summary>
    public decimal RegelBtw(int index)
    {
        var regel = Regels[index];
        var pct = Praktijkset.Percentage(regel.Papiercode);
        if (pct == 0)
        {
            return 0m;
        }

        var zelfde = Regels.Select((r, i) => (r, i)).Where(x => x.r.Papiercode == regel.Papiercode).ToList();
        if (zelfde[^1].i != index)
        {
            return Math.Round(regel.Netto * pct / 100m, 2, MidpointRounding.AwayFromZero);
        }

        var totaal = Btw.Single(b => b.Code == regel.Papiercode).Btw;
        return totaal - zelfde.Take(zelfde.Count - 1).Sum(x => Math.Round(x.r.Netto * pct / 100m, 2, MidpointRounding.AwayFromZero));
    }

    public string Bestandsnaam => $"{Datum:yyyy-MM-dd} {Praktijkset.Veilig(Partij.Naam)} {Praktijkset.Veilig(Nummer)}{(Formaat == Formaat.Ubl ? ".xml" : ".pdf")}";
}

/// <summary>Wat er met een bankmutatie moet gebeuren als het pakket hem niet zelf boekt.</summary>
internal enum Afhandelsoort
{
    Afletteren,
    OpRekening,
}

/// <param name="Nummers">Bij afletteren: de facturen (ook al betaalde, bij een dubbele betaling of terugbetaling).</param>
/// <param name="Verschil">Bij afletteren met korting: waar het verschil heen gaat.</param>
internal sealed record Afhandeling(Afhandelsoort Soort, IReadOnlyList<string> Nummers, string? Rekening = null, string? BtwCode = null, string? Verschilrekening = null,
    string? Verschilcode = null, string Toelichting = "")
{
    public static Afhandeling Op(params string[] nummers) => new(Afhandelsoort.Afletteren, nummers, Toelichting: $"Afletteren op {string.Join(", ", nummers)}");

    public static Afhandeling OpRekening(string rekening, string? btw, string toelichting) => new(Afhandelsoort.OpRekening, [], rekening, btw, Toelichting: toelichting);
}

/// <summary>
/// Een mutatie op het afschrift. Bij een verzamelbetaling staan de betalingen in <see cref="Batch"/> (één regel op het
/// afschrift, in de administratie per betaling een mutatie).
/// </summary>
internal sealed record Bankpost(
    DateOnly Datum,
    decimal Bedrag,
    string? Naam,
    string? Iban,
    string Omschrijving,
    string? Kenmerk,
    string EndToEnd,
    Afhandeling Afhandeling,
    IReadOnlyList<Bankpost>? Batch = null,
    bool Storno = false,
    bool Incasso = false);

/// <summary>Een bestand voor de test: de naam, de map (inkoop, verkoop, bank) en de inhoud.</summary>
internal sealed record Praktijkbestand(string Map, string Naam, byte[] Inhoud);

/// <summary>
/// Een verzonnen kwartaal van Fietsenhandel De Spaak B.V. (eerste kwartaal 2026, bank tot en met april): 100
/// inkoopfacturen van 15 leveranciers (UBL en PDF, 21%, 9%, vrijgesteld, verlegd, uit de EU, creditnota's), 100
/// verkoopfacturen aan 20 klanten (uit het kassapakket, UBL en PDF, ook binnen de EU, uitvoer en creditnota's) en de
/// bank: een betaalrekening (CAMT.053 per maand, april twee keer overlappend) en een spaarrekening (MT940). De bank zit vol
/// lastige gevallen: verzamelbetalingen, incasso's met een storno, sombetalingen, deelbetalingen, dubbel betaald en
/// terugbetaald, betaald min creditnota, kleine verschillen, korting, een ander IBAN, de Belastingdienst, lonen,
/// overboekingen tussen de eigen rekeningen (kruisposten) en een onbekende ontvangst. Alles is verzonnen.
/// </summary>
internal sealed class Praktijkset
{
    public static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");
    public static readonly DateOnly Begin = new(2026, 1, 1);
    public static readonly DateOnly Eind = new(2026, 4, 30);

    public const decimal BeginBetaal = 18_500m;
    public const decimal BeginSpaar = 25_000m;
    public const decimal Inventaris = 12_000m;
    public const decimal BtwVierdeKwartaal = 2_845m;
    public const decimal Loonheffing = 1_900m;
    public const decimal Aandelenkapitaal = 18_000m;
    public const decimal Rente = 37.50m;
    public const string Kruisposten = "1190";

    public static readonly Partijdata Eigen = new("Fietsenhandel De Spaak B.V.", "NL20INGB0001234567", "NL123456782B01", "12345678", "NL", "Dorpsstraat 1", "1234 AB", "Ergens");
    public static readonly string SpaarIban = MaakIban("NL", "INGB", "0009876543");

    /// <summary>De twee medewerkers (netto loon per maand).</summary>
    public static readonly (string Naam, string Iban, decimal Netto)[] Medewerkers =
    [
        ("P. Monteur", MaakIban("NL", "RABO", "0300112233"), 2_050m),
        ("S. Verkoper", MaakIban("NL", "ABNA", "0400223344"), 1_750m),
    ];

    public List<Factuurdata> Inkoop { get; } = [];

    public List<Factuurdata> Verkoop { get; } = [];

    public List<Bankpost> Betaal { get; } = [];

    public List<Bankpost> Spaar { get; } = [];

    /// <summary>Beginbalans en de loonjournaalposten van januari tot en met maart (memoriaal).</summary>
    public List<Journaalpost> Memoriaal { get; } = [];

    /// <summary>Wat de klanten en wij aan het eind nog moeten betalen (nummer, bedrag).</summary>
    public Dictionary<string, decimal> OpenDebiteuren { get; } = [];

    public Dictionary<string, decimal> OpenCrediteuren { get; } = [];

    /// <summary>De onbekende ontvangst die op de vraagposten komt.</summary>
    public decimal Vraagpost { get; private set; }

    /// <summary>De btw van het eerste kwartaal zoals die volgens de facturen en de bank moet zijn.</summary>
    public Btwaangifte AangifteQ1 { get; private set; } = null!;

    /// <summary>Korting (incl. btw) die de klant inhield en die wij bij de leverancier inhielden.</summary>
    public decimal KortingVerkoop { get; private set; }

    public decimal KortingInkoop { get; private set; }

    private readonly Random toeval = new(20260101);

    /// <summary>Hoe vaak de facturen voorkomen (1 = de gewone set van 100 + 100).</summary>
    public int Rondes { get; private init; } = 1;

    private int ronde;

    /// <summary>Het factuurnummer in de huidige ronde: het laatste getal 1000 per ronde hoger (ronde 0 = zoals het staat).</summary>
    private string N(string nummer) => ronde == 0 ? nummer : Regex.Replace(nummer, @"\d+(?=\D*$)", m =>
        (long.Parse(m.Value, CultureInfo.InvariantCulture) + ronde * 1000L).ToString(CultureInfo.InvariantCulture).PadLeft(m.Value.Length, '0'));
    private int volgnummer;

    /// <summary>De set; met <paramref name="rondes"/> groter dan 1 komen dezelfde soorten facturen (met andere nummers en bedragen) zo vaak voor, voor een stresstest.</summary>
    public static Praktijkset Maak(int rondes = 1)
    {
        var set = new Praktijkset { Rondes = rondes };
        set.MaakInkoop();
        set.MaakVerkoop();
        set.MaakMemoriaal();
        set.MaakBank();
        return set;
    }

    // ---------------------------------------------------------------- partijen

    private static readonly Partijdata Groothandel = Leverancier("Rijwielgroothandel Noord B.V.", "RABO", "0101010101", "Groningen", 1);
    private static readonly Partijdata Onderdelen = Leverancier("Onderdelen Centrale B.V.", "INGB", "0202020202", "Utrecht", 2);
    private static readonly Partijdata Energie = Leverancier("Energie Oost B.V.", "ABNA", "0303030303", "Zwolle", 3);
    private static readonly Partijdata Telecom = Leverancier("Telecom Noord B.V.", "INGB", "0404040404", "Leeuwarden", 4);
    private static readonly Partijdata Vastgoed = Leverancier("Vastgoed De Hoek B.V.", "RABO", "0505050505", "Ergens", 5);
    private static readonly Partijdata Koffie = Leverancier("Koffie en Co B.V.", "INGB", "0606060606", "Amersfoort", 6);
    private static readonly Partijdata Hamer = Leverancier("Bouwbedrijf Hamer B.V.", "ABNA", "0707070707", "Ergens", 7);
    private static readonly Partijdata Cloud = new("Wolkkassa Ltd", MaakIban("IE", "AIBK", "93115212345678"), "IE6388047V", null, "IE", "Harbour Road 12", "D02 X285", "Dublin");
    private static readonly Partijdata Drukkerij = Leverancier("Drukkerij Pers B.V.", "RABO", "0909090909", "Deventer", 9);
    private static readonly Partijdata Lease = Leverancier("Lease Auto B.V.", "INGB", "0110110110", "Amsterdam", 10);
    private static readonly Partijdata Verzekeraar = Leverancier("Verzekeraar Zeker N.V.", "ABNA", "0220220220", "Den Haag", 11);
    private static readonly Partijdata Cijfers = Leverancier("Administratiekantoor Cijfers B.V.", "RABO", "0330330330", "Ergens", 12);
    private static readonly Partijdata Transport = Leverancier("Transport Snel B.V.", "INGB", "0440440440", "Meppel", 13);
    private static readonly Partijdata Kantoor = Leverancier("Kantoorvakhandel Pen B.V.", "ABNA", "0550550550", "Assen", 14);
    private static readonly Partijdata Reclame = Leverancier("Reclamebureau Vonk B.V.", "RABO", "0660660660", "Heerenveen", 15);

    private static readonly Partijdata Fietsplan = Zakelijk("Fietsplan Zakelijk B.V.", "INGB", "0711711711", "Utrecht", 21);
    private static readonly Partijdata Hotel = Zakelijk("Hotel De Klant B.V.", "RABO", "0722722722", "Ergens", 22);
    private static readonly Partijdata Bezorg = Zakelijk("Bezorgdienst Vlot B.V.", "ABNA", "0733733733", "Zwolle", 23);
    private static readonly Partijdata Sportclub = Zakelijk("Sportclub De Wielen", "INGB", "0744744744", "Ergens", 24, btw: false);
    private static readonly Partijdata Zorg = Zakelijk("Zorginstelling Rust", "RABO", "0755755755", "Ergens", 25, btw: false);
    private static readonly Partijdata Steen = Zakelijk("Bouwbedrijf Steen B.V.", "ABNA", "0766766766", "Meppel", 26);
    private static readonly Partijdata Strand = Zakelijk("Fietsverhuur Strand B.V.", "INGB", "0777777777", "Zandvoort", 27);
    private static readonly Partijdata Muller = new("Radhaus Müller GmbH", MaakIban("DE", "", "370400440532013000"), "DE811569869", null, "DE", "Hauptstraße 5", "50667", "Köln");
    private static readonly Partijdata Velo = new("Velohaus Bergtal AG", MaakIban("CH", "", "00762011623852957"), null, null, "CH", "Bahnhofstrasse 9", "8001", "Zürich");
    private static readonly Partijdata School = Zakelijk("Scholengemeenschap Ergens", "RABO", "0788788788", "Ergens", 30, btw: false);

    private static readonly Partijdata[] Particulieren =
    [
        Particulier("J. de Vries", "0811811811", "Kerkstraat 4"),
        Particulier("A. Bakker", "0822822822", "Molenweg 17"),
        Particulier("M. Jansen", "0833833833", "Schoolstraat 2"),
        Particulier("S. Visser", "0844844844", "Brink 8"),
        Particulier("P. Smit", "0855855855", "Esweg 21"),
        Particulier("L. Meijer", "0866866866", "Vaart 3"),
        Particulier("R. de Boer", "0877877877", "Wijk 11"),
        Particulier("E. Mulder", "0888888888", "Dijk 6"),
        Particulier("T. de Groot", "0899899899", "Hoofdstraat 40"),
        Particulier("K. Bos", "0900900900", "Lindelaan 9"),
    ];

    /// <summary>De partner van J. de Vries, die een factuur van de eigen rekening betaalt.</summary>
    public static readonly (string Naam, string Iban) Partner = ("M. de Vries-Kok", MaakIban("NL", "SNSB", "0912912912"));

    private static Partijdata Leverancier(string naam, string bank, string rekening, string plaats, int n) =>
        new(naam, MaakIban("NL", bank, rekening), BtwNummerNl(n), (40000000 + n * 1013).ToString(CultureInfo.InvariantCulture), "NL", $"Industrieweg {n}", $"{9700 + n} AB", plaats);

    private static Partijdata Zakelijk(string naam, string bank, string rekening, string plaats, int n, bool btw = true) =>
        new(naam, MaakIban("NL", bank, rekening), btw ? BtwNummerNl(n) : null, (50000000 + n * 1013).ToString(CultureInfo.InvariantCulture), "NL", $"Stationsplein {n}", $"{3500 + n} CD", plaats);

    private static Partijdata Particulier(string naam, string rekening, string straat) =>
        new(naam, MaakIban("NL", "INGB", rekening), null, null, "NL", straat, "1234 XY", "Ergens");

    // ---------------------------------------------------------------- inkoop

    private void MaakInkoop()
    {
        for (ronde = 0; ronde < Rondes; ronde++)
        {
            var start = Inkoop.Count;
            MaakInkoopRonde();
            for (var i = start; i < Inkoop.Count; i++)
            {
                Inkoop[i] = Inkoop[i] with { Ronde = ronde };
            }
        }

        ronde = 0;
        if (Inkoop.Count != 100 * Rondes)
        {
            throw new InvalidOperationException($"{Inkoop.Count} inkoopfacturen in plaats van {100 * Rondes}.");
        }
    }

    private void MaakInkoopRonde()
    {
        // Rijwielgroothandel en Transport: wekelijks, betaald met een verzamelbetaling in de maand erna.
        for (var i = 0; i < 20; i++)
        {
            var datum = Begin.AddDays(2 + i * 4);
            var regels = new List<Regeldata> { new("Stadsfiets 28 inch", Aantal(2, 6), Prijs(380, 520), "V21", "7000", "I21") };
            if (i % 3 == 0)
            {
                regels.Add(new Regeldata("E-bike Comfort", Aantal(1, 3), Prijs(1_150, 1_550), "V21", "7000", "I21"));
            }

            if (i % 4 == 1)
            {
                regels.Add(new Regeldata("Fietsverlichting set", Aantal(5, 20), Prijs(8, 15), "V21", "7000", "I21"));
            }

            Inkoop.Add(new Factuurdata(true, Groothandel, N($"RG-26-{1001 + i}"), datum, datum.AddDays(30), "Levering fietsen", regels, Formaat.Ubl, Betaalwijze.Verzamel));
        }

        for (var i = 0; i < 20; i++)
        {
            var datum = Begin.AddDays(4 + i * 4);
            Inkoop.Add(new Factuurdata(true, Transport, N($"TS{260100 + i * 7}"), datum, datum.AddDays(30), "Transportkosten",
                [new Regeldata($"Transport rit {i + 1}", 1, Prijs(45, 180), "V21", "7000", "I21")], Formaat.Ubl, Betaalwijze.Verzamel));
        }

        // Onderdelen Centrale: PDF, per factuur betaald op nummer; een creditnota die met de volgende factuur verrekend wordt.
        for (var i = 0; i < 13; i++)
        {
            var datum = Begin.AddDays(5 + i * 6);
            if (i == 5)
            {
                Inkoop.Add(new Factuurdata(true, Onderdelen, N($"OC-2026-{501 + i}"), datum, datum.AddDays(14), "Retour onderdelen",
                    [new Regeldata("Retour kettingen", 10, 18.40m, "V21", "7000", "I21")], Formaat.Pdf, Betaalwijze.Verrekend, Credit: true, CreditVoor: N($"OC-2026-{501 + i - 1}")));
                continue;
            }

            Inkoop.Add(new Factuurdata(true, Onderdelen, N($"OC-2026-{501 + i}"), datum, datum.AddDays(14), "Onderdelen",
            [
                new Regeldata("Buitenbanden", Aantal(10, 30), Prijs(9, 16), "V21", "7000", "I21"),
                new Regeldata("Remblokken", Aantal(10, 40), Prijs(2, 5), "V21", "7000", "I21"),
            ], Formaat.Pdf, i == 6 ? Betaalwijze.MinCredit : Betaalwijze.Nummer));
        }

        // Maandelijks: energie (incasso, februari gestorneerd en opnieuw), telefoon (incasso), huur (kenmerk), lease (incasso), software uit Ierland.
        for (var m = 0; m < 3; m++)
        {
            var eerste = Begin.AddMonths(m);
            var maand = eerste.ToString("MMMM yyyy", Nl);
            Inkoop.Add(new Factuurdata(true, Energie, N($"EO{2026001 + m}"), eerste.AddDays(4), eerste.AddDays(19), $"Energie {maand}",
                [new Regeldata($"Elektriciteit en gas {maand}", 1, Prijs(410, 520), "V21", "4110", "I21")], Formaat.Ubl, m == 1 ? Betaalwijze.IncassoStorno : Betaalwijze.Incasso));
            Inkoop.Add(new Factuurdata(true, Telecom, N($"T-{9100234 + m}"), eerste.AddDays(6), eerste.AddDays(21), $"Abonnement {maand}",
                [new Regeldata($"Zakelijk abonnement {maand}", 1, 74.34m, "V21", "4310", "I21"), new Regeldata("Extra bundel", 1, 12.40m, "V21", "4310", "I21")], Formaat.Pdf, Betaalwijze.Incasso));
            var huur = N($"VH-2026-{m + 1:00}");
            Inkoop.Add(new Factuurdata(true, Vastgoed, huur, eerste, eerste, $"Huur winkelpand {maand}",
                [new Regeldata($"Huur Dorpsstraat 1 {maand}", 1, 2_750m, "VVR", "4100", "I0")], Formaat.Ubl, Betaalwijze.Kenmerk, Kenmerk: Betalingskenmerk.Maak("7" + huur)));
            Inkoop.Add(new Factuurdata(true, Lease, N($"LA{77000 + m}"), eerste.AddDays(9), eerste.AddDays(24), $"Lease bestelbus {maand}",
                [new Regeldata($"Leasetermijn bestelbus {maand}", 1, 489m, "V21", "4210", "I21")], Formaat.Ubl, Betaalwijze.Incasso));
            Inkoop.Add(new Factuurdata(true, Cloud, N($"INV-{4400 + m}"), eerste.AddDays(1), eerste.AddDays(15), $"Kassasoftware {maand}",
                [new Regeldata($"Kassasoftware licence {eerste:MMMM yyyy}", 1, 149m, "VVL", "4330", "IEU")], Formaat.Ubl, Betaalwijze.Nummer));
        }

        // Koffie: 9% en 21% op één factuur.
        for (var i = 0; i < 10; i++)
        {
            var datum = Begin.AddDays(7 + i * 8);
            Inkoop.Add(new Factuurdata(true, Koffie, N($"KC{3100 + i}"), datum, datum.AddDays(14), "Koffie en machine",
            [
                new Regeldata("Koffiebonen 1 kg", Aantal(3, 8), 17.95m, "V9", "4900", "I9"),
                new Regeldata("Melk en suiker", Aantal(2, 6), 3.49m, "V9", "4900", "I9"),
                new Regeldata("Huur koffiemachine", 1, 39.50m, "V21", "4900", "I21"),
            ], Formaat.Pdf, Betaalwijze.Nummer));
        }

        // Verlegd: de verbouwing van de werkplaats; de tweede staat aan het eind nog open.
        Inkoop.Add(new Factuurdata(true, Hamer, N("H-26-014"), new(2026, 1, 26), new(2026, 2, 9), "Verbouwing werkplaats termijn 1",
            [new Regeldata("Verbouwing werkplaats, eerste termijn", 1, 6_800m, "VVL", "4900", "IVL")], Formaat.Ubl, Betaalwijze.Nummer));
        Inkoop.Add(new Factuurdata(true, Hamer, N("H-26-031"), new(2026, 3, 27), new(2026, 4, 10), "Verbouwing werkplaats termijn 2",
            [new Regeldata("Verbouwing werkplaats, tweede termijn", 1, 4_250m, "VVL", "4900", "IVL")], Formaat.Ubl, Betaalwijze.Open));

        // Drukkerij: een creditnota, betaald als factuur min creditnota.
        Inkoop.Add(new Factuurdata(true, Drukkerij, N("DP-6610"), new(2026, 1, 12), new(2026, 1, 26), "Folders voorjaar",
            [new Regeldata("Folders A5, 5000 stuks", 1, 640m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.MinCredit));
        Inkoop.Add(new Factuurdata(true, Drukkerij, N("DP-6611"), new(2026, 1, 14), new(2026, 1, 28), "Drukfout folders",
            [new Regeldata("Korting drukfout folders", 1, 120m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.Verrekend, Credit: true, CreditVoor: N("DP-6610")));
        Inkoop.Add(new Factuurdata(true, Drukkerij, N("DP-6702"), new(2026, 2, 16), new(2026, 3, 2), "Posters",
            [new Regeldata("Posters A2", 25, 6.80m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.Nummer));
        Inkoop.Add(new Factuurdata(true, Drukkerij, N("DP-6788"), new(2026, 3, 10), new(2026, 3, 24), "Visitekaartjes",
            [new Regeldata("Visitekaartjes", 2, 64.50m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.Nummer));

        Inkoop.Add(new Factuurdata(true, Verzekeraar, N("POL-2026-118"), new(2026, 1, 3), new(2026, 1, 31), "Bedrijfsverzekering 1e kwartaal",
            [new Regeldata("Inventaris- en aansprakelijkheidsverzekering", 1, 1_245.60m, "VVR", "4500", "I0")], Formaat.Ubl, Betaalwijze.Kenmerk, Kenmerk: Betalingskenmerk.Maak($"82012611800{ronde + 1:00}")));
        Inkoop.Add(new Factuurdata(true, Cijfers, N("AC-2026-0042"), new(2026, 3, 30), new(2026, 4, 30), "Samenstellen jaarrekening 2025",
            [new Regeldata("Samenstellen jaarrekening 2025", 1, 1_850m, "V21", "4600", "I21")], Formaat.Pdf, Betaalwijze.Open));

        // Kantoorvakhandel: één factuur per abuis twee keer betaald; de leverancier betaalt terug.
        for (var i = 0; i < 12; i++)
        {
            var datum = Begin.AddDays(8 + i * 7);
            Inkoop.Add(new Factuurdata(true, Kantoor, N($"KV/{26}/{0150 + i:0000}"), datum, datum.AddDays(14), "Kantoorartikelen",
                [new Regeldata("Kantoorartikelen", 1, Prijs(25, 240), "V21", "4300", "I21")], Formaat.Pdf, i == 4 ? Betaalwijze.Dubbel : Betaalwijze.Nummer));
        }

        // Reclamebureau: de eerste betaald met 2% korting, de tweede nog open.
        Inkoop.Add(new Factuurdata(true, Reclame, N("RV-2026-007"), new(2026, 1, 20), new(2026, 2, 19), "Campagne voorjaar",
            [new Regeldata("Ontwerp campagne voorjaar", 1, 950m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.Korting));
        Inkoop.Add(new Factuurdata(true, Reclame, N("RV-2026-019"), new(2026, 3, 23), new(2026, 4, 22), "Social media maart",
            [new Regeldata("Social media maart", 1, 1_400m, "V21", "4400", "I21")], Formaat.Pdf, Betaalwijze.Open));

    }

    // ---------------------------------------------------------------- verkoop

    /// <summary>Een verkoopfactuur in de planning, voordat hij een nummer heeft.</summary>
    private sealed class Gepland(Partijdata klant, DateOnly datum, IReadOnlyList<Regeldata> regels, Betaalwijze betaling, Formaat formaat, Gepland? creditVoor = null)
    {
        public Partijdata Klant { get; } = klant;

        public DateOnly Datum { get; } = datum;

        public IReadOnlyList<Regeldata> Regels { get; } = regels;

        public Betaalwijze Betaling { get; } = betaling;

        public Formaat Formaat { get; } = formaat;

        public Gepland? CreditVoor { get; } = creditVoor;

        public string Nummer { get; set; } = "";

        public int Ronde { get; set; }
    }

    private void MaakVerkoop()
    {
        var planning = new List<Gepland>();
        for (ronde = 0; ronde < Rondes; ronde++)
        {
            var eersteVanRonde = planning.Count;
            void Plan(Partijdata klant, int aantal, int start, int stap, Func<int, IReadOnlyList<Regeldata>> regels, Func<int, Betaalwijze> betaling)
            {
                for (var i = 0; i < aantal; i++)
                {
                    planning.Add(new Gepland(klant, Begin.AddDays(start + i * stap), regels(i), betaling(i), Formaat.Ubl));
                }
            }

            Plan(Fietsplan, 13, 3, 6, _ => [new Regeldata("E-bike leasefiets", Aantal(1, 3), Prijs(1_850, 3_200), "V21", "8010", "V21")], _ => Betaalwijze.Som);
            Plan(Hotel, 6, 5, 14, _ => [new Regeldata("Onderhoud huurfietsen", Aantal(6, 12), 45m, "V9", "8000", "V9"), new Regeldata("Onderdelen", 1, Prijs(50, 200), "V21", "8010", "V21")],
                i => i < 3 ? Betaalwijze.Som : Betaalwijze.Kenmerk);
            Plan(Bezorg, 8, 4, 10, _ => [new Regeldata("Bakfiets elektrisch", 1, Prijs(2_400, 3_600), "V21", "8010", "V21"), new Regeldata("Reparatie en afstellen", 1, Prijs(60, 140), "V9", "8000", "V9")],
                i => i == 2 ? Betaalwijze.Deel : Betaalwijze.Kenmerk);
            Plan(Sportclub, 4, 9, 18, _ => [new Regeldata("Clubfietsen", Aantal(2, 4), Prijs(450, 700), "V21", "8010", "V21")], i => i == 1 ? Betaalwijze.Dubbel : Betaalwijze.Kenmerk);
            Plan(Zorg, 4, 11, 19, _ => [new Regeldata("Driewielfiets", 1, Prijs(1_400, 2_100), "V21", "8010", "V21")], i => i == 1 ? Betaalwijze.MinCredit : Betaalwijze.Nummer);
            Plan(Steen, 4, 6, 20, _ => [new Regeldata("Bedrijfsfiets", Aantal(1, 3), Prijs(900, 1_300), "V21", "8010", "V21")], i => i == 2 ? Betaalwijze.Verschil : Betaalwijze.Kenmerk);
            Plan(Strand, 5, 8, 16, _ => [new Regeldata("Huurfiets", Aantal(4, 10), Prijs(520, 640), "V21", "8010", "V21")], i => i == 3 ? Betaalwijze.Korting : Betaalwijze.Kenmerk);
            Plan(Muller, 3, 10, 25, _ => [new Regeldata("Lastenrad", Aantal(2, 4), Prijs(2_100, 2_600), "VEU", "8010", "VEU")], _ => Betaalwijze.Nummer);
            Plan(Velo, 2, 20, 45, _ => [new Regeldata("E-bike Trekking", Aantal(3, 5), Prijs(2_300, 2_800), "VEX", "8010", "VEX")], i => i == 0 ? Betaalwijze.Nummer : Betaalwijze.Open);
            Plan(School, 2, 13, 35, _ => [new Regeldata("Schoolfietsen", Aantal(5, 12), Prijs(420, 560), "V21", "8010", "V21")], _ => Betaalwijze.Kenmerk);

            // Particulieren: reparaties (9%) en fietsen (21%); een paar betalen niet, J. de Vries één keer via de rekening van de partner.
            for (var p = 0; p < Particulieren.Length; p++)
            {
                var aantal = p < 5 ? 5 : 4;
                for (var i = 0; i < aantal; i++)
                {
                    var betaling = (p, i) switch
                    {
                        (0, 2) => Betaalwijze.AnderIban,
                        (3, 3) or (6, 3) or (8, 3) => Betaalwijze.Open,
                        _ when i % 2 == 1 => Betaalwijze.Nummer,
                        _ => Betaalwijze.Kenmerk,
                    };
                    IReadOnlyList<Regeldata> regels = (p + i) % 3 == 0
                        ? [new Regeldata("Stadsfiets", 1, Prijs(600, 1_400), "V21", "8010", "V21"), new Regeldata("Ringslot", 1, 34.95m, "V21", "8010", "V21")]
                        : [new Regeldata("Reparatie fiets", 1, Prijs(35, 180), "V9", "8000", "V9")];
                    planning.Add(new Gepland(Particulieren[p], Begin.AddDays(2 + p * 3 + i * 15), regels, betaling, i == 1 ? Formaat.Pdf : Formaat.Ubl));
                }
            }

            // Vier creditnota's: Zorginstelling (verrekend in de betaling), Scholengemeenschap en A. Bakker (terugbetaald) en
            // Fietsplan (in de sombetaling van de maand).
            void Credit(Partijdata klant, Func<Gepland, bool> welke, Betaalwijze betaling, decimal deel)
            {
                var bron = planning.Skip(eersteVanRonde).First(f => f.Klant == klant && f.CreditVoor is null && welke(f));
                var regel = bron.Regels[0];
                planning.Add(new Gepland(klant, bron.Datum.AddDays(3),
                    [regel with { Omschrijving = $"Creditering {regel.Omschrijving.ToLowerInvariant()}", Aantal = 1, Prijs = Math.Round(regel.Prijs * deel, 2) }], betaling, Formaat.Ubl, bron));
            }

            Credit(Zorg, f => f.Betaling == Betaalwijze.MinCredit, Betaalwijze.Verrekend, 0.25m);
            Credit(School, _ => true, Betaalwijze.Terugbetaald, 0.10m);
            Credit(Particulieren[1], f => f.Regels[0].Papiercode == "V21", Betaalwijze.Terugbetaald, 1m);
            Credit(Fietsplan, _ => true, Betaalwijze.Som, 0.20m);

            foreach (var g in planning.Skip(eersteVanRonde))
            {
                g.Ronde = ronde;
            }
        }

        ronde = 0;

        // Het kassapakket nummert door op datum.
        var nummer = 0;
        foreach (var f in planning.OrderBy(f => f.Datum).ThenBy(f => f.CreditVoor is null ? 0 : 1).ThenBy(f => f.Klant.Naam, StringComparer.Ordinal).ToList())
        {
            f.Nummer = $"VF26-{++nummer:0000}";
            var credit = f.CreditVoor is not null;
            Verkoop.Add(new Factuurdata(false, f.Klant, f.Nummer, f.Datum, credit ? f.Datum : f.Datum.AddDays(14), credit ? $"Creditnota bij {f.CreditVoor!.Nummer}" : $"Factuur {f.Nummer}",
                f.Regels, f.Formaat, f.Betaling, Credit: credit, CreditVoor: f.CreditVoor?.Nummer, Kenmerk: credit ? null : Betalingskenmerk.Maak(f.Nummer), Ronde: f.Ronde));
        }

        if (Verkoop.Count != 100 * Rondes || Verkoop.Any(f => f.Datum > new DateOnly(2026, 3, 31)))
        {
            throw new InvalidOperationException($"{Verkoop.Count} verkoopfacturen in plaats van {100 * Rondes}, of een na het kwartaal.");
        }
    }

    // ---------------------------------------------------------------- memoriaal

    private void MaakMemoriaal()
    {
        Memoriaal.Add(new Journaalpost("MEM", "2026-0001", Begin, "Beginbalans 2026",
        [
            new Journaalregel("1100", BeginBetaal, "Bank"),
            new Journaalregel("1110", BeginSpaar, "Spaarrekening"),
            new Journaalregel("0200", Inventaris, "Inventaris"),
            new Journaalregel("1510", -BtwVierdeKwartaal, "Btw 4e kwartaal 2025"),
            new Journaalregel("1520", -Loonheffing, "Loonheffing december 2025"),
            new Journaalregel("0500", -Aandelenkapitaal, "Aandelenkapitaal"),
            new Journaalregel("0510", -(BeginBetaal + BeginSpaar + Inventaris - BtwVierdeKwartaal - Loonheffing - Aandelenkapitaal), "Overige reserves"),
        ]));
        for (var m = 0; m < 3; m++)
        {
            var dag = Begin.AddMonths(m + 1).AddDays(-1);
            var netto = Medewerkers.Sum(w => w.Netto);
            Memoriaal.Add(new Journaalpost("MEM", $"2026-{m + 2:0000}", dag, $"Loonjournaal {dag.ToString("MMMM yyyy", Nl)}",
            [
                new Journaalregel("4000", 4_800m, "Brutolonen"),
                new Journaalregel("4010", 900m, "Werkgeverslasten"),
                new Journaalregel("1520", -Loonheffing, "Loonheffing en premies"),
                new Journaalregel("1800", -netto, "Te betalen nettolonen"),
            ]));
        }
    }

    // ---------------------------------------------------------------- bank

    private string E2e() => $"SP{++volgnummer:000000}";

    private Bankpost Post(DateOnly datum, decimal bedrag, Partijdata? partij, string omschrijving, Afhandeling afhandeling, string? kenmerk = null, bool incasso = false) =>
        new(datum, bedrag, partij?.Naam, partij?.Iban, omschrijving, kenmerk, E2e(), afhandeling, Incasso: incasso);

    private void MaakBank()
    {
        // ---- ontvangsten van klanten
        foreach (var groep in Verkoop.Where(f => !f.Credit).GroupBy(f => f.Partij))
        {
            foreach (var f in groep)
            {
                var dag = Min(f.Datum.AddDays(toeval.Next(5, 26)), Eind);
                switch (f.Betaling)
                {
                    case Betaalwijze.Kenmerk:
                        Betaal.Add(Post(dag, f.Totaal, f.Partij, f.Kenmerk!, Afhandeling.Op(f.Nummer), kenmerk: f.Kenmerk));
                        break;
                    case Betaalwijze.Nummer:
                        Betaal.Add(Post(dag, f.Totaal, f.Partij, $"Factuur {f.Nummer}", Afhandeling.Op(f.Nummer)));
                        break;
                    case Betaalwijze.AnderIban:
                        Betaal.Add(new Bankpost(dag, f.Totaal, Partner.Naam, Partner.Iban, $"Fiets reparatie {f.Nummer} J de Vries", null, E2e(),
                            Afhandeling.Op(f.Nummer) with { Toelichting = $"Ander IBAN (partner van de klant): afletteren op {f.Nummer}" }));
                        break;
                    case Betaalwijze.Deel:
                        var eerste = Math.Round(f.Totaal / 2, 0);
                        Betaal.Add(Post(dag, eerste, f.Partij, $"Factuur {f.Nummer} deel 1", Afhandeling.Op(f.Nummer)));
                        Betaal.Add(Post(Min(dag.AddDays(21), Eind), f.Totaal - eerste, f.Partij, $"Factuur {f.Nummer} restant", Afhandeling.Op(f.Nummer)));
                        break;
                    case Betaalwijze.Dubbel:
                        Betaal.Add(Post(dag, f.Totaal, f.Partij, f.Kenmerk!, Afhandeling.Op(f.Nummer), kenmerk: f.Kenmerk));
                        Betaal.Add(Post(dag.AddDays(6), f.Totaal, f.Partij, $"Betaling {f.Nummer}", Afhandeling.Op(f.Nummer) with { Toelichting = $"Dubbel betaald: afletteren op {f.Nummer} (te veel ontvangen)" }));
                        Betaal.Add(Post(new DateOnly(2026, 4, 14), -f.Totaal, f.Partij, $"Terugbetaling dubbel betaald {f.Nummer}", Afhandeling.Op(f.Nummer) with { Toelichting = $"Terugbetaling: afletteren op {f.Nummer}" }));
                        break;
                    case Betaalwijze.MinCredit:
                        var credit = Verkoop.Single(c => c.Credit && c.Partij == f.Partij && c.Ronde == f.Ronde);
                        Betaal.Add(Post(Max(dag, credit.Datum.AddDays(2)), f.Totaal - credit.Totaal, f.Partij, $"Factuur {f.Nummer} minus creditnota {credit.Nummer}", Afhandeling.Op(f.Nummer, credit.Nummer)));
                        break;
                    case Betaalwijze.Verschil:
                        Betaal.Add(Post(dag, f.Totaal - 0.02m, f.Partij, f.Kenmerk!, Afhandeling.Op(f.Nummer) with { Toelichting = $"Twee cent te weinig: afletteren op {f.Nummer}, het verschil naar 9000" }, kenmerk: f.Kenmerk));
                        break;
                    case Betaalwijze.Korting:
                        var korting = Math.Round(f.Totaal * 0.02m, 2, MidpointRounding.AwayFromZero);
                        KortingVerkoop += korting;
                        Betaal.Add(Post(Min(dag, new DateOnly(2026, 3, 31)), f.Totaal - korting, f.Partij, $"Factuur {f.Nummer} -2% betalingskorting",
                            new Afhandeling(Afhandelsoort.Afletteren, [f.Nummer], Verschilrekening: "8010", Verschilcode: "V21", Toelichting: $"Korting: afletteren op {f.Nummer}, verschil op 8010 met V21")));
                        break;
                    case Betaalwijze.Open:
                        OpenDebiteuren[f.Nummer] = f.Totaal;
                        break;
                }
            }
        }

        // Sombetalingen: Fietsplan per maand met de nummers (en de creditnota), Hotel de eerste drie zonder nummers.
        foreach (var maand in Verkoop.Where(f => f.Partij == Fietsplan).GroupBy(f => (f.Datum.Month, f.Ronde)))
        {
            var lijst = maand.ToList();
            var dag = new DateOnly(2026, maand.Key.Month, 1).AddMonths(1).AddDays(9);
            Betaal.Add(Post(dag, lijst.Sum(f => f.Getekend), Fietsplan, $"Facturen {string.Join(" ", lijst.Select(f => f.Nummer))}", Afhandeling.Op([.. lijst.Select(f => f.Nummer)])));
        }

        foreach (var hotel in Verkoop.Where(f => f.Partij == Hotel && f.Betaling == Betaalwijze.Som).GroupBy(f => f.Ronde).Select(g => g.ToList()))
        {
            Betaal.Add(Post(hotel[^1].Datum.AddDays(12), hotel.Sum(f => f.Totaal), Hotel, "Betaling openstaande facturen",
                Afhandeling.Op([.. hotel.Select(f => f.Nummer)]) with { Toelichting = $"Som zonder factuurnummers: afletteren op {string.Join(", ", hotel.Select(f => f.Nummer))}" }));
        }

        // Creditnota's terugbetaald.
        foreach (var c in Verkoop.Where(f => f.Credit && f.Betaling == Betaalwijze.Terugbetaald))
        {
            Betaal.Add(Post(c.Datum.AddDays(10), -c.Totaal, c.Partij, $"Terugbetaling creditnota {c.Nummer}", Afhandeling.Op(c.Nummer)));
        }

        // Een ontvangst die nergens bij hoort: vraagposten.
        Vraagpost = 150m;
        Betaal.Add(new Bankpost(new DateOnly(2026, 3, 9), Vraagpost, "R. Onbekend", MaakIban("NL", "TRIO", "0198765432"), "Betaling", null, E2e(),
            Afhandeling.OpRekening("2000", null, "Onbekend: op de vraagposten (2000)")));

        // ---- betalingen aan leveranciers
        foreach (var f in Inkoop.Where(f => !f.Credit))
        {
            switch (f.Betaling)
            {
                case Betaalwijze.Kenmerk:
                    Betaal.Add(Post(f.Vervaldatum, -f.Totaal, f.Partij, f.Kenmerk!, Afhandeling.Op(f.Nummer), kenmerk: f.Kenmerk));
                    break;
                case Betaalwijze.Nummer:
                    Betaal.Add(Post(Min(f.Datum.AddDays(toeval.Next(7, 15)), Eind), -f.Totaal, f.Partij, $"Factuur {f.Nummer}", Afhandeling.Op(f.Nummer)));
                    break;
                case Betaalwijze.Incasso:
                    Betaal.Add(Post(f.Vervaldatum, -f.Totaal, f.Partij, $"Incasso {f.Omschrijving} factuur {f.Nummer}", Afhandeling.Op(f.Nummer), incasso: true));
                    break;
                case Betaalwijze.IncassoStorno:
                    Betaal.Add(Post(f.Vervaldatum, -f.Totaal, f.Partij, $"Incasso {f.Omschrijving} factuur {f.Nummer}", Afhandeling.Op(f.Nummer), incasso: true));
                    Betaal.Add(Post(f.Vervaldatum.AddDays(2), f.Totaal, f.Partij, $"Storno incasso {f.Omschrijving} factuur {f.Nummer}",
                        Afhandeling.Op(f.Nummer) with { Toelichting = $"Storno: afletteren op {f.Nummer} (staat daarna weer open)" }, incasso: true) with
                    { Storno = true });
                    Betaal.Add(Post(f.Vervaldatum.AddDays(9), -f.Totaal, f.Partij, $"Incasso {f.Omschrijving} factuur {f.Nummer} (2e poging)", Afhandeling.Op(f.Nummer), incasso: true));
                    break;
                case Betaalwijze.Dubbel:
                    var dag = f.Datum.AddDays(9);
                    Betaal.Add(Post(dag, -f.Totaal, f.Partij, $"Factuur {f.Nummer}", Afhandeling.Op(f.Nummer)));
                    Betaal.Add(Post(dag.AddDays(5), -f.Totaal, f.Partij, $"Factuur {f.Nummer}", Afhandeling.Op(f.Nummer) with { Toelichting = $"Dubbel betaald: afletteren op {f.Nummer} (te vorderen)" }));
                    Betaal.Add(Post(dag.AddDays(25), f.Totaal, f.Partij, $"Terugbetaling dubbel betaald {f.Nummer}", Afhandeling.Op(f.Nummer)));
                    break;
                case Betaalwijze.MinCredit:
                    var credit = Inkoop.Single(c => c.Credit && c.Partij == f.Partij && c.Ronde == f.Ronde);
                    Betaal.Add(Post(Max(f.Datum, credit.Datum).AddDays(8), -(f.Totaal - credit.Totaal), f.Partij, $"Factuur {f.Nummer} minus creditnota {credit.Nummer}", Afhandeling.Op(f.Nummer, credit.Nummer)));
                    break;
                case Betaalwijze.Korting:
                    var korting = Math.Round(f.Totaal * 0.02m, 2, MidpointRounding.AwayFromZero);
                    KortingInkoop += korting;
                    Betaal.Add(Post(f.Datum.AddDays(8), -(f.Totaal - korting), f.Partij, $"Factuur {f.Nummer} -2% betalingskorting",
                        new Afhandeling(Afhandelsoort.Afletteren, [f.Nummer], Verschilrekening: "4400", Verschilcode: "I21", Toelichting: $"Korting: afletteren op {f.Nummer}, verschil op 4400 met I21")));
                    break;
                case Betaalwijze.Open:
                    OpenCrediteuren[f.Nummer] = f.Totaal;
                    break;
            }
        }

        // Verzamelbetalingen: de facturen van een maand van de groothandel en de vervoerder in één opdracht.
        foreach (var maand in Inkoop.Where(f => f.Betaling == Betaalwijze.Verzamel).GroupBy(f => f.Datum.Month))
        {
            var dag = new DateOnly(2026, maand.Key, 1).AddMonths(1).AddDays(4);
            var batch = maand.Select(f => Post(dag, -f.Totaal, f.Partij, $"Factuur {f.Nummer}", Afhandeling.Op(f.Nummer))).ToList();
            Betaal.Add(new Bankpost(dag, batch.Sum(b => b.Bedrag), null, null, $"Verzamelbetaling {batch.Count} posten", null, E2e(), Afhandeling.Op(), batch));
        }

        // ---- vaste lasten, belasting, lonen en pin
        Betaal.Add(Post(new DateOnly(2026, 1, 15), -BtwVierdeKwartaal, Belastingdienst, BelastingKenmerk('1', 5, 30),
            Afhandeling.OpRekening("1510", null, "Belastingdienst: omzetbelasting 4e kwartaal 2025 op 1510"), kenmerk: BelastingKenmerk('1', 5, 30)));
        for (var m = 0; m < 4; m++)
        {
            var eerste = Begin.AddMonths(m);
            var vorige = eerste.AddMonths(-1);
            var lh = BelastingKenmerk('6', vorige.Year % 10, vorige.Month);
            Betaal.Add(Post(eerste.AddDays(9), -Loonheffing, Belastingdienst, lh, Afhandeling.OpRekening("1520", null, $"Belastingdienst: loonheffingen {vorige.ToString("MMMM yyyy", Nl)} op 1520"), kenmerk: lh));
            if (m == 3)
            {
                break;
            }

            foreach (var (naam, iban, netto) in Medewerkers)
            {
                Betaal.Add(new Bankpost(eerste.AddDays(24), -netto, naam, iban, $"Salaris {eerste.ToString("MMMM yyyy", Nl)}", null, E2e(),
                    Afhandeling.OpRekening("1800", null, "Nettoloon: op 1800 (te betalen nettolonen)")));
            }

            Betaal.Add(new Bankpost(eerste.AddMonths(1).AddDays(-1), -Math.Round(12.5m + m * 1.85m, 2), null, null, $"Kosten zakelijk betalingsverkeer {eerste.ToString("MMMM yyyy", Nl)}", null, E2e(),
                Afhandeling.OpRekening("9000", null, "Bankkosten: op 9000")));
            Betaal.Add(new Bankpost(eerste.AddDays(7), -Prijs(55, 90), "Tankstation De Pomp", null, $"BEA NR:{4100 + m} {eerste.AddDays(7):dd.MM.yy} Tankstation De Pomp Ergens", null, E2e(),
                Afhandeling.OpRekening("4200", "I21", "Brandstof (pin): op 4200 met I21")));
            Betaal.Add(new Bankpost(eerste.AddDays(19), -Prijs(55, 90), "Tankstation De Pomp", null, $"BEA NR:{4200 + m} {eerste.AddDays(19):dd.MM.yy} Tankstation De Pomp Ergens", null, E2e(),
                Afhandeling.OpRekening("4200", "I21", "Brandstof (pin): op 4200 met I21")));
            Betaal.Add(new Bankpost(eerste.AddDays(13), -Prijs(20, 70), "Bouwmarkt Ergens", null, $"BEA NR:{4300 + m} {eerste.AddDays(13):dd.MM.yy} Bouwmarkt Ergens", null, E2e(),
                Afhandeling.OpRekening("4320", "I21", "Kleine aanschaf (pin): op 4320 met I21")));
        }

        // ---- overboekingen tussen de eigen rekeningen (beide kanten via de kruisposten)
        var naarSpaar = new DateOnly(2026, 2, 12);
        var vanSpaar = new DateOnly(2026, 3, 18);
        Betaal.Add(new Bankpost(naarSpaar, -10_000m, Eigen.Naam, SpaarIban, "Naar spaarrekening", null, E2e(), Afhandeling.OpRekening(Kruisposten, null, $"Eigen overboeking: op {Kruisposten} kruisposten")));
        Betaal.Add(new Bankpost(vanSpaar, 3_000m, Eigen.Naam, SpaarIban, "Van spaarrekening", null, E2e(), Afhandeling.OpRekening(Kruisposten, null, $"Eigen overboeking: op {Kruisposten} kruisposten")));
        Spaar.Add(new Bankpost(naarSpaar, 10_000m, Eigen.Naam, Eigen.Iban, "Van betaalrekening", null, E2e(), Afhandeling.OpRekening(Kruisposten, null, $"Eigen overboeking: op {Kruisposten} kruisposten")));
        Spaar.Add(new Bankpost(vanSpaar, -3_000m, Eigen.Naam, Eigen.Iban, "Naar betaalrekening", null, E2e(), Afhandeling.OpRekening(Kruisposten, null, $"Eigen overboeking: op {Kruisposten} kruisposten")));
        Spaar.Add(new Bankpost(new DateOnly(2026, 3, 31), Rente, null, null, "Rente 1e kwartaal 2026", null, E2e(), Afhandeling.OpRekening("9010", null, "Rente: op 9010")));

        // ---- de btw van het eerste kwartaal, betaald eind april
        AangifteQ1 = BerekenAangifte();
        var q1 = AangifteQ1.Afgerond().TeBetalen;
        var btw = BelastingKenmerk('1', 6, 21);
        Betaal.Add(Post(new DateOnly(2026, 4, 28), -q1, Belastingdienst, btw, Afhandeling.OpRekening("1510", null, "Belastingdienst: omzetbelasting 1e kwartaal 2026 op 1510"), kenmerk: btw));

        Betaal.Sort((a, b) => a.Datum.CompareTo(b.Datum));
        if (Betaal.Any(p => p.Datum > Eind || p.Datum < Begin))
        {
            throw new InvalidOperationException("Een bankmutatie valt buiten de periode.");
        }
    }

    private static readonly Partijdata Belastingdienst = new("Belastingdienst", Belastingherkenning.BelastingdienstIban, null, null, "NL", "Postbus 2", "7300 AA", "Apeldoorn");

    /// <summary>Een betalingskenmerk van de Belastingdienst voor de eigen onderneming: soort (1 = omzetbelasting, 6 = loonheffingen), jaar en tijdvak.</summary>
    private static string BelastingKenmerk(char soort, int jaar, int tijdvak) =>
        Betalingskenmerk.Maak($"12345678{soort}{jaar % 10}01{tijdvak:00}0");

    /// <summary>
    /// De aangifte van het eerste kwartaal zoals hij hoort te zijn: de facturen, de pinbetalingen met btw en de korting
    /// die de klant (in maart) en wij bij de leverancier (in januari) inhielden.
    /// </summary>
    private Btwaangifte BerekenAangifte()
    {
        var rubrieken = new Dictionary<string, (decimal Grondslag, decimal Btw)>(StringComparer.Ordinal);
        void Tel(string rubriek, decimal grondslag, decimal btw)
        {
            var (g, b) = rubrieken.GetValueOrDefault(rubriek);
            rubrieken[rubriek] = (g + grondslag, b + btw);
        }

        var voorbelasting = 0m;
        foreach (var f in Verkoop)
        {
            var teken = f.Credit ? -1 : 1;
            foreach (var (code, grondslag, btw) in f.Btw)
            {
                Tel(code switch { "V21" => "1a", "V9" => "1b", "VEX" => "3a", "VEU" => "3b", _ => throw new InvalidOperationException(code) }, teken * grondslag, teken * btw);
            }
        }

        foreach (var f in Inkoop)
        {
            var teken = f.Credit ? -1 : 1;
            for (var i = 0; i < f.Regels.Count; i++)
            {
                var regel = f.Regels[i];
                switch (regel.Onzecode)
                {
                    case "I21" or "I9":
                        voorbelasting += teken * f.RegelBtw(i);
                        break;
                    case "IVL" or "IEU":
                        var verlegd = Math.Round(regel.Netto * 0.21m, 2, MidpointRounding.AwayFromZero);
                        Tel(regel.Onzecode == "IVL" ? "2a" : "4b", teken * regel.Netto, teken * verlegd);
                        voorbelasting += teken * verlegd;
                        break;
                }
            }
        }

        foreach (var p in Betaal.Where(p => p.Datum.Month <= 3 && p.Afhandeling.BtwCode == "I21"))
        {
            voorbelasting += BtwUitInclusief(-p.Bedrag);
        }

        // De korting van de klant: omzet en btw lager (21%); onze korting bij de leverancier: minder voorbelasting.
        var kortingBtw = BtwUitInclusief(KortingVerkoop);
        Tel("1a", -(KortingVerkoop - kortingBtw), -kortingBtw);
        voorbelasting -= BtwUitInclusief(KortingInkoop);

        return new Btwaangifte(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31),
            [.. rubrieken.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new Aangifterubriek(r.Key, r.Value.Grondslag, r.Value.Btw))], voorbelasting);
    }

    public static decimal BtwUitInclusief(decimal inclusief) => inclusief - Math.Round(inclusief * 100m / 121m, 2, MidpointRounding.AwayFromZero);

    // ---------------------------------------------------------------- bestanden

    /// <summary>De bestanden zoals ze uit de pakketten en de bank komen; inkoop met één factuur twee keer.</summary>
    public IReadOnlyList<Praktijkbestand> Bestanden()
    {
        var uit = new List<Praktijkbestand>();
        foreach (var f in Inkoop)
        {
            uit.Add(new Praktijkbestand("inkoop", f.Bestandsnaam, Document(f, f.Partij, Eigen)));
        }

        // Dezelfde factuur komt twee weken later nog eens binnen (doorgestuurd uit de mail): hetzelfde bestand.
        foreach (var dubbel in Inkoop.Where(f => f.Partij == Kantoor && f.Datum.Month == 2).GroupBy(f => f.Ronde).Select(g => g.First()))
        {
            uit.Add(new Praktijkbestand("inkoop", $"{dubbel.Datum.AddDays(14):yyyy-MM-dd} Doorgestuurd {Veilig(dubbel.Nummer)}.pdf", uit.Single(b => b.Naam == dubbel.Bestandsnaam).Inhoud));
        }
        foreach (var f in Verkoop)
        {
            uit.Add(new Praktijkbestand("verkoop", f.Bestandsnaam, Document(f, Eigen, f.Partij)));
        }

        foreach (var (naam, van, tot) in new[]
                 {
                     ("01 betaalrekening januari 2026.xml", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
                     ("02 betaalrekening februari 2026.xml", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
                     ("03 betaalrekening maart 2026.xml", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)),
                     ("04 betaalrekening 1 tm 15 april 2026.xml", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 15)),
                     ("05 betaalrekening april 2026 (overlapt).xml", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)),
                 })
        {
            uit.Add(new Praktijkbestand("bank", naam, Encoding.UTF8.GetBytes(Camt(van, tot))));
        }

        var spaar = new Bankafschrift(SpaarIban, "1", "EUR", Begin.AddDays(-1), BeginSpaar, Eind, BeginSpaar + Spaar.Sum(p => p.Bedrag),
            [.. Spaar.Select(p => new Bankmutatie(p.Datum, p.Bedrag, p.Omschrijving, p.Iban, p.Naam))]);
        uit.Add(new Praktijkbestand("bank", "06 spaarrekening januari tm april 2026.mt940", Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(spaar, "SPAAR2026Q1"))));
        return uit;
    }

    public static decimal SaldoBetaal(IEnumerable<Bankpost> posten, DateOnly tot) => BeginBetaal + posten.Where(p => p.Datum <= tot).Sum(p => p.Bedrag);

    /// <summary>Een CAMT.053 van de betaalrekening over een periode (één afschrift).</summary>
    public string Camt(DateOnly van, DateOnly tot)
    {
        XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02";
        var begin = SaldoBetaal(Betaal, van.AddDays(-1));
        var eind = SaldoBetaal(Betaal, tot);
        XElement Saldo(string code, decimal bedrag, DateOnly dag) =>
            new(ns + "Bal", new XElement(ns + "Tp", new XElement(ns + "CdOrPrtry", new XElement(ns + "Cd", code))),
                new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), Getal(Math.Abs(bedrag))),
                new XElement(ns + "CdtDbtInd", bedrag < 0 ? "DBIT" : "CRDT"),
                new XElement(ns + "Dt", new XElement(ns + "Dt", dag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        XElement Partij(string rol, string? naam, string? iban) =>
            new(ns + "RltdPties",
                naam is null ? null : new XElement(ns + rol, new XElement(ns + "Nm", naam)),
                iban is null ? null : new XElement(ns + (rol + "Acct"), new XElement(ns + "Id", new XElement(ns + "IBAN", iban))));
        XElement Tx(Bankpost p, bool richting)
        {
            var ontvangst = p.Bedrag > 0;
            // Bij een storno van een incasso staat de incassant als Cdtr, zoals de bank het doorgeeft.
            var rol = p.Storno ? "Cdtr" : ontvangst ? "Dbtr" : "Cdtr";
            var partijen = Partij(rol, p.Naam, p.Iban);
            if (p.Storno || (!ontvangst && p.Naam is not null))
            {
                partijen.AddFirst(new XElement(ns + "Dbtr", new XElement(ns + "Nm", Eigen.Naam)), new XElement(ns + "DbtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", Eigen.Iban))));
            }

            return new XElement(ns + "TxDtls",
                new XElement(ns + "Refs", new XElement(ns + "EndToEndId", p.EndToEnd), p.Incasso ? new XElement(ns + "MndtId", $"M-{p.Naam?.Length}") : null),
                richting ? new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), Getal(Math.Abs(p.Bedrag))) : null,
                richting ? new XElement(ns + "CdtDbtInd", p.Bedrag < 0 ? "DBIT" : "CRDT") : null,
                partijen.HasElements ? partijen : null,
                new XElement(ns + "RmtInf", p.Kenmerk is not null && p.Omschrijving == p.Kenmerk
                    ? new XElement(ns + "Strd", new XElement(ns + "CdtrRefInf", new XElement(ns + "Tp", new XElement(ns + "CdOrPrtry", new XElement(ns + "Cd", "SCOR")), new XElement(ns + "Issr", "CUR")), new XElement(ns + "Ref", p.Kenmerk)))
                    : new XElement(ns + "Ustrd", p.Omschrijving)));
        }

        var entries = Betaal.Where(p => p.Datum >= van && p.Datum <= tot).Select(p => new XElement(ns + "Ntry",
            new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), Getal(Math.Abs(p.Bedrag))),
            new XElement(ns + "CdtDbtInd", p.Bedrag < 0 ? "DBIT" : "CRDT"),
            p.Storno ? new XElement(ns + "RvslInd", "true") : null,
            new XElement(ns + "Sts", "BOOK"),
            new XElement(ns + "BookgDt", new XElement(ns + "Dt", p.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            new XElement(ns + "ValDt", new XElement(ns + "Dt", p.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            new XElement(ns + "BkTxCd", new XElement(ns + "Prtry", new XElement(ns + "Cd", p.Batch is not null ? "BATCH" : p.Incasso ? "DD" : p.Naam is null ? "MISC" : "TRF"))),
            new XElement(ns + "NtryDtls", p.Batch is { } batch
                ? new XElement(ns + "Btch", new XElement(ns + "NbOfTxs", batch.Count))
                : null,
                p.Batch is { } b ? b.Select(t => Tx(t, true)) : [Tx(p, false)])));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Document",
                new XElement(ns + "BkToCstmrStmt",
                    new XElement(ns + "GrpHdr", new XElement(ns + "MsgId", $"SPAAK{tot:yyyyMMdd}{van:dd}"), new XElement(ns + "CreDtTm", $"{tot:yyyy-MM-dd}T23:59:00")),
                    new XElement(ns + "Stmt",
                        new XElement(ns + "Id", $"{van:yyyyMMdd}-{tot:yyyyMMdd}"),
                        new XElement(ns + "ElctrncSeqNb", van.Month),
                        new XElement(ns + "CreDtTm", $"{tot:yyyy-MM-dd}T23:59:00"),
                        new XElement(ns + "Acct", new XElement(ns + "Id", new XElement(ns + "IBAN", Eigen.Iban)), new XElement(ns + "Ccy", "EUR"),
                            new XElement(ns + "Ownr", new XElement(ns + "Nm", Eigen.Naam))),
                        Saldo("PRCD", begin, van.AddDays(-1)),
                        Saldo("OPBD", begin, van),
                        Saldo("CLBD", eind, tot),
                        entries))));
        var uit = new StringBuilder();
        using (var schrijver = new StringWriter(uit, CultureInfo.InvariantCulture))
        {
            document.Save(schrijver);
        }

        return uit.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal);
    }

    private static string Getal(decimal bedrag) => bedrag.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>De factuur als bestand: een e-factuur (UBL) of een PDF in de opmaak van de afzender.</summary>
    private static byte[] Document(Factuurdata f, Partijdata afzender, Partijdata ontvanger)
    {
        var papier = f.Papier("X") with { Klant = ontvanger.Partij };
        return f.Formaat == Formaat.Ubl
            ? Encoding.UTF8.GetBytes(UblFactuurSchrijver.Schrijf(papier, afzender.Partij, BtwCodes.Standaard))
            : Pdf(f, afzender, ontvanger);
    }

    private static byte[] Pdf(Factuurdata f, Partijdata afzender, Partijdata ontvanger)
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var vet = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var y = 800.0;
        void Regel(string links, string? midden = null, string? rechts = null, bool zwaar = false, double grootte = 9)
        {
            var letter = zwaar ? vet : font;
            pagina.AddText(links, grootte, new PdfPoint(40, y), letter);
            if (midden is not null)
            {
                pagina.AddText(midden, grootte, new PdfPoint(330, y), letter);
            }

            if (rechts is not null)
            {
                pagina.AddText(rechts, grootte, new PdfPoint(470, y), letter);
            }

            y -= grootte + 5;
        }

        string Euro(decimal bedrag) => "EUR " + bedrag.ToString("N2", Nl);
        Regel(afzender.Naam, zwaar: true, grootte: 14);
        Regel($"{afzender.Straat}, {afzender.Postcode} {afzender.Plaats}");
        Regel($"IBAN {Iban(afzender.Iban)}" + (afzender.Kvk is null ? "" : $"   KvK {afzender.Kvk}") + (afzender.BtwNummer is null ? "" : $"   Btw-nr {afzender.BtwNummer}"));
        y -= 14;
        Regel(ontvanger.Naam);
        Regel(ontvanger.Straat);
        Regel($"{ontvanger.Postcode} {ontvanger.Plaats}");
        y -= 14;
        Regel(f.Credit ? "CREDITNOTA" : "FACTUUR", zwaar: true, grootte: 13);
        Regel($"Factuurnummer: {f.Nummer}");
        Regel($"Factuurdatum: {f.Datum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        if (!f.Credit)
        {
            Regel($"Vervaldatum: {f.Vervaldatum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        }

        if (f.CreditVoor is not null)
        {
            Regel($"Betreft factuur {f.CreditVoor}");
        }

        y -= 10;
        Regel("Omschrijving", "Aantal x prijs", "Bedrag", zwaar: true);
        var teken = f.Credit ? -1 : 1;
        foreach (var r in f.Regels)
        {
            Regel($"{r.Omschrijving} ({BtwTekst(r.Papiercode)})", $"{(teken * r.Aantal).ToString("0.##", Nl)} x {r.Prijs.ToString("N2", Nl)}", Euro(teken * r.Netto));
        }

        y -= 8;
        Regel("Subtotaal", null, Euro(teken * f.Exclusief));
        foreach (var (code, grondslag, btw) in f.Btw)
        {
            Regel(Percentage(code) > 0 ? $"BTW {Percentage(code).ToString("0", Nl)}% over {Euro(teken * grondslag)}" : $"{BtwTekst(code)} over {Euro(teken * grondslag)}", null, Euro(teken * btw));
        }

        Regel("Totaal", null, Euro(teken * f.Totaal), zwaar: true);
        y -= 12;
        if (!f.Credit)
        {
            Regel(f.Kenmerk is { } k && f.Inkoop is false
                ? $"Graag betalen voor {f.Vervaldatum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} onder vermelding van betalingskenmerk {Betalingskenmerk.Toon(k)}."
                : $"Graag betalen voor {f.Vervaldatum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} op {Iban(afzender.Iban)} o.v.v. {f.Nummer}.");
        }

        return builder.Build();
    }

    private static string BtwTekst(string code) => code switch
    {
        "V21" => "21%",
        "V9" => "9%",
        "VVL" => "btw verlegd",
        "VEU" => "intracommunautaire levering, btw verlegd",
        "VEX" => "uitvoer, 0%",
        "VVR" => "vrijgesteld van btw",
        _ => "0%",
    };

    public static decimal Percentage(string papiercode) => papiercode switch
    {
        "V21" => 21m,
        "V9" => 9m,
        _ => 0m,
    };

    private static string Iban(string iban) => Boekhouding.Relaties.Iban.Toon(iban);

    public static string Veilig(string tekst) => string.Concat(tekst.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '-' : c));

    // ---------------------------------------------------------------- hulp

    private int Aantal(int van, int tot) => toeval.Next(van, tot + 1);

    private decimal Prijs(int van, int tot) => Math.Round(van + (decimal)toeval.NextDouble() * (tot - van), 2);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>Een geldig IBAN bij een bankcode en rekeningnummer (controlegetal modulus 97).</summary>
    public static string MaakIban(string land, string bank, string rekening)
    {
        var bban = bank + rekening;
        var cijfers = string.Concat((bban + land + "00").Select(c => char.IsAsciiLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        var rest = cijfers.Aggregate(0, (r, c) => ((r * 10) + (c - '0')) % 97);
        return $"{land}{98 - rest:00}{bban}";
    }

    /// <summary>Een Nederlands btw-nummer dat de elfproef doorstaat.</summary>
    private static string BtwNummerNl(int n)
    {
        for (var basis = 80_000_000 + n * 7_919; ; basis++)
        {
            var acht = basis.ToString("00000000", CultureInfo.InvariantCulture);
            var som = acht.Select((c, i) => (c - '0') * (9 - i)).Sum() % 11;
            if (som < 10)
            {
                return $"NL{acht}{som}B01";
            }
        }
    }
}
