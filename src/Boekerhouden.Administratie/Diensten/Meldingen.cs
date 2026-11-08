namespace Boekerhouden.Administratie.Diensten;

/// <summary>Een korte bevestiging, eventueel met "Ongedaan maken"; het nummer houdt opeenvolgende meldingen uit elkaar.</summary>
public sealed record Melding(int Nummer, string Tekst, bool KanOngedaan);

/// <summary>
/// De bevestiging linksonder (<c>Meldingenhouder</c> in de layout): één per sessie (scoped), zodat hij ook na het
/// navigeren blijft staan, bijv. na "Boeken" op een inkoopfactuur als het volgende document al open is. Met een
/// handeling voor "Ongedaan maken" die een foutmelding geeft of leeg als het gelukt is; die handeling logt zelf in het
/// wijzigingslog (via de dienst die hij aanroept).
/// </summary>
public sealed class Meldingen
{
    private Func<Task<string?>>? ongedaan;
    private int nummer;

    public Melding? Huidig { get; private set; }

    public event Action? Gewijzigd;

    public void Toon(string tekst, Func<Task<string?>>? ongedaanMaken = null)
    {
        ongedaan = ongedaanMaken;
        Huidig = new Melding(++nummer, tekst, ongedaanMaken is not null);
        Gewijzigd?.Invoke();
    }

    /// <summary>Voert "Ongedaan maken" uit (één keer) en toont daarna wat er gebeurde.</summary>
    public async Task MaakOngedaan()
    {
        if (ongedaan is not { } handeling)
        {
            return;
        }

        ongedaan = null;
        Huidig = Huidig is null ? null : Huidig with { KanOngedaan = false };
        string? fout;
        try
        {
            fout = await handeling();
        }
        catch (GeenToegangException e)
        {
            fout = e.Message;
        }

        Toon(fout is null ? "Ongedaan gemaakt." : $"Niet ongedaan gemaakt: {fout}");
    }

    public void Sluit()
    {
        (Huidig, ongedaan) = (null, null);
        Gewijzigd?.Invoke();
    }
}
