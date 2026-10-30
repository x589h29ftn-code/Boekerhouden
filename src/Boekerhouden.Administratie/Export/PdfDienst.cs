using System.Diagnostics;
using System.Text;
using Boekerhouden.Administratie.Diensten;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Administratie.Export;

/// <summary>Instellingen voor het maken van PDF's (sectie "Pdf" in appsettings.json).</summary>
public sealed class PdfOpties
{
    /// <summary>Pad naar Edge, Chrome of Chromium. Leeg: zelf zoeken op de gebruikelijke plekken.</summary>
    public string? Browser { get; set; }

    /// <summary>Zonder sandbox starten; alleen nodig waar de sandbox niet werkt (bijv. sommige Linux-buildservers).</summary>
    public bool ZonderSandbox { get; set; }

    public int TijdslimietSeconden { get; set; } = 60;
}

/// <summary>
/// Maakt van HTML een PDF met een browser zonder venster (Edge op de kantoorserver, Chrome of Chromium elders), zoals
/// gekozen in docs §7.6. De HTML wordt als bestand aangeboden en mag niets van buiten laden.
/// </summary>
public sealed partial class PdfDienst(IOptions<PdfOpties> opties, ILogger<PdfDienst> log)
{
    public string? Browser => opties.Value.Browser is { Length: > 0 } pad ? pad : ZoekBrowser();

    public async Task<Uitkomst<byte[]>> Maak(string html, CancellationToken stoppen = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (Browser is not { } browser || !File.Exists(browser))
        {
            return Uitkomst<byte[]>.Mislukt("Er is geen browser gevonden om een PDF te maken. Installeer Microsoft Edge of Chromium, of stel Pdf:Browser in.");
        }

        var map = Directory.CreateTempSubdirectory("boekerhouden-pdf-");
        try
        {
            var invoer = Path.Combine(map.FullName, "document.html");
            var uitvoer = Path.Combine(map.FullName, "document.pdf");
            await File.WriteAllTextAsync(invoer, html, Encoding.UTF8, stoppen);

            var start = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in Argumenten(Path.Combine(map.FullName, "profiel"), uitvoer, invoer))
            {
                start.ArgumentList.Add(argument);
            }

            if (opties.Value.ZonderSandbox || (OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess))
            {
                // Chromium weigert als root te starten met sandbox.
                start.ArgumentList.Add("--no-sandbox");
            }

            using var proces = Process.Start(start) ?? throw new InvalidOperationException($"Kon {browser} niet starten.");
            var fouten = proces.StandardError.ReadToEndAsync(stoppen);
            _ = proces.StandardOutput.ReadToEndAsync(stoppen);
            using var tijdslimiet = CancellationTokenSource.CreateLinkedTokenSource(stoppen);
            tijdslimiet.CancelAfter(TimeSpan.FromSeconds(opties.Value.TijdslimietSeconden));
            try
            {
                await proces.WaitForExitAsync(tijdslimiet.Token);
            }
            catch (OperationCanceledException) when (!stoppen.IsCancellationRequested)
            {
                proces.Kill(entireProcessTree: true);
                return Uitkomst<byte[]>.Mislukt("Het maken van de PDF duurde te lang.");
            }

            if (!File.Exists(uitvoer))
            {
                PdfMislukt(log, browser, proces.ExitCode, await fouten);
                return Uitkomst<byte[]>.Mislukt("De PDF kon niet worden gemaakt.");
            }

            return Uitkomst<byte[]>.Met(await File.ReadAllBytesAsync(uitvoer, stoppen));
        }
        finally
        {
            try
            {
                map.Delete(recursive: true);
            }
            catch (IOException e)
            {
                NietOpgeruimd(log, e, map.FullName);
            }
            catch (UnauthorizedAccessException e)
            {
                NietOpgeruimd(log, e, map.FullName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PDF niet gemaakt door {Browser} (afsluitcode {Code}): {Fouten}")]
    private static partial void PdfMislukt(ILogger log, string browser, int code, string fouten);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tijdelijke map {Map} niet opgeruimd")]
    private static partial void NietOpgeruimd(ILogger log, Exception fout, string map);

    internal static IEnumerable<string> Argumenten(string profiel, string uitvoer, string invoer) =>
    [
        "--headless",
        "--disable-gpu",
        "--no-pdf-header-footer",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-extensions",
        "--disable-sync",
        "--disable-background-networking",
        "--disable-component-update",
        $"--user-data-dir={profiel}",
        $"--print-to-pdf={uitvoer}",
        new Uri(invoer).AbsoluteUri,
    ];

    /// <summary>Edge, Chrome of Chromium op de gebruikelijke plekken (Windows en Linux).</summary>
    internal static string? ZoekBrowser()
    {
        var kandidaten = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var map in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                var basis = Environment.GetFolderPath(map);
                kandidaten.Add(Path.Combine(basis, "Microsoft", "Edge", "Application", "msedge.exe"));
                kandidaten.Add(Path.Combine(basis, "Google", "Chrome", "Application", "chrome.exe"));
            }
        }
        else
        {
            kandidaten.AddRange(["/usr/bin/microsoft-edge", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"]);
            if (Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } playwright && Directory.Exists(playwright))
            {
                kandidaten.AddRange(Directory.EnumerateDirectories(playwright, "chromium-*")
                    .OrderDescending(StringComparer.Ordinal)
                    .SelectMany(m => new[] { Path.Combine(m, "chrome-linux", "chrome"), Path.Combine(m, "chrome-linux64", "chrome") }));
            }
        }

        return kandidaten.FirstOrDefault(File.Exists);
    }
}
