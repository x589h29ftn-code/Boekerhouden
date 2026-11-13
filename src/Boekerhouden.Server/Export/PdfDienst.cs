using System.Diagnostics;
using System.Text;
using Boekerhouden.Server.Diensten;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Server.Export;

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

    /// <summary>Hoogstens zoveel browsers tegelijk; een drukke machine met veel tegelijk laat de browser soms hangen.</summary>
    private static readonly SemaphoreSlim Tegelijk = new(Math.Clamp(Environment.ProcessorCount, 2, 8));

    public async Task<Uitkomst<byte[]>> Maak(string html, CancellationToken stoppen = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (Browser is not { } browser || !File.Exists(browser))
        {
            return Uitkomst<byte[]>.Mislukt("Er is geen browser gevonden om een PDF te maken. Installeer Microsoft Edge of Chromium, of stel Pdf:Browser in.");
        }

        await Tegelijk.WaitAsync(stoppen);
        try
        {
            // Een browser die blijft hangen zonder PDF (dat gebeurt af en toe op een drukke machine) krijgt één
            // nieuwe kans, met een vers profiel.
            var uitkomst = await Probeer(browser, html, stoppen);
            if (uitkomst is null)
            {
                PdfOpnieuw(log, browser);
                uitkomst = await Probeer(browser, html, stoppen);
            }

            return uitkomst ?? Uitkomst<byte[]>.Mislukt("Het maken van de PDF duurde te lang.");
        }
        finally
        {
            Tegelijk.Release();
        }
    }

    /// <summary>Eén poging; leeg als de browser binnen de tijdslimiet geen PDF maakte.</summary>
    private async Task<Uitkomst<byte[]>?> Probeer(string browser, string html, CancellationToken stoppen)
    {
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
                // Klaar als de browser stopt, of zodra de PDF compleet op schijf staat: op sommige (Linux-)machines
                // schrijft de browser de PDF maar sluit hij daarna niet af; dan sluiten we hem zelf.
                var gestopt = proces.WaitForExitAsync(tijdslimiet.Token);
                long vorige = -1;
                while (!gestopt.IsCompleted)
                {
                    await Task.WhenAny(gestopt, Task.Delay(200, tijdslimiet.Token));
                    if (gestopt.IsCompleted)
                    {
                        break;
                    }

                    tijdslimiet.Token.ThrowIfCancellationRequested();
                    var lengte = Compleet(uitvoer);
                    if (lengte > 0 && lengte == vorige)
                    {
                        proces.Kill(entireProcessTree: true);
                        break;
                    }

                    vorige = lengte;
                }

                await gestopt.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                tijdslimiet.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!stoppen.IsCancellationRequested)
            {
                if (Compleet(uitvoer) <= 0)
                {
                    proces.Kill(entireProcessTree: true);
                    return null;
                }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Browser} maakte binnen de tijdslimiet geen PDF; nog een keer")]
    private static partial void PdfOpnieuw(ILogger log, string browser);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PDF niet gemaakt door {Browser} (afsluitcode {Code}): {Fouten}")]
    private static partial void PdfMislukt(ILogger log, string browser, int code, string fouten);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tijdelijke map {Map} niet opgeruimd")]
    private static partial void NietOpgeruimd(ILogger log, Exception fout, string map);

    /// <summary>De lengte van de PDF als die compleet is (eindigt op %%EOF), anders 0.</summary>
    private static long Compleet(string pad)
    {
        try
        {
            var info = new FileInfo(pad);
            if (!info.Exists || info.Length < 16)
            {
                return 0;
            }

            using var bestand = new FileStream(pad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var staart = new byte[Math.Min(64, bestand.Length)];
            bestand.Seek(-staart.Length, SeekOrigin.End);
            bestand.ReadExactly(staart);
            return Encoding.ASCII.GetString(staart).Contains("%%EOF", StringComparison.Ordinal) ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    internal static IEnumerable<string> Argumenten(string profiel, string uitvoer, string invoer) =>
    [
        "--headless",
        "--disable-gpu",
        "--disable-dev-shm-usage",
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
