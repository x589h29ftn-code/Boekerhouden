namespace Boekerhouden.Administratie.Diensten;

/// <summary>
/// Waar de app draait. <see cref="Lokaal"/> is de Windows-app op één pc: één gebruiker die niet inlogt, zonder
/// webserver; schermen laten dan uitloggen, wachtwoord en tweestapsverificatie weg. <see cref="Kantoorserver"/> is de
/// Windows-app die zijn gegevens deelt met andere pc's op kantoor: wel een webserver en meerdere gebruikers, maar
/// inloggen gaat met de sleutel van een gekoppelde werkplek (<see cref="ApparaatDienst"/>), zonder wachtwoord.
/// </summary>
public sealed record Werkomgeving(bool Lokaal, bool Kantoorserver = false)
{
    /// <summary>Er zijn geen wachtwoorden: de Windows-app alleen, of de kantoorserver met gekoppelde werkplekken.</summary>
    public bool ZonderWachtwoord => Lokaal || Kantoorserver;
}

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

    /// <summary>De openstaande posten in een eigen venster (bijv. op een tweede beeldscherm naast het bankboek).</summary>
    void OpenOpenposten(Guid administratie, string titel);
}

/// <summary>De Windows-app opnieuw starten (bijv. om een reservekopie terug te zetten); in de webapp niet aanwezig.</summary>
public interface IHerstart
{
    void Herstart();
}
