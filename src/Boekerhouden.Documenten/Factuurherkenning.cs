using Boekerhouden.Boekhouding.Herkenning;

namespace Boekerhouden.Documenten;

/// <summary>
/// Gegevens van de eigen administratie. IBAN's en btw-nummers staan soms ook op de factuur, maar zijn niet van de
/// leverancier; met de namen wordt nagegaan of de factuur op naam van de onderneming staat.
/// </summary>
public sealed record EigenGegevens(IReadOnlyList<string> Ibans, IReadOnlyList<string> BtwNummers, IReadOnlyList<string>? Namen = null)
{
    public static EigenGegevens Geen { get; } = new([], []);
}

/// <summary>Een e-factuur (UBL) als los XML-bestand.</summary>
public sealed class UblHerkenner : IFactuurHerkenner
{
    public bool Kan(string bestandsnaam, string? inhoudstype) =>
        Path.GetExtension(bestandsnaam).Equals(".xml", StringComparison.OrdinalIgnoreCase) || inhoudstype is "application/xml" or "text/xml";

    public Task<HerkendeFactuur?> HerkenAsync(Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        return Task.FromResult(UblFactuurLezer.IsUbl(inhoud) ? UblFactuurLezer.Lees(inhoud) : null);
    }
}

/// <summary>
/// Een PDF: eerst een ingesloten e-factuur (UBL als bijlage in de PDF), anders de tekstlaag met de tekstanalyse.
/// Een PDF zonder tekst (een scan) levert niets op; die is voor de OCR-motor.
/// </summary>
public sealed class PdfFactuurHerkenner(EigenGegevens eigen) : IFactuurHerkenner
{
    public bool Kan(string bestandsnaam, string? inhoudstype) =>
        Path.GetExtension(bestandsnaam).Equals(".pdf", StringComparison.OrdinalIgnoreCase) || inhoudstype is "application/pdf";

    public Task<HerkendeFactuur?> HerkenAsync(Stream inhoud, string bestandsnaam, CancellationToken annulering = default)
    {
        var pdf = PdfTekst.Lees(inhoud);
        foreach (var bijlage in pdf.Bijlagen.Where(b => b.Naam.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var xml = new MemoryStream(bijlage.Inhoud);
            if (UblFactuurLezer.IsUbl(xml))
            {
                return Task.FromResult<HerkendeFactuur?>(UblFactuurLezer.Lees(xml));
            }
        }

        return Task.FromResult(pdf.IsScan ? null : Factuurtekst.Analyseer(pdf.Tekst, Herkenningsbron.Tekst, eigen.Ibans, eigen.BtwNummers, eigen.Namen));
    }
}

/// <summary>Het resultaat van de herkenning: de factuur, of waarom er niets herkend is.</summary>
public sealed record Herkenningsresultaat(HerkendeFactuur? Factuur, string? Melding);

/// <summary>Probeert de motoren op volgorde; de eerste die iets herkent, wint.</summary>
public sealed class Factuurherkenning(IEnumerable<IFactuurHerkenner> motoren)
{
    public static Factuurherkenning Standaard(EigenGegevens eigen) => new([new UblHerkenner(), new PdfFactuurHerkenner(eigen)]);

    public async Task<Herkenningsresultaat> HerkenAsync(Stream inhoud, string bestandsnaam, string? inhoudstype = null, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var geschikt = motoren.Where(m => m.Kan(bestandsnaam, inhoudstype)).ToList();
        if (geschikt.Count == 0)
        {
            return new(null, "Dit soort bestand wordt (nog) niet herkend; lever een PDF of e-factuur (XML) aan.");
        }

        using var kopie = new MemoryStream();
        await inhoud.CopyToAsync(kopie, annulering).ConfigureAwait(false);
        foreach (var motor in geschikt)
        {
            kopie.Position = 0;
            try
            {
                if (await motor.HerkenAsync(kopie, bestandsnaam, annulering).ConfigureAwait(false) is { } factuur)
                {
                    return new(factuur, null);
                }
            }
            catch (Exception fout) when (fout is DocumentException or FormatException)
            {
                return new(null, fout.Message);
            }
        }

        return new(null, "Geen tekst gevonden: dit lijkt een scan. Scans worden herkend zodra de OCR-motor er is.");
    }
}
