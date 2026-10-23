using System.Globalization;
using ClosedXML.Excel;

namespace Boekerhouden.Import;

/// <summary>
/// Leest het werkblad "Saldibalans" (of anders het eerste werkblad) uit een Excel-bestand (.xlsx/.xlsm).
/// </summary>
internal static class ExcelTabelLezer
{
    public static IReadOnlyList<Rij> Lees(Stream stream)
    {
        using var werkmap = new XLWorkbook(stream);
        var blad = werkmap.Worksheets.FirstOrDefault(w =>
                       string.Equals(w.Name, ImportSjabloon.WerkbladNaam, StringComparison.OrdinalIgnoreCase))
                   ?? werkmap.Worksheets.First();

        var laatsteKolom = blad.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rijen = new List<Rij>();
        foreach (var rij in blad.RowsUsed())
        {
            var cellen = new List<Cel>(laatsteKolom);
            for (var kolom = 1; kolom <= laatsteKolom; kolom++)
            {
                cellen.Add(LeesCel(rij.Cell(kolom)));
            }

            rijen.Add(new Rij(rij.RowNumber(), cellen));
        }

        return rijen;
    }

    private static Cel LeesCel(IXLCell cel)
    {
        var waarde = cel.HasFormula ? cel.CachedValue : cel.Value;
        if (cel.HasFormula && waarde.IsBlank)
        {
            // Bestand zonder opgeslagen uitkomst van de formule: laat ClosedXML rekenen.
            waarde = cel.Value;
        }

        return waarde.Type switch
        {
            XLDataType.Blank => Cel.Leeg,
            XLDataType.Number => NaarGetalCel(waarde.GetNumber()),
            XLDataType.Text => new Cel(waarde.GetText().Trim()),
            XLDataType.Boolean => new Cel(waarde.GetBoolean() ? "WAAR" : "ONWAAR"),
            XLDataType.DateTime => new Cel(waarde.GetDateTime().ToString("d-M-yyyy", CultureInfo.InvariantCulture)),
            XLDataType.TimeSpan => new Cel(waarde.GetTimeSpan().ToString()),
            XLDataType.Error => new Cel("#" + waarde.GetError()),
            _ => new Cel(cel.GetFormattedString().Trim()),
        };
    }

    private static Cel NaarGetalCel(double getal)
    {
        if (double.IsNaN(getal) || double.IsInfinity(getal) || Math.Abs(getal) > 1e15)
        {
            return new Cel(getal.ToString(CultureInfo.InvariantCulture));
        }

        // Convert.ToDecimal rondt af op 15 significante cijfers, zodat 0,1 + 0,2 gewoon 0,3 wordt.
        var bedrag = Convert.ToDecimal(getal);
        return new Cel(bedrag.ToString(CultureInfo.InvariantCulture), bedrag);
    }
}
