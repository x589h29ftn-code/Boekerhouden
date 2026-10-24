using Boekerhouden.Data;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Server.Diensten;
using Boekerhouden.Server.Export;
using Boekerhouden.Server.Grondslagen;
using Microsoft.EntityFrameworkCore;

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
        [.. rapport.Hoofdstukken.Single(h => h.Titel == "Algemene toelichting en grondslagen").Blokken.OfType<GrondslagBlok>()];

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

        Assert.Equal(["Samenstellingsverklaring van de accountant", "Balans per 31 december 2025", "Winst-en-verliesrekening over 2025", "Algemene toelichting en grondslagen", "Toelichting op de balans", "Toelichting op de winst-en-verliesrekening"],
            rapport.Hoofdstukken.Select(h => h.Titel));
        var blokken = Blokken(rapport);
        Assert.True(blokken.Single(b => b.Sleutel == "mva").Opgenomen);
        Assert.Contains("WOZ-waarde", blokken.Single(b => b.Sleutel == "mva").Tekst, StringComparison.Ordinal);
        Assert.Contains("geen latente belastingen", blokken.Single(b => b.Sleutel == "belastingen").Tekst, StringComparison.Ordinal);
        Assert.DoesNotContain(blokken, b => b.Sleutel is "latentie" or "voorraden" or "herwaardering");
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

        Assert.DoesNotContain(rapport.Hoofdstukken, h => h.Titel.Contains("grondslagen", StringComparison.Ordinal));
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
    public async Task De_algemene_toelichting_vult_de_gegevens_van_de_klant_in()
    {
        var (klantId, _, huidig, _) = await Bakkerij();

        var blokken = Blokken(await Rapport(huidig));

        Assert.Equal(["Algemene toelichting", "Algemene grondslagen", "Grondslagen voor de waardering van activa en passiva", "Grondslagen voor de bepaling van het resultaat"],
            (await Rapport(huidig)).Hoofdstukken.Single(h => h.Titel == "Algemene toelichting en grondslagen").Blokken.OfType<GroepBlok>().Select(g => g.Titel));
        Assert.Equal("Voorbeeld Bakkerij B.V. is een besloten vennootschap met beperkte aansprakelijkheid, statutair gevestigd te Ergens. "
            + "De vennootschap is feitelijk gevestigd op [vestigingsadres] te Ergens en is ingeschreven in het handelsregister onder nummer [KvK-nummer].",
            blokken.Single(b => b.Sleutel == "vestiging").Tekst);
        Assert.Contains("vormt de leiding van Voorbeeld Bakkerij B.V. zich oordelen", blokken.Single(b => b.Sleutel == "schattingen").Tekst, StringComparison.Ordinal);
        Assert.Contains("per 31 december 2025", blokken.Single(b => b.Sleutel == "continuiteit").Tekst, StringComparison.Ordinal);
        Assert.False(blokken.Single(b => b.Sleutel == "continuiteit").Opgenomen);
        Assert.Contains((await server.Dienst<RapportDienst>().Maak(huidig))!.Waarde!.Rapport.Waarschuwingen,
            w => w == "Vestigingsadres, rechtsvorm en inschrijving handelsregister: tekst tussen [ ] nog invullen.");

        // Een eigen tekst mag de variabelen ook gebruiken.
        Assert.True((await Grondslagen.ZetTekst(huidig, "activiteiten", "{naam} bakt brood in {plaats}.", Tester)).IsGelukt);
        Assert.Equal("Voorbeeld Bakkerij B.V. bakt brood in Ergens.", Blokken(await Rapport(huidig)).Single(b => b.Sleutel == "activiteiten").Tekst);

        var klant = (await server.Dienst<KlantDienst>().Haal(klantId))!;
        var gegevens = new KlantGegevens(Straat: "Dorpsstraat", Huisnummer: "12", Toevoeging: "A", Postcode: "1234ab", StatutaireNaam: "Bakkerij Holding B.V.",
            StatutaireZetel: "Amsterdam", Oprichtingsdatum: new DateOnly(2020, 6, 28));
        Assert.True((await server.Dienst<KlantDienst>().Wijzig(klantId, new KlantInvoer(klant.Klantnummer, klant.Naam, Rechtsvorm.BV, "87654321", "Ergens") { Gegevens = gegevens }, Tester)).IsGelukt);
        Assert.True((await Grondslagen.ZetOpgenomen(huidig, "oprichting", true, Tester)).IsGelukt);
        blokken = Blokken(await Rapport(huidig));
        Assert.Equal("Bakkerij Holding B.V. is een besloten vennootschap met beperkte aansprakelijkheid, statutair gevestigd te Amsterdam. "
            + "De vennootschap is feitelijk gevestigd op Dorpsstraat 12 A te Ergens en is ingeschreven in het handelsregister onder nummer 87654321.",
            blokken.Single(b => b.Sleutel == "vestiging").Tekst);
        Assert.StartsWith("Bakkerij Holding B.V. is opgericht bij notariële akte van 28 juni 2020.", blokken.Single(b => b.Sleutel == "oprichting").Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fiscale_grondslagen_hebben_eigen_onderdelen()
    {
        var bibliotheek = await Grondslagen.Bibliotheek();

        Assert.Contains(bibliotheek, g => g.Sleutel == "herinvestering" && g.TekstCommercieel is null && g.Codes == "BEivFirHer");
        Assert.Contains(bibliotheek, g => g.Sleutel == "fiscaalvoorheteerst" && g.TekstCommercieel is null && g.Jaargebonden);
        Assert.Contains(bibliotheek, g => g.Sleutel == "herwaardering" && g.TekstFiscaal is null);
        Assert.Contains(bibliotheek, g => g.Sleutel == "waardevermindering" && g.Opname == GrondslagOpname.Optioneel && g.Codes == "WWvi,WWfa");
        Assert.DoesNotContain(bibliotheek, g => g.Titel.Contains("kasstroom", StringComparison.OrdinalIgnoreCase) || g.Titel.Contains("staat van baten en lasten", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Nieuwe_standaardteksten_gaan_over_wat_het_kantoor_niet_heeft_aangepast()
    {
        await server.MetRgs();
        var nu = DateTimeOffset.UtcNow;
        await using (var db = await server.Dienst<IDbContextFactory<BoekerhoudenDbContext>>().CreateDbContextAsync())
        {
            var omzet = await db.GrondslagSjablonen.SingleAsync(g => g.Sleutel == "omzet");
            omzet.TekstFiscaal = "Een oude standaardtekst.";
            omzet.Volgorde = 999;
            var kosten = await db.GrondslagSjablonen.SingleAsync(g => g.Sleutel == "kosten");
            kosten.TekstFiscaal = "De tekst van het kantoor.";
            kosten.GewijzigdDoor = "Iemand van kantoor";
            db.GrondslagSjablonen.Add(new GrondslagSjabloon { Sleutel = "vervallen", Groep = "Algemeen", Titel = "Vervallen", TekstFiscaal = "x", GewijzigdDoor = "Standaard" });
            db.GrondslagSjablonen.Add(new GrondslagSjabloon { Sleutel = "eigen", Groep = "Algemeen", Titel = "Eigen", TekstFiscaal = "x", GewijzigdDoor = "Iemand van kantoor" });
            await db.SaveChangesAsync();
            await GrondslagDienst.VulAan(db, nu);
        }

        var bibliotheek = await Grondslagen.Bibliotheek();
        var standaard = StandaardGrondslagen.Maak(nu);
        Assert.Equal(standaard.Single(g => g.Sleutel == "omzet").TekstFiscaal, bibliotheek.Single(g => g.Sleutel == "omzet").TekstFiscaal);
        Assert.Equal(standaard.Single(g => g.Sleutel == "omzet").Volgorde, bibliotheek.Single(g => g.Sleutel == "omzet").Volgorde);
        Assert.Equal("De tekst van het kantoor.", bibliotheek.Single(g => g.Sleutel == "kosten").TekstFiscaal);
        Assert.DoesNotContain(bibliotheek, g => g.Sleutel == "vervallen");
        Assert.Contains(bibliotheek, g => g.Sleutel == "eigen");

        Assert.True((await Grondslagen.HerstelStandaard("kosten", 2025, Tester)).IsGelukt);
        Assert.Equal(StandaardGrondslagen.Door, (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "kosten").GewijzigdDoor);

        await using var opruimen = await server.Dienst<IDbContextFactory<BoekerhoudenDbContext>>().CreateDbContextAsync();
        await opruimen.GrondslagSjablonen.Where(g => g.Sleutel == "eigen" || g.Sleutel == "kosten").ExecuteDeleteAsync();
        await GrondslagDienst.VulAan(opruimen, nu);
    }

    [Fact]
    public async Task Een_aanpassing_in_de_bibliotheek_geldt_vanaf_een_boekjaar()
    {
        var (_, vorig, huidig, _) = await Bakkerij();
        var standaard = (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "omzet");

        Assert.True((await Grondslagen.Bewaar("omzet", 2025,
            new GrondslagInvoer("Netto-omzet", standaard.TekstCommercieel, "De omzet bestaat uit de verkoop van brood en banket.", GrondslagOpname.BijPost, "WOmz", false), Tester)).IsGelukt);

        Assert.Equal("De omzet bestaat uit de verkoop van brood en banket.", Blokken(await Rapport(huidig)).Single(b => b.Sleutel == "omzet").Tekst);
        Assert.Equal(standaard.TekstFiscaal, Blokken(await Rapport(vorig)).Single(b => b.Sleutel == "omzet").Tekst);
        Assert.Equal(standaard.TekstFiscaal, (await Grondslagen.Bibliotheek(2024)).Single(g => g.Sleutel == "omzet").TekstFiscaal);
        Assert.Equal([0, 2025], (await Grondslagen.Versies())["omzet"]);
        Assert.Contains(await server.Dienst<AuditDienst>().Recent(AuditActies.GrondslagenbibliotheekGewijzigd), a => a.Details == "omzet vanaf 2025: BijPost (WOmz)");

        // Terugzetten vanaf 2025: de versie is dan gelijk aan wat ervoor gold en vervalt.
        Assert.True((await Grondslagen.HerstelStandaard("omzet", 2025, Tester)).IsGelukt);
        Assert.Equal([0], (await Grondslagen.Versies())["omzet"]);
        Assert.False((await Grondslagen.Bewaar("omzet", 0, new GrondslagInvoer("Netto-omzet", "x", "x", GrondslagOpname.BijPost, "WOmz", false), Tester)).IsGelukt);
    }

    [Fact]
    public async Task De_bibliotheek_is_aan_te_passen_en_terug_te_zetten()
    {
        await server.MetRgs();
        Assert.False((await Grondslagen.Bewaar("mva", 2025, new GrondslagInvoer("", "x", null, GrondslagOpname.BijPost, "BMva", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", 2025, new GrondslagInvoer("MVA", "x", null, GrondslagOpname.BijPost, "", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", 2025, new GrondslagInvoer("MVA", "x", null, GrondslagOpname.BijPost, "1234", false), Tester)).IsGelukt);
        Assert.False((await Grondslagen.Bewaar("mva", 2025, new GrondslagInvoer("MVA", null, " ", GrondslagOpname.Altijd, "", false), Tester)).IsGelukt);

        Assert.True((await Grondslagen.Bewaar("verbonden", 2025, new GrondslagInvoer("Verbonden partijen", "Eigen tekst.", "Eigen fiscale tekst.", GrondslagOpname.Optioneel, "BFva, BVor", false), Tester)).IsGelukt);
        var aangepast = (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "verbonden");
        Assert.Equal("BFva,BVor", aangepast.Codes);
        Assert.Equal("Test Gebruiker", aangepast.GewijzigdDoor);

        Assert.True((await Grondslagen.HerstelStandaard("verbonden", 2025, Tester)).IsGelukt);
        Assert.Equal("Transacties met verbonden partijen", (await Grondslagen.Bibliotheek()).Single(g => g.Sleutel == "verbonden").Titel);
    }
}
