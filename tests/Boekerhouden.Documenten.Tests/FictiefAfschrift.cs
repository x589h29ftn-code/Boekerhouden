using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Een mutatie op het verzonnen afschrift: datum (dd-MM), code, tekstregels en het bedrag (af negatief).</summary>
public sealed record Afschriftregel(string Datum, string Code, IReadOnlyList<string> Tekst, decimal Bedrag);

/// <summary>
/// Een verzonnen rekeningafschrift als PDF in de opmaak van een zakelijk Rabobank-afschrift (kop met vorig en nieuw
/// saldo, kolommen voor debet en credit, mutaties over meer regels en pagina's). Alleen voor de tests.
/// </summary>
public static class FictiefAfschrift
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public const string Eigen = "NL20INGB0001234567";

    public static readonly string Kenmerk = Boekhouding.Verkoop.Betalingskenmerk.Maak("20260001");

    /// <summary>Tien mutaties over twee pagina's: incasso, betalingen, een naam op de tweede regel, een pinbetaling zonder tegenrekening, een kenmerk.</summary>
    public static readonly Afschriftregel[] Oktober =
    [
        new("01-10", "ei", ["NL63 TRIO 0212 3456 78 Belnet B.V.", "Kenmerk machtiging / incassant ID:", "BN-2026-0311", "Transactiereferentie:", "AD2609280809"], -24.99m),
        new("01-10", "cb", ["NL44 RABO 0123 4567 89 J. de Vries", "Factuur 20260001"], 1210m),
        new("02-10", "cb", ["NL36 INGB 0007 6543 21", "Kantoorhandel Jansen", "Betaling inkoop"], -199.65m),
        new("02-10", "ec", ["WEBWINKEL *ADS2164", "Pas: 4xxxx4382 Terminal: 99999999"], -4.58m),
        new("03-10", "cb", ["NL44 RABO 0123 4567 89 J. de Vries", Kenmerk], 605m),
        new("03-10", "db", ["NL20 INGB 0001 2345 67 Bank", "Kosten", "Periode 01-09-2026 t/m 30-09-2026"], -12.5m),
        new("05-10", "tb", ["NL44 RABO 0123 4567 89 Spaarrekening"], -1000m),
        new("06-10", "cb", ["NL36 INGB 0007 6543 21 Kantoorhandel Jansen", "Retour 1323/ 2026"], 25m),
        new("30-10", "bg", ["NL63 TRIO 0212 3456 78 Belnet B.V.", "Declaratie 4-10-26"], -2000m),
        new("31-10", "cb", ["NL44 RABO 0123 4567 89 J. de Vries", "Voorschot"], 1.234m * 1000m),
    ];

    /// <summary>Het afschrift van oktober 2026 als PDF; met een ander nieuw saldo sluit het niet.</summary>
    public static byte[] OktoberPdf(decimal? nieuwSaldo = null) =>
        Pdf(Eigen, new(2026, 9, 30), 5000m, new(2026, 10, 31), nieuwSaldo ?? 5000m + Oktober.Sum(m => m.Bedrag), Oktober);


    public static byte[] Pdf(string iban, DateOnly vorigeDatum, decimal vorigSaldo, DateOnly nieuweDatum, decimal nieuwSaldo, IReadOnlyList<Afschriftregel> mutaties, int perPagina = 8)
    {
        ArgumentNullException.ThrowIfNull(iban);
        ArgumentNullException.ThrowIfNull(mutaties);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Courier);
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
        var groepen = mutaties.Chunk(perPagina).ToList();
        for (var p = 0; p < groepen.Count; p++)
        {
            var pagina = builder.AddPage(PageSize.A4);
            var y = 800.0;
            void Tekst(double x, string tekst, PdfDocumentBuilder.AddedFont f, double grootte = 8) => pagina.AddText(tekst, grootte, new PdfPoint(x, y), f);

            Tekst(48, "Rekeningafschrift", helvetica, 14);
            y -= 30;
            Tekst(66, "Bakkerij Voorbeeld B.V.", font);
            Tekst(392, "Datum afschrift", helvetica, 7);
            Tekst(470, "Volgnummer", helvetica, 7);
            Tekst(520, "Bladnummer", helvetica, 7);
            y -= 12;
            Tekst(392, nieuweDatum.AddDays(1).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), font);
            Tekst(470, "0042", font);
            Tekst(520, $"{p + 1:0000}", font);
            if (p == 0)
            {
                y -= 20;
                Tekst(392, "Vorig afschrift", helvetica, 7);
                Tekst(540, "Vorig saldo", helvetica, 7);
                y -= 12;
                Tekst(392, vorigeDatum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), font);
                Tekst(520, Bedrag(Math.Abs(vorigSaldo)), font);
                Tekst(575, vorigSaldo < 0 ? "D" : "CR", font);
                y -= 20;
                Tekst(392, "Nieuw afschrift", helvetica, 7);
                Tekst(540, "Nieuw saldo", helvetica, 7);
                y -= 12;
                Tekst(392, nieuweDatum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), font);
                Tekst(520, Bedrag(Math.Abs(nieuwSaldo)), font);
                Tekst(575, nieuwSaldo < 0 ? "D" : "CR", font);
            }

            y -= 30;
            Tekst(48, "IBAN", helvetica, 7);
            Tekst(66, "Rekeningnummer", helvetica, 7);
            y -= 12;
            Tekst(48, string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4)))) + "  EUR", font);
            y -= 20;
            Tekst(48, "Rente", helvetica, 7);
            Tekst(82, "Code", helvetica, 7);
            Tekst(105, "Tegenrekening/naam/omschrijving", helvetica, 7);
            Tekst(419, "Mutaties af (debet)", helvetica, 7);
            Tekst(518, "Mutaties bij (credit)", helvetica, 7);
            y -= 9;
            Tekst(48, "Datum", helvetica, 7);
            foreach (var m in groepen[p])
            {
                y -= 9;
                Tekst(48, m.Datum, font);
                Tekst(85, m.Code, font);
                Tekst(105, m.Tekst[0], font);
                Tekst(m.Bedrag < 0 ? 440 : 545, Bedrag(Math.Abs(m.Bedrag)), font);
                foreach (var regel in m.Tekst.Skip(1))
                {
                    y -= 9;
                    Tekst(177, regel, font);
                }
            }

            y -= 40;
            Tekst(48, "ba = betaalautomaat   cb = crediteurenbetaling   ei = Euro-incasso", helvetica, 7);
        }

        return builder.Build();
    }

    private static string Bedrag(decimal bedrag) => bedrag.ToString("#,0.00", Nl);
}
