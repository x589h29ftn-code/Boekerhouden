using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Boekerhouden.Documenten.Tests;

public sealed class VirusscanTests
{
    /// <summary>Het EICAR-teken: een onschuldige teststring die elke virusscanner als virus meldt.</summary>
    private const string Teken = "EICAR-TEST";

    [Fact]
    public async Task Schoon_bestand_wordt_herkend()
    {
        using var clamd = new NepClamd();

        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen, clamd.Scanner)
            .HerkenAsync(new MemoryStream(PdfHerkenningTests.Webwinkelfactuur()), "factuur.pdf");

        Assert.NotNull(resultaat.Factuur);
        Assert.False(resultaat.Geweigerd);
        Assert.True(clamd.Ontvangen > 1000);
    }

    [Fact]
    public async Task Besmet_bestand_wordt_geweigerd_zonder_te_openen()
    {
        using var clamd = new NepClamd();
        var bestand = Encoding.ASCII.GetBytes("%PDF-1.7 " + Teken);

        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen, clamd.Scanner).HerkenAsync(new MemoryStream(bestand), "factuur.pdf");

        Assert.True(resultaat.Geweigerd);
        Assert.Null(resultaat.Factuur);
        Assert.Equal("Het bestand bevat kwaadaardige inhoud (Eicar-Test-Signature) en wordt niet verwerkt.", resultaat.Melding);
    }

    [Fact]
    public async Task Onbereikbare_scanner_betekent_niet_verwerken()
    {
        int poort;
        using (var vrij = new TcpListener(IPAddress.Loopback, 0))
        {
            vrij.Start();
            poort = ((IPEndPoint)vrij.LocalEndpoint).Port;
        }

        var resultaat = await Factuurherkenning.Standaard(EigenGegevens.Geen, new ClamAvScanner("127.0.0.1", poort))
            .HerkenAsync(new MemoryStream(PdfHerkenningTests.Webwinkelfactuur()), "factuur.pdf");

        Assert.Null(resultaat.Factuur);
        Assert.False(resultaat.Geweigerd);
        Assert.Equal($"De virusscanner (127.0.0.1:{poort}) is niet bereikbaar. Het bestand wordt pas verwerkt na een geslaagde scan.", resultaat.Melding);
    }

    [Theory]
    [InlineData("stream: OK\0", Scanuitslag.Schoon, null)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\0", Scanuitslag.Besmet, "Win.Test.EICAR_HDB-1")]
    [InlineData("INSTREAM size limit exceeded. ERROR\0", Scanuitslag.NietGescand, "De virusscanner gaf een fout: INSTREAM size limit exceeded. ERROR")]
    public void Antwoord_van_clamd(string antwoord, Scanuitslag uitslag, string? toelichting)
    {
        var resultaat = ClamAvScanner.Uitslag(antwoord);

        Assert.Equal(uitslag, resultaat.Uitslag);
        Assert.Equal(toelichting, resultaat.Toelichting);
    }

    [Fact]
    public void Adres_met_en_zonder_poort()
    {
        Assert.NotNull(ClamAvScanner.Van("clamav.local:3311"));
        Assert.NotNull(ClamAvScanner.Van("127.0.0.1"));
    }

    /// <summary>Een nagebootste clamd die het INSTREAM-protocol spreekt en het EICAR-teken herkent.</summary>
    private sealed class NepClamd : IDisposable
    {
        private readonly TcpListener luisteraar = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();

        public NepClamd()
        {
            luisteraar.Start();
            _ = Task.Run(Bedien);
        }

        public ClamAvScanner Scanner => new("127.0.0.1", ((IPEndPoint)luisteraar.LocalEndpoint).Port);

        public long Ontvangen { get; private set; }

        public void Dispose()
        {
            stop.Cancel();
            luisteraar.Stop();
            stop.Dispose();
        }

        private async Task Bedien()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await luisteraar.AcceptTcpClientAsync(stop.Token);
                }
                catch (Exception fout) when (fout is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                using (client)
                {
                    var stream = client.GetStream();
                    var opdracht = new byte[10];
                    await stream.ReadExactlyAsync(opdracht);
                    Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(opdracht));
                    var inhoud = new MemoryStream();
                    var lengte = new byte[4];
                    while (true)
                    {
                        await stream.ReadExactlyAsync(lengte);
                        var n = (int)BinaryPrimitives.ReadUInt32BigEndian(lengte);
                        if (n == 0)
                        {
                            break;
                        }

                        var blok = new byte[n];
                        await stream.ReadExactlyAsync(blok);
                        inhoud.Write(blok);
                    }

                    Ontvangen = inhoud.Length;
                    var antwoord = Encoding.ASCII.GetString(inhoud.ToArray()).Contains(Teken, StringComparison.Ordinal)
                        ? "stream: Eicar-Test-Signature FOUND\0"
                        : "stream: OK\0";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(antwoord));
                }
            }
        }
    }
}
