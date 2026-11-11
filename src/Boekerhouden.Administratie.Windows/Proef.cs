using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Boekerhouden.Administratie.Identiteit;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De proef voor de CI (<c>--proef map</c>): eerst een administratie met twee jaar gegevens (memoriaal, een bankafschrift
/// met honderden mutaties, inkoopfacturen; <see cref="Proefgegevens"/>), dan in het venster het eerste scherm, een
/// controle op een bekend bedrag, en daarna elke pagina van de administratie: laadtijd meten, alle schakelaars heen en
/// terug klikken (<c>proef.js</c>) en kijken of er een fout in beeld komt. Verslag (<c>proef.txt</c>) en schermafdrukken
/// komen in de map; afsluitcode 1 bij een fout.
/// </summary>
internal static class Proef
{
    /// <summary>De pagina's van een administratie die de proef langsloopt (het deel na <c>/a/{id}/</c>).</summary>
    public static readonly string[] Paginas =
    [
        "", "balans", "kolommenbalans", "kolommenbalans?vergelijk=jaar", "kolommenbalans?weergave=maand", "kolommenbalans?indeling=rgs", "proefbalans", "grootboek?rekening=1100",
        "grootboek?rekening=4300", "journaal", "inkoopboek", "verkoopboek", "crediteuren", "debiteuren", "aansluitingen", "bank",
        "bank/openstaand", "inkoop", "verkoop", "verkoop/nieuw", "verkoop/terugkerend", "memoriaal", "relaties", "regels",
        "rekeningschema", "instellingen", "factuurlayout", "activa", "activa?weergave=staat", "activa/nieuw", "perioden",
        "btw", "btw/rondrekening", "vpb", "beginbalans", "facturen",
    ];

    public static async Task<Proefuitkomst> Voorbereiden(IServiceProvider diensten)
    {
        Foutlog.Stil = true;
        var wie = diensten.GetRequiredService<LokaleSessie>().Gebruiker.AlsUitvoerder();
        return await Proefgegevens.Vul(diensten, wie, new Proefomvang(), DateTime.Today.Year);
    }

    public static async Task Voer(HoofdVenster venster, WebView2 webview, string map, Proefuitkomst? gegevens)
    {
        var verslag = new StringBuilder();
        var fouten = new List<string>();
        try
        {
            await Voer(webview, map, gegevens, verslag, fouten);
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            fouten.Add($"De proef zelf liep vast: {fout}");
        }

        var log = Path.Combine(map, "fouten.log");
        if (File.Exists(log))
        {
            fouten.Add("fouten.log:" + Environment.NewLine + await File.ReadAllTextAsync(log));
        }

        verslag.AppendLine().AppendLine(fouten.Count == 0 ? "Geen fouten." : $"{fouten.Count} fout(en):");
        foreach (var fout in fouten)
        {
            verslag.AppendLine("- " + fout);
        }

        await File.WriteAllTextAsync(Path.Combine(map, "proef.txt"), verslag.ToString());
        venster.Uitslag = fouten.Count == 0 ? 0 : 1;
        venster.Close();
    }

    private static async Task Voer(WebView2 webview, string map, Proefuitkomst? gegevens, StringBuilder verslag, List<string> fouten)
    {
        if (Merk.Icoon is null)
        {
            fouten.Add("Het pictogram van de app (sifer.ico) ontbreekt.");
        }

        var start = await Wacht(webview, "Proef B.V.");
        await Afdruk(webview, Path.Combine(map, "proef-start.png"));
        if (!start.Contains("Nieuwe administratie", StringComparison.Ordinal) || !start.Contains("Proef B.V.", StringComparison.Ordinal))
        {
            fouten.Add("Het eerste scherm toont de administraties niet.");
        }

        if (gegevens is null)
        {
            fouten.Add("Geen proefgegevens.");
            return;
        }

        verslag.AppendLine(CultureInfo.InvariantCulture, $"Proefgegevens: {gegevens.Boekingen} boekingen, {gegevens.Mutaties} bankmutaties, {gegevens.Facturen} inkoopfacturen.");
        foreach (var (stap, ms) in gegevens.Tijden)
        {
            verslag.AppendLine(CultureInfo.InvariantCulture, $"  {stap,-40} {ms,7} ms");
        }

        // Een bekend bedrag: de beginbalans van vorig jaar staat als beginsaldo op het eigen vermogen.
        await Ga(webview, $"/a/{gegevens.Administratie}/grootboek?rekening=0500");
        if (!(await Wacht(webview, "1.234,56")).Contains("1.234,56", StringComparison.Ordinal))
        {
            fouten.Add("De grootboekkaart 0500 toont de beginbalans (1.234,56) niet.");
        }

        await Afdruk(webview, Path.Combine(map, "proef-grootboek-0500.png"));

        var script = Proefscript();
        verslag.AppendLine().AppendLine("Pagina                              laden  knoppen  klikken").AppendLine(new string('-', 62));
        for (var i = 0; i < Paginas.Length; i++)
        {
            var pagina = Paginas[i];
            var klok = Stopwatch.StartNew();
            await Ga(webview, $"/a/{gegevens.Administratie}/{pagina}");
            await Rustig(webview);
            var laden = klok.ElapsedMilliseconds;
            await Afdruk(webview, Path.Combine(map, $"proef-{i + 1:00}-{Bestandsnaam(pagina)}.png"));
            if (await FoutInBeeld(webview))
            {
                fouten.Add($"{pagina}: fout in beeld na openen.");
                continue;
            }

            klok.Restart();
            var (klikken, gevonden) = await Klik(webview, script);
            fouten.AddRange(gevonden);
            verslag.AppendLine(CultureInfo.InvariantCulture, $"{(pagina.Length == 0 ? "(dashboard)" : pagina),-34} {laden,6} ms {klok.ElapsedMilliseconds,6} ms {klikken,6}");
        }

        // Snel heen en weer tussen zware pagina's, zonder te wachten tot ze klaar zijn.
        var snel = Stopwatch.StartNew();
        for (var r = 0; r < 3; r++)
        {
            foreach (var pagina in new[] { "kolommenbalans", "grootboek?rekening=1100", "bank", "journaal", "inkoop", "kolommenbalans?vergelijk=jaar" })
            {
                await Ga(webview, $"/a/{gegevens.Administratie}/{pagina}");
                await Task.Delay(150);
            }
        }

        await Rustig(webview);
        verslag.AppendLine(CultureInfo.InvariantCulture, $"{"snel wisselen (18 pagina's)",-34} {snel.ElapsedMilliseconds,6} ms");
        if (await FoutInBeeld(webview))
        {
            fouten.Add("Fout in beeld na snel wisselen tussen pagina's.");
        }

        await Afdruk(webview, Path.Combine(map, "proef-snel-wisselen.png"));

        // Reservekopieën (alleen in de Windows-app): de pagina, een kopie maken en controleren.
        await Ga(webview, "/beheer/reservekopieen");
        await Rustig(webview);
        await webview.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Nu een reservekopie maken')?.click()");
        var kopie = await Wacht(webview, "Reservekopie gemaakt");
        await webview.CoreWebView2.ExecuteScriptAsync("document.querySelector('table.reservekopieen button')?.click()");
        var gecontroleerd = await Wacht(webview, "in orde");
        await Afdruk(webview, Path.Combine(map, "proef-reservekopieen.png"));
        if (!kopie.Contains("Reservekopie gemaakt", StringComparison.Ordinal) || !gecontroleerd.Contains("in orde", StringComparison.Ordinal) || await FoutInBeeld(webview))
        {
            fouten.Add("Reservekopieën: de kopie is niet gemaakt of niet in orde.");
        }

        var bestanden = Directory.Exists(Path.Combine(map, "reservekopieen")) ? Directory.GetFiles(Path.Combine(map, "reservekopieen"), "*.db").Length : 0;
        verslag.AppendLine(CultureInfo.InvariantCulture, $"{"reservekopieën",-34} {bestanden,6} kopie(ën) in de map");
    }

    private static async Task<(int Klikken, IReadOnlyList<string> Fouten)> Klik(WebView2 webview, string script)
    {
        await webview.CoreWebView2.ExecuteScriptAsync(script);
        await webview.CoreWebView2.ExecuteScriptAsync(
            "window.__proef = null; window.boekerhoudenProef().then(u => window.__proef = JSON.stringify(u), f => window.__proef = JSON.stringify({ klikken: 0, fouten: ['proef.js: ' + f] }));");
        for (var i = 0; i < 600; i++)
        {
            await Task.Delay(500);
            var uitkomst = JsonSerializer.Deserialize<string?>(await webview.CoreWebView2.ExecuteScriptAsync("window.__proef"));
            if (uitkomst is not null)
            {
                var gelezen = JsonSerializer.Deserialize<Knoppenuitkomst>(uitkomst, Json)!;
                return (gelezen.Klikken, gelezen.Fouten);
            }
        }

        return (0, ["De knoppenproef werd niet binnen 5 minuten klaar."]);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Knoppenuitkomst(int Klikken, string[] Fouten);

    private static string Proefscript()
    {
        using var stroom = typeof(Proef).Assembly.GetManifestResourceStream("proef.js")
            ?? throw new InvalidOperationException("proef.js ontbreekt in de app.");
        using var lezer = new StreamReader(stroom);
        return lezer.ReadToEnd();
    }

    private static Task<string> Ga(WebView2 webview, string adres) =>
        webview.CoreWebView2.ExecuteScriptAsync($"Blazor.navigateTo({JsonSerializer.Serialize(adres)})");

    /// <summary>Wacht tot de pagina klaar is: geen laadschets meer en de tekst twee keer achter elkaar gelijk.</summary>
    private static async Task Rustig(WebView2 webview)
    {
        var vorige = string.Empty;
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            var tekst = JsonSerializer.Deserialize<string>(await webview.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.laadschets') ? '' : document.body.innerText")) ?? string.Empty;
            if (tekst.Length > 0 && tekst == vorige)
            {
                return;
            }

            vorige = tekst;
        }
    }

    private static async Task<bool> FoutInBeeld(WebView2 webview) =>
        JsonSerializer.Deserialize<bool>(await webview.CoreWebView2.ExecuteScriptAsync(
            "(() => { const ui = document.getElementById('blazor-error-ui'); return document.body.innerText.includes('Er ging iets mis') || (!!ui && getComputedStyle(ui).display !== 'none'); })()"));

    private static string Bestandsnaam(string pagina) =>
        pagina.Length == 0 ? "dashboard" : new string([.. pagina.Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

    private static async Task<string> Wacht(WebView2 webview, string verwacht)
    {
        var tekst = string.Empty;
        for (var i = 0; i < 60 && !tekst.Contains(verwacht, StringComparison.Ordinal); i++)
        {
            await Task.Delay(1000);
            tekst = JsonSerializer.Deserialize<string>(await webview.CoreWebView2.ExecuteScriptAsync("document.body.innerText")) ?? string.Empty;
        }

        return tekst;
    }

    private static async Task Afdruk(WebView2 webview, string pad)
    {
        await using var afdruk = File.Create(pad);
        await webview.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, afdruk);
    }
}
