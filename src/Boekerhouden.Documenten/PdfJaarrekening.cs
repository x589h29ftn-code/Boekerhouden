using Boekerhouden.Boekhouding.Beginbalans;

namespace Boekerhouden.Documenten;

/// <summary>De balans uit een jaarrekening als PDF (met tekst), voor de beginbalans van het volgende jaar.</summary>
public static class PdfJaarrekening
{
    public static Jaarrekeningbalans? Lees(byte[] pdf) => Jaarrekeninglezer.Lees(PdfTekst.Regels(PdfTekst.Woorden(pdf)));
}
