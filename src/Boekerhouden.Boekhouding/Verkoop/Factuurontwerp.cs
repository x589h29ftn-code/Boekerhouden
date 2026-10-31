using System.Globalization;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>Wat een blok op de factuur toont.</summary>
public enum Bloksoort
{
    Logo,

    /// <summary>De eigen onderneming: naam, adres en contact.</summary>
    Afzender,

    /// <summary>Het adresblok van de klant (bij een vensterenvelop links op 20 × 50 mm).</summary>
    Klant,

    /// <summary>"Factuur" of "Creditfactuur" (of een eigen tekst).</summary>
    Titel,

    /// <summary>Nummer, datum, vervaldatum, referentie en betalingskenmerk.</summary>
    Factuurgegevens,

    /// <summary>De regels met de totalen, de btw en de vermeldingen; loopt door naar de volgende pagina.</summary>
    Regels,

    /// <summary>Hoe de klant betaalt; staat direct onder de regels.</summary>
    Betaalinstructie,

    /// <summary>Onderaan elke pagina, bijv. KvK, btw-nummer en IBAN.</summary>
    Voettekst,

    /// <summary>Een eigen tekst op een vaste plek (op de eerste pagina).</summary>
    Tekst,
}

public enum Uitlijning
{
    Links,
    Midden,
    Rechts,
}

/// <summary>Hoe de regels eruitzien: met lijnen tussen de regels, met een kleur om de andere regel, of zonder lijnen.</summary>
public enum Tabelstijl
{
    Lijnen,
    Strepen,
    Rustig,
}

/// <summary>
/// Een blok op de factuur, met zijn plek (vanaf de linkerbovenhoek van het A4) en breedte in millimeters. De regels en de
/// betaalinstructie lopen in de tekst door (de regels beginnen op <see cref="Y"/>); de voettekst staat op elke pagina.
/// </summary>
/// <param name="Tekst">Bij een tekstblok, de titel, de betaalinstructie of de voettekst: de tekst, met variabelen
/// (<see cref="Factuurteksten"/>).</param>
public sealed record Ontwerpblok(
    string Id,
    Bloksoort Soort,
    decimal X,
    decimal Y,
    decimal Breedte,
    decimal? Hoogte = null,
    decimal? Lettergrootte = null,
    bool Vet = false,
    Uitlijning Uitlijning = Uitlijning.Links,
    string? Kleur = null,
    string? Tekst = null,
    bool Zichtbaar = true)
{
    /// <summary>Loopt het blok mee met de tekst (de plek in de hoogte volgt uit de regels)?</summary>
    public bool Stroomt => Soort == Bloksoort.Betaalinstructie;
}

/// <summary>
/// De lay-out van een verkoopfactuur: het lettertype en de kleuren, welke kolommen de regels hebben, en de blokken met
/// hun plek op het A4. Wordt als JSON bewaard bij de lay-out van de administratie; de ontwerper wijzigt het, de opmaak
/// (PDF, afdruk, voorbeeld) volgt het.
/// </summary>
public sealed partial record Factuurontwerp(
    string Lettertype,
    decimal Lettergrootte,
    string Accentkleur,
    string Tekstkleur,
    bool KolomAantal,
    bool KolomEenheid,
    bool KolomPrijs,
    bool KolomKorting,
    bool KolomBtw,
    Tabelstijl Tabelstijl,
    IReadOnlyList<Ontwerpblok> Blokken)
{
    public const decimal PaginaBreedte = 210m;
    public const decimal PaginaHoogte = 297m;

    /// <summary>De lettertypen die er overal zijn (browser, PDF, Windows), met de terugval.</summary>
    public static IReadOnlyList<(string Naam, string Css)> Lettertypen { get; } =
    [
        ("IBM Plex Sans", "'IBM Plex Sans', Arial, sans-serif"),
        ("Arial", "Arial, 'Liberation Sans', Helvetica, sans-serif"),
        ("Helvetica", "Helvetica, Arial, 'Liberation Sans', sans-serif"),
        ("Segoe UI", "'Segoe UI', Selawik, Arial, sans-serif"),
        ("Calibri", "Calibri, Carlito, Arial, sans-serif"),
        ("Verdana", "Verdana, 'DejaVu Sans', sans-serif"),
        ("Georgia", "Georgia, 'DejaVu Serif', serif"),
        ("Times New Roman", "'Times New Roman', 'Liberation Serif', Times, serif"),
    ];

    public const string StandaardBetaalinstructie =
        "Wij verzoeken u het bedrag van #totaal vóór #vervaldatum over te maken op #iban ten name van #naam, onder vermelding van betalingskenmerk #kenmerk.";

    public const string StandaardVoettekst = "#naam · #adres · KvK #kvk · Btw #btwnummer · IBAN #iban";

    /// <summary>Een rustige standaard: logo linksboven, afzender rechts, klant op de plek van het envelopvenster.</summary>
    public static Factuurontwerp Standaard { get; } = new(
        "IBM Plex Sans", 9.5m, "#1f5f66", "#222222", true, true, true, false, true, Tabelstijl.Lijnen,
        [
            new("logo", Bloksoort.Logo, 20m, 15m, 50m, 22m),
            new("afzender", Bloksoort.Afzender, 125m, 15m, 65m, Lettergrootte: 8.5m, Uitlijning: Uitlijning.Rechts),
            new("klant", Bloksoort.Klant, 20m, 50m, 85m),
            new("gegevens", Bloksoort.Factuurgegevens, 125m, 50m, 65m),
            new("titel", Bloksoort.Titel, 20m, 92m, 120m, Lettergrootte: 18m, Vet: true),
            new("regels", Bloksoort.Regels, 20m, 108m, 170m),
            new("betaal", Bloksoort.Betaalinstructie, 20m, 0m, 170m, Tekst: StandaardBetaalinstructie),
            new("voet", Bloksoort.Voettekst, 20m, 282m, 170m, Lettergrootte: 7.5m, Uitlijning: Uitlijning.Midden, Kleur: "#777777", Tekst: StandaardVoettekst),
        ]);

    public Ontwerpblok? Blok(string id) => Blokken.FirstOrDefault(b => b.Id == id);

    public Ontwerpblok? Blok(Bloksoort soort) => Blokken.FirstOrDefault(b => b.Soort == soort);

    /// <summary>Het lettertype zoals in CSS (met terugval); een onbekende naam wordt het standaardlettertype.</summary>
    public string LettertypeCss => Lettertypen.Where(l => l.Naam == Lettertype).Select(l => l.Css).FirstOrDefault() ?? Lettertypen[0].Css;

    /// <summary>Het blok verplaatst (op hele millimeters, binnen het A4).</summary>
    public Factuurontwerp Verplaats(string id, decimal x, decimal y, decimal? breedte = null) =>
        Wijzig(id, b =>
        {
            var w = Math.Clamp(Math.Round(breedte ?? b.Breedte), 10m, PaginaBreedte);
            return b with
            {
                Breedte = w,
                X = Math.Clamp(Math.Round(x), 0m, PaginaBreedte - w),
                Y = b.Stroomt ? b.Y : Math.Clamp(Math.Round(y), 0m, PaginaHoogte - 5m),
            };
        });

    public Factuurontwerp Wijzig(string id, Func<Ontwerpblok, Ontwerpblok> wijziging)
    {
        ArgumentNullException.ThrowIfNull(wijziging);
        return this with { Blokken = [.. Blokken.Select(b => b.Id == id ? wijziging(b) : b)] };
    }

    /// <summary>Een nieuw tekstblok midden op de pagina.</summary>
    public Factuurontwerp MetTekstblok(out string id)
    {
        var nummer = Blokken.Count(b => b.Soort == Bloksoort.Tekst) + 1;
        while (Blokken.Any(b => b.Id == $"tekst{nummer}"))
        {
            nummer++;
        }

        id = $"tekst{nummer}";
        return this with { Blokken = [.. Blokken, new Ontwerpblok(id, Bloksoort.Tekst, 20m, 240m, 80m, Tekst: "Eigen tekst")] };
    }

    /// <summary>Alleen een tekstblok kan weg; de vaste blokken zet je uit (<see cref="Ontwerpblok.Zichtbaar"/>).</summary>
    public Factuurontwerp ZonderBlok(string id) =>
        this with { Blokken = [.. Blokken.Where(b => b.Id != id || b.Soort != Bloksoort.Tekst)] };

    /// <summary>
    /// Zorgt dat alle vaste blokken er zijn (een ontwerp van een oudere versie krijgt nieuwe blokken van de standaard) en
    /// dat de waarden binnen de grenzen liggen.
    /// </summary>
    public Factuurontwerp Compleet()
    {
        var blokken = Blokken.Where(b => b.Soort == Bloksoort.Tekst || Blokken.First(x => x.Soort == b.Soort) == b).ToList();
        blokken.AddRange(Standaard.Blokken.Where(s => blokken.All(b => b.Soort != s.Soort)));
        return this with
        {
            Lettertype = Lettertypen.Any(l => l.Naam == Lettertype) ? Lettertype : Standaard.Lettertype,
            Lettergrootte = Math.Clamp(Lettergrootte, 6m, 16m),
            Accentkleur = IsKleur(Accentkleur) ? Accentkleur : Standaard.Accentkleur,
            Tekstkleur = IsKleur(Tekstkleur) ? Tekstkleur : Standaard.Tekstkleur,
            Blokken = [.. blokken.Select(b => b with
            {
                Breedte = Math.Clamp(b.Breedte, 10m, PaginaBreedte),
                X = Math.Clamp(b.X, 0m, PaginaBreedte - 10m),
                Y = Math.Clamp(b.Y, 0m, PaginaHoogte - 5m),
                Hoogte = b.Hoogte is { } h ? Math.Clamp(h, 5m, PaginaHoogte) : null,
                Lettergrootte = b.Lettergrootte is { } l ? Math.Clamp(l, 5m, 40m) : null,
                Kleur = b.Kleur is { } k && IsKleur(k) ? k : null,
                Tekst = b.Tekst is { Length: > 2000 } t ? t[..2000] : b.Tekst,
            })],
        };
    }

    /// <summary>Een kleur als <c>#rrggbb</c> (alleen dat: de kleur komt in de CSS van de factuur).</summary>
    public static bool IsKleur(string? kleur) => kleur is not null && Hexkleur().IsMatch(kleur);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Hexkleur();
}

/// <summary>
/// De variabelen in de teksten van de factuur (titel, betaalinstructie, voettekst, eigen tekst), bijv.
/// <c>#totaal</c> of <c>#iban</c>. Een onbekende variabele blijft staan.
/// </summary>
public static partial class Factuurteksten
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static IReadOnlyList<(string Naam, string Betekenis)> Alle { get; } =
    [
        ("#factuurnummer", "het factuurnummer"),
        ("#datum", "de factuurdatum"),
        ("#vervaldatum", "de vervaldatum"),
        ("#termijn", "de betalingstermijn in dagen"),
        ("#totaal", "het totaal inclusief btw"),
        ("#kenmerk", "het betalingskenmerk"),
        ("#klant", "de naam van de klant"),
        ("#naam", "de naam van de eigen onderneming"),
        ("#adres", "het adres van de eigen onderneming op één regel"),
        ("#iban", "het (eerste) eigen IBAN"),
        ("#kvk", "het eigen KvK-nummer"),
        ("#btwnummer", "het eigen btw-nummer"),
        ("#email", "het eigen e-mailadres"),
        ("#telefoon", "het eigen telefoonnummer"),
        ("#website", "de eigen website"),
    ];

    /// <summary>De tekst met de variabelen gevuld; dubbele scheidingstekens door lege waarden vallen weg.</summary>
    public static string Vul(string tekst, Verkoopfactuur factuur, Partij afzender, decimal totaal)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(afzender);
        var gevuld = Variabele().Replace(tekst, m => Waarde(m.Groups[1].Value.ToLowerInvariant(), factuur, afzender, totaal) ?? m.Value);
        // "KvK  · Btw" als een waarde leeg is: het label met de lege waarde valt weg.
        gevuld = LeegLabel().Replace(gevuld, " · ");
        return Scheiding().Replace(gevuld, " · ").Trim(' ', '·');
    }

    private static string? Waarde(string naam, Verkoopfactuur f, Partij a, decimal totaal) => naam switch
    {
        "factuurnummer" => f.Nummer ?? "(concept)",
        "datum" => f.Datum.ToString("d MMMM yyyy", Nl),
        "vervaldatum" => f.Vervaldatum.ToString("d MMMM yyyy", Nl),
        "termijn" => (f.Vervaldatum.DayNumber - f.Datum.DayNumber).ToString(Nl),
        "totaal" => "€ " + totaal.ToString("#,0.00", Nl),
        "kenmerk" => f.Betalingskenmerk is { } k ? Betalingskenmerk.Toon(k) : f.Nummer ?? "",
        "klant" => f.Klant.Naam,
        "naam" => a.Naam,
        "adres" => string.Join(", ", a.Adres.Regels),
        "iban" => a.Ibans is [var iban, ..] ? Relaties.Iban.Toon(iban) : "",
        "kvk" => a.KvkNummer ?? "",
        "btwnummer" => a.BtwNummer ?? "",
        "email" => a.Email ?? "",
        "telefoon" => a.Telefoon ?? "",
        "website" => a.Website ?? "",
        _ => null,
    };

    [GeneratedRegex(@"#([a-zA-Z]+)")]
    private static partial Regex Variabele();

    [GeneratedRegex(@"\s*·\s*(KvK|Btw|IBAN|Tel\.?|E-mail)\s*(?=·|$)")]
    private static partial Regex LeegLabel();

    [GeneratedRegex(@"(\s*·\s*)+")]
    private static partial Regex Scheiding();
}
