using System.Text;
using Boekerhouden.Import;

namespace Boekerhouden.Import.Tests;

public class CsvTabelLezerTests
{
    [Theory]
    [InlineData("Grootboeknummer;Omschrijving;Bedrag", ';')]
    [InlineData("Grootboeknummer\tOmschrijving\tBedrag", '\t')]
    [InlineData("Grootboeknummer,Omschrijving,Bedrag", ',')]
    [InlineData("Boekjaar;2025\nGrootboeknummer,Omschrijving,Bedrag", ',')]
    public void Herkent_het_scheidingsteken_aan_de_kopregel(string tekst, char verwacht)
    {
        Assert.Equal(verwacht, CsvTabelLezer.BepaalScheidingsteken(tekst));
    }

    [Fact]
    public void Velden_tussen_aanhalingstekens_mogen_scheidingstekens_en_aanhalingstekens_bevatten()
    {
        var rijen = CsvTabelLezer.SplitsInRijen("1100,\"Bank, rekening \"\"zakelijk\"\"\",\"1.234,56\"\r\n1300,Debiteuren,5", ',');

        Assert.Equal(2, rijen.Count);
        Assert.Equal("Bank, rekening \"zakelijk\"", rijen[0][1].Tekst);
        Assert.Equal("1.234,56", rijen[0][2].Tekst);
        Assert.Equal(2, rijen[1].Rijnummer);
        Assert.Equal("5", rijen[1][2].Tekst);
    }

    [Fact]
    public void Cellen_worden_getrimd_en_ontbrekende_cellen_zijn_leeg()
    {
        var rijen = CsvTabelLezer.SplitsInRijen("  1100 ;  Bank  ", ';');

        Assert.Equal("1100", rijen[0][0].Tekst);
        Assert.Equal("Bank", rijen[0][1].Tekst);
        Assert.True(rijen[0][5].IsLeeg);
    }

    [Fact]
    public void Leest_Windows1252_als_het_geen_geldige_UTF8_is()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("0700;Privéonttrekkingen;12,00");

        Assert.Equal("0700;Privéonttrekkingen;12,00", CsvTabelLezer.LeesTekst(new MemoryStream(bytes)));
    }

    [Theory]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    [InlineData("utf-8")]
    public void Leest_UTF8_en_UTF16(string codering)
    {
        const string tekst = "0700;Privéonttrekkingen;12,00";
        var bytes = codering switch
        {
            "utf-8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(tekst)],
            "utf-16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(tekst)],
            _ => Encoding.UTF8.GetBytes(tekst),
        };

        Assert.Equal(tekst, CsvTabelLezer.LeesTekst(new MemoryStream(bytes)));
    }
}
