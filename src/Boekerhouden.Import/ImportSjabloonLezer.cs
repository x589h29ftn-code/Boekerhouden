using Boekerhouden.Domain;
using DocumentFormat.OpenXml.Packaging;

namespace Boekerhouden.Import;

/// <summary>
/// Leest een saldibalans in uit het standaard importsjabloon (.xlsx of .csv) en controleert die.
/// Alleen als alle controles slagen (onder meer: saldo nihil) bevat het resultaat een <see cref="Saldibalans"/>.
/// </summary>
public static class ImportSjabloonLezer
{
    public static ImportResultaat Lees(string pad, ImportOpties? opties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pad);
        using var stream = File.OpenRead(pad);
        return Lees(stream, Path.GetFileName(pad), opties);
    }

    public static ImportResultaat Lees(Stream stream, string bestandsnaam, ImportOpties? opties = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(bestandsnaam);

        var extensie = Path.GetExtension(bestandsnaam).ToUpperInvariant();
        IReadOnlyList<Rij> rijen;
        try
        {
            switch (extensie)
            {
                case ".XLSX" or ".XLSM":
                    rijen = ExcelTabelLezer.Lees(stream);
                    break;
                case ".CSV" or ".TXT":
                    rijen = CsvTabelLezer.Lees(stream);
                    break;
                default:
                    return Mislukt($"Bestandstype '{extensie.ToLowerInvariant()}' wordt niet ondersteund. Gebruik het importsjabloon als .xlsx of .csv.");
            }
        }
        catch (Exception fout) when (fout is InvalidDataException or IOException or FormatException or InvalidOperationException or OpenXmlPackageException)
        {
            return Mislukt($"Het bestand kan niet worden gelezen ({fout.Message}). Is het een geldig Excel- of CSV-bestand?");
        }

        return SjabloonVerwerker.Verwerk(rijen, opties ?? ImportOpties.Standaard);
    }

    private static ImportResultaat Mislukt(string tekst) =>
        new(null, [new ImportMelding(ImportCodes.Bestand, MeldingErnst.Fout, tekst)]);
}
