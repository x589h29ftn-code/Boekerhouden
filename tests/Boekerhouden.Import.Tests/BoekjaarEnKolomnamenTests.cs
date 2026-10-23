using Boekerhouden.Import;
using static Boekerhouden.Import.Tests.TestHulp;

namespace Boekerhouden.Import.Tests;

/// <summary>Boekjaar uit het bestand of uit de keuze van de gebruiker, en gangbare kolomnamen uit exports.</summary>
public class BoekjaarEnKolomnamenTests
{
    private const string ExportZonderBoekjaar =
        "Rekening;Omschr;Saldo\n0210.01;Inventaris aanschafwaarde;4.000,00\n0210.03;Inventaris cum. afschrijvingen;-1.500,00\n0500;Aandelenkapitaal;-2.500,00\n";

    [Fact]
    public void Export_met_Rekening_Omschr_Saldo_wordt_herkend_als_het_boekjaar_wordt_opgegeven()
    {
        var resultaat = LeesCsv(ExportZonderBoekjaar, boekjaar: 2025);

        Assert.True(resultaat.IsGeslaagd);
        Assert.Equal(2025, resultaat.Saldibalans!.Boekjaar);
        Assert.Equal(["0210.01", "0210.03", "0500"], resultaat.Saldibalans.Regels.Select(r => r.Grootboeknummer));
    }

    [Fact]
    public void Zonder_boekjaar_in_bestand_en_zonder_keuze_volgt_I2()
    {
        var fout = EnigeFout(LeesCsv(ExportZonderBoekjaar), ImportCodes.Boekjaar);
        Assert.Contains("geef het op bij het importeren", fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Gekozen_boekjaar_moet_gelijk_zijn_aan_het_boekjaar_in_het_bestand()
    {
        var bestand = Sjabloon("2024", "1100;Bank;10,00", "0500;Kapitaal;-10,00");

        Assert.True(LeesCsv(bestand, boekjaar: 2024).IsGeslaagd);
        var fout = EnigeFout(LeesCsv(bestand, boekjaar: 2025), ImportCodes.Boekjaar);
        Assert.Contains("wijkt af", fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Ongeldig_gekozen_boekjaar_wordt_geweigerd()
    {
        EnigeFout(LeesCsv(ExportZonderBoekjaar, boekjaar: 25), ImportCodes.Boekjaar);
    }

    [Theory]
    [InlineData("Grootboeknummer;Omschrijving;Bedrag")]
    [InlineData("Rekeningnummer;Naam;Eindsaldo")]
    [InlineData("Rek.nr.;Omschr.;Saldo")]
    [InlineData("grootboekrekening;OMSCHRIJVING;saldo")]
    public void Gangbare_kolomnamen_worden_herkend(string kopregel)
    {
        var resultaat = LeesCsv($"Boekjaar;2025\n{kopregel}\n1100;Bank;10,00\n0500;Kapitaal;-10,00\n");
        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
    }

    [Fact]
    public void Regel_met_alleen_Rekening_boven_de_tabel_wordt_niet_als_kopregel_gezien()
    {
        var resultaat = LeesCsv("Boekjaar;2025\nRekening;Rabobank\n\nGrootboeknummer;Omschrijving;Bedrag\n1100;Bank;10,00\n0500;Kapitaal;-10,00\n");
        Assert.True(resultaat.IsGeslaagd, string.Join(" | ", resultaat.Meldingen.Select(m => m.Tekst)));
        Assert.Equal(2, resultaat.Saldibalans!.Regels.Count);
    }
}
