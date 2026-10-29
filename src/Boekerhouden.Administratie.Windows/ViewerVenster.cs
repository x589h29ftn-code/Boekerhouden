using Boekerhouden.Administratie.Components.Gedeeld;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>De factuur in een eigen venster, bijv. op een tweede beeldscherm; een aangewezen gebied gaat naar het verwerkscherm.</summary>
internal sealed class ViewerVenster : Form
{
    public ViewerVenster(IServiceProvider diensten, Guid administratie, Guid document, string titel)
    {
        Text = $"{titel} – Boekerhouden Administratie";
        Size = new Size(900, 1100);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        var webview = new BlazorWebView { Dock = DockStyle.Fill, HostPage = Path.Combine("wwwroot", "index.html"), Services = diensten };
        webview.RootComponents.Add<LosseViewer>("#app", new Dictionary<string, object?>
        {
            [nameof(LosseViewer.Administratie)] = administratie,
            [nameof(LosseViewer.Document)] = document,
        });
        Controls.Add(webview);
    }

    /// <summary>Gesloten omdat het volgende document in een nieuw venster op dezelfde plek staat (niet door de gebruiker).</summary>
    public bool Vervangen { get; private set; }

    public void Sluit()
    {
        Vervangen = true;
        Close();
    }
}
