using System.Globalization;
using System.Text;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Een rekening kiezen op nummer of naam. De voorkeur (bijv. de rekeningen die bij deze leverancier het vaakst gebruikt
/// zijn) staat bovenaan; daarna nummers die zo beginnen, dan namen waarin een woord zo begint, dan namen die de tekst
/// ergens bevatten. Hoofdletters en accenten tellen niet.
/// </summary>
public static class Rekeningzoeker
{
    public static IReadOnlyList<Rekening> Zoek(string? tekst, IEnumerable<Rekening> rekeningen, IReadOnlyList<string>? voorkeur = null, int maximum = 12)
    {
        ArgumentNullException.ThrowIfNull(rekeningen);
        voorkeur ??= [];
        var zoek = Gewoon(tekst?.Trim() ?? string.Empty);
        var lijst = rekeningen.ToList();
        return lijst
            .Select(r => (Rekening: r, Score: Score(r, zoek), Voorkeur: Plaats(voorkeur, r.Nummer)))
            .Where(x => x.Score > 0)
            .OrderBy(x => x.Voorkeur)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Rekening.Nummer, StringComparer.Ordinal)
            .Take(maximum)
            .Select(x => x.Rekening)
            .ToList();
    }

    /// <summary>De rekening bij wat de gebruiker typte: het nummer zelf, of de enige rekening die past.</summary>
    public static Rekening? Enige(string? tekst, IEnumerable<Rekening> rekeningen)
    {
        ArgumentNullException.ThrowIfNull(rekeningen);
        var lijst = rekeningen.ToList();
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return null;
        }

        return lijst.FirstOrDefault(r => r.Nummer == tekst.Trim()) ?? (Zoek(tekst, lijst, maximum: 2) is [var enige] ? enige : null);
    }

    private static int Plaats(IReadOnlyList<string> voorkeur, string nummer)
    {
        for (var i = 0; i < voorkeur.Count; i++)
        {
            if (voorkeur[i] == nummer)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int Score(Rekening rekening, string zoek)
    {
        if (zoek.Length == 0)
        {
            return 1;
        }

        var naam = Gewoon(rekening.Naam);
        if (rekening.Nummer.StartsWith(zoek, StringComparison.Ordinal))
        {
            return 4;
        }

        if (naam.StartsWith(zoek, StringComparison.Ordinal))
        {
            return 3;
        }

        if (naam.Split(' ', '-', '/', '(').Any(w => w.StartsWith(zoek, StringComparison.Ordinal)))
        {
            return 2;
        }

        return naam.Contains(zoek, StringComparison.Ordinal) ? 1 : 0;
    }

    private static string Gewoon(string tekst)
    {
        var zonder = new StringBuilder();
        foreach (var c in tekst.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                zonder.Append(char.ToLowerInvariant(c));
            }
        }

        return zonder.ToString();
    }
}
