using System.Diagnostics;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Documenten;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// Documenten openen zonder webserver. Het origineel gaat naar het standaardprogramma, maar alleen als PDF of XML
/// (op de inhoud bepaald, niet op de naam), zodat een geüpload programma nooit wordt gestart.
/// </summary>
internal sealed class Documentvenster(InkoopDienst inkoop, IServiceProvider diensten) : IDocumentvenster
{
    public async Task OpenOrigineel(Uitvoerder wie, Guid administratie, Guid document)
    {
        if (await inkoop.Inhoud(wie, administratie, document) is not { } bestand)
        {
            return;
        }

        if (Bestandssoort.Extensie(bestand.Inhoud) is not { } extensie)
        {
            MessageBox.Show("Dit document is geen PDF of XML en wordt daarom niet geopend.", "Boekerhouden Administratie", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var map = Path.Combine(Path.GetTempPath(), "Boekerhouden", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(map);
        var pad = Path.Combine(map, Documentpad.Veilig(Path.GetFileNameWithoutExtension(bestand.Bestandsnaam)) + extensie);
        await File.WriteAllBytesAsync(pad, bestand.Inhoud);
        Process.Start(new ProcessStartInfo(pad) { UseShellExecute = true })?.Dispose();
    }

    private ViewerVenster? open;

    public bool ViewerOpen => open is { IsDisposed: false };

    /// <summary>Eén viewervenster: bij de volgende factuur komt het nieuwe document op dezelfde plek.</summary>
    public void OpenViewer(Guid administratie, Guid document, string titel, Action gesloten)
    {
        var venster = new ViewerVenster(diensten, administratie, document, titel);
        if (open is { IsDisposed: false } vorige)
        {
            venster.StartPosition = FormStartPosition.Manual;
            venster.Bounds = vorige.Bounds;
            venster.WindowState = vorige.WindowState;
            vorige.Sluit();
        }

        open = venster;
        venster.FormClosed += (_, _) =>
        {
            if (!venster.Vervangen)
            {
                gesloten();
            }
        };
        venster.Show();
    }
}
