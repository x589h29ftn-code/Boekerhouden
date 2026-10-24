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

/// <summary>Waar een regel over gaat, zodat de viewer weet wat je aanpast.</summary>
public enum Onderwerp
{
    /// <summary>Een RGS-post (hoofd- of subpost); de naam is per klant aan te passen.</summary>
    Post,

    /// <summary>Een grootboekrekening in een specificatie; omschrijving aanpassen of uitsplitsen.</summary>
    Rekening,

    /// <summary>Een eigen post van de klant.</summary>
    EigenPost,

    /// <summary>Een regel uit de uitsplitsing van een rekening.</summary>
    Uitsplitsing,
}

/// <param name="Sleutel">RGS-code of "eigen:&lt;id&gt;" (post), of het rekeningnummer.</param>
/// <param name="Id">De rekening (bij rekening en uitsplitsing).</param>
/// <param name="Herkomst">Voor de tooltip, bijv. "WBedAut · RGS: Autokosten" of "Rekening 4200 Autokosten".</param>
public sealed record Bron(Onderwerp Soort, string Sleutel, Guid? Id, string Herkomst);

/// <summary>
/// Eén regel in een tabel van het rapport. Bedragen zijn gepresenteerd: positief aan de normale kant van de post
/// (activa en kosten debet, passiva en opbrengsten credit). Null = leeg vak.
/// </summary>
public sealed record Regel(RegelSoort Soort, string Omschrijving, decimal? Detail = null, decimal? Totaal = null, decimal? DetailVorig = null, decimal? TotaalVorig = null)
{
    /// <summary>Waar de regel over gaat; null bij vaste regels zoals "Brutomarge" of "Totaal".</summary>
    public Bron? Bron { get; init; }

    /// <summary>Null: niet in of uit te klappen. Anders de stand; de sleutel is die van <see cref="Bron"/>.</summary>
    public bool? Uitgeklapt { get; init; }
}

/// <param name="Tweekoloms">Per jaar een detail- en een totaalkolom (balans en W&amp;V); anders één kolom per jaar.</param>
/// <param name="KopHuidig">Kolomkop van het boekjaar, bijv. "31 december 2025" of "2025".</param>
/// <param name="Code">De post waar de tabel over gaat (bij een specificatie), bijv. "BMva".</param>
public sealed record Tabel(string? Titel, bool Tweekoloms, string KopHuidig, string KopVorig, IReadOnlyList<Regel> Regels, string? Code = null)
{
    /// <summary>De bron van de titel (de post), om de naam aan te passen.</summary>
    public Bron? Bron { get; init; }
}

public abstract record Blok;

public sealed record TabelBlok(Tabel Tabel) : Blok;

public sealed record TekstBlok(string Tekst) : Blok
{
    /// <summary>De post (hoofdpost) waar de tekst een toelichting bij is; null bij een vaste tekst.</summary>
    public string? Toelichting { get; init; }

    /// <summary>Een eigen paragraaftitel, als de specificatie van de post niet in het rapport staat.</summary>
    public string? Titel { get; init; }
}

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
