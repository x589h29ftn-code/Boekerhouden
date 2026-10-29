using System.Text;

namespace Boekerhouden.Documenten.Tests;

/// <summary>
/// De Defender-koppeling met een nagemaakte <c>MpCmdRun</c> (een shellscript), zodat de uitslagen ook buiten Windows
/// te testen zijn. Het script krijgt dezelfde argumenten: <c>-Scan -ScanType 3 -File pad -DisableRemediation</c>.
/// </summary>
public sealed class DefenderScannerTests : IDisposable
{
    private readonly string map = Path.Combine(Path.GetTempPath(), "defender-test-" + Guid.NewGuid().ToString("N"));

    public DefenderScannerTests() => Directory.CreateDirectory(map);

    public void Dispose() => Directory.Delete(map, recursive: true);

    [Fact]
    public async Task Uitslagen_van_defender()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // De nagemaakte scanner is een shellscript.
        }

        Assert.Equal(Scanuitslag.Schoon, (await Scan("exit 0")).Uitslag);
        Assert.Equal(new Scanresultaat(Scanuitslag.Besmet, "Virus:DOS/EICAR_Test_File"),
            await Scan("echo 'Threat                  : Virus:DOS/EICAR_Test_File'; exit 2"));
        Assert.Equal(new Scanresultaat(Scanuitslag.Besmet, "verwijderd door Microsoft Defender"), await Scan("rm \"$5\"; exit 1"));
        Assert.Equal(new Scanresultaat(Scanuitslag.NietGescand, "Microsoft Defender gaf foutcode 5."), await Scan("exit 5"));
        Assert.Equal(Scanuitslag.NietGescand, (await Scan("sleep 5", TimeSpan.FromMilliseconds(300))).Uitslag);
    }

    [Fact]
    public async Task Zonder_defender_wordt_niets_ingelezen()
    {
        var resultaat = await new DefenderScanner(Path.Combine(map, "bestaat-niet.exe")).ScanAsync(new MemoryStream([1, 2, 3]));

        Assert.Equal(new Scanresultaat(Scanuitslag.NietGescand, "Microsoft Defender is niet gevonden."), resultaat);
    }

    private async Task<Scanresultaat> Scan(string script, TimeSpan? limiet = null)
    {
        var pad = Path.Combine(map, Guid.NewGuid().ToString("N") + ".sh");
        await File.WriteAllTextAsync(pad, "#!/bin/sh\n" + script + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(pad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return await new DefenderScanner(pad, limiet).ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 factuur")));
    }
}
