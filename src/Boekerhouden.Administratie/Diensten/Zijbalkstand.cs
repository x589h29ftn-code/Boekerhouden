namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Wat de gebruiker in de zijbalk heeft opengeklapt (bijv. Inrichting), zolang de verbinding open is: de zijbalk
/// wordt op elke pagina opnieuw getekend, deze stand blijft. Scoped, dus per gebruiker en tabblad.
/// </summary>
public sealed class Zijbalkstand
{
    private readonly HashSet<string> open = new(StringComparer.Ordinal);

    public bool IsOpen(string groep) => open.Contains(groep);

    public void Wissel(string groep)
    {
        if (!open.Remove(groep))
        {
            open.Add(groep);
        }
    }
}
