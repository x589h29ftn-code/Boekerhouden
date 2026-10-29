using System.Text.Json;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De proef voor de CI (<c>--proef map</c>): een administratie met een boeking, dan in het venster het eerste scherm
/// (de administraties) en de proefbalans. De tekst en schermafdrukken komen in de map; afsluitcode 1 als iets niet
/// verscheen.
/// </summary>
internal static class Proef
{
    public static async Task<Guid> Voorbereiden(IServiceProvider diensten)
    {
        var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
        var administratie = await diensten.GetRequiredService<AdministratieDienst>()
            .Maak(new AdministratieInvoer("Proef B.V.", Boekhouding.Grootboek.Ondernemingsvorm.BV, null, null), wie);
        await diensten.GetRequiredService<GrootboekDienst>().Boek(wie, administratie.Waarde,
            new("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new("1100", 1234.56m), new("0500", -1234.56m)]));
        return administratie.Waarde;
    }

    public static async Task Voer(HoofdVenster venster, WebView2 webview, string map, Guid? administratie)
    {
        var start = await Wacht(webview, "Proef B.V.");
        await Afdruk(webview, Path.Combine(map, "proef-start.png"));
        var balans = string.Empty;
        if (start.Contains("Proef B.V.", StringComparison.Ordinal))
        {
            await webview.CoreWebView2.ExecuteScriptAsync($"Blazor.navigateTo('/a/{administratie}/proefbalans')");
            balans = await Wacht(webview, "1.234,56");
            await Afdruk(webview, Path.Combine(map, "proef-proefbalans.png"));
        }

        await File.WriteAllTextAsync(Path.Combine(map, "proef.txt"), $"{start}{Environment.NewLine}---{Environment.NewLine}{balans}");
        venster.Uitslag = start.Contains("Nieuwe administratie", StringComparison.Ordinal) && balans.Contains("1.234,56", StringComparison.Ordinal) ? 0 : 1;
        venster.Close();
    }

    private static async Task<string> Wacht(WebView2 webview, string verwacht)
    {
        var tekst = string.Empty;
        for (var i = 0; i < 60 && !tekst.Contains(verwacht, StringComparison.Ordinal); i++)
        {
            await Task.Delay(1000);
            tekst = JsonSerializer.Deserialize<string>(await webview.CoreWebView2.ExecuteScriptAsync("document.body.innerText")) ?? string.Empty;
        }

        return tekst;
    }

    private static async Task Afdruk(WebView2 webview, string pad)
    {
        await using var afdruk = File.Create(pad);
        await webview.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, afdruk);
    }
}
