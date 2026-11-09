using Boekerhouden.Boekhouding.Bank;

namespace Boekerhouden.Documenten.Tests;

/// <summary>Het PDF-afschrift van ING (over een periode, met begin- en eindsaldo en één bedragkolom), met een verzonnen afschrift.</summary>
public sealed class PdfIngAfschriftTests
{
    [Fact]
    public void Kop_en_mutaties_over_twee_paginas()
    {
        var afschrift = PdfBankafschrift.Lees(FictiefIngAfschrift.Pdf());

        Assert.Equal((FictiefIngAfschrift.Eigen, new DateOnly(2025, 12, 31), 1000m, new DateOnly(2026, 3, 31)), (afschrift.Iban, afschrift.Begindatum, afschrift.Beginsaldo, afschrift.Einddatum));
        Assert.True(afschrift.SluitAan);
        Assert.Equal(FictiefIngAfschrift.Mutaties.Select(m => m.Bedrag), afschrift.Mutaties.Select(m => m.Bedrag));
        Assert.Equal(new DateOnly(2026, 1, 26), afschrift.Mutaties[0].Datum);
    }

    [Fact]
    public void Bankkosten_hebben_de_omschrijving_van_de_kosten_als_tegenpartij()
    {
        var kosten = PdfBankafschrift.Lees(FictiefIngAfschrift.Pdf()).Mutaties[0];

        Assert.Equal(("Kosten Zakelijk Betalingsverkeer", (string?)null), (kosten.TegenNaam, kosten.TegenIban));
        Assert.Equal("Factuurnr. 1000000001 Periode: 01-12-2025 / 31-12-2025", kosten.Omschrijving);
    }

    [Fact]
    public void Naam_iban_en_omschrijving_over_meer_regels()
    {
        var mutaties = PdfBankafschrift.Lees(FictiefIngAfschrift.Pdf()).Mutaties;

        Assert.Equal(("Kantoorhandel Jansen", "NL36INGB0007654321", "Factuur 2026-0042 betaling inkoop"), (mutaties[1].TegenNaam, mutaties[1].TegenIban, mutaties[1].Omschrijving));
        Assert.Equal(("J. de Vries", 1210m), (mutaties[2].TegenNaam, mutaties[2].Bedrag));
        Assert.Equal(("Webwinkel via Betaaldienst B.V.", "NL02ABNA0123456789"), (mutaties[4].TegenNaam, mutaties[4].TegenIban));
    }

    [Fact]
    public void Als_mt940_hetzelfde_terug_en_een_afschrift_dat_niet_sluit_wordt_geweigerd()
    {
        var afschrift = PdfBankafschrift.Lees(FictiefIngAfschrift.Pdf(beginsaldo: -250m));
        Assert.Equal(-250m, afschrift.Beginsaldo);
        var terug = Assert.Single(Mt940Lezer.Lees(new StringReader(Mt940Schrijver.Schrijf(afschrift))));
        Assert.Equal(afschrift.Mutaties.Select(m => (m.Datum, m.Bedrag, m.TegenIban, m.TegenNaam)), terug.Mutaties.Select(m => (m.Datum, m.Bedrag, m.TegenIban, m.TegenNaam)));

        var fout = Assert.Throws<BankbestandException>(() => PdfBankafschrift.Lees(FictiefIngAfschrift.Pdf(eindsaldo: 1m)));
        Assert.Contains("sluit niet", fout.Message, StringComparison.Ordinal);
    }
}
