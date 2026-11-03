using System.Globalization;
using System.Text;
using Boekerhouden.Boekhouding.Bank;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Test die alleen draait als BOEKERHOUDEN_BANKPDF naar een echt PDF-afschrift wijst (dat staat niet in git).</summary>
internal sealed class EchtAfschriftFactAttribute : FactAttribute
{
    public EchtAfschriftFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BOEKERHOUDEN_BANKPDF") is not { Length: > 0 } pad || !File.Exists(pad))
        {
            Skip = "Zet BOEKERHOUDEN_BANKPDF op een PDF-rekeningafschrift om deze test te draaien.";
        }
    }
}

/// <summary>Een rekeningafschrift als PDF omzetten naar MT940: een verzonnen afschrift in de opmaak van de Rabobank.</summary>
public sealed class PdfBankafschriftTests
{
    private const string Eigen = FictiefAfschrift.Eigen;

    private static string Kenmerk => FictiefAfschrift.Kenmerk;

    private static Afschriftregel[] Mutaties => FictiefAfschrift.Oktober;

    private static byte[] Pdf(decimal? nieuwSaldo = null) => FictiefAfschrift.OktoberPdf(nieuwSaldo);

    [Fact]
    public void Een_pdf_afschrift_wordt_gelezen_met_kop_kolommen_en_regels_over_twee_paginas()
    {
        var afschrift = PdfBankafschrift.Lees(Pdf());

        Assert.Equal((Eigen, "0042", new DateOnly(2026, 9, 30), 5000m, new DateOnly(2026, 10, 31)), (afschrift.Iban, afschrift.Nummer, afschrift.Begindatum, afschrift.Beginsaldo, afschrift.Einddatum));
        Assert.True(afschrift.SluitAan);
        Assert.Equal(Mutaties.Select(m => m.Bedrag), afschrift.Mutaties.Select(m => m.Bedrag));
        Assert.Equal(new DateOnly(2026, 10, 1), afschrift.Mutaties[0].Datum);

        var incasso = afschrift.Mutaties[0];
        Assert.Equal(("NL63TRIO0212345678", "Belnet B.V.", "AD2609280809"), (incasso.TegenIban, incasso.TegenNaam, incasso.EndToEndId));
        Assert.Equal("Kenmerk machtiging / incassant ID: BN-2026-0311", incasso.Omschrijving);
        Assert.Equal(("NL36INGB0007654321", "Kantoorhandel Jansen", "Betaling inkoop"), (afschrift.Mutaties[2].TegenIban, afschrift.Mutaties[2].TegenNaam, afschrift.Mutaties[2].Omschrijving));
        var pin = afschrift.Mutaties[3];
        Assert.Equal((null, null), (pin.TegenIban, pin.TegenNaam));
        Assert.StartsWith("WEBWINKEL *ADS2164 Pas:", pin.Omschrijving, StringComparison.Ordinal);
        Assert.Equal(Kenmerk, afschrift.Mutaties[4].Betalingskenmerk);
        Assert.Equal("Kosten Periode 01-09-2026 t/m 30-09-2026", afschrift.Mutaties[5].Omschrijving);
    }

    [Fact]
    public void Als_mt940_komt_het_afschrift_hetzelfde_terug()
    {
        var afschrift = PdfBankafschrift.Lees(Pdf());

        var mt940 = Mt940Schrijver.Schrijf(afschrift);
        var terug = Assert.Single(Mt940Lezer.Lees(new StringReader(mt940)));

        Assert.Equal((afschrift.Iban, afschrift.Nummer, afschrift.Beginsaldo, afschrift.Eindsaldo, afschrift.Begindatum, afschrift.Einddatum),
            (terug.Iban, terug.Nummer, terug.Beginsaldo, terug.Eindsaldo, terug.Begindatum, terug.Einddatum));
        Assert.Equal(afschrift.Mutaties, terug.Mutaties);
        Assert.Contains(":25:NL20INGB0001234567 EUR", mt940, StringComparison.Ordinal);
        Assert.Contains("/REMI/USTD//Retour 1323/ 2026/", mt940.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Een_afschrift_dat_niet_sluit_of_geen_tekst_heeft_wordt_geweigerd()
    {
        var fout = Assert.Throws<BankbestandException>(() => PdfBankafschrift.Lees(Pdf(nieuwSaldo: 1m)));
        Assert.Contains("sluit niet", fout.Message, StringComparison.Ordinal);

        var leeg = new PdfDocumentBuilder();
        leeg.AddPage(PageSize.A4);
        Assert.Contains("geen tekst", Assert.Throws<BankbestandException>(() => PdfBankafschrift.Lees(leeg.Build())).Message, StringComparison.Ordinal);

        var gewonePdf = new PdfDocumentBuilder();
        gewonePdf.AddPage(PageSize.A4).AddText("Factuur 2026-001 aan NL20 INGB 0001 2345 67", 10, new PdfPoint(50, 700), gewonePdf.AddStandard14Font(Standard14Font.Helvetica));
        Assert.Throws<BankbestandException>(() => PdfBankafschrift.Lees(gewonePdf.Build()));
    }

    [EchtAfschriftFact]
    public void Een_echt_afschrift_sluit_en_komt_als_mt940_hetzelfde_terug()
    {
        var afschrift = PdfBankafschrift.Lees(File.ReadAllBytes(Environment.GetEnvironmentVariable("BOEKERHOUDEN_BANKPDF")!));

        Assert.True(afschrift.SluitAan);
        Assert.NotEmpty(afschrift.Mutaties);
        var terug = Assert.Single(Mt940Lezer.Lees(new StringReader(Mt940Schrijver.Schrijf(afschrift))));
        // In MT940 scheidt "/" de velden; in een naam of referentie wordt hij "-".
        Assert.Equal(afschrift.Mutaties.Select(m => (m.Datum, m.Bedrag, m.TegenIban, m.TegenNaam?.Replace('/', '-'), m.EndToEndId?.Replace('/', '-'))),
            terug.Mutaties.Select(m => (m.Datum, m.Bedrag, m.TegenIban, m.TegenNaam, m.EndToEndId)));
        Assert.Equal((afschrift.Beginsaldo, afschrift.Eindsaldo, afschrift.Iban), (terug.Beginsaldo, terug.Eindsaldo, terug.Iban));
    }
}
