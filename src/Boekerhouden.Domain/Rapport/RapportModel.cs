namespace Boekerhouden.Domain.Rapport;

public enum RapportSoort
{
    /// <summary>Jaarrekening van een BV (opdracht samenstellen).</summary>
    Jaarrekening,

    /// <summary>Bijlage bij de aangifte inkomstenbelasting (eenmanszaak, opdracht winstaangifte).</summary>
    AangifteBijlage,
}

public enum RegelSoort
{
    /// <summary>Een kop zonder bedragen, bijv. "Vaste activa".</summary>
    Kop,

    /// <summary>Een post met zijn totaal, eventueel gevolgd door detailregels.</summary>
    Post,

    Detail,

    /// <summary>Subtotaal met een enkele lijn erboven.</summary>
    Subtotaal,

    /// <summary>Eindtotaal met een dubbele lijn eronder.</summary>
    Eindtotaal,
}

/// <summary>
/// Eén regel in een tabel van het rapport. Bedragen zijn gepresenteerd: positief aan de normale kant van de post
/// (activa en kosten debet, passiva en opbrengsten credit). Null = leeg vak.
/// </summary>
public sealed record Regel(RegelSoort Soort, string Omschrijving, decimal? Detail = null, decimal? Totaal = null, decimal? DetailVorig = null, decimal? TotaalVorig = null);

/// <param name="Tweekoloms">Per jaar een detail- en een totaalkolom (balans en W&amp;V); anders één kolom per jaar.</param>
/// <param name="KopHuidig">Kolomkop van het boekjaar, bijv. "31 december 2025" of "2025".</param>
/// <param name="Code">De post waar de tabel over gaat (bij een specificatie), bijv. "BMva".</param>
public sealed record Tabel(string? Titel, bool Tweekoloms, string KopHuidig, string KopVorig, IReadOnlyList<Regel> Regels, string? Code = null);

public abstract record Blok;

public sealed record TabelBlok(Tabel Tabel) : Blok;

public sealed record TekstBlok(string Tekst) : Blok;

/// <summary>Een genummerd hoofdstuk; het begint op een nieuwe pagina.</summary>
/// <param name="Paragrafen">De titels van de tabellen zijn genummerde paragrafen (3.1, 3.2, …), zoals bij de specificaties.</param>
public sealed record Hoofdstuk(int Nummer, string Titel, IReadOnlyList<Blok> Blokken, bool Paragrafen = false);

/// <summary>Een deel van het rapport met een tussenblad, bijv. "JAARREKENING 2025".</summary>
public sealed record Deel(string Titel, IReadOnlyList<Hoofdstuk> Hoofdstukken);

/// <param name="Titel">Bijv. "Jaarrekening 2025" of "Bijlage bij de aangifte inkomstenbelasting 2025".</param>
/// <param name="Statustekst">Voor de voettekst, bijv. "Concept" of "Geen accountantscontrole toegepast".</param>
/// <param name="Waarschuwingen">Wat het rapport (nog) onvolledig maakt; staat vooraan in een concept.</param>
public sealed record Rapport(
    RapportSoort Soort,
    string Titel,
    string Klantnaam,
    string? Plaats,
    int Boekjaar,
    string Statustekst,
    bool Concept,
    IReadOnlyList<Deel> Delen,
    IReadOnlyList<string> Waarschuwingen)
{
    public IEnumerable<Hoofdstuk> Hoofdstukken => Delen.SelectMany(d => d.Hoofdstukken);
}
