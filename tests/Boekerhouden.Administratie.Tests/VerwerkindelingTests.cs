namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Het verwerkscherm (inkoop) past altijd in het venster: een onthouden breedte van de factuur die niet past (met de
/// werkvoorraad erbij, of na een groter scherm) krimpt mee, het formulier houdt minstens 520 pixels en krijgt wat over
/// is, en slepen aan de scheiding telt de werkvoorraad mee. In de browser: <c>scripts/ui/verwerkbreedte.js</c>.
/// </summary>
public sealed class VerwerkindelingTests
{
    [Fact]
    public void De_factuur_krimpt_mee_en_het_formulier_krijgt_wat_over_is()
    {
        var wwwroot = Path.Combine(Repository(), "src", "Boekerhouden.Administratie", "wwwroot");
        var css = File.ReadAllText(Path.Combine(wwwroot, "administratie.css"));
        Assert.Matches(@"\.verwerken-formulier \{[^}]*flex: 1 1 0;[^}]*min-width: 520px;", css);
        Assert.Matches(@"\.verwerken-viewer \{[^}]*flex: 0 1 auto;[^}]*min-width: 320px;", css);

        var viewer = File.ReadAllText(Path.Combine(wwwroot, "factuurviewer.js"));
        Assert.Contains(".verwerken-formulier", viewer, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var map = new DirectoryInfo(AppContext.BaseDirectory);
        while (map is not null && !File.Exists(Path.Combine(map.FullName, "Boekerhouden.slnx")))
        {
            map = map.Parent;
        }

        return map?.FullName ?? throw new InvalidOperationException("De map van de repository is niet gevonden.");
    }
}
