using System.Diagnostics;

namespace Boekerhouden.Administratie.Bureaublad;

/// <summary>Een fout bij het starten van de Windows-app, met een melding voor de gebruiker.</summary>
public sealed class BureaubladException : Exception
{
    public BureaubladException()
    {
    }

    public BureaubladException(string melding)
        : base(melding)
    {
    }

    public BureaubladException(string melding, Exception binnen)
        : base(melding, binnen)
    {
    }
}

/// <summary>Een programma uitvoeren en wachten op de uitslag (met een tijdslimiet).</summary>
internal static class Opdracht
{
    public sealed record Uitslag(int Code, string Uitvoer);

    /// <param name="uitvoerOpvangen">
    /// Uit voor <c>pg_ctl start</c>: de database erft de uitvoer en houdt die open, dus wachten op het einde van de
    /// uitvoer zou blijven hangen.
    /// </param>
    public static async Task<Uitslag> Voer(string programma, IEnumerable<string> argumenten, TimeSpan limiet, bool uitvoerOpvangen = true)
    {
        var info = new ProcessStartInfo(programma)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = uitvoerOpvangen,
            RedirectStandardError = uitvoerOpvangen,
        };
        foreach (var argument in argumenten)
        {
            info.ArgumentList.Add(argument);
        }

        using var proces = Process.Start(info) ?? throw new BureaubladException($"{Path.GetFileName(programma)} kon niet worden gestart.");
        using var tijd = new CancellationTokenSource(limiet);
        var uit = uitvoerOpvangen ? proces.StandardOutput.ReadToEndAsync(tijd.Token) : Task.FromResult(string.Empty);
        var fout = uitvoerOpvangen ? proces.StandardError.ReadToEndAsync(tijd.Token) : Task.FromResult(string.Empty);
        try
        {
            await proces.WaitForExitAsync(tijd.Token);
            return new Uitslag(proces.ExitCode, (await uit + await fout).Trim());
        }
        catch (OperationCanceledException)
        {
            proces.Kill(entireProcessTree: true);
            throw new BureaubladException($"{Path.GetFileName(programma)} reageerde niet binnen {limiet.TotalSeconds:0} seconden.");
        }
    }
}
