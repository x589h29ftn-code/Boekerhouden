namespace Boekerhouden.Server.Grondslagen;

/// <summary>
/// Meegeleverde toelichtingen bij posten in de jaarrekening van een BV, met variabelen zodat ze elk jaar vanzelf
/// kloppen. Ze staan onder de specificatie zolang het dossier (of een eerder jaar) geen eigen toelichting heeft;
/// aanpassen of leegmaken kan in de viewer en geldt dan vanaf dat jaar.
/// </summary>
public static class StandaardToelichtingen
{
    /// <summary>Per hoofdpost (RGS-code) de standaardtekst.</summary>
    public static IReadOnlyDictionary<string, string> Jaarrekening { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BEiv"] = "Het ingehouden deel van het resultaat over {boekjaar} bedraagt {valuta} {nettoresultaat} ({boekjaar_vorig}: {valuta} {nettoresultaat_vorig}).",
        ["WPer"] = "Gedurende {boekjaar} waren gemiddeld [aantal] werknemers in dienst op basis van een volledig dienstverband ({boekjaar_vorig}: [aantal]).",
    };
}
