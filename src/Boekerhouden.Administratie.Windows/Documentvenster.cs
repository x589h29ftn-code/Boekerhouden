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

    public void OpenViewer(Guid administratie, Guid document, string titel, Action gesloten)
    {
        var venster = new ViewerVenster(diensten, administratie, document, titel);
        venster.FormClosed += (_, _) => gesloten();
        venster.Show();
    }
}
