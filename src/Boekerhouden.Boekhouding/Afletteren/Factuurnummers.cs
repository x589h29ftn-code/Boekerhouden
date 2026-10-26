namespace Boekerhouden.Boekhouding.Afletteren;

/// <summary>
/// Een factuurnummer terugvinden in de omschrijving van een betaling, ongeacht leestekens en hoofdletters
/// (<c>2026-001</c> = <c>2026001</c> = <c>factuur:2026/001</c>).
/// </summary>
public static class Factuurnummers
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
    /// <c>2026-001</c> ook in <c>2026-0012</c> gevonden worden).
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

        return false;
    }

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
