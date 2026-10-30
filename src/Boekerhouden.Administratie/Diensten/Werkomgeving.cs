namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Waar de app draait. <see cref="Lokaal"/> is de Windows-app op één pc: één gebruiker die niet inlogt, zonder
/// webserver; schermen laten dan uitloggen, wachtwoord en tweestapsverificatie weg.
/// </summary>
public sealed record Werkomgeving(bool Lokaal);

/// <summary>
/// Documenten openen in de Windows-app, waar geen webserver is: het origineel in het standaardprogramma, en de
/// factuurviewer in een eigen venster (bijv. op een tweede beeldscherm).
/// </summary>
public interface IDocumentvenster
{
    Task OpenOrigineel(Uitvoerder wie, Guid administratie, Guid document);

    /// <param name="gesloten">Volgt als de gebruiker het venster sluit.</param>
    void OpenViewer(Guid administratie, Guid document, string titel, Action gesloten);

    /// <summary>Staat het viewervenster open? Dan toont een volgende factuur zich daar.</summary>
    bool ViewerOpen { get; }
}
