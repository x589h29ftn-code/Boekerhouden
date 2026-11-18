namespace Boekerhouden.Administratie.Windows;

/// <summary>Een hoofdvenster van de app dat de proef (voor de CI) kan dragen: de afsluitcode komt van de proef.</summary>
internal abstract class Proefvenster : Form
{
    /// <summary>De afsluitcode: 0, of 1 als de proef mislukte.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Uitslag { get; set; }

    /// <summary>De maat en plek van een hoofdvenster; op een kleiner scherm beeldvullend.</summary>
    protected void ZetMaat(bool proef)
    {
        Size = new Size(1440, 960);
        MinimumSize = new Size(1024, 700);
        StartPosition = FormStartPosition.CenterScreen;

        // Op een kleiner scherm (een laptop) past het venster niet: dan beeldvullend, anders valt de rand buiten beeld.
        if (!proef && Screen.PrimaryScreen?.WorkingArea is { } scherm && (scherm.Width < Width || scherm.Height < Height))
        {
            WindowState = FormWindowState.Maximized;
        }
    }
}
