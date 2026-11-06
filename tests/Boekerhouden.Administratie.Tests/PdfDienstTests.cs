using Boekerhouden.Administratie.Export;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De PDF-dienst met een nagebootste browser (een script): op sommige Linux-machines schrijft de browser de PDF maar
/// sluit hij daarna niet af. De dienst moet dan klaar zijn zodra de PDF compleet is, niet pas na de tijdslimiet.
/// </summary>
public sealed class PdfDienstTests
{
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string Browser(string map, string gedrag)
    {
        var pad = Path.Combine(map, "browser.sh");
        File.WriteAllText(pad, $$"""
            #!/bin/sh
            for a in "$@"; do case "$a" in --print-to-pdf=*) uit="${a#--print-to-pdf=}";; esac; done
            {{gedrag}}
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.SetUnixFileMode(pad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return pad;
    }

    private static PdfDienst Dienst(string browser, int seconden) =>
        new(Options.Create(new PdfOpties { Browser = browser, TijdslimietSeconden = seconden }), NullLogger<PdfDienst>.Instance);

    [Fact]
    public async Task Een_browser_die_na_de_pdf_blijft_hangen_wordt_gestopt_en_de_pdf_gebruikt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var map = Directory.CreateTempSubdirectory("pdftest-").FullName;
        try
        {
            var browser = Browser(map, """printf '%%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%%%EOF\n' > "$uit"; sleep 120""");
            var klok = System.Diagnostics.Stopwatch.StartNew();

            var uitkomst = await Dienst(browser, 30).Maak("<p>proef</p>");

            Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(uitkomst.Waarde!, 0, 4), StringComparison.Ordinal);
            Assert.True(klok.Elapsed < TimeSpan.FromSeconds(10), $"duurde {klok.Elapsed}");
        }
        finally
        {
            Directory.Delete(map, recursive: true);
        }
    }

    [Fact]
    public async Task Een_browser_die_niets_schrijft_geeft_na_de_tijdslimiet_een_melding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var map = Directory.CreateTempSubdirectory("pdftest-").FullName;
        try
        {
            var uitkomst = await Dienst(Browser(map, "sleep 120"), 2).Maak("<p>proef</p>");

            Assert.Equal(["Het maken van de PDF duurde te lang."], uitkomst.Fouten);
        }
        finally
        {
            Directory.Delete(map, recursive: true);
        }
    }
}
