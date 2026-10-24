using Boekerhouden.Data;
using Boekerhouden.Server.Demo;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Tests.Gedeeld;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boekerhouden.Server.Tests;

public sealed class DemoVullingTests(ServerFixture server) : IClassFixture<ServerFixture>, IDisposable
{
    private readonly string map = Directory.CreateTempSubdirectory("boekerhouden-demo-").FullName;

    public void Dispose() => Directory.Delete(map, recursive: true);

    /// <summary>Een demomap met de fictieve bakkerij (naam uit het bestand) en een eenmanszaak met klant.json.</summary>
    private string MaakDemomap()
    {
        var demo = Directory.CreateDirectory(Path.Combine(map, "demo")).FullName;
        var bakkerij = Directory.CreateDirectory(Path.Combine(demo, "bv-bakkerij")).FullName;
        foreach (var jaar in new[] { 2023, 2024, 2025 })
        {
            File.Copy(TestGegevens.FictiefPad("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv"), Path.Combine(bakkerij, $"saldibalans-{jaar}.csv"));
        }

        var schilder = Directory.CreateDirectory(Path.Combine(demo, "ez-schilder")).FullName;
        File.Copy(TestGegevens.FictiefPad("ez-schildersbedrijf", "saldibalans-2025.csv"), Path.Combine(schilder, "saldibalans-2025.csv"));
        File.WriteAllText(Path.Combine(schilder, "klant.json"), """{ "naam": "Schilder Demo", "plaats": "Testplaats", "kvk": "00000000" }""");
        Directory.CreateDirectory(Path.Combine(demo, "leeg"));
        return demo;
    }

    [Fact]
    public void Klanten_uit_de_submappen_met_naam_rechtsvorm_en_jaren()
    {
        var klanten = DemoVulling.Klanten([MaakDemomap(), Path.Combine(map, "bestaat-niet")]);

        Assert.Equal(2, klanten.Count);
        var bakkerij = klanten[0];
        Assert.Equal("Voorbeeld Bakkerij B.V.", bakkerij.Naam);
        Assert.Equal(Rechtsvorm.BV, bakkerij.Rechtsvorm);
        Assert.Equal(Verslaggeving.Fiscaal, bakkerij.Grondslag);
        Assert.Equal([2023, 2024, 2025], bakkerij.Saldibalansen.Keys.Order());
        var schilder = klanten[1];
        Assert.Equal(("Schilder Demo", Rechtsvorm.Eenmanszaak, "Testplaats", "00000000"), (schilder.Naam, schilder.Rechtsvorm, schilder.Plaats, schilder.Kvk));
    }

    [Fact]
    public void Demo_op_de_opdrachtregel_zet_de_instelling_aan()
    {
        Assert.Equal(["--urls", "http://localhost:5080", "--Demo:Aan=true"], DemoVulling.Argumenten(["--urls", "http://localhost:5080", "--demo"]));
    }

    [Fact]
    public async Task Vult_account_rgs_en_klanten_met_dossier_en_rubricering_en_kan_opnieuw()
    {
        var werkboek = Path.Combine(map, RgsTestWerkboek.Bestandsnaam);
        await File.WriteAllBytesAsync(werkboek, RgsTestWerkboek.Maak());
        var opties = new DemoOpties { Aan = true, RgsWerkboek = werkboek };
        opties.Mappen.Add(MaakDemomap());

        await DemoVulling.Vul(server.Services, opties, map, NullLogger.Instance);
        await DemoVulling.Vul(server.Services, opties, map, NullLogger.Instance);

        Assert.Contains(await server.Dienst<GebruikerDienst>().Lijst(), g => g.Email == DemoVulling.Email);
        Assert.NotNull(await server.Dienst<RgsDienst>().ActiefSchema());
        Assert.Equal("Voorbeeld Accountants", (await server.Dienst<KantoorDienst>().Haal()).Naam);
        await using var db = await server.Services.GetRequiredService<IDbContextFactory<BoekerhoudenDbContext>>().CreateDbContextAsync();
        var bakkerij = await db.Klanten.Include(k => k.Dossiers).SingleAsync(k => k.Naam == "Voorbeeld Bakkerij B.V.");
        var dossier = Assert.Single(bakkerij.Dossiers);
        Assert.Equal((2025, SoortOpdracht.SamenstellenJaarrekening, Verslaggeving.Fiscaal), (dossier.Boekjaar, dossier.SoortOpdracht, dossier.Grondslag));
        Assert.Equal(3, await db.ImportBatches.CountAsync(b => b.KlantId == bakkerij.Id && b.Actief));
        Assert.True(await db.Koppelingen.AnyAsync(k => k.KlantId == bakkerij.Id));
        var schilder = await db.Klanten.Include(k => k.Dossiers).SingleAsync(k => k.Naam == "Schilder Demo");
        Assert.Equal(SoortOpdracht.WinstaangifteOpstellen, Assert.Single(schilder.Dossiers).SoortOpdracht);
    }
}
