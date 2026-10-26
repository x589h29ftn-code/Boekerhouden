namespace Boekerhouden.Boekhouding.Grootboek;

public enum Rekeningsoort
{
    Balans,
    Resultaat,
}

/// <summary>
/// Wat een rekening doet bij automatische boekingen. Zo vinden factuur-, bank- en btw-boekingen de juiste rekening
/// zonder vaste nummers.
/// </summary>
public enum Rekeningfunctie
{
    Geen,
    Bank,
    Kas,
    Debiteuren,
    Crediteuren,

    /// <summary>Te vorderen omzetbelasting (voorbelasting).</summary>
    Voorbelasting,

    /// <summary>Af te dragen omzetbelasting.</summary>
    AfTeDragenBtw,

    /// <summary>Het resultaat van eerdere boekjaren dat nog niet is verdeeld; de proefbalans zet het hier neer.</summary>
    OnverdeeldResultaat,

    /// <summary>Tussenrekening voor bankmutaties die (nog) niet te verwerken zijn.</summary>
    Vraagposten,
}

/// <summary>Een grootboekrekening van de administratie, gekoppeld aan een RGS-code.</summary>
/// <param name="RgsCode">De RGS-code (niveau 3 of 4), bijv. <c>WBedAlkTel</c>; leeg als (nog) niet gekoppeld.</param>
public sealed record Rekening(
    string Nummer,
    string Naam,
    Rekeningsoort Soort,
    string? RgsCode = null,
    Rekeningfunctie Functie = Rekeningfunctie.Geen)
{
    public bool IsBalans => Soort == Rekeningsoort.Balans;
}
