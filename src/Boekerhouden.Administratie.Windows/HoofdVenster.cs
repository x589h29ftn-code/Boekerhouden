using Boekerhouden.Administratie.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>Het venster van de app: de schermen van het administratiepakket in een BlazorWebView.</summary>
internal sealed class HoofdVenster : Proefvenster
{
    private const string Naam = "Sifer";

    public HoofdVenster(IServiceProvider diensten, string? proefmap = null, Lokaal.Proefuitkomst? proefgegevens = null)
    {
        Text = Naam;
        Icon = Merk.Icoon ?? Icon;
        ZetMaat(proefmap is not null);
        var webview = new BlazorWebView { Dock = DockStyle.Fill, HostPage = Path.Combine("wwwroot", "index.html"), Services = diensten };
        webview.RootComponents.Add<Routes>("#app");
        webview.RootComponents.Add<HeadOutlet>("head::after");
        webview.BlazorWebViewInitialized += (_, e) =>
        {
            e.WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                Text = e.WebView.CoreWebView2.DocumentTitle is { Length: > 0 } titel && titel != Naam ? $"{titel} – {Naam}" : Naam;
            if (proefmap is not null)
            {
                _ = Proef.Voer(this, e.WebView, proefmap, proefgegevens);
            }
        };
        Controls.Add(webview);
    }
}
