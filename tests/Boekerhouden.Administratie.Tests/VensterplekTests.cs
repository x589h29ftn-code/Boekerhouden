using Boekerhouden.Administratie.Lokaal;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De Windows-app onthoudt waar het venster stond, en zet het terug zoals het nu op de schermen past.</summary>
public sealed class VensterplekTests : IDisposable
{
    private static readonly Schermvlak Laptop = new(0, 0, 1366, 728);
    private static readonly Schermvlak Tweede = new(1366, 0, 1920, 1040);
    private readonly string map = Directory.CreateTempSubdirectory("vensterplek").FullName;

    [Fact]
    public void Bewaren_en_lezen_geeft_dezelfde_plek()
    {
        var plek = new Vensterplek(100, 80, 1200, 700, Gemaximaliseerd: true, Donker: true);
        plek.Bewaar(map);
        Assert.Equal(plek, Vensterplek.Lees(map));
    }

    [Fact]
    public void Zonder_of_met_een_onleesbaar_bestand_de_gewone_startplek()
    {
        Assert.Null(Vensterplek.Lees(map));
        File.WriteAllText(Path.Combine(map, Vensterplek.Bestand), "{ kapot");
        Assert.Null(Vensterplek.Lees(map));
    }

    [Fact]
    public void Op_een_scherm_dat_er_niet_meer_is_de_gewone_startplek()
    {
        // Gesloten op het tweede scherm, dat nu is losgekoppeld.
        var plek = new Vensterplek(1500, 100, 1200, 800, false);
        Assert.NotNull(plek.Passend([Laptop, Tweede], 1024, 700));
        Assert.Null(plek.Passend([Laptop], 1024, 700));
    }

    [Fact]
    public void Te_groot_of_half_buiten_beeld_wordt_passend_gemaakt()
    {
        // Van een groot scherm naar een laptop: kleiner en binnen het scherm.
        var plek = new Vensterplek(400, 300, 1800, 1000, false).Passend([Laptop], 1024, 700)!;
        Assert.Equal(new Schermvlak(0, 0, 1366, 728), plek.Vlak);

        // Half rechts buiten beeld: teruggeschoven.
        Assert.Equal(new Schermvlak(266, 20, 1100, 700), new Vensterplek(900, 20, 1100, 700, false).Passend([Laptop], 1024, 700)!.Vlak);
    }

    [Fact]
    public void Kleiner_dan_het_minimum_wordt_het_minimum()
    {
        Assert.Equal(new Schermvlak(10, 10, 1024, 700), new Vensterplek(10, 10, 300, 200, false).Passend([Laptop], 1024, 700)!.Vlak);
    }

    public void Dispose() => Directory.Delete(map, recursive: true);
}
