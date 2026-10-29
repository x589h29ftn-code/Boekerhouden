using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Domain;

namespace Boekerhouden.Administratie.Components.Gedeeld;

/// <summary>
/// De verwerkingsinstellingen zoals ze op het scherm staan (als tekst). Op kantoorniveau is alles ingevuld; in een
/// administratie betekent leeg "zoals het kantoor".
/// </summary>
public sealed class InstellingenInvoer
{
    public const string Kantoor = "";
    public const string GeenLimiet = "geen";
    public const string EigenLimiet = "eigen";
    public const string Aan = "aan";
    public const string Uit = "uit";

    public string? Drempel { get; set; }

    public string? Bandbreedte { get; set; }

    /// <summary>Alleen in een administratie: <see cref="Kantoor"/>, <see cref="GeenLimiet"/> of <see cref="EigenLimiet"/>.</summary>
    public string LimietKeuze { get; set; } = Kantoor;

    public string? Limiet { get; set; }

    public string? Betalingstermijn { get; set; }

    /// <summary><see cref="Kantoor"/> (alleen in een administratie), <see cref="Aan"/> of <see cref="Uit"/>.</summary>
    public string Adrescontrole { get; set; } = Aan;

    public string? Documentmap { get; set; }

    public static InstellingenInvoer Van(Verwerkingsinstellingen i)
    {
        ArgumentNullException.ThrowIfNull(i);
        return new()
        {
            Drempel = i.Drempel.ToString(Nl),
            Bandbreedte = i.Bandbreedte.ToString("0.##", Nl),
            Limiet = i.Limiet is { } l ? Bedragen.Getal(l) : null,
            Betalingstermijn = i.Betalingstermijn.ToString(Nl),
            Adrescontrole = i.Adrescontrole ? Aan : Uit,
            Documentmap = i.Documentmap,
        };
    }

    public static InstellingenInvoer Van(AfwijkendeInstellingen a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new()
        {
            Drempel = a.Drempel?.ToString(Nl),
            Bandbreedte = a.Bandbreedte?.ToString("0.##", Nl),
            LimietKeuze = !a.LimietAfwijkend ? Kantoor : a.Limiet is null ? GeenLimiet : EigenLimiet,
            Limiet = a.Limiet is { } l ? Bedragen.Getal(l) : null,
            Betalingstermijn = a.Betalingstermijn?.ToString(Nl),
            Adrescontrole = a.Adrescontrole switch { null => Kantoor, true => Aan, false => Uit },
            Documentmap = a.Documentmap,
        };
    }

    /// <summary>Als kantoorinstellingen; een leeg veld wordt de standaard, een onleesbaar veld een fout.</summary>
    public Verwerkingsinstellingen NaarKantoor(List<string> fouten)
    {
        ArgumentNullException.ThrowIfNull(fouten);
        var standaard = Verwerkingsinstellingen.Standaard;
        return new Verwerkingsinstellingen(
            Geheel(Drempel, "De drempel", fouten) ?? standaard.Drempel,
            Getal(Bandbreedte, "De bandbreedte", fouten) ?? standaard.Bandbreedte,
            Getal(Limiet, "De limiet", fouten),
            Geheel(Betalingstermijn, "De betalingstermijn", fouten) ?? standaard.Betalingstermijn,
            Adrescontrole != Uit,
            string.IsNullOrWhiteSpace(Documentmap) ? null : Documentmap.Trim());
    }

    /// <summary>Als afwijkingen van het kantoor; een leeg veld volgt het kantoor.</summary>
    public AfwijkendeInstellingen NaarAfwijkend(List<string> fouten)
    {
        ArgumentNullException.ThrowIfNull(fouten);
        var eigenLimiet = LimietKeuze == EigenLimiet ? Getal(Limiet, "De limiet", fouten) : null;
        if (LimietKeuze == EigenLimiet && eigenLimiet is null && fouten.Count == 0)
        {
            fouten.Add("Vul de eigen limiet in.");
        }

        return new AfwijkendeInstellingen(
            Geheel(Drempel, "De drempel", fouten),
            Getal(Bandbreedte, "De bandbreedte", fouten),
            LimietKeuze != Kantoor,
            eigenLimiet,
            Geheel(Betalingstermijn, "De betalingstermijn", fouten),
            Adrescontrole switch { Aan => true, Uit => false, _ => null },
            string.IsNullOrWhiteSpace(Documentmap) ? null : Documentmap.Trim());
    }

    private static readonly System.Globalization.CultureInfo Nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    private static int? Geheel(string? tekst, string naam, List<string> fouten)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return null;
        }

        if (int.TryParse(tekst.Trim(), System.Globalization.NumberStyles.Integer, Nl, out var waarde))
        {
            return waarde;
        }

        fouten.Add($"{naam} is geen geheel getal.");
        return null;
    }

    private static decimal? Getal(string? tekst, string naam, List<string> fouten)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return null;
        }

        if (BedragParser.TryParse(tekst, out var waarde))
        {
            return waarde;
        }

        fouten.Add($"{naam} is geen getal.");
        return null;
    }
}
