using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;

namespace Boekerhouden.Server.Tests;

public sealed class GrondslagDienstTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Uitvoerder Tester = new(null, "Test Gebruiker");

    private GrondslagDienst Grondslagen => server.Dienst<GrondslagDienst>();

    /// <summary>De voorbeeldbakkerij met een dossier 2024 en 2025; alleen een paar rekeningen gerubriceerd.</summary>
    private async Task<(Guid KlantId, Guid Vorig, Guid Huidig, string Klantnummer)> Bakkerij(Verslaggeving? vorigJaar = Verslaggeving.Fiscaal)
    {
        await server.MetRgs();
        var klantnummer = TestGegevens.Klantnummer();
        var klantId = (await server.Dienst<KlantDienst>().Maak(new KlantInvoer(klantnummer, "Voorbeeld Bakkerij B.V.", Rechtsvorm.BV, null, "Ergens"), Tester)).Waarde;
        foreach (var jaar in new[] { 2024, 2025 })
        {
            Assert.True((await server.Dienst<ImportDienst>().Importeer(klantId, jaar, TestGegevens.Fictief("bv-voorbeeld-bakkerij", $"saldibalans-{jaar}.csv"), $"{jaar}.csv", Tester)).IsGelukt);
        }

        var dossiers = server.Dienst<DossierDienst>();
        var vorig = (await dossiers.Maak(klantId, 2024, SoortOpdracht.SamenstellenJaarrekening, Tester, vorigJaar)).Waarde;
        var huidig = (await dossiers.Maak(klantId, 2025, SoortOpdracht.SamenstellenJaarrekening, Tester)).Waarde;
        var rubriceren = server.Dienst<RubriceerDienst>();
        var rekeningen = (await rubriceren.Overzicht(huidig, metVoorstellen: false))!.Rekeningen.ToDictionary(r => r.Nummer, r => r.Id);
        foreach (var (nummer, code) in new[] { ("0200", "BMvaObeVvp"), ("4800", "WAfsAmvObe"), ("8000", "WOmzNopOlh"), ("9500", "WBelBgrLab") })
        {
            Assert.True((await rubriceren.Koppel(klantId, [rekeningen[nummer]], code, false, Tester)).IsGelukt);
        }

        return (klantId, vorig, huidig, klantnummer);
    }

    private async Task<Rapport> Rapport(Guid dossierId) => (await server.Dienst<RapportDienst>().Maak(dossierId))!.Waarde!.Rapport;

    private static List<GrondslagBlok> Blokken(Rapport rapport) =>
        [.. rapport.Hoofdstukken.Single(h => h.Titel == "Grondslagen").Blokken.OfType<GrondslagBlok>()];

    [Fact]
    public async Task De_meegeleverde_bibliotheek_staat_klaar()
    {
        var bibliotheek = await Grondslagen.Bibliotheek();

        Assert.Contains(bibliotheek, g => g.Sleutel == "mva" && g.Opname == GrondslagOpname.BijPost && g.Codes == "BMva");
        Assert.Contains(bibliotheek, g => g.Sleutel == "foutenherstel" && g.Opname == GrondslagOpname.Optioneel && g.Jaargebonden);
        Assert.Contains(bibliotheek, g => g.Sleutel == "latentie" && g.TekstFiscaal is null && g.TekstCommercieel is not null);
        Assert.Contains("100% van de WOZ-waarde", bibliotheek.Single(g => g.Sleutel == "mva").TekstFiscaal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fiscale_grondslagen_staan_bij_de_posten_met_saldo()
    {
        var (_, _, huidig, _) = await Bakkerij();

        var rapport = await Rapport(huidig);

        Assert.Equal(["Balans per 31 december 2025", "Winst-en-verliesrekening over 2025", "Grondslagen", "Toelichting op de balans", "Toelichting op de winst-en-verliesrekening"],
            rapport.Hoofdstukken.Select(h => h.Titel));
        var blokken = Blokken(rapport);
        Assert.True(blokken.Single(b => b.Sleutel == "mva").Opgenomen);
        Assert.Contains("WOZ-waarde", blokken.Single(b => b.Sleutel == "mva").Tekst, StringComparison.Ordinal);
        Assert.Contains("geen latente belastingen", blokken.Single(b => b.Sleutel == "belastingen").Tekst, StringComparison.Ordinal);
        Assert.DoesNotContain(blokken, b => b.Sleutel is "latentie" or "voorraden" or "schattingen");
        Assert.False(blokken.Single(b => b.Sleutel == "foutenherstel").Opgenomen);
        Assert.DoesNotContain(rapport.Waarschuwingen, w => w.Contains("grondslag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Commerciele_grondslagen_hebben_hun_eigen_teksten()
    {
        var (_, _, huidig, klantnummer) = await Bakkerij();
        Assert.True((await Grondslagen.KiesVerslaggeving(huidig, Verslaggeving.Commercieel, Tester)).IsGelukt);

        var blokken = Blokken(await Rapport(huidig));

        Assert.Contains("Titel 9 Boek 2 BW", blokken.Single(b => b.Sleutel == "algemeen").Tekst, StringComparison.Ordinal);
        Assert.Contains("economische levensduur", blokken.Single(b => b.Sleutel == "mva").Tekst, StringComparison.Ordinal);
        Assert.Contains(blokken, b => b.Sleutel == "schattingen" && b.Opgenomen);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.VerslaggevingGekozen),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "Fiscale grondslagen → Commerciële grondslagen");
    }

    [Fact]
    public async Task Zonder_keuze_staan_er_geen_grondslagen_maar_een_waarschuwing()
    {
        var (_, _, huidig, _) = await Bakkerij(vorigJaar: null);

        var rapport = await Rapport(huidig);

        Assert.DoesNotContain(rapport.Hoofdstukken, h => h.Titel == "Grondslagen");
        Assert.Contains(rapport.Waarschuwingen, w => w == "Grondslagen nog niet gekozen.");
        Assert.Null((await server.Dienst<DossierDienst>().Haal(huidig))!.Grondslag);
    }

    [Fact]
    public async Task Een_nieuw_dossier_neemt_de_keuze_van_vorig_jaar_over()
    {
        var (klantId, _, huidig, _) = await Bakkerij(Verslaggeving.Commercieel);

        Assert.Equal(Verslaggeving.Commercieel, (await server.Dienst<DossierDienst>().Haal(huidig))!.Grondslag);
        Assert.Equal(Verslaggeving.Commercieel, await Grondslagen.VorigeVerslaggeving(klantId));
    }

    [Fact]
    public async Task Open_dicht_en_eigen_tekst_rollen_door_behalve_jaargebonden()
    {
        var (_, vorig, huidig, klantnummer) = await Bakkerij();
        Assert.True((await Grondslagen.ZetOpgenomen(vorig, "mva", false, Tester)).IsGelukt);
        Assert.True((await Grondslagen.ZetTekst(vorig, "omzet", "De omzet bestaat uit de verkoop van brood en banket.", Tester)).IsGelukt);
        Assert.True((await Grondslagen.ZetOpgenomen(vorig, "foutenherstel", true, Tester)).IsGelukt);

        var dezeKeer = Blokken(await Rapport(huidig));
        Assert.False(dezeKeer.Single(b => b.Sleutel == "mva").Opgenomen);
        Assert.True(dezeKeer.Single(b => b.Sleutel == "omzet").EigenTekst);
        Assert.Equal("De omzet bestaat uit de verkoop van brood en banket.", dezeKeer.Single(b => b.Sleutel == "omzet").Tekst);
        Assert.False(dezeKeer.Single(b => b.Sleutel == "foutenherstel").Opgenomen);

        // Alleen open zetten houdt de eigen tekst van vorig jaar vast.
        Assert.True((await Grondslagen.ZetOpgenomen(huidig, "omzet", true, Tester)).IsGelukt);
        Assert.Equal("De omzet bestaat uit de verkoop van brood en banket.", Blokken(await Rapport(huidig)).Single(b => b.Sleutel == "omzet").Tekst);

        Assert.True((await Grondslagen.ZetTekst(huidig, "omzet", "  ", Tester)).IsGelukt);
        Assert.False(Blokken(await Rapport(huidig)).Single(b => b.Sleutel == "omzet").EigenTekst);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.GrondslagGewijzigd),
            a => a.Onderwerp!.StartsWith(klantnummer, StringComparison.Ordinal) && a.Details == "Materiële vaste activa: niet in het rapport");
    }

    [Fact]
    public async Task Tekst_om_in_te_vullen_geeft_een_waarschuwing_en_staat_in_de_pdf()
    {
        var (_, _, huidig, _) = await Bakkerij();
        Assert.True((await Grondslagen.ZetOpgenomen(huidig, "foutenherstel", true, Tester)).IsGelukt);

        var inhoud = (await server.Dienst<RapportDienst>().Maak(huidig))!.Waarde!;
        Assert.Contains(inhoud.Rapport.Waarschuwingen, w => w == "Foutenherstel: tekst tussen [ ] nog invullen.");

        var html = await RapportHtml.Maak(server.Services, inhoud);
        Assert.Contains("Foutenherstel", html, StringComparison.Ordinal);
        Assert.Contains("vaste activa", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Gebeurtenissen na balansdatum", html, StringComparison.Ordinal);
        Assert.DoesNotContain("niet in het rapport", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task De_bibliotheek_is_aan_te_passen_en_terug_te_zetten()
    {
        await server.MetRgs();
        Assert.False((await Grondslagen.Bewaar("mva", new GrondslagInvoer("", "x", null, GrondslagOpname.BijPost, "BMva", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", new GrondslagInvoer("MVA", "x", null, GrondslagOpname.BijPost, "", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", new GrondslagInvoer("MVA", "x", null, GrondslagOpname.BijPost, "1234", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", new GrondslagInvoer("MVA", null, " ", GrondslagOpname.Altijd, "", false), Tester)).IsGelukt);

        Assert.True((await Grondslagen.Bewaar("verbonden", new GrondslagInvoer("Verbonden partijen", "Eigen tekst.", "Eigen fiscale tekst.", GrondslagOpname.Optioneel, "BFva, BVor", false), Tester)).IsGelukt);
        var aangepast = (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "verbonden");
        Assert.Equal("BFva,BVor", aangepast.Codes);
        Assert.Equal("Test Gebruiker", aangepast.GewijzigdDoor);

        Assert.True((await Grondslagen.HerstelStandaard("verbonden", Tester)).IsGelukt);
        Assert.Equal("Transacties met verbonden partijen", (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "verbonden").Titel);
    }
}
