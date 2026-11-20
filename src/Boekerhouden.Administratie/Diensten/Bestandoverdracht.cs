namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Een bestand dat op de ene pagina wordt losgelaten en op de volgende wordt ingelezen (bijv. een staat vaste activa
/// op het overzicht vaste activa, ingelezen op de pagina om de staat over te nemen). Per sessie (scoped), hoogstens
/// één bestand, en alleen op te halen door de pagina waarvoor het bedoeld is.
/// </summary>
public sealed class Bestandoverdracht
{
    private (string Doel, string Naam, byte[] Inhoud)? bestand;

    public void Geef(string doel, string naam, byte[] inhoud)
    {
        ArgumentNullException.ThrowIfNull(doel);
        ArgumentNullException.ThrowIfNull(naam);
        ArgumentNullException.ThrowIfNull(inhoud);
        bestand = (doel, naam, inhoud);
    }

    /// <summary>Het bestand voor dit doel, of leeg; daarna is het weg.</summary>
    public (string Naam, byte[] Inhoud)? Neem(string doel)
    {
        if (bestand is { } b && b.Doel == doel)
        {
            bestand = null;
            return (b.Naam, b.Inhoud);
        }

        return null;
    }
}
