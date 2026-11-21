using System.Text.Json;

namespace Boekerhouden.Administratie.Lokaal;

/// <summary>Een rechthoek op het bureaublad (in pixels): een scherm (het werkgebied, zonder taakbalk) of een venster.</summary>
public readonly record struct Schermvlak(int X, int Y, int Breedte, int Hoogte)
{
    public int Rechts => X + Breedte;

    public int Onder => Y + Hoogte;
}

/// <summary>
/// Waar het venster van de Windows-app stond toen het werd gesloten, en of de weergave donker was (zodat het venster
/// bij het opstarten meteen de goede achtergrond heeft in plaats van een witte flits). Bewaard als
/// <c>vensterplek.json</c> in de gegevensmap.
/// </summary>
public sealed record Vensterplek(int X, int Y, int Breedte, int Hoogte, bool Gemaximaliseerd, bool Donker = false)
{
    public const string Bestand = "vensterplek.json";

    /// <summary>Zoveel van de titelbalk moet op een scherm vallen, anders is het venster niet meer te pakken.</summary>
    private const int Grijpbaar = 120;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Schermvlak Vlak => new(X, Y, Breedte, Hoogte);

    /// <summary>De bewaarde plek, of leeg als er niets (leesbaars) is bewaard.</summary>
    public static Vensterplek? Lees(string map)
    {
        try
        {
            var pad = Path.Combine(map, Bestand);
            return File.Exists(pad) ? JsonSerializer.Deserialize<Vensterplek>(File.ReadAllText(pad), Json) : null;
        }
        catch (Exception fout) when (fout is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Bewaren; lukt het niet (bijv. een map zonder schrijfrecht), dan begint het venster de volgende keer gewoon in het midden.</summary>
    public void Bewaar(string map)
    {
        try
        {
            File.WriteAllText(Path.Combine(map, Bestand), JsonSerializer.Serialize(this, Json));
        }
        catch (Exception fout) when (fout is IOException or UnauthorizedAccessException)
        {
            // Niet erg: alleen de plek van het venster.
        }
    }

    /// <summary>
    /// De plek zoals hij nu op de schermen past: het scherm waar de titelbalk op valt (een scherm dat is losgekoppeld
    /// telt niet), niet groter dan dat scherm en niet kleiner dan het minimum, helemaal binnen het scherm geschoven.
    /// Leeg als het venster op geen enkel scherm meer te pakken is: dan de gewone startplek.
    /// </summary>
    public Vensterplek? Passend(IReadOnlyList<Schermvlak> schermen, int minimumBreedte, int minimumHoogte)
    {
        ArgumentNullException.ThrowIfNull(schermen);
        if (Breedte <= 0 || Hoogte <= 0)
        {
            return null;
        }

        // De titelbalk: de bovenste strook van het venster.
        var titel = new Schermvlak(X, Y, Breedte, 32);
        var scherm = schermen.Select(s => (Scherm: s, Overlap: Overlap(titel, s)))
            .Where(x => x.Overlap.Breedte >= Math.Min(Grijpbaar, Breedte) && x.Overlap.Hoogte > 0)
            .OrderByDescending(x => x.Overlap.Breedte)
            .Select(x => (Schermvlak?)x.Scherm)
            .FirstOrDefault();
        if (scherm is not { } s)
        {
            return null;
        }

        var breedte = Math.Clamp(Breedte, Math.Min(minimumBreedte, s.Breedte), s.Breedte);
        var hoogte = Math.Clamp(Hoogte, Math.Min(minimumHoogte, s.Hoogte), s.Hoogte);
        var x = Math.Clamp(X, s.X, s.Rechts - breedte);
        var y = Math.Clamp(Y, s.Y, s.Onder - hoogte);
        return this with { X = x, Y = y, Breedte = breedte, Hoogte = hoogte };
    }

    private static Schermvlak Overlap(Schermvlak a, Schermvlak b)
    {
        var links = Math.Max(a.X, b.X);
        var boven = Math.Max(a.Y, b.Y);
        return new Schermvlak(links, boven, Math.Max(0, Math.Min(a.Rechts, b.Rechts) - links), Math.Max(0, Math.Min(a.Onder, b.Onder) - boven));
    }
}
