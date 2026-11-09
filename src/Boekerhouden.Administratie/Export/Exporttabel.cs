namespace Boekerhouden.Administratie.Export;

public enum Exportsoort
{
    Rekening,
    Kop,
    Subtotaal,
    Totaal,
}

/// <summary>Een regel van een overzicht zoals op het scherm: een rekening, een kop of een (sub)totaal.</summary>
/// <param name="Niveau">Hoe diep ingesprongen (0 = sectie).</param>
/// <param name="Bedragen">Per kolom het bedrag, of leeg.</param>
public sealed record Exportregel(Exportsoort Soort, string? Nummer, string Tekst, int Niveau, IReadOnlyList<decimal?> Bedragen);

/// <summary>
/// Een overzicht precies zoals het op het scherm staat (bijv. de kolommenbalans per maand of ingedeeld naar RGS), om
/// als Excel of PDF te bewaren: titel, periode, kolomkoppen na rekening en omschrijving, en de regels.
/// </summary>
public sealed record Exporttabel(string Titel, DateOnly Van, DateOnly Tot, IReadOnlyList<string> Koppen, IReadOnlyList<Exportregel> Regels)
{
    /// <summary>De PDF staand (A4 portrait) in plaats van liggend, voor een smal overzicht.</summary>
    public bool Staand { get; init; }
}
