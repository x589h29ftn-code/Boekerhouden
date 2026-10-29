using Boekerhouden.Boekhouding.Btw;

namespace Boekerhouden.Boekhouding.Herkenning;

public enum Herkenningsbron
{
    /// <summary>Een e-factuur (UBL): exact, geen herkenning nodig.</summary>
    EFactuur,

    /// <summary>De tekstlaag van een PDF.</summary>
    Tekst,

    /// <summary>Tekstherkenning van een scan of foto.</summary>
    Ocr,
}

/// <summary>Btw per tarief zoals op de factuur.</summary>
public sealed record Btwregel(decimal Percentage, decimal? Grondslag, decimal Btw);

/// <summary>Een regel van de factuur (alleen bij e-facturen betrouwbaar).</summary>
public sealed record HerkendeRegel(string Omschrijving, decimal Bedrag, decimal? Percentage);

/// <summary>
/// Wat er van een inkoopfactuur is herkend. Alles is optioneel: bij een slechte scan kan veel ontbreken. Bedragen
/// zijn zoals op de factuur (positief), bij een creditfactuur negatief.
/// </summary>
/// <param name="OpEigenNaam">
/// Staat de naam van de onderneming op de factuur? Nodig voor de aftrek van btw. Leeg als dat niet is nagegaan.
/// </param>
/// <param name="Postcodes">De Nederlandse postcodes op de factuur (genormaliseerd), om een ander adres op te merken.</param>
/// <param name="Verlegd">
/// Op de factuur staat dat de btw is verlegd ("btw verlegd", "reverse charge"): de ondernemer berekent de btw zelf.
/// </param>
public sealed record HerkendeFactuur(
    Herkenningsbron Bron,
    string? Naam = null,
    string? BtwNummer = null,
    string? KvkNummer = null,
    string? Iban = null,
    string? Factuurnummer = null,
    DateOnly? Factuurdatum = null,
    DateOnly? Vervaldatum = null,
    decimal? TotaalExclusief = null,
    decimal? TotaalInclusief = null,
    IReadOnlyList<Btwregel>? Btw = null,
    IReadOnlyList<HerkendeRegel>? Regels = null,
    string? Betalingskenmerk = null,
    string? Omschrijving = null,
    bool? OpEigenNaam = null,
    IReadOnlyList<string>? Postcodes = null,
    bool Verlegd = false)
{
    public IReadOnlyList<Btwregel> Btwregels => Btw ?? [];

    public decimal TotaalBtw => Btwregels.Sum(b => b.Btw);

    /// <summary>Het enige btw-tarief op de factuur, of leeg (geen of meerdere tarieven).</summary>
    public decimal? EnigTarief => Btwregels.Select(b => b.Percentage).Distinct().ToList() is [var enig] ? enig : null;

    /// <summary>Wat ontbreekt of niet klopt; leeg als de factuur compleet en sluitend is.</summary>
    public IReadOnlyList<string> Controleer()
    {
        var punten = new List<string>();
        if (Factuurnummer is null)
        {
            punten.Add("Factuurnummer niet gevonden.");
        }

        if (Factuurdatum is null)
        {
            punten.Add("Factuurdatum niet gevonden.");
        }

        if (TotaalInclusief is null)
        {
            punten.Add("Totaalbedrag niet gevonden.");
        }

        if (TotaalExclusief is { } excl && TotaalInclusief is { } incl && Math.Abs(excl + TotaalBtw - incl) > 0.01m * Math.Max(1, Btwregels.Count))
        {
            punten.Add($"Exclusief plus btw ({excl + TotaalBtw:0.00}) is niet het totaal ({incl:0.00}).");
        }

        foreach (var regel in Btwregels.Where(b => b.Grondslag is { } g && !BtwBerekening.Klopt(g, b.Btw, b.Percentage, Math.Max(1, Regels?.Count ?? 1))))
        {
            punten.Add($"Btw {regel.Percentage:0.##}% over {regel.Grondslag:0.00} is niet {regel.Btw:0.00}.");
        }

        return punten;
    }
}

/// <summary>
/// Een motor die een document (PDF, afbeelding of e-factuur) herkent. Er kunnen meerdere motoren zijn (e-factuur,
/// PDF-tekst, OCR, later eventueel een AI-model); de eerste die iets herkent, wint.
/// </summary>
public interface IFactuurHerkenner
{
    /// <summary>Kan deze motor dit soort bestand lezen (op naam en inhoudstype)?</summary>
    bool Kan(string bestandsnaam, string? inhoudstype);

    /// <summary>De herkende factuur, of leeg als de motor er niets mee kan.</summary>
    Task<HerkendeFactuur?> HerkenAsync(Stream inhoud, string bestandsnaam, CancellationToken annulering = default);
}
