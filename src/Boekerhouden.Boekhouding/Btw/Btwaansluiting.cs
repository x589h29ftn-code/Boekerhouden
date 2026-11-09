using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Btw;

/// <summary>
/// De aansluiting van de btw met het grootboek, van 1 januari tot en met het eind van een periode: het saldo van de
/// btw-rekeningen aan het begin van het jaar, plus wat de aangiften te betalen geven, min wat er betaald of ontvangen is
/// (boekingen op de btw-rekeningen zonder btw-code), hoort het saldo van de btw-rekeningen in de kolommenbalans te zijn.
/// Alle bedragen als "te betalen" (een schuld positief).
/// </summary>
/// <param name="Overig">Betalingen, ontvangsten en correcties op de btw-rekeningen (zonder btw-code); betaald is negatief.</param>
/// <param name="Saldo">Het saldo van de btw-rekeningen volgens het grootboek op de einddatum.</param>
public sealed record Btwaansluiting(DateOnly Van, DateOnly Tot, decimal Begin, decimal Aangiften, decimal Overig, decimal Saldo)
{
    /// <summary>Wat het saldo zou moeten zijn: begin plus aangiften plus betalingen.</summary>
    public decimal Verwacht => Begin + Aangiften + Overig;

    /// <summary>Niet nul als er btw op een btw-rekening staat die niet in een aangifte valt (bijv. een code zonder rubriek).</summary>
    public decimal Verschil => Saldo - Verwacht;

    /// <param name="posten">Alle posten van 1 januari tot en met <paramref name="tot"/>, met de beginstand (boekstuk "begin").</param>
    public static Btwaansluiting Bereken(IReadOnlyList<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes, DateOnly van, DateOnly tot)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(schema);
        var btwrekening = schema.Alle.Where(Btwaangifte.IsBtwrekening).Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
        var regels = posten.Where(p => p.Datum <= tot).SelectMany(p => p.Regels.Select(r => (Post: p, Regel: r))).Where(x => btwrekening.Contains(x.Regel.Rekeningnummer)).ToList();
        var begin = -regels.Where(x => x.Post.Datum < van).Sum(x => x.Regel.Bedrag);
        var saldo = -regels.Sum(x => x.Regel.Bedrag);
        var overig = -regels.Where(x => x.Post.Datum >= van && x.Regel.BtwCode is null && !Btwaangifte.IsPrivegebruikregel(x.Post, x.Regel, schema)).Sum(x => x.Regel.Bedrag);
        var aangiften = Btwaangifte.Bereken(posten.Where(p => p.Datum >= van), schema, codes, van, tot).TeBetalen;
        return new Btwaansluiting(van, tot, begin, aangiften, overig, saldo);
    }
}

/// <summary>Een aangifte zoals die is vastgelegd (ingediend): per rubriek grondslag en btw, en de voorbelasting.</summary>
public sealed record VastgelegdeAangifte(DateOnly Van, DateOnly Tot, IReadOnlyList<Aangifterubriek> Rubrieken, decimal Voorbelasting, DateTimeOffset Op, string Door)
{
    public decimal TeBetalen => Rubrieken.Sum(r => r.Btw) - Voorbelasting;
}

/// <summary>
/// Een periode in het btw-overzicht van een jaar: de aangifte zoals die nu uit de boekingen volgt, en (als die is
/// vastgelegd) zoals hij is ingediend. Wijkt de berekening af van wat is ingediend (later geboekt in een periode die al
/// is aangegeven), dan is dat het verschil voor de suppletie.
/// </summary>
public sealed record Btwperiode(Aangifteperiode Periode, Btwaangifte Berekend, VastgelegdeAangifte? Vastgelegd)
{
    public decimal? Verschil => Vastgelegd is { } v ? Berekend.TeBetalen - v.TeBetalen : null;
}

/// <summary>Het btw-overzicht van een jaar: de perioden en de rondrekening over het hele jaar.</summary>
/// <param name="Vastgelegd">Alle vastgelegde aangiften van het jaar (ook van een andere frequentie, als die is gewijzigd).</param>
public sealed record Btwjaar(int Jaar, Aangiftefrequentie Frequentie, IReadOnlyList<Btwperiode> Perioden, Btwaangifte Jaaraangifte, IReadOnlyList<VastgelegdeAangifte> Vastgelegd)
{
    /// <summary>Wat er in het jaar is aangegeven (de vastgelegde aangiften).</summary>
    public decimal Aangegeven => Vastgelegd.Sum(v => v.TeBetalen);

    /// <summary>
    /// Wat er in perioden die al zijn aangegeven later nog is geboekt (volgens de boekingen nu min wat is aangegeven):
    /// positief = nog te betalen, negatief = terug te vragen. Dit hoort in een suppletie.
    /// </summary>
    public decimal Suppletie => Perioden.Sum(p => p.Verschil ?? 0m);

    /// <summary>Wat de perioden die nog niet zijn aangegeven te betalen (of terug) geven.</summary>
    public decimal NietAangegeven => Perioden.Where(p => p.Vastgelegd is null).Sum(p => p.Berekend.TeBetalen);

    /// <summary>Het hele jaar min aangegeven, suppletie en nog niet aangegeven: nul, tenzij de frequentie in het jaar is gewijzigd.</summary>
    public decimal Rest => Jaaraangifte.TeBetalen - Aangegeven - Suppletie - NietAangegeven;

    /// <summary>Perioden die nog niet zijn vastgelegd maar wel btw hebben.</summary>
    public IEnumerable<Btwperiode> Open => Perioden.Where(p => p.Vastgelegd is null && (p.Berekend.Verschuldigd != 0 || p.Berekend.Voorbelasting != 0));

    public static Btwjaar Maak(int jaar, Aangiftefrequentie frequentie, IReadOnlyList<Journaalpost> posten, Rekeningschema schema, IReadOnlyList<BtwCode> codes,
        IReadOnlyList<VastgelegdeAangifte> vastgelegd)
    {
        ArgumentNullException.ThrowIfNull(vastgelegd);
        var perioden = new List<Btwperiode>();
        for (var periode = Aangifteperiode.Waarin(new DateOnly(jaar, 1, 1), frequentie); periode.Van.Year == jaar; periode = periode.Volgende)
        {
            perioden.Add(new Btwperiode(periode, Btwaangifte.Bereken(posten, schema, codes, periode.Van, periode.Tot),
                vastgelegd.FirstOrDefault(v => v.Van == periode.Van && v.Tot == periode.Tot)));
        }

        return new Btwjaar(jaar, frequentie, perioden, Btwaangifte.Bereken(posten, schema, codes, new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31)),
            [.. vastgelegd.Where(v => v.Van.Year == jaar)]);
    }
}
