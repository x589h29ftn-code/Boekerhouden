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

    /// <summary>De kolommen van het sjabloon, in volgorde; de lezer herkent ze op deze woorden.</summary>
    public static IReadOnlyList<string> Sjabloonkolommen(int jaar) =>
    [
        "Omschrijving", "Groep", "Aanschafdatum", "Aanschafwaarde", "Restwaarde", "Afschrijving % per jaar", $"Afgeschreven t/m 31-12-{jaar}", $"Boekwaarde per 31-12-{jaar}",
        "Activarekening", "Afschrijvingsrekening", "Kostenrekening", "Privégebruik", "Cataloguswaarde", "Notitie",
    ];

    /// <summary>
    /// Het vaste sjabloon voor een staat van vaste activa per 31 december van <paramref name="jaar"/>: één regel per
    /// actief met alles wat het pakket gebruikt, een blad met uitleg en een blad met de rekeningen van de administratie.
    /// Rekeningnummers als tekst (zodat 0200 geen 200 wordt), datums en bedragen opgemaakt.
    /// </summary>
    public static byte[] Sjabloon(int jaar, IEnumerable<(string Nummer, string Naam, string Soort)> rekeningen)
    {
        ArgumentNullException.ThrowIfNull(rekeningen);
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Vaste activa");
        var kop = Sjabloonkolommen(jaar);
        for (var c = 0; c < kop.Count; c++)
        {
            var cel = blad.Cell(1, c + 1);
            cel.Value = kop[c];
            cel.Style.Font.Bold = true;
            cel.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EDF3");
            cel.Style.Alignment.WrapText = true;
        }

        blad.Column(1).Width = 36;
        blad.Column(2).Width = 18;
        blad.Column(3).Width = 14;
        blad.Column(3).Style.DateFormat.Format = "dd-mm-yyyy";
        foreach (var c in new[] { 4, 5, 7, 8, 13 })
        {
            blad.Column(c).Width = 15;
            blad.Column(c).Style.NumberFormat.Format = "#,##0.00";
        }

        blad.Column(6).Width = 12;
        foreach (var c in new[] { 9, 10, 11 })
        {
            blad.Column(c).Width = 15;
            blad.Column(c).Style.NumberFormat.Format = "@";
        }

        blad.Column(12).Width = 16;
        blad.Column(14).Width = 40;
        blad.Row(1).Height = 32;
        blad.SheetView.FreezeRows(1);
        var privegebruik = blad.Range(2, 12, 1000, 12).CreateDataValidation();
        privegebruik.List("\"ja,ja werkelijk,nee,bestelauto,rittenregistratie,verklaring\"");
        privegebruik.IgnoreBlanks = true;

        var uitleg = boek.AddWorksheet("Uitleg");
        string[][] regels =
        [
            ["Kolom", "Wat", "Verplicht"],
            ["Omschrijving", "Naam van het actief, bijv. \"Bestelbus VW Caddy\".", "ja"],
            ["Groep", "Bijv. Inventaris, Vervoermiddelen, Machines; gelijke groepen krijgen dezelfde rekeningen.", "nee"],
            ["Aanschafdatum", "De datum van aanschaf (dd-mm-jjjj); leeg = geschat uit wat al is afgeschreven.", "liefst"],
            ["Aanschafwaarde", "Exclusief btw, zoals geactiveerd.", "ja"],
            ["Restwaarde", "Leeg = 0. Gelijk aan de aanschafwaarde = niet afschrijven (bijv. kunst, grond).", "nee"],
            ["Afschrijving % per jaar", "Bijv. 20 voor 5 jaar; lineair over aanschafwaarde min restwaarde. Leeg = niet afschrijven.", "ja"],
            [$"Afgeschreven t/m 31-12-{jaar}", "Wat er tot en met het eind van het jaar al is afgeschreven.", "ja"],
            [$"Boekwaarde per 31-12-{jaar}", "Ter controle: aanschafwaarde min afgeschreven.", "nee"],
            ["Activarekening", "Het nummer van de balansrekening voor de aanschafwaarde (zie het blad Rekeningen). Leeg = kiezen bij het inlezen.", "nee"],
            ["Afschrijvingsrekening", "De balansrekening voor de cumulatieve afschrijving.", "nee"],
            ["Kostenrekening", "De rekening voor de afschrijvingskosten.", "nee"],
            ["Privégebruik", "Alleen bij een auto: ja (forfait), ja werkelijk, nee, bestelauto, rittenregistratie of verklaring.", "nee"],
            ["Cataloguswaarde", "Bij privégebruik volgens het forfait: de cataloguswaarde inclusief btw en bpm.", "nee"],
            ["Notitie", "Vrije tekst bij het actief.", "nee"],
        ];
        for (var r = 0; r < regels.Length; r++)
        {
            for (var c = 0; c < regels[r].Length; c++)
            {
                uitleg.Cell(r + 1, c + 1).Value = regels[r][c];
            }
        }

        uitleg.Row(1).Style.Font.Bold = true;
        uitleg.Column(1).Width = 28;
        uitleg.Column(2).Width = 100;
        uitleg.Column(3).Width = 10;

        var lijst = boek.AddWorksheet("Rekeningen");
        lijst.Cell(1, 1).Value = "Nummer";
        lijst.Cell(1, 2).Value = "Naam";
        lijst.Cell(1, 3).Value = "Soort";
        lijst.Row(1).Style.Font.Bold = true;
        lijst.Column(1).Style.NumberFormat.Format = "@";
        var rij = 2;
        foreach (var (nummer, naam, soort) in rekeningen)
        {
            lijst.Cell(rij, 1).SetValue(nummer);
            lijst.Cell(rij, 2).Value = naam;
            lijst.Cell(rij, 3).Value = soort;
            rij++;
        }

        lijst.Column(2).Width = 40;
        lijst.Column(3).Width = 18;
        blad.SetTabActive();
        using var stream = new MemoryStream();
        boek.SaveAs(stream);
        return stream.ToArray();
    }
}
