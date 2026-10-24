namespace Boekerhouden.Server.Components.Rapportweergave;

/// <summary>Welk deel van het rapport bij één post wordt getoond (in het werkprogramma).</summary>
public enum PostDeel
{
    /// <summary>Specificatie, toelichting, verloop en grondslagen.</summary>
    Alles,

    /// <summary>Wat bij de post in de toelichting staat: specificatie, toelichting en verloop.</summary>
    Rapportage,

    /// <summary>De grondslagen van de post uit de algemene toelichting.</summary>
    Grondslagen,
}
