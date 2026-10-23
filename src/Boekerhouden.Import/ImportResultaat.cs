using Boekerhouden.Domain;

namespace Boekerhouden.Import;

/// <summary>
/// Uitkomst van het inlezen van een importsjabloon. Bij één of meer fouten is er geen saldibalans:
/// dan wordt er niets geïmporteerd.
/// </summary>
public sealed class ImportResultaat
{
    public ImportResultaat(Saldibalans? saldibalans, IReadOnlyList<ImportMelding> meldingen)
    {
        ArgumentNullException.ThrowIfNull(meldingen);
        if (saldibalans is not null && meldingen.Any(m => m.Ernst == MeldingErnst.Fout))
        {
            throw new ArgumentException("Een resultaat met fouten kan geen saldibalans bevatten.", nameof(saldibalans));
        }

        Saldibalans = saldibalans;
        Meldingen = meldingen;
    }

    public Saldibalans? Saldibalans { get; }

    public IReadOnlyList<ImportMelding> Meldingen { get; }

    public bool IsGeslaagd => Saldibalans is not null;

    public IEnumerable<ImportMelding> Fouten => Meldingen.Where(m => m.Ernst == MeldingErnst.Fout);

    public IEnumerable<ImportMelding> Waarschuwingen => Meldingen.Where(m => m.Ernst == MeldingErnst.Waarschuwing);
}
