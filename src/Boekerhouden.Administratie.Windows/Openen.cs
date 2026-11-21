using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Boekerhouden.Administratie.Diensten;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// Bestanden openen vanuit Windows: dubbelklikken op een auditfile of afschrift, <i>Openen met → Sifer</i> bij een
/// factuur, of bestanden op het pictogram slepen. Er draait één Sifer: een tweede start geeft zijn bestanden aan de
/// eerste en stopt. Het venster geeft de bestanden aan de pagina alsof ze erin gesleept worden (het script in
/// <see cref="Vensterstijl"/>), zodat ze dezelfde weg gaan als slepen: virusscan, herkennen, de juiste werklijst; een
/// auditfile wordt een nieuwe administratie.
/// </summary>
internal static partial class Openen
{
    /// <summary>De soorten die Sifer in <i>Openen met</i> aanbiedt (nooit als standaardprogramma: dat kiest de gebruiker).</summary>
    public static readonly string[] Extensies = [".xaf", ".mt940", ".940", ".sta", ".pdf", ".xml", ".jpg", ".jpeg", ".png"];

    private const string Programmanaam = "Sifer.Bestand";

    /// <summary>De bestanden op de opdrachtregel (geen opties zoals <c>--proef</c>).</summary>
    public static IReadOnlyList<string> Bestanden(string[] args) =>
        [.. args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a)).Select(Path.GetFullPath)];

    /// <summary>
    /// Draait Sifer al (voor deze gebruiker en deze gegevensmap), dan krijgt die de bestanden en is het antwoord waar:
    /// deze start stopt dan. Anders houdt deze start het slot vast zolang hij draait.
    /// </summary>
    public static bool AlGestart(string map, IReadOnlyList<string> bestanden, out Mutex? slot)
    {
        var naam = Naam(map);
        slot = new Mutex(true, $@"Local\{naam}", out var eerste);
        if (eerste)
        {
            return false;
        }

        slot.Dispose();
        slot = null;
        try
        {
            using var pijp = new NamedPipeClientStream(".", naam, PipeDirection.Out);
            pijp.Connect(3000);
            using var schrijver = new StreamWriter(pijp, Encoding.UTF8);
            schrijver.Write(JsonSerializer.Serialize(bestanden));
        }
        catch (Exception fout) when (fout is TimeoutException or IOException)
        {
            // De andere Sifer reageert niet: toch melden dat hij al draait, niet een tweede op dezelfde gegevens.
        }

        return true;
    }

    /// <summary>Luistert naar een volgende start; <paramref name="ontvangen"/> krijgt de bestanden (op een andere draad).</summary>
    public static void Luister(string map, Action<IReadOnlyList<string>> ontvangen)
    {
        var naam = Naam(map);
        var draad = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pijp = new NamedPipeServerStream(naam, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    pijp.WaitForConnection();
                    using var lezer = new StreamReader(pijp, Encoding.UTF8);
                    var bestanden = JsonSerializer.Deserialize<string[]>(lezer.ReadToEnd()) ?? [];
                    ontvangen([.. bestanden.Where(File.Exists)]);
                }
                catch (Exception fout) when (fout is IOException or JsonException)
                {
                    // Een afgebroken start: gewoon weer luisteren.
                }
            }
        })
        { IsBackground = true, Name = "Sifer openen" };
        draad.Start();
    }

    /// <summary>
    /// De bestanden aan de pagina geven (<c>window.siferOpen</c> uit het script in <see cref="Vensterstijl"/>). Te groot of onleesbaar: een melding
    /// in de foutlog, de rest gaat door.
    /// </summary>
    public static async Task Lever(CoreWebView2 kern, IReadOnlyList<string> bestanden)
    {
        var lijst = new List<object>();
        foreach (var pad in bestanden)
        {
            try
            {
                if (new FileInfo(pad).Length > InleesDienst.MaximumBytes)
                {
                    continue;
                }

                lijst.Add(new { naam = Path.GetFileName(pad), inhoud = Convert.ToBase64String(await File.ReadAllBytesAsync(pad)) });
            }
            catch (Exception fout) when (fout is IOException or UnauthorizedAccessException)
            {
                Foutlog.Schrijf(fout);
            }
        }

        if (lijst.Count > 0)
        {
            await kern.ExecuteScriptAsync($"window.siferOpen && window.siferOpen({JsonSerializer.Serialize(lijst)});");
        }
    }

    /// <summary>
    /// Sifer in <i>Openen met</i> voor deze gebruiker (geen beheerdersrechten nodig, geen standaardprogramma
    /// overgenomen). Bij elke start, zodat het pad klopt als de map van de app verplaatst is.
    /// </summary>
    public static void Registreer()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null)
            {
                return;
            }

            using (var programma = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Programmanaam}"))
            {
                programma.SetValue("", "Document voor Sifer");
                using var icoon = programma.CreateSubKey("DefaultIcon");
                icoon.SetValue("", $"\"{exe}\",0");
                using var opdracht = programma.CreateSubKey(@"shell\open\command");
                opdracht.SetValue("", $"\"{exe}\" \"%1\"");
            }

            foreach (var extensie in Extensies)
            {
                using var met = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extensie}\OpenWithProgids");
                met.SetValue(Programmanaam, Array.Empty<byte>(), RegistryValueKind.None);
            }

            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
        }
        catch (Exception fout) when (fout is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Foutlog.Schrijf(fout);
        }
    }

    /// <summary>Per gebruiker en gegevensmap (een proef in een eigen map stoort de gewone app niet).</summary>
    private static string Naam(string map) =>
        $"Sifer-{Environment.UserName}-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(map).ToUpperInvariant())))[..16]}";

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int gebeurtenis, int vlaggen, IntPtr eerste, IntPtr tweede);
}
