using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Boekerhouden.Documenten;

public enum Scanuitslag
{
    Schoon,
    Besmet,

    /// <summary>De scanner was niet bereikbaar of gaf een fout: het bestand is niet gescand.</summary>
    NietGescand,
}

/// <summary>De uitslag van een virusscan, met de naam van wat er gevonden is of de fout.</summary>
public sealed record Scanresultaat(Scanuitslag Uitslag, string? Toelichting = null);

/// <summary>Scant een bestand op virussen en andere kwaadaardige inhoud, vóórdat het pakket het opent.</summary>
public interface IVirusscanner
{
    Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default);
}

/// <summary>
/// ClamAV (open source) via de daemon <c>clamd</c> op de server, met het <c>INSTREAM</c>-protocol over TCP: het bestand
/// gaat in blokken naar de scanner, zonder het eerst op schijf te zetten. Werkt op Windows en Linux.
/// </summary>
public sealed class ClamAvScanner(string host = "127.0.0.1", int poort = 3310, TimeSpan? tijdslimiet = null) : IVirusscanner
{
    private const int Blok = 64 * 1024;
    private readonly TimeSpan limiet = tijdslimiet ?? TimeSpan.FromSeconds(60);

    /// <summary>Leest <c>host:poort</c>, bijv. <c>127.0.0.1:3310</c>; zonder poort is het 3310.</summary>
    public static ClamAvScanner Van(string adres)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adres);
        var deel = adres.Split(':');
        return deel.Length == 2 && int.TryParse(deel[1], System.Globalization.CultureInfo.InvariantCulture, out var p)
            ? new ClamAvScanner(deel[0], p)
            : new ClamAvScanner(adres);
    }

    public async Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        using var tijd = CancellationTokenSource.CreateLinkedTokenSource(annulering);
        tijd.CancelAfter(limiet);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, poort, tijd.Token).ConfigureAwait(false);
            await using var verbinding = client.GetStream();
            await verbinding.WriteAsync("zINSTREAM\0"u8.ToArray(), tijd.Token).ConfigureAwait(false);
            var buffer = new byte[Blok];
            var lengte = new byte[4];
            int gelezen;
            while ((gelezen = await inhoud.ReadAsync(buffer, tijd.Token).ConfigureAwait(false)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(lengte, (uint)gelezen);
                await verbinding.WriteAsync(lengte, tijd.Token).ConfigureAwait(false);
                await verbinding.WriteAsync(buffer.AsMemory(0, gelezen), tijd.Token).ConfigureAwait(false);
            }

            BinaryPrimitives.WriteUInt32BigEndian(lengte, 0);
            await verbinding.WriteAsync(lengte, tijd.Token).ConfigureAwait(false);
            return Uitslag(await LeesAntwoord(verbinding, tijd.Token).ConfigureAwait(false));
        }
        catch (Exception fout) when (fout is SocketException or IOException or OperationCanceledException && !annulering.IsCancellationRequested)
        {
            return new Scanresultaat(Scanuitslag.NietGescand, $"De virusscanner ({host}:{poort}) is niet bereikbaar.");
        }
    }

    /// <summary>Het antwoord van clamd: <c>stream: OK</c>, <c>stream: &lt;naam&gt; FOUND</c> of <c>... ERROR</c>.</summary>
    internal static Scanresultaat Uitslag(string antwoord)
    {
        var tekst = antwoord.TrimEnd('\0', '\n', ' ');
        if (tekst.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var naam = tekst[..^" FOUND".Length];
            return new Scanresultaat(Scanuitslag.Besmet, naam.StartsWith("stream: ", StringComparison.Ordinal) ? naam["stream: ".Length..] : naam);
        }

        return tekst.EndsWith(": OK", StringComparison.Ordinal)
            ? new Scanresultaat(Scanuitslag.Schoon)
            : new Scanresultaat(Scanuitslag.NietGescand, $"De virusscanner gaf een fout: {tekst}");
    }

    private static async Task<string> LeesAntwoord(NetworkStream verbinding, CancellationToken annulering)
    {
        var antwoord = new MemoryStream();
        var buffer = new byte[256];
        int gelezen;
        while ((gelezen = await verbinding.ReadAsync(buffer, annulering).ConfigureAwait(false)) > 0)
        {
            antwoord.Write(buffer, 0, gelezen);
            if (buffer.AsSpan(0, gelezen).Contains((byte)0) || antwoord.Length > 4096)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(antwoord.ToArray());
    }
}
