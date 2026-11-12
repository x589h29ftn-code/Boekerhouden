using Boekerhouden.Administratie.Lokaal;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De keuze bij de eerste start op een pc zonder gegevens: alleen op deze pc werken, of verbinden met de kantoorserver.
/// Ook gebruikt om later een ander adres van de kantoorserver op te geven.
/// </summary>
internal sealed class Startkeuze : Form
{
    private readonly TextBox adres = new() { Width = 260 };

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
        Padding = new Padding(24);
        Font = new Font("Segoe UI", 10F);

        var inhoud = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        if (server is null)
        {
            inhoud.Controls.Add(new Label { Text = "Hoe werk je op deze pc?", AutoSize = true, Font = new Font("Segoe UI", 14F), Margin = new Padding(3, 0, 3, 14) });
            var alleen = new Button { Text = "Alleen op deze pc", AutoSize = true, Padding = new Padding(10, 4, 10, 4), FlatStyle = FlatStyle.System };
            alleen.Click += (_, _) => Kies(new Werkwijze());
            inhoud.Controls.Add(alleen);
            inhoud.Controls.Add(new Label { Text = "Of verbinden met de kantoor-pc die de gegevens deelt:", AutoSize = true, Margin = new Padding(3, 22, 3, 6) });
        }
        else
        {
            inhoud.Controls.Add(new Label { Text = "Naam of adres van de kantoor-pc:", AutoSize = true, Margin = new Padding(3, 0, 3, 6) });
            adres.Text = Uri.TryCreate(server, UriKind.Absolute, out var oud) ? oud.Host : server;
        }

        adres.PlaceholderText = "Naam van de kantoor-pc";
        var verbinden = new Button { Text = "Verbinden", AutoSize = true, Padding = new Padding(10, 4, 10, 4), FlatStyle = FlatStyle.System, Margin = new Padding(8, 0, 0, 0) };
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
        var rij = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        rij.Controls.AddRange([adres, verbinden]);
        inhoud.Controls.Add(rij);
        Controls.Add(inhoud);
        if (server is not null)
        {
            AcceptButton = verbinden;
        }
    }

    /// <summary>Wat er gekozen is; leeg als het venster gesloten is zonder keuze.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Werkwijze? Gekozen { get; private set; }

    private void Kies(Werkwijze werkwijze)
    {
        Gekozen = werkwijze;
        DialogResult = DialogResult.OK;
        Close();
    }
}
