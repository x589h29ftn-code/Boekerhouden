namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Zoeken in het inkoopboek: tekst (in leverancier, factuurnummer, omschrijving en boekstuk; meerdere woorden moeten
/// allemaal ergens voorkomen), één leverancier, het bedrag inclusief btw van–tot (een creditnota telt met zijn
/// bedrag zonder minteken) en alleen wat nog openstaat. Leeg = geen voorwaarde.
/// </summary>
public sealed record Inkoopboekfilter(string? Tekst = null, string? Relatie = null, decimal? BedragVan = null, decimal? BedragTot = null, bool AlleenOpen = false)
{
    public bool Past(Factuurboekregel regel)
    {
        ArgumentNullException.ThrowIfNull(regel);
        if (Relatie is { Length: > 0 } relatie && regel.Relatie != relatie)
        {
            return false;
        }

        var bedrag = Math.Abs(regel.Totaal);
        if ((BedragVan is { } van && bedrag < van) || (BedragTot is { } tot && bedrag > tot) || (AlleenOpen && regel.Openstaand == 0))
        {
            return false;
        }

        var velden = $"{regel.Leverancier} {regel.Relatie} {regel.Factuurnummer} {regel.Omschrijving} {regel.Dagboek} {regel.Boekstuk}";
        return (Tekst ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(woord => velden.Contains(woord, StringComparison.OrdinalIgnoreCase));
    }
}
