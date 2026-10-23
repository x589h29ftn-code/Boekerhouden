using System.Text;
using Boekerhouden.Data;
using Boekerhouden.Import;
using Boekerhouden.Server.Diensten;

namespace Boekerhouden.Server.Tests;

public sealed class ImportDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private ImportDienst Import => server.Dienst<ImportDienst>();

    private async Task<Guid> NieuweBakkerij(string naam = "Voorbeeld Bakkerij B.V.") =>
        (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(TestGegevens.Klantnummer(), naam, Rechtsvorm.BV, null, null), Tester)).Waarde;

    private static byte[] Bakkerij(int jaar) => TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv");

    [Fact]
    public async Task Eerste_import_slaagt_en_zet_dossier_in_uitvoering()
    {
        var klantId = await NieuweBakkerij();
        var dossierId = (await server.Dienst<DossierDienst>().Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;

        var controle = await Import.Controleer(klantId, 2025, Bakkerij(2025), "saldibalans-2025.csv");
        Assert.True(controle.KanImporteren);
        Assert.Empty(controle.Fouten);
        Assert.Empty(controle.Waarschuwingen);
        Assert.Contains(controle.Informatie, m => m.Code == ImportCodes.Verschillen && m.Tekst.StartsWith("Eerste saldibalans", StringComparison.Ordinal));

        var uitkomst = await Import.Importeer(klantId, 2025, Bakkerij(2025), "saldibalans-2025.csv", Tester);

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var batch = Assert.Single(await Import.Imports(klantId));
        Assert.True(batch.Actief);
        Assert.Equal(controle.Saldibalans!.Regels.Count, batch.AantalRegels);
        Assert.Equal(controle.Saldibalans.TotaalDebet, batch.TotaalDebet);
        Assert.Equal(DossierStatus.InUitvoering, (await server.Dienst<DossierDienst>().Haal(dossierId))!.Status);

        var proefbalans = await Import.Proefbalans(klantId, 2025);
        Assert.Equal(batch.AantalRegels, proefbalans.Regels.Count);
        Assert.Equal(0m, proefbalans.Totaal);
        Assert.Null(proefbalans.ImportVorigJaar);
        Assert.All(proefbalans.Regels, r => Assert.False(string.IsNullOrEmpty(r.Omschrijving)));

        var log = await server.Dienst<AuditDienst>().Recent(AuditActies.SaldibalansGeimporteerd);
        Assert.Contains(log, r => r.Details!.Contains(controle.Sha256, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Saldo_niet_nihil_wordt_niet_geimporteerd()
    {
        var klantId = await NieuweBakkerij();
        var inhoud = TestGegevens.Fictief("fouten", "saldo-niet-nihil.csv");

        var controle = await Import.Controleer(klantId, 2025, inhoud, "saldo-niet-nihil.csv");
        var uitkomst = await Import.Importeer(klantId, 2025, inhoud, "saldo-niet-nihil.csv", Tester);

        Assert.False(controle.KanImporteren);
        Assert.Contains(controle.Fouten, m => m.Code == ImportCodes.SaldoNihil);
        Assert.False(uitkomst.IsGelukt);
        Assert.Empty(await Import.Imports(klantId));
    }

    [Fact]
    public async Task Boekjaar_in_bestand_moet_overeenkomen()
    {
        var klantId = await NieuweBakkerij();

        var controle = await Import.Controleer(klantId, 2024, Bakkerij(2025), "saldibalans-2025.csv");

        Assert.False(controle.KanImporteren);
        Assert.Contains(controle.Fouten, m => m.Code == ImportCodes.Boekjaar);
    }

    [Fact]
    public async Task Opnieuw_importeren_vervangt_en_bewaart_historie()
    {
        var klantId = await NieuweBakkerij();
        await Import.Importeer(klantId, 2025, Bakkerij(2025), "eerste.csv", Tester);

        var controle = await Import.Controleer(klantId, 2025, Bakkerij(2025), "tweede.csv");
        Assert.True(controle.VervangtBestaande);
        var melding = Assert.Single(controle.Waarschuwingen);
        Assert.Equal(ImportCodes.BestaandeImport, melding.Code);
        Assert.Contains("hetzelfde bestand", melding.Tekst, StringComparison.Ordinal);
        Assert.Contains(controle.Informatie, m => m.Tekst.StartsWith("Dezelfde rekeningen", StringComparison.Ordinal));

        await Import.Importeer(klantId, 2025, Bakkerij(2025), "tweede.csv", Tester);

        var imports = await Import.Imports(klantId);
        Assert.Equal(2, imports.Count);
        Assert.Equal("tweede.csv", Assert.Single(imports, i => i.Actief).Bestandsnaam);
        Assert.Equal(imports[0].AantalRegels, (await Import.Proefbalans(klantId, 2025)).Regels.Count);
    }

    [Fact]
    public async Task Vergelijking_met_vorig_jaar()
    {
        var klantId = await NieuweBakkerij();
        await Import.Importeer(klantId, 2024, Bakkerij(2024), "2024.csv", Tester);

        var controle = await Import.Controleer(klantId, 2025, Bakkerij(2025), "2025.csv");
        Assert.All(controle.Informatie, m => Assert.Contains("2024", m.Tekst, StringComparison.Ordinal));

        await Import.Importeer(klantId, 2025, Bakkerij(2025), "2025.csv", Tester);
        var proefbalans = await Import.Proefbalans(klantId, 2025);

        Assert.NotNull(proefbalans.ImportVorigJaar);
        Assert.Equal(0m, proefbalans.TotaalVorigJaar);
        Assert.Contains(proefbalans.Regels, r => r.Bedrag is not null && r.BedragVorigJaar is not null);
    }

    [Fact]
    public async Task Nieuwe_vervallen_en_hernoemde_rekeningen_worden_gemeld()
    {
        var klantId = await NieuweBakkerij("Testklant Verschillen");
        await Import.Importeer(klantId, 2024, Csv(2024, "1000;Kas;100,00", "1100;Bank;50,00", "0800;Eigen vermogen;-150,00"), "2024.csv", Tester);

        var controle = await Import.Controleer(klantId, 2025, Csv(2025, "1000;Kasgeld;100,00", "1200;Spaarrekening;50,00", "0800;Eigen vermogen;-150,00"), "2025.csv");

        var teksten = controle.Informatie.Select(m => m.Tekst).ToList();
        Assert.Contains(teksten, t => t.StartsWith("1 nieuwe rekening(en)", StringComparison.Ordinal) && t.EndsWith(": 1200", StringComparison.Ordinal));
        Assert.Contains(teksten, t => t.StartsWith("1 rekening(en) uit boekjaar 2024 komen niet meer voor", StringComparison.Ordinal) && t.EndsWith(": 1100", StringComparison.Ordinal));
        Assert.Contains(teksten, t => t.Contains("1000: 'Kas' → 'Kasgeld'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Historisch_jaar_overschrijft_omschrijving_niet()
    {
        var klantId = await NieuweBakkerij("Testklant Historie");
        await Import.Importeer(klantId, 2025, Csv(2025, "1000;Kas nieuw;100,00", "0800;Eigen vermogen;-100,00"), "2025.csv", Tester);
        await Import.Importeer(klantId, 2024, Csv(2024, "1000;Kas oud;80,00", "0800;Eigen vermogen;-80,00"), "2024.csv", Tester);

        var proefbalans = await Import.Proefbalans(klantId, 2025);

        Assert.Equal("Kas nieuw", proefbalans.Regels.Single(r => r.Nummer == "1000").Omschrijving);
        Assert.Equal(80m, proefbalans.Regels.Single(r => r.Nummer == "1000").BedragVorigJaar);
    }

    [Fact]
    public async Task Afwijkende_klant_in_bestand_geeft_waarschuwing()
    {
        var klantId = await NieuweBakkerij("Heel Andere Klant B.V.");

        var controle = await Import.Controleer(klantId, 2025, Bakkerij(2025), "saldibalans-2025.csv");

        Assert.True(controle.KanImporteren);
        Assert.Contains(controle.Waarschuwingen, m => m.Code == ImportCodes.KlantAfwijkend);
    }

    [Theory]
    [InlineData("Voorbeeld Bakkerij B.V.", "Voorbeeld Bakkerij BV", "1001", false)]
    [InlineData("1001 Voorbeeld Bakkerij", "Voorbeeld Bakkerij BV", "1001", false)]
    [InlineData("Bakkerij", "Voorbeeld Bakkerij BV", "1001", false)]
    [InlineData("Garage De Snelweg", "Voorbeeld Bakkerij BV", "1001", true)]
    [InlineData(null, "Voorbeeld Bakkerij BV", "1001", false)]
    public void Klantvergelijking(string? inBestand, string naam, string nummer, bool wijktAf) =>
        Assert.Equal(wijktAf, ImportDienst.KlantWijktAf(inBestand, new Klant { Naam = naam, Klantnummer = nummer }));

    [Fact]
    public async Task Te_groot_bestand_wordt_geweigerd()
    {
        var klantId = await NieuweBakkerij();

        var controle = await Import.Controleer(klantId, 2025, new byte[ImportDienst.MaximaleBestandsgrootte + 1], "groot.csv");

        Assert.Equal(ImportCodes.Bestand, Assert.Single(controle.Fouten).Code);
    }

    private static byte[] Csv(int jaar, params string[] regels) =>
        Encoding.UTF8.GetBytes($"Boekjaar;{jaar}\n\nGrootboeknummer;Omschrijving;Bedrag\n{string.Join('\n', regels)}\n");
}
