using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De iconen van de huisstijl: het SVG-bestand is geldig en elk icoon dat de schermen gebruiken, staat erin.</summary>
public sealed partial class UitstralingTests
{
    [Fact]
    public void Elk_gebruikt_icoon_staat_in_iconen_svg()
    {
        var src = Path.Combine(Repository(), "src");
        var iconen = XDocument.Load(Path.Combine(src, "Boekerhouden.Huisstijl", "wwwroot", "iconen.svg"))
            .Descendants(XName.Get("symbol", "http://www.w3.org/2000/svg")).Select(s => (string)s.Attribute("id")!).ToHashSet(StringComparer.Ordinal);
        var gebruikt = Directory.EnumerateFiles(Path.Combine(src, "Boekerhouden.Administratie", "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => IcoonNaam().Matches(File.ReadAllText(f)).Select(m => (Bestand: Path.GetFileName(f), Naam: m.Groups[1].Value)))
            .Concat(GroepsIcoon().Matches(File.ReadAllText(Path.Combine(src, "Boekerhouden.Administratie", "Components", "Gedeeld", "AdministratieKader.razor")))
                .Select(m => (Bestand: "AdministratieKader.razor", Naam: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(gebruikt);
        Assert.Empty(gebruikt.Where(g => !iconen.Contains(g.Naam)).Select(g => $"{g.Bestand}: {g.Naam}"));
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

    [GeneratedRegex("<Icoon Naam=\"([a-z0-9-]+)\"")]
    private static partial Regex IcoonNaam();

    [GeneratedRegex("""=> "([a-z0-9-]+)",""")]
    private static partial Regex GroepsIcoon();
}
