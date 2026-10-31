using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Afletteren;

/// <summary>
/// Zoekt openstaande posten bij wat de gebruiker typt in de kolom factuurnummer: een nummer (of een deel ervan), een
/// bedrag ("99,99", ook zonder minteken) of (een deel van) de naam van de leverancier of klant. De beste treffers eerst:
/// een gelijk nummer of bedrag, dan een nummer dat ermee begint, de naam, en een deel van het nummer; bij gelijke stand
/// de posten van de voorkeursrelaties (de tegenpartij van de mutatie) en dan de oudste.
/// </summary>
public static class Postzoeker
{
    public static IReadOnlyList<OpenPost> Zoek(string? tekst, IEnumerable<OpenPost> posten, IEnumerable<Relatie> relaties, IReadOnlySet<string>? voorkeur = null, int aantal = 5)
    {
        ArgumentNullException.ThrowIfNull(posten);
        ArgumentNullException.ThrowIfNull(relaties);
        var namen = relaties.GroupBy(r => r.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Naam, StringComparer.Ordinal);
        var lijst = posten.ToList();
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return [.. lijst.OrderByDescending(p => voorkeur?.Contains(p.Relatie) == true).ThenBy(p => p.Datum).Take(aantal)];
        }

        var zoek = tekst.Trim();
        var nummer = Factuurnummers.Normaliseer(zoek);
        decimal? bedrag = BedragParser.TryParse(zoek, out var b) ? Math.Abs(b) : null;
        int Score(OpenPost p)
        {
            var eigen = Factuurnummers.Normaliseer(p.Factuurnummer);
            var naam = namen.GetValueOrDefault(p.Relatie) ?? p.Relatie;
            var score = 0;
            if (bedrag is { } bd && (Math.Abs(p.Openstaand) == bd || Math.Abs(p.Bedrag) == bd))
            {
                score = Math.Max(score, Math.Abs(p.Openstaand) == bd ? 100 : 90);
            }

            if (nummer.Length > 0)
            {
                score = Math.Max(score, eigen == nummer ? 95 : eigen.StartsWith(nummer, StringComparison.Ordinal) ? 80 : eigen.Contains(nummer, StringComparison.Ordinal) ? 50 : 0);
            }

            if (naam.StartsWith(zoek, StringComparison.CurrentCultureIgnoreCase) || p.Relatie.StartsWith(zoek, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 70);
            }
            else if (naam.Contains(zoek, StringComparison.CurrentCultureIgnoreCase))
            {
                score = Math.Max(score, 40);
            }

            return score;
        }

        return [.. lijst.Select(p => (Post: p, Score: Score(p))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => voorkeur?.Contains(x.Post.Relatie) == true).ThenBy(x => x.Post.Datum)
            .Take(aantal).Select(x => x.Post)];
    }
}
