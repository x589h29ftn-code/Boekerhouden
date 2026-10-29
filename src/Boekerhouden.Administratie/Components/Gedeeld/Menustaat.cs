namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>
/// De stand van het zijmenu (ingeklapt, dichte groepen), per sessie in het geheugen zodat een andere pagina meteen
/// goed staat; de browser onthoudt hem tussen sessies (<c>menu.js</c>).
/// </summary>
public sealed class Menustaat
{
    public bool Gelezen { get; private set; }

    public bool Ingeklapt { get; set; }

    public HashSet<string> Dicht { get; } = new(StringComparer.Ordinal);

    /// <summary>Leest de opgeslagen stand ("ingeklapt;Groep,Groep"); geeft aan of er iets veranderde.</summary>
    public bool Lees(string? tekst)
    {
        Gelezen = true;
        if (string.IsNullOrEmpty(tekst))
        {
            return false;
        }

        var delen = tekst.Split(';');
        Ingeklapt = delen[0] == "ingeklapt";
        Dicht.Clear();
        if (delen.Length > 1)
        {
            Dicht.UnionWith(delen[1].Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        return true;
    }

    public string Tekst() => $"{(Ingeklapt ? "ingeklapt" : "open")};{string.Join(',', Dicht)}";
}
