namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een pagina in het menu van een administratie: het pad na <c>/a/{id}</c>, de naam en het recht dat nodig is.</summary>
public sealed record Menuitem(string Pad, string Naam, Recht Recht);

/// <summary>Een groep in het menu, met zijn pagina's.</summary>
public sealed record Menugroep(string Naam, IReadOnlyList<Menuitem> Items);

/// <summary>
/// Het menu van een administratie (de zijbalk) per groep: boekhouden (verwerken), dagboeken (terugvinden),
/// rapportages en inrichting. Ook de zoekfunctie vindt de pagina's hier.
/// </summary>
public static class Administratiemenu
{
    public static readonly IReadOnlyList<Menugroep> Groepen =
    [
        new("Boekhouden",
        [
            new("/inkoop", "Inkoop", Recht.Lezen),
            new("/verkoop", "Verkoop", Recht.Lezen),
            new("/verkoop/terugkerend", "Terugkerende facturen", Recht.Lezen),
            new("/bank", "Bank", Recht.Lezen),
            new("/memoriaal", "Memoriaal", Recht.Boekhouden),
            new("/activa", "Vaste activa", Recht.Lezen),
        ]),
        new("Dagboeken",
        [
            new("/inkoopboek", "Inkoop", Recht.Lezen),
            new("/verkoopboek", "Verkoop", Recht.Lezen),
            new("/journaal", "Journaal", Recht.Lezen),
        ]),
        new("Rapportages",
        [
            new("/balans", "Balans en W&V", Recht.Lezen),
            new("/kolommenbalans", "Kolommenbalans", Recht.Lezen),
            new("/debiteuren", "Openstaande debiteuren", Recht.Lezen),
            new("/crediteuren", "Openstaande crediteuren", Recht.Lezen),
            new("/aansluitingen", "Aansluitingen", Recht.Lezen),
            new("/proefbalans", "Proefbalans", Recht.Lezen),
            new("/grootboek", "Grootboekkaart", Recht.Lezen),
        ]),
        new("Inrichting",
        [
            new("/relaties", "Relaties", Recht.Lezen),
            new("/factuurlayout", "Factuurlay-out", Recht.Lezen),
            new("/regels", "Regels", Recht.Lezen),
            new("/rekeningschema", "Rekeningschema", Recht.Lezen),
            new("/perioden", "Perioden", Recht.Lezen),
            new("/instellingen", "Instellingen", Recht.Boekhouden),
        ]),
    ];
}
