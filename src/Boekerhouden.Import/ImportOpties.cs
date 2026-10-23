namespace Boekerhouden.Import;

/// <summary>Keuzes die de gebruiker bij het importeren maakt.</summary>
public sealed record ImportOpties
{
    public static readonly ImportOpties Standaard = new();

    /// <summary>
    /// Het boekjaar dat de gebruiker bij het importeren opgeeft. Nodig als het bestand geen
    /// boekjaarcel heeft (zoals bij veel exports uit boekhoudpakketten). Staat het boekjaar ook in
    /// het bestand, dan moeten beide gelijk zijn.
    /// </summary>
    public int? Boekjaar { get; init; }
}
