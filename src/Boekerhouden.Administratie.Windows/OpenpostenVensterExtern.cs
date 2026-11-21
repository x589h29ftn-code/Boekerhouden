using Boekerhouden.Administratie.Components.Gedeeld;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>De openstaande posten in een eigen venster, bijv. op een tweede beeldscherm naast het bankboek.</summary>
internal sealed class OpenpostenVensterExtern : Form
{
    public OpenpostenVensterExtern(IServiceProvider diensten, Guid administratie, string titel)
    {
        Text = $"{titel} – Sifer";
        Icon = Merk.Icoon ?? Icon;
        Size = new Size(1100, 900);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        var webview = new BlazorWebView { Dock = DockStyle.Fill, HostPage = Path.Combine("wwwroot", "index.html"), Services = diensten };
        webview.RootComponents.Add<OpenpostenExtern>("#app", new Dictionary<string, object?> { [nameof(OpenpostenExtern.Administratie)] = administratie });
        webview.BlazorWebViewInitialized += (_, e) => Vensterstijl.Pas(e.WebView.CoreWebView2);
        Controls.Add(webview);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Vensterstijl.Titelbalk(Handle);
    }
}
