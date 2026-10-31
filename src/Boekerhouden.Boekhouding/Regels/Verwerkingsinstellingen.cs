namespace Boekerhouden.Boekhouding.Regels;

/// <summary>
/// Hoe het pakket facturen verwerkt. Het kantoor legt de standaard vast; een administratie kan per instelling afwijken
/// (<see cref="AfwijkendeInstellingen"/>).
/// </summary>
/// <param name="Drempel">Na hoeveel gelijke bevestigingen op rij een leverancier automatisch wordt verwerkt.</param>
/// <param name="Bandbreedte">Automatisch tot zoveel keer het hoogste bevestigde bedrag.</param>
/// <param name="Limiet">Boven dit bedrag (inclusief btw) nooit automatisch; leeg = geen limiet (besluit B2).</param>
/// <param name="Betalingstermijn">In dagen, als de factuur geen vervaldatum noemt.</param>
/// <param name="Adrescontrole">Een nieuwe postcode op de factuur van een bekende leverancier ter controle voorleggen.</param>
/// <param name="Documentmap">
/// Waar een kopie van elk geboekt document komt, met variabelen (zie <see cref="Facturen.Documentpad"/>); leeg = nergens.
/// </param>
/// <param name="Betaalverschil">
/// Een betaling die hoogstens zoveel euro afwijkt van de enige passende factuur (nummer en IBAN kloppen) wordt
/// automatisch afgeletterd en het verschil afgeboekt op <paramref name="Verschilrekening"/>; leeg of 0 = uit.
/// </param>
/// <param name="Verschilrekening">De grootboekrekening voor kleine betaalverschillen.</param>
public sealed record Verwerkingsinstellingen(
    int Drempel = 2,
    decimal Bandbreedte = Leergeheugen.StandaardBandbreedte,
    decimal? Limiet = null,
    int Betalingstermijn = 30,
    bool Adrescontrole = true,
    string? Documentmap = null,
    decimal? Betaalverschil = null,
    string? Verschilrekening = null)
{
    public static Verwerkingsinstellingen Standaard { get; } = new();

    /// <summary>Wat er niet klopt aan deze instellingen; leeg als alles goed is.</summary>
    public IReadOnlyList<string> Controleer()
    {
        var fouten = new List<string>();
        if (Drempel is < 1 or > 10)
        {
            fouten.Add("Automatisch verwerken na 1 tot 10 gelijke keuzes.");
        }

        if (Bandbreedte is < 1 or > 10)
        {
            fouten.Add("De bandbreedte ligt tussen 1 en 10 keer het hoogste bedrag.");
        }

        if (Limiet is < 0)
        {
            fouten.Add("De limiet kan niet negatief zijn.");
        }

        if (Betalingstermijn is < 0 or > 365)
        {
            fouten.Add("De betalingstermijn ligt tussen 0 en 365 dagen.");
        }

        if (Betaalverschil is < 0 or > 100)
        {
            fouten.Add("Een klein betaalverschil is hoogstens € 100.");
        }

        if (Betaalverschil > 0 && string.IsNullOrWhiteSpace(Verschilrekening))
        {
            fouten.Add("Kies de rekening voor kleine betaalverschillen.");
        }

        fouten.AddRange(Facturen.Documentpad.Controleer(Documentmap));
        return fouten;
    }
}

/// <summary>
/// Waar een administratie afwijkt van het kantoor; een lege waarde volgt het kantoor. Bij de limiet betekent leeg
/// ook "geen limiet", dus daar zegt <see cref="LimietAfwijkend"/> of de administratie een eigen keuze heeft.
/// </summary>
public sealed record AfwijkendeInstellingen(
    int? Drempel = null,
    decimal? Bandbreedte = null,
    bool LimietAfwijkend = false,
    decimal? Limiet = null,
    int? Betalingstermijn = null,
    bool? Adrescontrole = null,
    string? Documentmap = null,
    decimal? Betaalverschil = null,
    string? Verschilrekening = null)
{
    public static AfwijkendeInstellingen Geen { get; } = new();

    /// <summary>De instellingen die voor de administratie gelden.</summary>
    public Verwerkingsinstellingen Op(Verwerkingsinstellingen kantoor)
    {
        ArgumentNullException.ThrowIfNull(kantoor);
        return new Verwerkingsinstellingen(
            Drempel ?? kantoor.Drempel,
            Bandbreedte ?? kantoor.Bandbreedte,
            LimietAfwijkend ? Limiet : kantoor.Limiet,
            Betalingstermijn ?? kantoor.Betalingstermijn,
            Adrescontrole ?? kantoor.Adrescontrole,
            string.IsNullOrWhiteSpace(Documentmap) ? kantoor.Documentmap : Documentmap,
            Betaalverschil ?? kantoor.Betaalverschil,
            string.IsNullOrWhiteSpace(Verschilrekening) ? kantoor.Verschilrekening : Verschilrekening);
    }
}
