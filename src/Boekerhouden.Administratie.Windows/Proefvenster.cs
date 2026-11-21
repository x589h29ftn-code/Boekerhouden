using Boekerhouden.Administratie.Lokaal;
using Microsoft.Web.WebView2.Core;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// Een hoofdvenster van de app: de titelbalk in de kleur van de app, de plek en maat van de vorige keer (zolang die
/// nog op een scherm past) en de achtergrond van de weergave (licht of donker) vóór de schermen er zijn. Kan de proef
/// (voor de CI) dragen: de afsluitcode komt van de proef.
/// </summary>
internal abstract class Proefvenster : Form
{
    private static readonly Size Standaard = new(1440, 960);
    private static readonly Size Minimum = new(1024, 700);
    private readonly List<string> teOpenen = [];
    private string? map;
    private bool donker;
    private CoreWebView2? kern;
    private bool geladen;

    /// <summary>De afsluitcode: 0, of 1 als de proef mislukte.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Uitslag { get; set; }

    /// <summary>Of de vorige keer de donkere weergave aan stond (voor de achtergrond tijdens het laden).</summary>
    protected bool Donker => donker;

    /// <summary>
    /// De maat en plek: die van de vorige keer, anders in het midden, op een klein scherm (een laptop) beeldvullend.
    /// Bij de proef altijd de standaardmaat en niets onthouden.
    /// </summary>
    protected void ZetMaat(bool proef, string gegevensmap)
    {
        MinimumSize = Minimum;
        var vorige = proef ? null : Vensterplek.Lees(gegevensmap);
        map = proef ? null : gegevensmap;
        donker = vorige?.Donker ?? false;
        BackColor = Vensterstijl.Achtergrond(donker);

        var schermen = Screen.AllScreens.Select(s => new Schermvlak(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToList();
        if (vorige?.Passend(schermen, Minimum.Width, Minimum.Height) is { } plek)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(plek.X, plek.Y, plek.Breedte, plek.Hoogte);
            if (plek.Gemaximaliseerd)
            {
                WindowState = FormWindowState.Maximized;
            }

            return;
        }

        Size = Standaard;
        StartPosition = FormStartPosition.CenterScreen;

        // Op een kleiner scherm (een laptop) past het venster niet: dan beeldvullend, anders valt de rand buiten beeld.
        if (!proef && Screen.PrimaryScreen?.WorkingArea is { } scherm && (scherm.Width < Width || scherm.Height < Height))
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    /// <summary>De weergave is licht of donker geworden (het script in Vensterstijl): de achtergrond nu, en de volgende keer bij het opstarten.</summary>
    protected void ZetThema(bool isDonker)
    {
        donker = isDonker;
        BackColor = Vensterstijl.Achtergrond(isDonker);
    }

    /// <summary>Bestanden openen (vanuit Windows): zodra de pagina er is, krijgt die ze; het venster komt naar voren.</summary>
    public void Open(IReadOnlyList<string> bestanden)
    {
        if (bestanden.Count == 0)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => Open(bestanden));
            return;
        }

        teOpenen.AddRange(bestanden);
        if (!IsHandleCreated)
        {
            // Vóór het opstarten (de opdrachtregel): de pagina krijgt ze als hij geladen is.
            return;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        _ = LeverWachtend();
    }

    /// <summary>De webweergave van dit venster, voor het openen van bestanden.</summary>
    protected void Koppel(CoreWebView2 webweergave)
    {
        kern = webweergave;
        webweergave.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                geladen = true;
                _ = LeverWachtend();
            }
        };
    }

    private async Task LeverWachtend()
    {
        if (kern is null || !geladen || teOpenen.Count == 0)
        {
            return;
        }

        var lijst = teOpenen.ToList();
        teOpenen.Clear();
        await Openen.Lever(kern, lijst);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Vensterstijl.Titelbalk(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (map is not null)
        {
            var vlak = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            new Vensterplek(vlak.X, vlak.Y, vlak.Width, vlak.Height, WindowState == FormWindowState.Maximized, donker).Bewaar(map);
        }

        base.OnFormClosing(e);
    }
}
