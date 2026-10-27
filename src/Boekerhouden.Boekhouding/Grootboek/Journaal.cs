using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een mutatie op een grootboekkaart, met het lopende saldo.</summary>
public sealed record Kaartregel(
    DateOnly Datum,
    string Dagboek,
    string Boekstuk,
    string Omschrijving,
    decimal Bedrag,
    decimal Saldo,
    string? Relatie,
    string? Factuurnummer);

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
            mutaties.Add(new Kaartregel(post.Datum, post.Dagboek, post.Boekstuk, regel.Omschrijving ?? post.Omschrijving, regel.Bedrag, saldo, regel.Relatie, regel.Factuurnummer));
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

    /// <summary>De proefbalans van een kalenderjaar.</summary>
    public Proefbalans Proefbalans(int boekjaar) => Proefbalans(new DateOnly(boekjaar, 1, 1), new DateOnly(boekjaar, 12, 31));

    private static DateOnly Boekjaarbegin(DateOnly datum) => new(datum.Year, 1, 1);

    private IEnumerable<(Journaalpost Post, Journaalregel Regel)> Regels(string rekeningnummer) =>
        posten.SelectMany(p => p.Regels.Where(r => r.Rekeningnummer == rekeningnummer).Select(r => (p, r)));
}
