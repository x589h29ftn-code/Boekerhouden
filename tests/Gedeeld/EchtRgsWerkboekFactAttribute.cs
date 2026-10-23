namespace Boekerhouden.Tests.Gedeeld;

/// <summary>
/// Test die alleen draait als het echte RGS-werkboek beschikbaar is (omgevingsvariabele BOEKERHOUDEN_RGS_WERKBOEK
/// met het pad). Het werkboek staat niet in de repository; in CI worden deze tests overgeslagen.
/// </summary>
internal sealed class EchtRgsWerkboekFactAttribute : FactAttribute
{
    public EchtRgsWerkboekFactAttribute()
    {
        if (Pad is null)
        {
            Skip = "Zet BOEKERHOUDEN_RGS_WERKBOEK op het pad van het officiële RGS-werkboek om deze test te draaien.";
        }
    }

    public static string? Pad =>
        Environment.GetEnvironmentVariable("BOEKERHOUDEN_RGS_WERKBOEK") is { Length: > 0 } pad && File.Exists(pad) ? pad : null;
}
