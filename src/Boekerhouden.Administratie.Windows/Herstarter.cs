using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Windows;

/// <summary>Start de app opnieuw (op de UI-thread): het venster sluit, de app start met dezelfde map.</summary>
internal sealed class Herstarter : IHerstart
{
    public void Herstart()
    {
        if (Application.OpenForms.Count > 0 && Application.OpenForms[0] is { } venster)
        {
            venster.BeginInvoke(Application.Restart);
        }
        else
        {
            Application.Restart();
        }
    }
}
