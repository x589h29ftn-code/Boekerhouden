using Boekerhouden.Administratie.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>Het venster van de app: de schermen van het administratiepakket in een BlazorWebView.</summary>
internal sealed class HoofdVenster : Form
{
    private const string Naam = "Boekerhouden Administratie";

    public HoofdVenster(IServiceProvider diensten, string? proefmap = null, Guid? proefadministratie = null)
    {
        Text = Naam;
        Size = new Size(1440, 960);
        StartPosition = FormStartPosition.CenterScreen;
        var webview = new BlazorWebView { Dock = DockStyle.Fill, HostPage = Path.Combine("wwwroot", "index.html"), Services = diensten };
        webview.RootComponents.Add<Routes>("#app");
        webview.RootComponents.Add<HeadOutlet>("head::after");
        webview.BlazorWebViewInitialized += (_, e) =>
        {
            e.WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                Text = e.WebView.CoreWebView2.DocumentTitle is { Length: > 0 } titel && titel != Naam ? $"{titel} – {Naam}" : Naam;
            if (proefmap is not null)
            {
                _ = Proef.Voer(this, e.WebView, proefmap, proefadministratie);
            }
        };
        Controls.Add(webview);
    }

    /// <summary>De afsluitcode: 0, of 1 als de proef mislukte.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Uitslag { get; set; }
}
