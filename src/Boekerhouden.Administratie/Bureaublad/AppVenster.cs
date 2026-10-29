using System.Diagnostics;

namespace Boekerhouden.Administratie.Bureaublad;

/// <summary>
/// Opent de app in Edge als los venster (<c>--app</c>, zonder adresbalk en tabbladen), met een eigen profiel zodat
/// het een eigen proces is: sluit de gebruiker het venster, dan stopt de app. Zonder Edge (of een ingestelde browser)
/// opent het adres in de standaardbrowser.
/// </summary>
public static class AppVenster
{
    public static IEnumerable<string> Kandidaten()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var map in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
            {
                yield return Path.Combine(Environment.GetFolderPath(map), "Microsoft", "Edge", "Application", "msedge.exe");
            }
        }
        else
        {
            yield return "/usr/bin/microsoft-edge";
            yield return "/usr/bin/chromium";
            yield return "/usr/bin/google-chrome";
        }
    }

    /// <summary>Start het venster; <paramref name="gesloten"/> volgt als de gebruiker het sluit.</summary>
    /// <returns>Of er een eigen venster is (anders is de standaardbrowser geopend en blijft de app draaien).</returns>
    public static bool Open(string adres, string profielmap, string? browser, Action gesloten)
    {
        ArgumentNullException.ThrowIfNull(gesloten);
        var programma = browser is { Length: > 0 } ? browser : Kandidaten().FirstOrDefault(File.Exists);
        if (programma is null || !File.Exists(programma))
        {
            Process.Start(new ProcessStartInfo(adres) { UseShellExecute = true });
            return false;
        }

        var info = new ProcessStartInfo(programma) { UseShellExecute = false };
        foreach (var argument in new[] { $"--app={adres}", $"--user-data-dir={profielmap}", "--no-first-run", "--no-default-browser-check", "--window-size=1440,960" })
        {
            info.ArgumentList.Add(argument);
        }

        var proces = Process.Start(info);
        if (proces is null)
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            await proces.WaitForExitAsync();
            proces.Dispose();
            gesloten();
        });
        return true;
    }
}
