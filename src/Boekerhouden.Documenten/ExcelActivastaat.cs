using Boekerhouden.Boekhouding.Activa;
using ClosedXML.Excel;

namespace Boekerhouden.Documenten;

/// <summary>
/// Een staat van vaste activa uit Excel (.xlsx) als tabel voor de <see cref="Activastaatlezer"/>: per blad de cellen
/// met hun waarde (bij een formule de laatst berekende waarde, zoals Excel hem bewaarde). Het blad waarop de lezer de
/// meeste activa vindt, telt. Samengevoegde kopcellen staan linksboven in hun gebied; de lezer kijkt daarvoor naar links.
/// </summary>
public static class ExcelActivastaat
{
    /// <summary>Hoogstens zoveel rijen en kolommen per blad: een staat is nooit groter, een vreemd bestand wel.</summary>
    private const int MaxRijen = 5000;
    private const int MaxKolommen = 80;

    public static Ingelezenstaat Lees(Stream inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        XLWorkbook boek;
        try
        {
            boek = new XLWorkbook(inhoud);
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            throw new DocumentException("Dit Excel-bestand is niet te lezen (alleen .xlsx; sla een oud .xls eerst op als .xlsx).", fout);
        }

        using (boek)
        {
            Ingelezenstaat? beste = null;
            foreach (var blad in boek.Worksheets)
            {
                var staat = Activastaatlezer.Lees(Tabel(blad));
                if (beste is null || staat.Regels.Count() > beste.Regels.Count())
                {
                    beste = staat;
                }
            }

            return beste ?? Activastaatlezer.Lees(new Staattabel([]));
        }
    }

    internal static Staattabel Tabel(IXLWorksheet blad)
    {
        ArgumentNullException.ThrowIfNull(blad);
        var gebruikt = blad.RangeUsed();
        if (gebruikt is null)
        {
            return new Staattabel([]);
        }

        var (rijen, kolommen) = (Math.Min(gebruikt.LastRow().RowNumber(), MaxRijen), Math.Min(gebruikt.LastColumn().ColumnNumber(), MaxKolommen));
        var tabel = new List<IReadOnlyList<Staatcel>>(rijen);
        for (var r = 1; r <= rijen; r++)
        {
            var rij = new Staatcel[kolommen];
            for (var c = 1; c <= kolommen; c++)
            {
                rij[c - 1] = Cel(blad.Cell(r, c));
            }

            tabel.Add(rij);
        }

        return new Staattabel(tabel);
    }

    private static XLCellValue Uitgerekend(IXLCell cel)
    {
        try
        {
            return cel.Value;
        }
        catch (Exception fout) when (fout is not OutOfMemoryException)
        {
            // Een formule met verwijzingen naar namen of bladen die er niet zijn: leeg.
            return Blank.Value;
        }
    }

    private static Staatcel Cel(IXLCell cel)
    {
        // Een formule: de waarde die Excel bewaarde; is die er niet (een bestand uit een ander programma), dan rekenen.
        var waarde = cel.HasFormula && !cel.CachedValue.IsBlank ? cel.CachedValue : Uitgerekend(cel);
        return waarde.Type switch
        {
            XLDataType.Number => new Staatcel(null, Math.Round((decimal)waarde.GetNumber(), 2, MidpointRounding.AwayFromZero)),
            XLDataType.DateTime => new Staatcel(null, Datum: DateOnly.FromDateTime(waarde.GetDateTime())),
            XLDataType.Text => new Staatcel(waarde.GetText()),
            _ => Staatcel.Leeg,
        };
    }
}
