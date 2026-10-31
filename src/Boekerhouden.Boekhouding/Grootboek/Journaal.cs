using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een mutatie op een grootboekkaart, met het lopende saldo.</summary>
/// <param name="BtwCode">De btw-code van de regel.</param>
/// <param name="Btw">De btw die in dezelfde boeking bij deze regel hoort (zelfde btw-code), naar verhouding als meer regels
/// die code hebben; leeg op de btw-rekeningen zelf en zonder btw-code.</param>
public sealed record Kaartregel(
    DateOnly Datum,
    string Dagboek,
    string Boekstuk,
    string Omschrijving,
    decimal Bedrag,
    decimal Saldo,
    string? Relatie,
    string? Factuurnummer,
    string? BtwCode = null,
    decimal? Btw = null);

/// <summary>De grootboekkaart van één rekening over een periode.</summary>
public sealed record Grootboekkaart(Rekening Rekening, DateOnly Van, DateOnly Tot, decimal Beginsaldo, IReadOnlyList<Kaartregel> Mutaties)
{
    public decimal Eindsaldo => Beginsaldo + Mutaties.Sum(m => m.Bedrag);

    public decimal TotaalDebet => Mutaties.Where(m => m.Bedrag > 0).Sum(m => m.Bedrag);

    public decimal TotaalCredit => -Mutaties.Where(m => m.Bedrag < 0).Sum(m => m.Bedrag);
}

/// <summary>Het saldo van één rekening in de proefbalans.</summary>
public sealed record Proefbalansregel(Rekening Rekening, decimal Saldo);

/// <summary>
/// De proefbalans over een periode: balansrekeningen met hun stand op de einddatum, resultaatrekeningen met de
/// mutaties in de periode. Het resultaat van vóór de periode staat op de rekening <see cref="Rekeningfunctie.OnverdeeldResultaat"/>,
/// zodat de proefbalans altijd sluit.
/// </summary>
public sealed record Proefbalans(DateOnly Van, DateOnly Tot, IReadOnlyList<Proefbalansregel> Regels)
{
    public decimal Saldo => Regels.Sum(r => r.Saldo);

    /// <summary>Het resultaat van de periode: winst positief (de som van de resultaatrekeningen, omgekeerd).</summary>
    public decimal Resultaat => -Regels.Where(r => !r.Rekening.IsBalans).Sum(r => r.Saldo);

    /// <summary>De saldibalans voor het samenstelpakket: alle rekeningen met een saldo, met hun RGS-code.</summary>
    public Saldibalans NaarSaldibalans(int boekjaar, string? klant) =>
        new(boekjaar, klant, Regels
            .Where(r => r.Saldo != 0)
            .Select(r => new SaldibalansRegel(r.Rekening.Nummer, r.Rekening.Naam, r.Saldo, r.Rekening.RgsCode))
            .ToList());
}

/// <summary>
/// Het journaal van één administratie: alle geboekte journaalposten, met de overzichten die eruit volgen
/// (grootboekkaart, proefbalans). Een journaalpost wordt alleen geboekt als hij door de controles komt.
/// </summary>
public sealed class Journaal
{
    private readonly List<Journaalpost> posten = [];
    private readonly List<Dagboek> dagboeken;

    public Journaal(Rekeningschema schema, IEnumerable<Dagboek> dagboeken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(dagboeken);
        Schema = schema;
        this.dagboeken = dagboeken.ToList();
    }

    public Rekeningschema Schema { get; }

    /// <summary>
    /// Het journaal zoals het al geboekt is (uit de database): de posten worden niet opnieuw gecontroleerd, want dat
    /// is bij het boeken gebeurd.
    /// </summary>
    public static Journaal Herstel(Rekeningschema schema, IEnumerable<Dagboek> dagboeken, IEnumerable<Journaalpost> geboekt)
    {
        ArgumentNullException.ThrowIfNull(geboekt);
        var journaal = new Journaal(schema, dagboeken);
        journaal.posten.AddRange(geboekt);
        return journaal;
    }

    public IReadOnlyList<Dagboek> Dagboeken => dagboeken;

    public IReadOnlyList<Journaalpost> Posten => posten;

    public IReadOnlyList<string> Controleer(Journaalpost post)
    {
        var fouten = Journaalcontrole.Controleer(post, Schema, dagboeken).ToList();
        if (posten.Any(p => p.Dagboek == post.Dagboek && p.Boekstuk == post.Boekstuk))
        {
            fouten.Add($"Boekstuk {post.Boekstuk} bestaat al in dagboek {post.Dagboek}.");
        }

        return fouten;
    }

    /// <summary>Boekt de journaalpost; een <see cref="ArgumentException"/> met alle fouten als dat niet kan.</summary>
    public void Boek(Journaalpost post)
    {
        var fouten = Controleer(post);
        if (fouten.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", fouten), nameof(post));
        }

        posten.Add(post);
    }

    /// <summary>Het volgende boekstuknummer in een dagboek: jaar plus volgnummer, bijv. <c>2026-0007</c>.</summary>
    public string VolgendBoekstuk(string dagboek, int jaar)
    {
        var voorvoegsel = $"{jaar}-";
        var hoogste = posten
            .Where(p => p.Dagboek == dagboek && p.Boekstuk.StartsWith(voorvoegsel, StringComparison.Ordinal))
            .Select(p => int.TryParse(p.Boekstuk.AsSpan(voorvoegsel.Length), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{voorvoegsel}{hoogste + 1:0000}";
    }

    public Grootboekkaart Kaart(string rekeningnummer, DateOnly van, DateOnly tot)
    {
        var rekening = Schema.Zoek(rekeningnummer) ?? throw new ArgumentException($"Rekening {rekeningnummer} bestaat niet.", nameof(rekeningnummer));
        var regels = Regels(rekeningnummer).ToList();
        var beginsaldo = regels.Where(x => x.Post.Datum < van && (rekening.IsBalans || x.Post.Datum >= Boekjaarbegin(van)))
            .Sum(x => x.Regel.Bedrag);
        var saldo = beginsaldo;
        var mutaties = new List<Kaartregel>();
        foreach (var (post, regel) in regels.Where(x => x.Post.Datum >= van && x.Post.Datum <= tot)
                     .OrderBy(x => x.Post.Datum).ThenBy(x => x.Post.Dagboek, StringComparer.Ordinal).ThenBy(x => x.Post.Boekstuk, StringComparer.Ordinal))
        {
            saldo += regel.Bedrag;
            mutaties.Add(new Kaartregel(post.Datum, post.Dagboek, post.Boekstuk, regel.Omschrijving ?? post.Omschrijving, regel.Bedrag, saldo, regel.Relatie, regel.Factuurnummer,
                regel.BtwCode, BtwBij(post, regel)));
        }

        return new Grootboekkaart(rekening, van, tot, beginsaldo, mutaties);
    }

    public Proefbalans Proefbalans(DateOnly van, DateOnly tot)
    {
        var saldi = Schema.Alle.ToDictionary(r => r.Nummer, _ => 0m, StringComparer.Ordinal);
        var eerder = 0m;
        foreach (var post in posten.Where(p => p.Datum <= tot))
        {
            foreach (var regel in post.Regels)
            {
                var rekening = Schema.Zoek(regel.Rekeningnummer)!;
                if (rekening.IsBalans || post.Datum >= van)
                {
                    saldi[rekening.Nummer] += regel.Bedrag;
                }
                else
                {
                    eerder += regel.Bedrag;
                }
            }
        }

        if (eerder != 0)
        {
            var onverdeeld = Schema.MetFunctie(Rekeningfunctie.OnverdeeldResultaat).FirstOrDefault()
                ?? throw new InvalidOperationException("Er is resultaat van vóór de periode, maar geen rekening voor het onverdeelde resultaat.");
            saldi[onverdeeld.Nummer] += eerder;
        }

        return new Proefbalans(van, tot, Schema.Alle.Select(r => new Proefbalansregel(r, saldi[r.Nummer])).ToList());
    }

    /// <summary>
    /// De kolommenbalans over een periode. Balansrekeningen beginnen met hun stand van vóór de periode (als debet of
    /// credit), resultaatrekeningen met nul; het resultaat van vóór de periode komt op het onverdeelde resultaat. De
    /// saldi zijn gelijk aan die van de <see cref="Proefbalans(DateOnly, DateOnly)"/>.
    /// </summary>
    public Kolommenbalans Kolommenbalans(DateOnly van, DateOnly tot)
    {
        var debet = Schema.Alle.ToDictionary(r => r.Nummer, _ => 0m, StringComparer.Ordinal);
        var credit = Schema.Alle.ToDictionary(r => r.Nummer, _ => 0m, StringComparer.Ordinal);
        var begin = Schema.Alle.ToDictionary(r => r.Nummer, _ => 0m, StringComparer.Ordinal);
        var eerder = 0m;
        foreach (var post in posten.Where(p => p.Datum <= tot))
        {
            foreach (var regel in post.Regels)
            {
                var rekening = Schema.Zoek(regel.Rekeningnummer)!;
                if (post.Datum >= van)
                {
                    if (regel.Bedrag > 0)
                    {
                        debet[rekening.Nummer] += regel.Bedrag;
                    }
                    else
                    {
                        credit[rekening.Nummer] -= regel.Bedrag;
                    }
                }
                else if (rekening.IsBalans)
                {
                    begin[rekening.Nummer] += regel.Bedrag;
                }
                else
                {
                    eerder += regel.Bedrag;
                }
            }
        }

        if (eerder != 0)
        {
            var onverdeeld = Schema.MetFunctie(Rekeningfunctie.OnverdeeldResultaat).FirstOrDefault()
                ?? throw new InvalidOperationException("Er is resultaat van vóór de periode, maar geen rekening voor het onverdeelde resultaat.");
            begin[onverdeeld.Nummer] += eerder;
        }

        return new Kolommenbalans(van, tot, Schema.Alle.Select(r => new Kolommenbalansregel(
            r,
            debet[r.Nummer] + Math.Max(begin[r.Nummer], 0),
            credit[r.Nummer] + Math.Max(-begin[r.Nummer], 0))).ToList());
    }

    /// <summary>
    /// De kolommenbalans per maand over een periode (hoogstens <see cref="Maandoverzicht.MaximumMaanden"/> maanden):
    /// begin, mutaties per maand en eind per rekening. Het eind is gelijk aan het saldo in de
    /// <see cref="Kolommenbalans(DateOnly, DateOnly)"/>.
    /// </summary>
    public Maandoverzicht Maandoverzicht(DateOnly van, DateOnly tot)
    {
        if (tot < van)
        {
            throw new ArgumentException("De periode eindigt vóór het begin.", nameof(tot));
        }

        var maanden = new List<DateOnly>();
        for (var m = new DateOnly(van.Year, van.Month, 1); m <= tot; m = m.AddMonths(1))
        {
            maanden.Add(m);
        }

        if (maanden.Count > Grootboek.Maandoverzicht.MaximumMaanden)
        {
            throw new ArgumentException($"Per maand kan over hoogstens {Grootboek.Maandoverzicht.MaximumMaanden} maanden.", nameof(tot));
        }

        var begin = Schema.Alle.ToDictionary(r => r.Nummer, _ => 0m, StringComparer.Ordinal);
        var perMaand = Schema.Alle.ToDictionary(r => r.Nummer, _ => new decimal[maanden.Count], StringComparer.Ordinal);
        var eerder = 0m;
        foreach (var post in posten.Where(p => p.Datum <= tot))
        {
            foreach (var regel in post.Regels)
            {
                var rekening = Schema.Zoek(regel.Rekeningnummer)!;
                if (post.Datum >= van)
                {
                    perMaand[rekening.Nummer][((post.Datum.Year - van.Year) * 12) + post.Datum.Month - van.Month] += regel.Bedrag;
                }
                else if (rekening.IsBalans)
                {
                    begin[rekening.Nummer] += regel.Bedrag;
                }
                else
                {
                    eerder += regel.Bedrag;
                }
            }
        }

        if (eerder != 0)
        {
            var onverdeeld = Schema.MetFunctie(Rekeningfunctie.OnverdeeldResultaat).FirstOrDefault()
                ?? throw new InvalidOperationException("Er is resultaat van vóór de periode, maar geen rekening voor het onverdeelde resultaat.");
            begin[onverdeeld.Nummer] += eerder;
        }

        return new Maandoverzicht(van, tot, maanden, [.. Schema.Alle.Select(r => new Maandoverzichtregel(r, begin[r.Nummer], perMaand[r.Nummer]))]);
    }

    /// <summary>De kolommenbalans van een kalenderjaar.</summary>
    public Kolommenbalans Kolommenbalans(int boekjaar) => Kolommenbalans(new DateOnly(boekjaar, 1, 1), new DateOnly(boekjaar, 12, 31));

    /// <summary>De proefbalans van een kalenderjaar.</summary>
    public Proefbalans Proefbalans(int boekjaar) => Proefbalans(new DateOnly(boekjaar, 1, 1), new DateOnly(boekjaar, 12, 31));

    private static DateOnly Boekjaarbegin(DateOnly datum) => new(datum.Year, 1, 1);

    private static bool IsBtwRekening(Rekening? rekening) => rekening?.Functie is Rekeningfunctie.Voorbelasting or Rekeningfunctie.AfTeDragenBtw;

    /// <summary>
    /// De btw bij een regel: de btw-regels in dezelfde boeking met dezelfde btw-code (eerst de voorbelasting, anders de
    /// af te dragen btw, zodat bij verlegde btw de aftrek telt), verdeeld over de regels met die code naar hun bedrag; de
    /// laatste krijgt het afrondingsverschil.
    /// </summary>
    private decimal? BtwBij(Journaalpost post, Journaalregel regel)
    {
        if (string.IsNullOrWhiteSpace(regel.BtwCode) || IsBtwRekening(Schema.Zoek(regel.Rekeningnummer)))
        {
            return null;
        }

        var code = regel.BtwCode;
        var btwregels = post.Regels.Where(r => r.BtwCode == code && IsBtwRekening(Schema.Zoek(r.Rekeningnummer))).ToList();
        var voorbelasting = btwregels.Where(r => Schema.Zoek(r.Rekeningnummer)!.Functie == Rekeningfunctie.Voorbelasting).ToList();
        var btw = (voorbelasting.Count > 0 ? voorbelasting : btwregels).Sum(r => r.Bedrag);
        var grondslagen = post.Regels.Where(r => r.BtwCode == code && !IsBtwRekening(Schema.Zoek(r.Rekeningnummer))).ToList();
        if (btwregels.Count == 0 || grondslagen.Count == 0)
        {
            return btwregels.Count == 0 ? 0m : null;
        }

        var totaal = grondslagen.Sum(r => r.Bedrag);
        if (totaal == 0)
        {
            return null;
        }

        var verdeeld = grondslagen.Select(r => decimal.Round(btw * r.Bedrag / totaal, 2, MidpointRounding.AwayFromZero)).ToList();
        var index = grondslagen.FindIndex(r => ReferenceEquals(r, regel));
        return index == grondslagen.Count - 1 ? btw - verdeeld.Take(index).Sum() : verdeeld[index];
    }

    private IEnumerable<(Journaalpost Post, Journaalregel Regel)> Regels(string rekeningnummer) =>
        posten.SelectMany(p => p.Regels.Where(r => r.Rekeningnummer == rekeningnummer).Select(r => (p, r)));
}
