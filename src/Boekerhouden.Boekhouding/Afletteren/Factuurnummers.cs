namespace Boekerhouden.Boekhouding.Afletteren;

/// <summary>
/// Een factuurnummer terugvinden in de omschrijving van een betaling, ongeacht leestekens en hoofdletters
/// (<c>2026-001</c> = <c>2026001</c> = <c>factuur:2026/001</c>).
/// </summary>
public static partial class Factuurnummers
{
    private static readonly char[] Scheidingen = [' ', '\t', '\n', ',', ';', ':', '(', ')', '[', ']'];

    public static string Normaliseer(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        return string.Concat(tekst.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    /// <summary>
    /// Komt het factuurnummer voor in de tekst? Gelijk aan een woord (bij alleen cijfers ook zonder voorloopnullen),
    /// of vanaf vier tekens als deel van een woord, mits er geen cijfer direct voor of na staat (anders zou
    /// <c>2026-001</c> ook in <c>2026-0012</c> gevonden worden), of los geschreven met dezelfde groepen in dezelfde
    /// volgorde ("inv 2026 87" voor INV-2026-0087, bij minstens drie groepen).
    /// </summary>
    public static bool KomtVoor(string? factuurnummer, string? tekst)
    {
        if (string.IsNullOrWhiteSpace(factuurnummer) || string.IsNullOrWhiteSpace(tekst))
        {
            return false;
        }

        var nummer = Normaliseer(factuurnummer);
        if (nummer.Length == 0)
        {
            return false;
        }

        foreach (var woord in tekst.Split(Scheidingen, StringSplitOptions.RemoveEmptyEntries).Select(Normaliseer))
        {
            if (woord == nummer || (IsGetal(woord) && IsGetal(nummer) && woord.TrimStart('0') is { Length: > 0 } w && w == nummer.TrimStart('0')))
            {
                return true;
            }

            if (nummer.Length >= 4 && BevatMetGrens(woord, nummer))
            {
                return true;
            }
        }

        return LosGeschreven(factuurnummer, tekst);
    }

    /// <summary>
    /// Wordt het factuurnummer genoemd, ook los geschreven ("inv 2026 87" voor INV-2026-0087) of alleen met het laatste
    /// getal als dat minstens drie cijfers heeft ("Fact GB-2026-1041 1042 1043")?
    /// </summary>
    public static bool Genoemd(string? factuurnummer, string? tekst)
    {
        if (KomtVoor(factuurnummer, tekst))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(factuurnummer) || string.IsNullOrWhiteSpace(tekst))
        {
            return false;
        }

        var laatste = Delen(factuurnummer).LastOrDefault(IsGetal);
        return laatste is { Length: >= 3 } && Delen(tekst).Contains(laatste);
    }

    /// <summary>
    /// Noemt de tekst een ander nummer uit dezelfde reeks als het factuurnummer: even lang (zonder leestekens), met
    /// dezelfde eerste vier tekens, maar niet gelijk ("9100234603" naast factuur 9100234601, "GB-2026-1105" naast
    /// GB-2026-1104)?
    /// </summary>
    public static bool AnderNummerUitReeks(string? factuurnummer, string? tekst)
    {
        if (string.IsNullOrWhiteSpace(factuurnummer) || string.IsNullOrWhiteSpace(tekst) || KomtVoor(factuurnummer, tekst))
        {
            return false;
        }

        var nummer = Normaliseer(factuurnummer);
        return nummer.Length >= 6 && nummer.Any(char.IsDigit)
               && tekst.Split(Scheidingen, StringSplitOptions.RemoveEmptyEntries).Select(Normaliseer)
                   .Any(w => w.Length == nummer.Length && w != nummer && w.AsSpan(0, 4).SequenceEqual(nummer.AsSpan(0, 4)));
    }

    /// <summary>De letter- en cijfergroepen, zonder voorloopnullen ("INV-2026-0087" → INV, 2026, 87).</summary>
    private static List<string> Delen(string tekst) =>
        [.. GroepPatroon().Matches(tekst.ToUpperInvariant()).Select(m => IsGetal(m.Value) ? m.Value.TrimStart('0') is { Length: > 0 } g ? g : "0" : m.Value)];

    /// <summary>Komen de groepen van het nummer (minstens drie) achter elkaar in de tekst voor?</summary>
    private static bool LosGeschreven(string factuurnummer, string tekst)
    {
        var nummer = Delen(factuurnummer);
        if (nummer.Count < 3 || !nummer.Any(IsGetal))
        {
            return false;
        }

        var woorden = Delen(tekst);
        for (var i = 0; i + nummer.Count <= woorden.Count; i++)
        {
            if (nummer.Select((d, j) => woorden[i + j] == d).All(x => x))
            {
                return true;
            }
        }

        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[A-Z]+|[0-9]+")]
    private static partial System.Text.RegularExpressions.Regex GroepPatroon();

    private static bool BevatMetGrens(string woord, string nummer)
    {
        for (var i = woord.IndexOf(nummer, StringComparison.Ordinal); i >= 0; i = woord.IndexOf(nummer, i + 1, StringComparison.Ordinal))
        {
            var ervoor = i > 0 && char.IsDigit(woord[i - 1]) && char.IsDigit(nummer[0]);
            var erna = i + nummer.Length < woord.Length && char.IsDigit(woord[i + nummer.Length]) && char.IsDigit(nummer[^1]);
            if (!ervoor && !erna)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGetal(string tekst) => tekst.Length > 0 && tekst.All(char.IsDigit);
}
