namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Welk hoofdstuk van de documentatie (<c>/hulp</c>) bij een pagina hoort: het vraagteken rechtsonder opent de
/// documentatie op die plek. Pagina's die bij een andere horen (geboekt, een factuur, een boeking) wijzen naar die.
/// </summary>
public static class Hulpsectie
{
    /// <summary>Alle hoofdstukken, in de volgorde van de documentatie.</summary>
    public static readonly IReadOnlyList<(string Id, string Titel)> Hoofdstukken =
    [
        ("administraties", "Administraties"),
        ("overzicht", "Overzicht"),
        ("slepen", "Overal slepen en zoeken"),
        ("afdrukken", "Afdrukken"),
        ("inkoop", "Inkoop"),
        ("verkoop", "Verkoop inlezen"),
        ("bank", "Bank"),
        ("memoriaal", "Memoriaal"),
        ("facturen", "Facturen maken"),
        ("terugkerend", "Terugkerende facturen"),
        ("factuurlayout", "Lay-out van de factuur"),
        ("kolommenbalans", "Kolommenbalans"),
        ("grootboek", "Grootboekkaart"),
        ("journaal", "Journaal"),
        ("openstaand", "Openstaande posten"),
        ("btw", "Btw-aangifte"),
        ("vpb", "Fiscale positie"),
        ("activa", "Vaste activa"),
        ("aansluitingen", "Aansluitingen"),
        ("relaties", "Relaties"),
        ("regels", "Regels"),
        ("rekeningschema", "Rekeningschema"),
        ("perioden", "Perioden"),
        ("instellingen", "Instellingen en beginbalans"),
        ("beheer", "Beheer"),
    ];

    /// <summary>Het hoofdstuk bij een pad zonder de eerste slash (zoals <c>NavigationManager.ToBaseRelativePath</c> het geeft).</summary>
    public static string Van(string pad)
    {
        ArgumentNullException.ThrowIfNull(pad);
        var delen = pad.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return delen switch
        {
            ["a", _] => "overzicht",
            ["a", _, "verkoop", "terugkerend", ..] => "terugkerend",
            ["a", _, "verkoop", var tweede, ..] when tweede != "terugkerend" => "facturen",
            ["a", _, var onderdeel, ..] => onderdeel switch
            {
                "inkoop" or "inkoopboek" or "stapel" => "inkoop",
                "verkoop" or "verkoopboek" => "verkoop",
                "debiteuren" or "crediteuren" => "openstaand",
                "boeking" => "journaal",
                "proefbalans" or "balans" => "kolommenbalans",
                "beginbalans" => "instellingen",
                _ when Hoofdstukken.Any(h => h.Id == onderdeel) => onderdeel,
                _ => "overzicht",
            },
            ["beheer", ..] => "beheer",
            _ => "administraties",
        };
    }
}
