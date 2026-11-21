using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// Wat een venster van de app tot een Windows-app maakt in plaats van een browser in een kader: de titelbalk in de
/// kleur van de kopbalk (één donkere balk bovenaan), de achtergrond van de app vóór de schermen er zijn (geen witte
/// flits), en geen browsergedrag (geen menu met Terug/Opnieuw laden/Inspecteren, geen adres linksonder bij een link,
/// geen ontwikkelhulpmiddelen; F5 en Ctrl+R in het script hieronder), en bestanden openen vanuit Windows (<see cref="Openen"/>).
/// </summary>
internal static partial class Vensterstijl
{
    /// <summary>De kopbalk (<c>--bh-inkt</c> in uitstraling.css).</summary>
    public static readonly Color Kopbalk = Color.FromArgb(0x17, 0x22, 0x38);

    /// <summary>De achtergrond van de app, licht en donker (<c>--bh-vlak</c> in vormgeving.css en donker.css).</summary>
    public static Color Achtergrond(bool donker) => donker ? Color.FromArgb(0x15, 0x19, 0x1f) : Color.FromArgb(0xf3, 0xf4, 0xf6);

    /// <summary>Het bericht van het script als de weergave licht of donker wordt.</summary>
    public const string Themabericht = "sifer:thema:";

    // Bewerken: wat het menu in een veld en bij geselecteerde tekst houdt; de rest (terug, opnieuw laden, opslaan als,
    // afdrukken, inspecteren, ...) is browsergedrag.
    private static readonly HashSet<string> Bewerken = new(StringComparer.OrdinalIgnoreCase)
    {
        "cut", "copy", "paste", "pasteAndMatchStyle", "selectAll", "undo", "redo", "emoji", "spellCheck", "copyLinkLocation",
    };

    /// <summary>
    /// In elke pagina (ook die van de kantoorserver): F5 en Ctrl+R laden de app niet opnieuw (een half ingevuld formulier
    /// zou weg zijn), en de weergave (licht of donker) gaat naar het venster, behalve tijdens het afdrukken (dan is hij
    /// even licht). Komt vóór de scripts van de pagina, dus vóór thema.js.
    /// </summary>
    private const string Script = """
        (function () {
          addEventListener('keydown', function (e) {
            if (e.key === 'F5' || ((e.ctrlKey || e.metaKey) && (e.key === 'r' || e.key === 'R'))) { e.preventDefault(); }
          }, true);
          var afdrukken = false, vorige = null;
          function meld() {
            if (afdrukken || !document.documentElement) { return; }
            var donker = document.documentElement.getAttribute('data-bs-theme') === 'dark';
            if (donker === vorige) { return; }
            vorige = donker;
            try { window.chrome.webview.postMessage('sifer:thema:' + (donker ? 'donker' : 'licht')); } catch (_) { }
          }
          addEventListener('beforeprint', function () { afdrukken = true; });
          addEventListener('afterprint', function () { afdrukken = false; setTimeout(meld, 0); });
          // Bestanden geopend vanuit Windows (Openen.cs): in het sleepvlak van de pagina, alsof ze erin gesleept zijn.
          // Een auditfile wordt een nieuwe administratie; de rest gaat naar "overal inlezen" van de administratie.
          var wachtend = [], melding = null;
          function bestand(b) {
            var bin = atob(b.inhoud), bytes = new Uint8Array(bin.length);
            for (var i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
            return new File([bytes], b.naam);
          }
          function zet(veld, lijst) {
            var dt = new DataTransfer();
            lijst.forEach(function (f) { dt.items.add(f); });
            veld.files = dt.files;
            veld.dispatchEvent(new Event('change', { bubbles: true }));
            wachtend = wachtend.filter(function (f) { return lijst.indexOf(f) < 0; });
          }
          function toon() {
            if (!wachtend.length) { if (melding) { melding.remove(); melding = null; } return; }
            if (!melding) {
              melding = document.createElement('div');
              melding.className = 'melding-kort';
              melding.setAttribute('role', 'status');
              melding.style.position = 'fixed'; melding.style.left = '1rem'; melding.style.bottom = '2.4rem'; melding.style.zIndex = '1080';
              document.body.appendChild(melding);
            }
            var tekst = (wachtend.length === 1 ? wachtend[0].name : wachtend.length + ' bestanden') + ' klaar om in te lezen: open een administratie.';
            if (melding.textContent !== tekst) { melding.textContent = tekst; }
          }
          // Het bestandsveld is pas klaar als Blazor het heeft gekoppeld (InputFile zet dan dit veld); eerder gaat het verloren.
          function klaar(veld) { return veld && !veld.disabled && veld._blazorInputFileNextFileId !== undefined; }
          var straks = null;
          function later() { if (!straks) { straks = setTimeout(function () { straks = null; lever(); }, 200); } }
          function lever() {
            if (!wachtend.length || !document.body) { toon(); return; }
            var naar = window.Blazor && window.Blazor.navigateTo;
            var xaf = wachtend.filter(function (f) { return /\.xaf$/i.test(f.name); });
            if (xaf.length) {
              var a = document.getElementById('auditfile');
              if (klaar(a)) { zet(a, [xaf[0]]); }
              else if (a) { later(); }
              else if (naar && !a && location.pathname !== '/administraties/nieuw') { naar('/administraties/nieuw'); return; }
            }
            var rest = wachtend.filter(function (f) { return !/\.xaf$/i.test(f.name); });
            if (rest.length) {
              var veld = document.getElementById('overal-inlezen');
              var administratie = /^\/a\/([0-9a-f-]{36})/i.exec(location.pathname);
              if (klaar(veld)) { zet(veld, rest); }
              else if (veld) { later(); return; }
              else if (!veld && administratie && naar && location.pathname !== '/a/' + administratie[1]) { naar('/a/' + administratie[1]); return; }
            }
            toon();
          }
          window.siferOpen = function (lijst) { lijst.forEach(function (b) { wachtend.push(bestand(b)); }); lever(); };
          addEventListener('DOMContentLoaded', function () {
            meld();
            new MutationObserver(meld).observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
            // Alleen wijzigingen van de pagina zelf (niet van de eigen melding, anders roept die zichzelf steeds op).
            new MutationObserver(function (wijzigingen) {
              if (wachtend.length && wijzigingen.some(function (w) { return !melding || !melding.contains(w.target); })) { later(); }
            }).observe(document.body, { childList: true, subtree: true });
          });
        })();
        """;

    /// <summary>De titelbalk dezelfde kleur als de kopbalk (Windows 11), en anders in elk geval donker (Windows 10).</summary>
    public static void Titelbalk(IntPtr venster)
    {
        Zet(venster, 20, 1);                                                          // DWMWA_USE_IMMERSIVE_DARK_MODE
        Zet(venster, 35, Kopbalk.R | (Kopbalk.G << 8) | (Kopbalk.B << 16));            // DWMWA_CAPTION_COLOR
        Zet(venster, 34, Kopbalk.R | (Kopbalk.G << 8) | (Kopbalk.B << 16));            // DWMWA_BORDER_COLOR
        Zet(venster, 36, 0xFFFFFF);                                                   // DWMWA_TEXT_COLOR
    }

    /// <summary>De instellingen van de webweergave; <paramref name="thema"/> krijgt licht (onwaar) of donker (waar).</summary>
    public static void Pas(CoreWebView2 kern, Action<bool>? thema = null)
    {
        ArgumentNullException.ThrowIfNull(kern);
        kern.Settings.IsStatusBarEnabled = false;
        kern.Settings.IsSwipeNavigationEnabled = false;
        kern.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        kern.ContextMenuRequested += (_, e) => Menu(e);
        _ = kern.AddScriptToExecuteOnDocumentCreatedAsync(Script);
        if (thema is not null)
        {
            kern.WebMessageReceived += (_, e) =>
            {
                string? bericht;
                try
                {
                    bericht = e.TryGetWebMessageAsString();
                }
                catch (ArgumentException)
                {
                    return;
                }

                if (bericht?.StartsWith(Themabericht, StringComparison.Ordinal) == true)
                {
                    thema(bericht[Themabericht.Length..] == "donker");
                }
            };
        }
    }

    /// <summary>Alleen knippen, kopiëren, plakken e.d.; is er niets te bewerken, dan geen menu.</summary>
    private static void Menu(CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var items = e.MenuItems;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Kind != CoreWebView2ContextMenuItemKind.Separator && !Bewerken.Contains(items[i].Name))
            {
                items.RemoveAt(i);
            }
        }

        // Geen scheidingslijnen aan het begin, aan het eind of twee achter elkaar.
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var scheiding = items[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
            if (scheiding && (i == 0 || i == items.Count - 1 || items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator))
            {
                items.RemoveAt(i);
            }
        }

        if (items.Count == 0)
        {
            e.Handled = true;
        }
    }

    private static void Zet(IntPtr venster, int attribuut, int waarde)
    {
        // Een oudere Windows kent het attribuut niet: dan blijft de gewone titelbalk.
        _ = DwmSetWindowAttribute(venster, attribuut, ref waarde, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr venster, int attribuut, ref int waarde, int grootte);
}
