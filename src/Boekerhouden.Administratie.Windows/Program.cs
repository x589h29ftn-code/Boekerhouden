using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De Windows-app van het administratiepakket voor tests op één pc: een eigen venster met dezelfde schermen als de
/// webapp, zonder webserver, zonder databaseserver (één SQLite-bestand) en zonder inloggen.
/// Met <c>--proef &lt;map&gt;</c> start de app in die map, controleert dat het eerste scherm verschijnt en sluit weer
/// (voor de CI).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var proef = args.Length >= 2 && args[0] == "--proef" ? args[1] : null;
        var map = proef ?? LokaleApp.StandaardMap;
        Directory.CreateDirectory(map);
        Foutlog.Map = map;
        Application.ThreadException += (_, e) => Foutlog.Toon(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Foutlog.Schrijf(e.ExceptionObject as Exception);

        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Voor dit venster is Microsoft Edge WebView2 nodig. Installeer die eenmalig (winget install Microsoft.EdgeWebView2Runtime) en start opnieuw.",
                "Boekerhouden Administratie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        // Beide vensters (de app en de losse factuurviewer) delen hetzelfde profiel, zodat ze met elkaar kunnen praten.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(map, "venster"));

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        LokaleApp.ConfigureerDiensten(services, map);
        services.AddSingleton<IDocumentvenster, Documentvenster>();
        using var diensten = services.BuildServiceProvider();
        try
        {
            LokaleApp.Start(diensten, Environment.UserName).GetAwaiter().GetResult();
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            Foutlog.Toon(fout, "De gegevens konden niet worden geopend.");
            return 1;
        }

        // De proef boekt vooraf iets, zodat ook een scherm met gegevens te controleren is.
        var proefadministratie = proef is null ? (Guid?)null : Proef.Voorbereiden(diensten).GetAwaiter().GetResult();
        using var venster = new HoofdVenster(diensten, proef, proefadministratie);
        Application.Run(venster);
        return venster.Uitslag;
    }
}
