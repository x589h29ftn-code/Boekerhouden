using Boekerhouden.Administratie.Components.Gedeeld;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>De openstaande posten in een eigen venster, bijv. op een tweede beeldscherm naast het bankboek.</summary>
internal sealed class OpenpostenVensterExtern : Form
{
    public OpenpostenVensterExtern(IServiceProvider diensten, Guid administratie, string titel)
    {
        Text = $"{titel} – Boekerhouden Administratie";
        Size = new Size(1100, 900);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        var webview = new BlazorWebView { Dock = DockStyle.Fill, HostPage = Path.Combine("wwwroot", "index.html"), Services = diensten };
        webview.RootComponents.Add<OpenpostenExtern>("#app", new Dictionary<string, object?> { [nameof(OpenpostenExtern.Administratie)] = administratie });
        Controls.Add(webview);
    }
}
