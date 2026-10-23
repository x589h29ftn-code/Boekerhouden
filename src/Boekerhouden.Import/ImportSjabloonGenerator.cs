using ClosedXML.Excel;

namespace Boekerhouden.Import;

/// <summary>Maakt een leeg standaard importsjabloon (.xlsx) dat de gebruiker kan invullen.</summary>
public static class ImportSjabloonGenerator
{
    private const int EersteDataRij = ImportSjabloon.KopRij + 1;
    private const int LaatsteDataRij = 100_000;

    public static void Schrijf(string pad, int? boekjaar = null, string? klant = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pad);
        using var stream = File.Create(pad);
        Schrijf(stream, boekjaar, klant);
    }

    public static void Schrijf(Stream doel, int? boekjaar = null, string? klant = null)
    {
        ArgumentNullException.ThrowIfNull(doel);

        using var werkmap = new XLWorkbook();
        MaakSaldibalansBlad(werkmap.Worksheets.Add(ImportSjabloon.WerkbladNaam), boekjaar, klant);
        MaakToelichtingBlad(werkmap.Worksheets.Add(ImportSjabloon.ToelichtingWerkbladNaam));
        werkmap.Worksheet(ImportSjabloon.WerkbladNaam).SetTabActive();
        werkmap.SaveAs(doel);
    }

    private static void MaakSaldibalansBlad(IXLWorksheet blad, int? boekjaar, string? klant)
    {
        var invoerKleur = XLColor.FromHtml("#FFF2CC");
        var kopKleur = XLColor.FromHtml("#DDEBF7");

        blad.Cell(1, 1).Value = ImportSjabloon.LabelBoekjaar;
        blad.Cell(1, 2).Value = boekjaar is { } jaar ? jaar : Blank.Value;
        blad.Cell(2, 1).Value = $"{ImportSjabloon.LabelKlant} (optioneel)";
        blad.Cell(2, 2).Value = klant ?? string.Empty;
        blad.Range(1, 1, 2, 1).Style.Font.Bold = true;
        blad.Range(1, 2, 2, 2).Style.Fill.BackgroundColor = invoerKleur;

        // Controle op saldo nihil, rechts van de invoer zodat die de tabel niet stoort.
        var bedragen = $"C{EersteDataRij}:C{LaatsteDataRij}";
        blad.Cell(1, 5).Value = "Totaal bedragen";
        blad.Cell(1, 6).FormulaA1 = $"ROUND(SUM({bedragen}),2)";
        blad.Cell(1, 6).Style.NumberFormat.Format = "#,##0.00;-#,##0.00";
        blad.Cell(2, 5).Value = "Controle";
        blad.Cell(2, 6).FormulaA1 = "IF(F1=0,\"Saldo nihil: in orde\",\"Saldo niet nihil: controleer de bedragen\")";
        blad.Range(1, 5, 2, 5).Style.Font.Bold = true;

        string[] koppen =
        [
            ImportSjabloon.KolomGrootboeknummer,
            ImportSjabloon.KolomOmschrijving,
            ImportSjabloon.KolomBedrag,
            $"{ImportSjabloon.KolomRgsCode} (optioneel)",
        ];
        for (var kolom = 0; kolom < koppen.Length; kolom++)
        {
            blad.Cell(ImportSjabloon.KopRij, kolom + 1).Value = koppen[kolom];
        }

        var kopBereik = blad.Range(ImportSjabloon.KopRij, 1, ImportSjabloon.KopRij, koppen.Length);
        kopBereik.Style.Font.Bold = true;
        kopBereik.Style.Fill.BackgroundColor = kopKleur;
        kopBereik.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        // Opmaak per kolom (niet per cel, dat maakt het bestand groot en traag).
        // Grootboeknummers als tekst, zodat voorloopnullen (0100) behouden blijven.
        blad.Column(1).Style.NumberFormat.Format = "@";
        blad.Column(3).Style.NumberFormat.Format = "#,##0.00;-#,##0.00";

        blad.Column(1).Width = 18;
        blad.Column(2).Width = 45;
        blad.Column(3).Width = 16;
        blad.Column(4).Width = 22;
        blad.Column(5).Width = 16;
        blad.Column(6).Width = 40;
        blad.SheetView.FreezeRows(ImportSjabloon.KopRij);
    }

    private static void MaakToelichtingBlad(IXLWorksheet blad)
    {
        string[] regels =
        [
            "Importsjabloon saldibalans",
            "",
            "1. Vul in cel B1 van het werkblad 'Saldibalans' het boekjaar in, bijvoorbeeld 2025.",
            "2. Vul eventueel in cel B2 de klant in (ter controle).",
            "3. Vul vanaf rij 5 per grootboekrekening het grootboeknummer, de omschrijving en het bedrag in.",
            "4. Bedrag = saldo per einde boekjaar. Debet is positief, credit is negatief (met minteken).",
            "5. De kolom RGS-code mag leeg blijven. Is hij ingevuld, dan kan de rekening automatisch worden gerubriceerd.",
            "",
            "Het bestand wordt alleen geïmporteerd als:",
            "- het boekjaar is ingevuld;",
            "- elke regel een grootboeknummer en een omschrijving heeft;",
            "- elk grootboeknummer maar één keer voorkomt;",
            "- alle bedragen geldige getallen zijn met hoogstens 2 decimalen;",
            "- het totaal van alle bedragen precies 0,00 is (saldo nihil). Zie de controle in cel F1/F2.",
            "",
            "Hetzelfde sjabloon kan worden gebruikt voor vorige boekjaren (vergelijkende cijfers en meerjarenoverzicht).",
        ];
        for (var i = 0; i < regels.Length; i++)
        {
            blad.Cell(i + 1, 1).Value = regels[i];
        }

        blad.Cell(1, 1).Style.Font.Bold = true;
        blad.Cell(1, 1).Style.Font.FontSize = 14;
        blad.Column(1).Width = 110;
    }
}
