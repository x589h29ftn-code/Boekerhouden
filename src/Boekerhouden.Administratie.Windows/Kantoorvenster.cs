using System.Diagnostics;
using System.Security.Cryptography;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Lokaal;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// Het venster van de app als de gegevens bij een kantoorserver staan (op deze pc zelf, of op een andere pc op
/// kantoor): de schermen komen van de server. Het venster meldt de werkplek met zijn sleutel, wacht zo nodig tot de
/// beheerder hem koppelt (met de controlecode in beeld) en logt dan in met de ondertekende uitdaging. Het certificaat
/// van de server is bij de eerste verbinding vastgelegd; alleen dat certificaat wordt aangenomen.
/// </summary>
internal sealed class Kantoorvenster : Proefvenster
{
    private const string Naam = "Sifer";

    private readonly WebView2 webview = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel paneel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 245, 243) };
    private readonly Label kop = new() { AutoSize = true, Font = new Font("Segoe UI", 16F), ForeColor = Color.FromArgb(31, 41, 55) };
    private readonly Label tekst = new() { AutoSize = true, Font = new Font("Segoe UI", 10.5F), ForeColor = Color.FromArgb(90, 98, 110), MaximumSize = new Size(560, 0) };
    private readonly Label code = new() { AutoSize = true, Font = new Font("Segoe UI", 30F, FontStyle.Bold), ForeColor = Color.FromArgb(31, 58, 104) };
    private readonly FlowLayoutPanel knoppen = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 18, 0, 0) };
    private readonly System.Windows.Forms.Timer opnieuw = new();
    private readonly Werkplekverbinding verbinding;
    private readonly Werkpleksleutel sleutel;
    private readonly string map;
    private readonly bool server;
    private readonly string? proefmap;
    private readonly Proefuitkomst? proefgegevens;
    private Werkwijze werkwijze;
    private string? certificaat;
    private bool bezig;
    private bool proefGestart;

    /// <param name="server">Deze pc is zelf de kantoorserver (het adres is dan deze pc).</param>
    public Kantoorvenster(Uri adres, Werkpleksleutel sleutel, string? certificaat, string map, Werkwijze werkwijze, bool server, string? proefmap = null, Proefuitkomst? proefgegevens = null)
    {
        (this.sleutel, this.certificaat, this.map, this.werkwijze, this.server, this.proefmap, this.proefgegevens) = (sleutel, certificaat, map, werkwijze, server, proefmap, proefgegevens);
        verbinding = new Werkplekverbinding(adres, sleutel, certificaat);
        Text = Naam;
        Icon = Merk.Icoon ?? Icon;
        ZetMaat(proefmap is not null);

        var inhoud = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new Point(64, 64) };
        tekst.Margin = new Padding(3, 10, 3, 0);
        code.Margin = new Padding(0, 14, 0, 0);
        inhoud.Controls.AddRange([kop, tekst, code, knoppen]);
        knoppen.Controls.Add(Knop("Opnieuw proberen", async () => await Verbind()));
        if (!server)
        {
            knoppen.Controls.Add(Knop("Ander adres…", AnderAdres));
            knoppen.Controls.Add(Knop("Alleen op deze pc werken", () => Wissel(new Werkwijze())));
        }

        paneel.Controls.Add(inhoud);
        Controls.Add(webview);
        Controls.Add(paneel);
        paneel.BringToFront();
        opnieuw.Tick += async (_, _) => await Verbind();
        Load += async (_, _) => await Begin();
        FormClosing += Sluiten;
    }

    private static Button Knop(string tekst, Action actie)
    {
        var knop = new Button { Text = tekst, AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.System };
        knop.Click += (_, _) => actie();
        return knop;
    }

    private async Task Begin()
    {
        Toon("Verbinden…", verbinding.Server.Host, null, knoppenZichtbaar: false);
        try
        {
            await webview.EnsureCoreWebView2Async();
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            Foutlog.Toon(fout, "Het venster kon niet worden geopend.");
            Close();
            return;
        }

        var kern = webview.CoreWebView2;
        kern.Settings.AreDevToolsEnabled = false;

        // Het certificaat van de kantoorserver is door niemand ondertekend: alleen het vastgelegde certificaat mag.
        kern.ServerCertificateErrorDetected += (_, e) =>
        {
            using var gezien = e.ServerCertificate.ToX509Certificate2();
            e.Action = certificaat is not null && string.Equals(gezien.GetCertHashString(HashAlgorithmName.SHA256), certificaat, StringComparison.OrdinalIgnoreCase)
                ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
                : CoreWebView2ServerCertificateErrorAction.Cancel;
        };
        kern.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var doel))
            {
                return;
            }

            if (!string.Equals(doel.Host, verbinding.Server.Host, StringComparison.OrdinalIgnoreCase))
            {
                // Een adres buiten de kantoorserver hoort in de gewone browser.
                e.Cancel = true;
                if (doel.Scheme == Uri.UriSchemeHttps || doel.Scheme == Uri.UriSchemeHttp)
                {
                    Process.Start(new ProcessStartInfo(doel.AbsoluteUri) { UseShellExecute = true });
                }
            }
            else if (doel.AbsolutePath.Equals(Kantoorserver.GeenWerkplek, StringComparison.OrdinalIgnoreCase))
            {
                // De sessie is verlopen (of de server is opnieuw gestart): opnieuw inloggen met de sleutel.
                e.Cancel = true;
                BeginInvoke(async () => await Verbind());
            }
        };
        kern.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                paneel.Visible = false;
                webview.Visible = true;
                if (proefmap is not null && !proefGestart)
                {
                    proefGestart = true;
                    _ = Proef.Voer(this, webview, proefmap, proefgegevens);
                }
            }
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                Onbereikbaar();
            }
        };
        kern.DocumentTitleChanged += (_, _) => Text = kern.DocumentTitle is { Length: > 0 } titel && titel != Naam ? $"{titel} – {Naam}" : Naam;
        await Verbind();
    }

    /// <summary>Melden bij de server en, als de werkplek gekoppeld is, inloggen; anders wachten en het straks opnieuw proberen.</summary>
    private async Task Verbind()
    {
        if (bezig || IsDisposed)
        {
            return;
        }

        bezig = true;
        opnieuw.Stop();
        try
        {
            Werkplekantwoord antwoord;
            try
            {
                antwoord = await verbinding.Meld(Environment.UserName, Environment.MachineName);
            }
            catch (Exception fout) when (fout is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                Onbereikbaar();
                return;
            }

            if (certificaat is null && verbinding.GezienCertificaat is { } gezien)
            {
                // Eerste verbinding: het certificaat van de server vastleggen; de controlecode hangt ervan af.
                certificaat = gezien;
                if (!server)
                {
                    werkwijze = werkwijze with { Certificaat = gezien };
                    werkwijze.Bewaar(map);
                }
            }

            switch (antwoord.Stand)
            {
                case Werkplekstand.Gekoppeld when antwoord.Uitdaging is { } uitdaging:
                    webview.CoreWebView2.Navigate(verbinding.Binnen(uitdaging).AbsoluteUri);
                    break;
                case Werkplekstand.Wacht:
                    Toon("Wacht op de beheerder", $"Deze pc is aangemeld bij {verbinding.Server.Host} als {Environment.UserName}. De beheerder koppelt de werkplek onder Beheer, Gebruikers en toegang; daar hoort dezelfde code te staan.",
                        verbinding.Code, knoppenZichtbaar: !server);
                    Straks(3000);
                    break;
                default:
                    Toon("Geen toegang", "Deze werkplek is niet aangenomen, of de gebruiker staat uit. Vraag de beheerder.", null, knoppenZichtbaar: true);
                    Straks(15000);
                    break;
            }
        }
        finally
        {
            bezig = false;
        }
    }

    private void Onbereikbaar()
    {
        Toon("De kantoorserver is niet bereikbaar", server
            ? "De server op deze pc reageert niet. Sluit Sifer en start opnieuw."
            : $"Staat de kantoor-pc ({verbinding.Server.Host}) aan en is Sifer daar gestart? Dit venster probeert het vanzelf opnieuw.", null, knoppenZichtbaar: true);
        Straks(5000);
    }

    private void Straks(int ms)
    {
        opnieuw.Interval = ms;
        opnieuw.Start();
    }

    private void Toon(string titel, string uitleg, string? controlecode, bool knoppenZichtbaar)
    {
        (kop.Text, tekst.Text, code.Text) = (titel, uitleg, controlecode ?? "");
        code.Visible = controlecode is not null;
        knoppen.Visible = knoppenZichtbaar;
        webview.Visible = false;
        paneel.Visible = true;
        paneel.BringToFront();
    }

    private void AnderAdres()
    {
        using var keuze = new Startkeuze(werkwijze.Server);
        if (keuze.ShowDialog(this) == DialogResult.OK && keuze.Gekozen is { } nieuw)
        {
            Wissel(nieuw);
        }
    }

    private void Wissel(Werkwijze nieuw)
    {
        nieuw.Bewaar(map);
        opnieuw.Stop();
        Application.Restart();
    }

    private void Sluiten(object? sender, FormClosingEventArgs e)
    {
        opnieuw.Stop();
        if (server && proefmap is null && e.CloseReason == CloseReason.UserClosing
            && MessageBox.Show(this, "Deze pc deelt de gegevens: als je Sifer sluit, kunnen de andere pc's niet verder werken. Sluiten?", Naam, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            e.Cancel = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            opnieuw.Dispose();
            verbinding.Dispose();
            sleutel.Dispose();
        }

        base.Dispose(disposing);
    }
}
