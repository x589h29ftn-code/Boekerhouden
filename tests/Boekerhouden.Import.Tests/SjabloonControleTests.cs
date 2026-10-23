using System.Text;
using Boekerhouden.Import;
using static Boekerhouden.Import.Tests.TestHulp;

namespace Boekerhouden.Import.Tests;

/// <summary>De controles I1–I6 (en I11) uit docs/onderzoek §3.3, via CSV-invoer.</summary>
public class SjabloonControleTests
{
    [Fact]
    public void Geldige_saldibalans_wordt_ingelezen()
    {
        var resultaat = LeesCsv(Sjabloon("2025",
            "0100;Inventaris;1.500,00",
            "1100;Bank;250,25",
            "0500;Geplaatst kapitaal;-1.750,25"));

        Assert.True(resultaat.IsGeslaagd);
        Assert.Empty(resultaat.Meldingen);
        var saldibalans = resultaat.Saldibalans!;
        Assert.Equal(2025, saldibalans.Boekjaar);
        Assert.Equal("Test B.V.", saldibalans.Klant);
        Assert.Equal(["0100", "1100", "0500"], saldibalans.Regels.Select(r => r.Grootboeknummer));
        Assert.Equal(1_500m, saldibalans.Regels[0].Bedrag);
        Assert.Equal(-1_750.25m, saldibalans.Regels[2].Bedrag);
        Assert.True(saldibalans.IsInEvenwicht);
    }

    [Fact]
    public void Kolommen_mogen_in_een_andere_volgorde_staan_en_RGS_code_is_optioneel()
    {
        var resultaat = LeesCsv(
            "Boekjaar;2024\n\nBedrag;RGS-code (optioneel);Omschrijving;Grootboeknummer\n" +
            "100,00;BLimBanRba;Bank;1100\n-100,00;;Kapitaal;0500\n");

        Assert.True(resultaat.IsGeslaagd);
        var regels = resultaat.Saldibalans!.Regels;
        Assert.Equal("BLimBanRba", regels[0].RgsCode);
        Assert.Null(regels[1].RgsCode);
        Assert.Null(resultaat.Saldibalans.Klant);
    }

    [Fact]
    public void Lege_regels_en_een_totaalregel_worden_overgeslagen_en_leeg_bedrag_telt_als_nihil()
    {
        var resultaat = LeesCsv(Sjabloon("2025",
            "1100;Bank;100,00",
            ";;",
            "1200;Spaarrekening;",
            "0500;Kapitaal;-100,00",
            ";Totaal;0,00"));

        Assert.True(resultaat.IsGeslaagd);
        Assert.Equal(3, resultaat.Saldibalans!.Regels.Count);
        Assert.Equal(0m, resultaat.Saldibalans.Regels[1].Bedrag);
    }

    [Fact]
    public void I1_bestand_zonder_kopregel_wordt_geweigerd()
    {
        var fout = EnigeFout(LeesCsv("Boekjaar;2025\n1100;Bank;100\n"), ImportCodes.Bestand);
        Assert.Contains("kolomkoppen", fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void I1_ontbrekende_kolom_wordt_genoemd()
    {
        var fout = EnigeFout(LeesCsv("Boekjaar;2025\nGrootboeknummer;Omschrijving;Debet\n1100;Bank;0\n"), ImportCodes.Bestand);
        Assert.Contains("'Bedrag'", fout.Tekst, StringComparison.Ordinal);
        Assert.Equal(2, fout.Rij);
    }

    [Theory]
    [InlineData(".xls")]
    [InlineData(".pdf")]
    [InlineData("")]
    public void I1_niet_ondersteund_bestandstype(string extensie)
    {
        var resultaat = ImportSjabloonLezer.Lees(new MemoryStream([1, 2, 3]), "saldibalans" + extensie);
        EnigeFout(resultaat, ImportCodes.Bestand);
    }

    [Fact]
    public void I1_beschadigd_Excel_bestand_geeft_een_melding_in_plaats_van_een_crash()
    {
        var resultaat = ImportSjabloonLezer.Lees(new MemoryStream(Encoding.UTF8.GetBytes("geen excel")), "saldibalans.xlsx");
        var fout = EnigeFout(resultaat, ImportCodes.Bestand);
        Assert.Contains("kan niet worden gelezen", fout.Tekst, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("twintig")]
    [InlineData("25")]
    [InlineData("2025,5")]
    public void I2_boekjaar_ontbreekt_of_is_ongeldig(string boekjaar)
    {
        EnigeFout(LeesCsv(Sjabloon(boekjaar, "1100;Bank;0")), ImportCodes.Boekjaar);
    }

    [Fact]
    public void I3_grootboeknummer_of_omschrijving_ontbreekt_met_rijnummer()
    {
        var resultaat = LeesCsv(Sjabloon("2025",
            "1100;Bank;100,00",
            ";Kas;0,00",
            "1300;;0,00",
            "0500;Kapitaal;-100,00"));

        var fouten = resultaat.Fouten.ToList();
        Assert.Equal(2, fouten.Count);
        Assert.All(fouten, f => Assert.Equal(ImportCodes.VerplichteVelden, f.Code));
        Assert.Equal([6, 7], fouten.Select(f => f.Rij!.Value));
        Assert.Null(resultaat.Saldibalans);
    }

    [Fact]
    public void I3_bestand_zonder_regels()
    {
        var fout = EnigeFout(LeesCsv(Sjabloon("2025")), ImportCodes.VerplichteVelden);
        Assert.Contains("geen grootboekregels", fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void I4_dubbele_grootboeknummers_worden_met_rijnummers_gemeld()
    {
        var fout = EnigeFout(LeesCsv(Sjabloon("2025",
            "1100;Bank;100,00",
            "1100;Bank 2;0,00",
            "0500;Kapitaal;-100,00")), ImportCodes.UniekeNummers);

        Assert.Contains("1100", fout.Tekst, StringComparison.Ordinal);
        Assert.Contains("rij 5, 6", fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Voorloopnullen_maken_grootboeknummers_verschillend()
    {
        var resultaat = LeesCsv(Sjabloon("2025", "0100;A;10,00", "100;B;-10,00"));
        Assert.True(resultaat.IsGeslaagd);
        Assert.Equal(["0100", "100"], resultaat.Saldibalans!.Regels.Select(r => r.Grootboeknummer));
    }

    [Theory]
    [InlineData("abc", "geen geldig bedrag")]
    [InlineData("12,345", "meer dan 2 decimalen")]
    public void I5_ongeldig_bedrag(string bedrag, string verwachteTekst)
    {
        var resultaat = LeesCsv(Sjabloon("2025", "1100;Bank;" + bedrag, "0500;Kapitaal;-12,00"));

        var fout = EnigeFout(resultaat, ImportCodes.GeldigeBedragen);
        Assert.Contains(verwachteTekst, fout.Tekst, StringComparison.Ordinal);
        Assert.Equal(5, fout.Rij);
        Assert.DoesNotContain(resultaat.Fouten, f => f.Code == ImportCodes.SaldoNihil);
    }

    [Theory]
    [InlineData("100,01", "€ 0,01 debet")]
    [InlineData("99,99", "€ 0,01 credit")]
    [InlineData("100", "€ 0,00")]
    public void I6_saldo_moet_nihil_zijn(string bank, string verwachtVerschil)
    {
        var resultaat = LeesCsv(Sjabloon("2025", "1100;Bank;" + bank, "0500;Kapitaal;-100,00"));

        if (verwachtVerschil == "€ 0,00")
        {
            Assert.True(resultaat.IsGeslaagd);
            return;
        }

        var fout = EnigeFout(resultaat, ImportCodes.SaldoNihil);
        Assert.Contains(verwachtVerschil, fout.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Alle_fouten_worden_in_een_keer_gemeld()
    {
        var resultaat = LeesCsv(Sjabloon("",
            "1100;Bank;100,00",
            "1100;Bank;0,00",
            "1300;Debiteuren;veel"));

        Assert.Equal(
            [ImportCodes.Boekjaar, ImportCodes.UniekeNummers, ImportCodes.GeldigeBedragen],
            resultaat.Fouten.Select(f => f.Code).Order());
    }

    [Fact]
    public void I11_ongeldige_RGS_code_geeft_alleen_een_waarschuwing()
    {
        var resultaat = LeesCsv(
            "Boekjaar;2025\nGrootboeknummer;Omschrijving;Bedrag;RGS-code\n1100;Bank;10,00;12345\n0500;Kapitaal;-10,00;BEivGokGea\n");

        Assert.True(resultaat.IsGeslaagd);
        var waarschuwing = Assert.Single(resultaat.Waarschuwingen);
        Assert.Equal(ImportCodes.RgsFormaat, waarschuwing.Code);
        Assert.Null(resultaat.Saldibalans!.Regels[0].RgsCode);
        Assert.Equal("BEivGokGea", resultaat.Saldibalans.Regels[1].RgsCode);
    }
}
