namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>Hoe het ervoor staat, voor het bolletje van een <see cref="Statusstip"/>.</summary>
public enum Stipsoort
{
    /// <summary>Nog te doen of nog niet af (een ring).</summary>
    Open,

    /// <summary>Af: geboekt, betaald, afgesloten, in orde.</summary>
    Klaar,

    /// <summary>Vraagt aandacht: vervallen, achterstand, beschadigd.</summary>
    LetOp,

    /// <summary>Niet meer in gebruik: gestopt, afgeschreven, gedesinvesteerd.</summary>
    Uit,
}
