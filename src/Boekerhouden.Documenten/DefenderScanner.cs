using System.Diagnostics;

namespace Boekerhouden.Documenten;

/// <summary>
/// Microsoft Defender, de virusscanner van Windows, voor de Windows-app op één pc (geen ClamAV nodig). Het bestand
/// gaat naar een eigen tijdelijke map en wordt gescand met <c>MpCmdRun.exe -Scan -ScanType 3 -File</c>. Uitslag
/// 0 = schoon, 2 = besmet; verdwijnt het bestand tijdens het scannen (Defender verwijdert het meteen), dan is het ook
/// besmet. Elke andere uitslag of een time-out: niet gescand, dus het bestand wordt niet ingelezen (<i>fail closed</i>).
/// </summary>
public sealed class DefenderScanner(string? programma = null, TimeSpan? tijdslimiet = null) : IVirusscanner
{
    private readonly TimeSpan limiet = tijdslimiet ?? TimeSpan.FromSeconds(120);

    /// <summary>De gebruikelijke plek van de opdrachtregel van Defender.</summary>
    public static string StandaardProgramma =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");

    public async Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var exe = programma ?? StandaardProgramma;
        if (!File.Exists(exe))
        {
            return new(Scanuitslag.NietGescand, "Microsoft Defender is niet gevonden.");
        }

        var map = Path.Combine(Path.GetTempPath(), "Boekerhouden-scan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(map);
        var bestand = Path.Combine(map, "document.bin");
        try
        {
            await using (var uit = new FileStream(bestand, FileMode.CreateNew, FileAccess.Write))
            {
                await inhoud.CopyToAsync(uit, annulering).ConfigureAwait(false);
            }

            using var proces = Process.Start(new ProcessStartInfo(exe)
            {
                ArgumentList = { "-Scan", "-ScanType", "3", "-File", bestand, "-DisableRemediation" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proces is null)
            {
                return new(Scanuitslag.NietGescand, "Microsoft Defender kon niet worden gestart.");
            }

            using var tijd = CancellationTokenSource.CreateLinkedTokenSource(annulering);
            tijd.CancelAfter(limiet);
            var uitvoer = proces.StandardOutput.ReadToEndAsync(tijd.Token);
            try
            {
                await proces.WaitForExitAsync(tijd.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                proces.Kill(entireProcessTree: true);
                return new(Scanuitslag.NietGescand, "Microsoft Defender reageerde niet op tijd.");
            }

            var tekst = await uitvoer.ConfigureAwait(false);
            return proces.ExitCode switch
            {
                0 when File.Exists(bestand) => new(Scanuitslag.Schoon),
                0 or 2 => new(Scanuitslag.Besmet, Gevonden(tekst) ?? "gevonden door Microsoft Defender"),
                _ when !File.Exists(bestand) => new(Scanuitslag.Besmet, "verwijderd door Microsoft Defender"),
                _ => new(Scanuitslag.NietGescand, $"Microsoft Defender gaf foutcode {proces.ExitCode}."),
            };
        }
        catch (Exception fout) when (fout is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Defender verwijdert een besmet bestand soms al tijdens het wegschrijven.
            return File.Exists(bestand) || fout is System.ComponentModel.Win32Exception
                ? new(Scanuitslag.NietGescand, $"Scannen mislukt: {fout.Message}")
                : new(Scanuitslag.Besmet, "verwijderd door Microsoft Defender");
        }
        finally
        {
            try
            {
                Directory.Delete(map, recursive: true);
            }
            catch (IOException)
            {
                // Opruimen mag mislukken; de map staat in de tijdelijke map van de gebruiker.
            }
        }
    }

    /// <summary>De naam van de bedreiging uit de uitvoer ("Threat ... : Virus:DOS/EICAR_Test_File").</summary>
    private static string? Gevonden(string uitvoer) =>
        uitvoer.Split('\n').Select(r => r.Trim()).FirstOrDefault(r => r.StartsWith("Threat", StringComparison.OrdinalIgnoreCase) && r.Contains(':', StringComparison.Ordinal)) is { } regel
            ? regel[(regel.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim()
            : null;
}
