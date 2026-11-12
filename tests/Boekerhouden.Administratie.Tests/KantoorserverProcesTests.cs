using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Data.Sqlite;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De kantoorserver zoals de Windows-app hem start: eerst de voorbereiding in het eigen proces (gegevens bijwerken,
/// de gebruiker van de pc, de eigen werkplek), dan de webapp als los proces op het SQLite-bestand, met een versleutelde
/// verbinding. De pc zelf komt meteen binnen; een andere pc wacht op de beheerder; een ander certificaat wordt geweigerd.
/// </summary>
public sealed class KantoorserverProcesTests : IDisposable
{
    private readonly string map = Path.Combine(Path.GetTempPath(), "kantoor-" + Guid.NewGuid().ToString("N"));
    private Process? server;

    public void Dispose()
    {
        try
        {
            if (server is { HasExited: false })
            {
                server.Kill(entireProcessTree: true);
                server.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Al gestopt.
        }

        server?.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(map))
        {
            Directory.Delete(map, recursive: true);
        }
    }

    [Fact]
    public async Task De_kantoor_pc_komt_meteen_binnen_en_een_andere_pc_wacht_op_de_beheerder()
    {
        using var eigen = Werkpleksleutel.Nieuw();
        Assert.Empty(await Kantoorstart.Bereid(map, "Karin Kantoor", "KANTOOR-PC", eigen));
        var poort = VrijePoort();
        var adres = Werkwijze.Adres($"localhost:{poort}")!;
        Start(poort);

        // Eerste verbinding: het certificaat wordt vastgelegd; de eigen werkplek is al gekoppeld.
        string certificaat;
        using (var eerste = new Werkplekverbinding(adres, eigen, null))
        {
            var antwoord = await WachtOpServer(eerste, "Karin Kantoor", "KANTOOR-PC");
            Assert.Equal(Werkplekstand.Gekoppeld, antwoord.Stand);
            certificaat = eerste.GezienCertificaat!;
            Assert.Equal(antwoord.Code, eerste.Code);

            // Inloggen met de ondertekende uitdaging, zoals het venster doet.
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == certificaat,
            };
            using var browser = new HttpClient(handler);
            var start = await browser.GetAsync(eerste.Binnen(antwoord.Uitdaging!));
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            Assert.Equal("/", start.RequestMessage!.RequestUri!.AbsolutePath);
            Assert.Contains("Karin Kantoor", await start.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri(adres, "/beheer/gebruikers"))).StatusCode);
        }

        // Een andere pc: wacht op de beheerder, met dezelfde code als de server toont.
        using var ander = Werkpleksleutel.Nieuw();
        using (var werkplek = new Werkplekverbinding(adres, ander, certificaat))
        {
            var antwoord = await werkplek.Meld("piet", "PC-02");
            Assert.Equal((Werkplekstand.Wacht, werkplek.Code), (antwoord.Stand, antwoord.Code));
        }

        // Een server met een ander certificaat dan vastgelegd (iemand ertussen): geen verbinding.
        using var verkeerd = new Werkplekverbinding(adres, ander, new string('A', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => verkeerd.Meld("piet", "PC-02"));
    }

    private void Start(int poort)
    {
        // De webapp uit de build van de oplossing, naast deze tests (zelfde configuratie).
        var hier = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuratie = hier.Parent!.Name;
        var wortel = hier;
        while (wortel is not null && !File.Exists(Path.Combine(wortel.FullName, "Boekerhouden.slnx")))
        {
            wortel = wortel.Parent;
        }

        var dll = Path.Combine(wortel!.FullName, "src", "Boekerhouden.Administratie", "bin", configuratie, hier.Name, "Boekerhouden.Administratie.dll");
        Assert.True(File.Exists(dll), $"De webapp is niet gebouwd: {dll}");
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(dll);
        foreach (var argument in Kantoorstart.Argumenten(map, poort))
        {
            info.ArgumentList.Add(argument);
        }

        // De statische bestanden komen in een build uit het manifest van de ontwikkelomgeving.
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment.Remove("ASPNETCORE_URLS");
        server = Process.Start(info)!;
        server.OutputDataReceived += (_, _) => { };
        server.ErrorDataReceived += (_, _) => { };
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
    }

    private async Task<Werkplekantwoord> WachtOpServer(Werkplekverbinding verbinding, string naam, string computer)
    {
        for (var i = 0; ; i++)
        {
            Assert.False(server!.HasExited, "De server is gestopt.");
            try
            {
                return await verbinding.Meld(naam, computer);
            }
            catch (HttpRequestException) when (i < 120)
            {
                await Task.Delay(500);
            }
        }
    }

    private static int VrijePoort()
    {
        using var luisteraar = new TcpListener(IPAddress.Loopback, 0);
        luisteraar.Start();
        return ((IPEndPoint)luisteraar.LocalEndpoint).Port;
    }
}
