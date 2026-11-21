using Boekerhouden.Administratie.Lokaal;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De keuze bij de eerste start op een pc zonder gegevens: alleen op deze pc werken, of verbinden met de kantoorserver.
/// Ook gebruikt om later een ander adres van de kantoorserver op te geven. In de stijl van de app: de titelbalk in de
/// kleur van de kopbalk, het logo, twee keuzes als kaart en een knop in de accentkleur.
/// </summary>
internal sealed class Startkeuze : Form
{
    private static readonly Color Vlak = Color.FromArgb(0xf3, 0xf4, 0xf6);
    private static readonly Color Kaart = Color.White;
    private static readonly Color Rand = Color.FromArgb(0xe2, 0xe5, 0xea);
    private static readonly Color Accent = Color.FromArgb(0x24, 0x42, 0x6e);
    private static readonly Color Tekst = Color.FromArgb(0x1f, 0x29, 0x37);
    private static readonly Color Grijs = Color.FromArgb(0x5a, 0x62, 0x6e);
    private readonly TextBox adres = new() { Width = 300, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5F) };

    /// <param name="server">Een eerder adres: dan gaat het alleen om het adres (geen keuze "alleen op deze pc").</param>
    public Startkeuze(string? server = null)
    {
        Text = "Sifer";
        Icon = Merk.Icoon ?? Icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Vlak;
        Padding = new Padding(32, 28, 32, 32);
        Font = new Font("Segoe UI", 10F);

        var inhoud = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Vlak };
        inhoud.Controls.Add(Kop(server is null ? "Hoe werk je op deze pc?" : "Verbinden met de kantoor-pc"));

        adres.PlaceholderText = "Naam van de kantoor-pc";
        var verbinden = Knop("Verbinden", primair: true);
        verbinden.Click += (_, _) =>
        {
            if (Werkwijze.Adres(adres.Text) is { } gekozen)
            {
                Kies(new Werkwijze(Werkwijzestand.Werkplek, Server: gekozen.ToString()));
            }
            else
            {
                adres.Focus();
            }
        };
        var rij = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Kaart, Margin = new Padding(0, 10, 0, 0) };
        adres.Margin = new Padding(0, 4, 8, 0);
        rij.Controls.AddRange([adres, verbinden]);

        if (server is null)
        {
            var alleen = Knop("Alleen op deze pc", primair: true);
            alleen.Click += (_, _) => Kies(new Werkwijze());
            inhoud.Controls.Add(Keuze("Alleen op deze pc", "De gegevens staan op deze pc. Later delen met andere pc's op kantoor kan altijd.", alleen));
            inhoud.Controls.Add(Keuze("Met de kantoor-pc", "Een andere pc op kantoor deelt de gegevens; deze pc werkt daarin mee.", rij));
        }
        else
        {
            adres.Text = Uri.TryCreate(server, UriKind.Absolute, out var oud) ? oud.Host : server;
            inhoud.Controls.Add(Keuze("Naam of adres van de kantoor-pc", null, rij));
            AcceptButton = verbinden;
        }

        Controls.Add(inhoud);
    }

    /// <summary>Wat er gekozen is; leeg als het venster gesloten is zonder keuze.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Werkwijze? Gekozen { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Vensterstijl.Titelbalk(Handle);
    }

    private static FlowLayoutPanel Kop(string tekst)
    {
        var kop = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Vlak, Margin = new Padding(0, 0, 0, 18) };
        if (Merk.Icoon is { } icoon)
        {
            kop.Controls.Add(new PictureBox { Image = new Icon(icoon, 32, 32).ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 2, 12, 0) });
        }

        kop.Controls.Add(new Label { Text = tekst, AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), ForeColor = Tekst, Margin = new Padding(0, 4, 0, 0) });
        return kop;
    }

    /// <summary>Een keuze als kaart: titel, een regel uitleg en de knop of het veld.</summary>
    private static FlowLayoutPanel Keuze(string titel, string? uitleg, Control onder)
    {
        var kaart = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Kaart,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0, 0, 0, 12),
            MinimumSize = new Size(460, 0),
        };
        kaart.Paint += (_, e) =>
        {
            using var pen = new Pen(Rand);
            e.Graphics.DrawRectangle(pen, 0, 0, kaart.Width - 1, kaart.Height - 1);
        };
        kaart.Controls.Add(new Label { Text = titel, AutoSize = true, Font = new Font("Segoe UI Semibold", 11F), ForeColor = Tekst });
        if (uitleg is not null)
        {
            kaart.Controls.Add(new Label { Text = uitleg, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Grijs, Margin = new Padding(3, 4, 3, 0) });
        }

        onder.Margin = new Padding(0, 10, 0, 0);
        kaart.Controls.Add(onder);
        return kaart;
    }

    private static Button Knop(string tekst, bool primair)
    {
        var knop = new Button
        {
            Text = tekst,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 5, 12, 5),
            BackColor = primair ? Accent : Kaart,
            ForeColor = primair ? Color.White : Tekst,
            Font = new Font("Segoe UI Semibold", 10F),
        };
        knop.FlatAppearance.BorderColor = primair ? Accent : Rand;
        knop.FlatAppearance.MouseOverBackColor = primair ? Color.FromArgb(0x1b, 0x33, 0x57) : Vlak;
        return knop;
    }

    private void Kies(Werkwijze werkwijze)
    {
        Gekozen = werkwijze;
        DialogResult = DialogResult.OK;
        Close();
    }
}
