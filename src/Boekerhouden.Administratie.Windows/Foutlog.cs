namespace Boekerhouden.Administratie.Windows;

/// <summary>Onverwachte fouten: naar <c>fouten.log</c> in de gegevensmap, en een melding in beeld.</summary>
internal static class Foutlog
{
    public static string Map { get; set; } = Path.GetTempPath();

    public static void Toon(Exception fout, string? melding = null)
    {
        Schrijf(fout);
        MessageBox.Show($"{melding ?? "Er ging iets mis."}{Environment.NewLine}{Environment.NewLine}{fout.Message}{Environment.NewLine}{Environment.NewLine}Details staan in {Path.Combine(Map, "fouten.log")}.",
            "Boekerhouden Administratie", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static void Schrijf(Exception? fout)
    {
        try
        {
            File.AppendAllText(Path.Combine(Map, "fouten.log"), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {fout}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Het log is niet te schrijven; de melding in beeld blijft.
        }
    }
}
