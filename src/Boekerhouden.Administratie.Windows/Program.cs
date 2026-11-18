using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De Windows-app van het administratiepakket: een eigen venster met dezelfde schermen als de webapp. Drie werkwijzen
/// (<see cref="Werkwijze"/>): alleen op deze pc (zonder webserver, één SQLite-bestand, zonder inloggen), als
/// kantoorserver (deze pc deelt zijn gegevens met de andere pc's op kantoor) of als werkplek van een kantoorserver.
/// Met <c>--proef &lt;map&gt;</c> start de app in die map, vult een administratie, loopt alle pagina's en knoppen langs en sluit weer
/// (voor de CI); met <c>--kantoorserver</c> erbij doet hij dat als kantoorserver, door het venster van een werkplek.
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
                "Sifer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        // Alle vensters (de app, de losse factuurviewer) delen hetzelfde profiel, zodat ze met elkaar kunnen praten.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(map, "venster"));

        var werkwijze = proef is not null
            ? new Werkwijze(args.Contains("--kantoorserver") ? Werkwijzestand.Kantoorserver : Werkwijzestand.Alleen)
            : Werkwijze.Lees(map);
        if (werkwijze is null)
        {
            if (File.Exists(Path.Combine(map, LokaleApp.Bestandsnaam)))
            {
                // Er zijn al gegevens op deze pc: verder zoals het was.
                werkwijze = new Werkwijze();
            }
            else
            {
                using var keuze = new Startkeuze();
                if (keuze.ShowDialog() != DialogResult.OK || keuze.Gekozen is null)
                {
                    return 0;
                }

                werkwijze = keuze.Gekozen;
                werkwijze.Bewaar(map);
            }
        }

        return werkwijze.Stand switch
        {
            Werkwijzestand.Kantoorserver => AlsKantoorserver(map, werkwijze, proef),
            Werkwijzestand.Werkplek when Uri.TryCreate(werkwijze.Server, UriKind.Absolute, out var server) => AlsWerkplek(map, werkwijze, server),
            _ => Alleen(map, proef),
        };
    }

    /// <summary>Alleen op deze pc: de schermen rechtstreeks op het eigen gegevensbestand.</summary>
    private static int Alleen(string map, string? proef)
    {
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        LokaleApp.ConfigureerDiensten(services, map);
        services.AddSingleton<IDocumentvenster, Documentvenster>();
        services.AddSingleton<IHerstart, Herstarter>();
        using var diensten = services.BuildServiceProvider();
        try
        {
            var meldingen = LokaleApp.Start(diensten, Environment.UserName).GetAwaiter().GetResult();
            Meld(meldingen, proef);
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            Foutlog.Toon(fout, "De gegevens konden niet worden geopend.");
            return 1;
        }

        // De proef vult vooraf een administratie met twee jaar gegevens, zodat de schermen iets te tonen hebben.
        var proefgegevens = proef is null ? null : Proef.Voorbereiden(diensten).GetAwaiter().GetResult();
        using var venster = new HoofdVenster(diensten, proef, proefgegevens);
        Application.Run(venster);
        return venster.Uitslag;
    }

    /// <summary>
    /// Deze pc deelt zijn gegevens: eerst de gewone start (update, reservekopie, de gebruiker van deze pc) en de eigen
    /// werkplek, dan de server in dit proces, en het venster als werkplek van die server.
    /// </summary>
    private static int AlsKantoorserver(string map, Werkwijze werkwijze, string? proef)
    {
        var sleutel = Werkplekopslag.Sleutel(map);
        Proefuitkomst? proefgegevens = null;
        Microsoft.AspNetCore.Builder.WebApplication server;
        try
        {
            if (proef is not null)
            {
                proefgegevens = Task.Run(() => Proefvulling(map)).GetAwaiter().GetResult();
            }

            // Buiten de UI-thread, zodat wachten hier het venster niet kan blokkeren.
            Meld(Task.Run(() => Kantoorstart.Bereid(map, Environment.UserName, Environment.MachineName, sleutel)).GetAwaiter().GetResult(), proef);
            server = Task.Run(() => Kantoorserver.Start(map, werkwijze.Poort, s => s.AddSingleton<IHerstart, Herstarter>())).GetAwaiter().GetResult();
        }
        catch (IOException fout)
        {
            sleutel.Dispose();
            Foutlog.Toon(fout, $"De kantoorserver kon niet starten op poort {werkwijze.Poort}. Draait Sifer al, of gebruikt een ander programma die poort?");
            return 1;
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            sleutel.Dispose();
            Foutlog.Toon(fout, "De gegevens konden niet worden geopend.");
            return 1;
        }

        try
        {
            using var venster = new Kantoorvenster(new Uri($"https://localhost:{werkwijze.Poort}/"), sleutel, Kantoorserver.Certificaatvingerafdruk, map, werkwijze, server: true, proef, proefgegevens);
            Application.Run(venster);
            return venster.Uitslag;
        }
        finally
        {
            Task.Run(() => server.StopAsync()).Wait(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>De gegevens staan op de kantoor-pc: alleen het venster, verbonden met die server.</summary>
    private static int AlsWerkplek(string map, Werkwijze werkwijze, Uri server)
    {
        using var venster = new Kantoorvenster(server, Werkplekopslag.Sleutel(map), werkwijze.Certificaat, map, werkwijze, server: false);
        Application.Run(venster);
        return venster.Uitslag;
    }

    /// <summary>De proefgegevens voor de proef als kantoorserver: gevuld op het eigen bestand, vóór de server start.</summary>
    private static async Task<Proefuitkomst> Proefvulling(string map)
    {
        var services = new ServiceCollection();
        LokaleApp.ConfigureerDiensten(services, map);
        Proefuitkomst gegevens;
        await using (var diensten = services.BuildServiceProvider())
        {
            await LokaleApp.Start(diensten, Environment.UserName).ConfigureAwait(false);
            gegevens = await Proef.Voorbereiden(diensten).ConfigureAwait(false);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return gegevens;
    }

    private static void Meld(IReadOnlyList<string> meldingen, string? proef)
    {
        if (meldingen.Count > 0 && proef is null)
        {
            MessageBox.Show(string.Join(Environment.NewLine + Environment.NewLine, meldingen), "Sifer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
