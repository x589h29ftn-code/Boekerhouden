namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Een pagina in het menu van een administratie: het pad na <c>/a/{id}</c>, de naam, het recht dat nodig is, (bij
/// <paramref name="AlleenBv"/>) alleen voor een BV, en het icoon in de zijbalk (een naam uit iconen.svg).
/// </summary>
public sealed record Menuitem(string Pad, string Naam, Recht Recht, bool AlleenBv = false, string? Icoon = null)
{
    public bool Zichtbaar(AdministratieKop kop)
    {
        ArgumentNullException.ThrowIfNull(kop);
        return kop.Mag(Recht) && (!AlleenBv || kop.Ondernemingsvorm == Boekhouding.Grootboek.Ondernemingsvorm.BV);
    }
}

/// <summary>Een groep in het menu, met zijn pagina's; een inklapbare groep (inrichting) is dicht tot je hem opent.</summary>
public sealed record Menugroep(string Naam, IReadOnlyList<Menuitem> Items, bool Inklapbaar = false);

/// <summary>
/// Pagina's die bij elkaar horen onder één regel in het menu, als tabbladen bovenaan de pagina (bijv. Inkoop: te
/// verwerken en geboekt). De naam is de titel van de pagina; de eerste tab is de pagina waar het menu naartoe gaat.
/// </summary>
public sealed record Tabgroep(string Naam, IReadOnlyList<Menuitem> Tabs);

/// <summary>
/// Het menu van een administratie (de zijbalk) per groep: verwerken, facturen maken, overzichten en inrichting. Elke
/// soort staat er één keer in; wat erbij hoort (geboekt, terugkerend, crediteuren, beginbalans) is een tabblad op die
/// pagina. Ook de zoekfunctie vindt de pagina's hier, de tabbladen en de pagina's zonder eigen regel inbegrepen.
/// </summary>
public static class Administratiemenu
{
    public static readonly IReadOnlyList<Menugroep> Groepen =
    [
        new("Verwerken",
        [
            new("/inkoop", "Inkoop", Recht.Lezen, Icoon: "shopping-cart-simple"),
            new("/verkoop", "Verkoop", Recht.Lezen, Icoon: "storefront"),
            new("/bank", "Bank", Recht.Lezen, Icoon: "bank"),
            new("/memoriaal", "Memoriaal", Recht.Boekhouden, Icoon: "note-pencil"),
        ]),
        new("Facturen",
        [
            new("/facturen", "Facturen maken", Recht.Lezen, Icoon: "file-plus"),
        ]),
        new("Overzichten",
        [
            new("/kolommenbalans", "Kolommenbalans", Recht.Lezen, Icoon: "table"),
            new("/grootboek", "Grootboekkaart", Recht.Lezen, Icoon: "book-open-text"),
            new("/journaal", "Journaal", Recht.Lezen, Icoon: "list-numbers"),
            new("/debiteuren", "Openstaande posten", Recht.Lezen, Icoon: "hourglass-medium"),
            new("/btw", "Btw-aangifte", Recht.Lezen, Icoon: "percent"),
            new("/vpb", "Fiscale positie", Recht.Lezen, AlleenBv: true, Icoon: "scales"),
            new("/activa", "Vaste activa", Recht.Lezen, Icoon: "buildings"),
            new("/aansluitingen", "Aansluitingen", Recht.Lezen, Icoon: "checks"),
        ]),
        new("Inrichting",
        [
            new("/relaties", "Relaties", Recht.Lezen, Icoon: "address-book"),
            new("/regels", "Regels", Recht.Lezen, Icoon: "sliders-horizontal"),
            new("/rekeningschema", "Rekeningschema", Recht.Lezen, Icoon: "tree-structure"),
            new("/perioden", "Perioden", Recht.Lezen, Icoon: "calendar-check"),
            new("/instellingen", "Instellingen", Recht.Boekhouden, Icoon: "gear-six"),
        ], Inklapbaar: true),
    ];

    public static readonly IReadOnlyList<Tabgroep> Tabgroepen =
    [
        new("Inkoop", [new("/inkoop", "Te verwerken", Recht.Lezen), new("/inkoopboek", "Geboekt", Recht.Lezen)]),
        new("Verkoop", [new("/verkoop", "Te verwerken", Recht.Lezen), new("/verkoopboek", "Geboekt", Recht.Lezen)]),
        new("Facturen maken", [new("/facturen", "Facturen", Recht.Lezen), new("/verkoop/terugkerend", "Terugkerend", Recht.Lezen), new("/factuurlayout", "Lay-out", Recht.Lezen)]),
        new("Openstaande posten", [new("/debiteuren", "Debiteuren", Recht.Lezen), new("/crediteuren", "Crediteuren", Recht.Lezen)]),
        new("Instellingen", [new("/instellingen", "Instellingen", Recht.Boekhouden), new("/beginbalans", "Beginbalans", Recht.Boekhouden)]),
    ];

    /// <summary>Pagina's zonder eigen regel of tabblad: alleen te vinden via Zoeken (Ctrl+K).</summary>
    public static readonly IReadOnlyList<Menuitem> Verborgen =
    [
        new("/balans", "Balans en W&V", Recht.Lezen),
        new("/proefbalans", "Proefbalans", Recht.Lezen),
    ];

    /// <summary>De tabgroep waar de pagina met dit pad bij hoort, of null.</summary>
    public static Tabgroep? GroepVan(string pad) => Tabgroepen.FirstOrDefault(g => g.Tabs.Any(t => t.Pad == pad));

    /// <summary>De regel in het menu die oplicht voor de pagina met dit pad (bij een tabblad: de regel van de groep).</summary>
    public static string MenuPad(string pad) => GroepVan(pad)?.Tabs[0].Pad ?? pad;

    /// <summary>
    /// Alle pagina's om te zoeken, met de groep als toelichting: de regels van het menu, de tabbladen (bijv. "Inkoop ·
    /// Geboekt") en de verborgen pagina's; elk pad één keer.
    /// </summary>
    public static IEnumerable<(string Groep, Menuitem Item)> AllePaginas()
    {
        var gezien = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in Groepen)
        {
            foreach (var i in g.Items.Where(i => gezien.Add(i.Pad)))
            {
                yield return (g.Naam, i);
            }
        }

        foreach (var g in Tabgroepen)
        {
            foreach (var t in g.Tabs.Where(t => gezien.Add(t.Pad)))
            {
                yield return (g.Naam, t with { Naam = $"{g.Naam} · {t.Naam}" });
            }
        }

        foreach (var i in Verborgen.Where(i => gezien.Add(i.Pad)))
        {
            yield return ("Overzichten", i);
        }
    }
}
