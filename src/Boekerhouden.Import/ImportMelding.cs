namespace Boekerhouden.Import;

public enum MeldingErnst
{
    /// <summary>Blokkeert de import.</summary>
    Fout,

    /// <summary>Vraagt aandacht; de import gaat door.</summary>
    Waarschuwing,

    /// <summary>Alleen ter informatie (bijvoorbeeld een overzicht van verschillen).</summary>
    Informatie,
}

/// <summary>Een melding uit de importcontrole. <see cref="Rij"/> is het rijnummer in het bestand (1-based).</summary>
public sealed record ImportMelding(string Code, MeldingErnst Ernst, string Tekst, int? Rij = null);
