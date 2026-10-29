using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Inkoopfacturen inlezen, herkennen, boeken en leren (plakje A7a), met de verzonnen e-factuur van Belnet.</summary>
public sealed class InkoopTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>De e-factuur, met een ander factuurnummer en een andere datum per maand.</summary>
    internal static byte[] Belnet(string nummer = "BN-2026-0411", string datum = "2026-04-01") =>
        Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fictief", "administratie", "belnet-2026-0411.xml"))
            .Replace("<cbc:ID>BN-2026-0411</cbc:ID>", $"<cbc:ID>{nummer}</cbc:ID>", StringComparison.Ordinal)
            .Replace("<cbc:IssueDate>2026-04-01</cbc:IssueDate>", $"<cbc:IssueDate>{datum}</cbc:IssueDate>", StringComparison.Ordinal));

    private static Factuurinvoer Invoer(string? relatie, NieuweRelatie? nieuw, string nummer, string rekening = "4310") =>
        new(relatie, nieuw, nummer, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 15), "Abonnement",
            [new Factuurregel("Abonnement", rekening, "I21", 20.65m, 4.34m)]);

    [Fact]
    public async Task Eerste_factuur_van_een_nieuwe_leverancier_boeken_maakt_relatie_journaalpost_en_openstaande_post()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var inkoop = app.Dienst<InkoopDienst>();

        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(Belnet()), "belnet.xml", "application/xml");
        Assert.Equal(Documentstatus.TeVerwerken, gelezen.Status);
        Assert.Contains("Leverancier niet herkend.", gelezen.Melding, StringComparison.Ordinal);

        var verwerking = (await inkoop.Verwerking(wie, id, gelezen.DocumentId!.Value))!;
        Assert.Equal("Belnet B.V.", verwerking.Herkend?.Naam);
        Assert.Null(verwerking.Voorstel?.Leverancier);

        var nieuw = new NieuweRelatie("Belnet B.V.", verwerking.Herkend!.Iban, verwerking.Herkend.BtwNummer, verwerking.Herkend.KvkNummer);
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId.Value, Invoer(null, nieuw, "BN-2026-0411"));

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        var kaart = await app.Dienst<GrootboekDienst>().Kaart(wie, id, "1600", new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(-24.99m, kaart.Eindsaldo);
        Assert.Equal(4.34m, (await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026)).Regels.Single(r => r.Rekening.Nummer == "1500").Saldo);
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        var relatie = await db.Relaties.SingleAsync();
        Assert.Equal(("BELNET", "NL63TRIO0212345678", "NL123456782B01", "4310"), (relatie.Code, relatie.Ibans.Single(), relatie.BtwNummer, relatie.Tegenrekening));
        var open = await db.OpenstaandePosten.SingleAsync();
        Assert.Equal((Factuursoort.Inkoop, -24.99m, -24.99m, "BN-2026-0411"), (open.Soort, open.Bedrag, open.Openstaand, open.Factuurnummer));
        Assert.Equal(Documentstatus.Geboekt, (await db.Documenten.SingleAsync()).Status);
        Assert.Empty(await app.Dienst<GrootboekDienst>().ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Tweede_keer_een_voorstel_derde_keer_automatisch_geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var inkoop = app.Dienst<InkoopDienst>();

        var eerste = await inkoop.Lees(wie, id, new MemoryStream(Belnet("BN-1", "2026-04-01")), "1.xml", null);
        var herkend = (await inkoop.Verwerking(wie, id, eerste.DocumentId!.Value))!.Herkend!;
        await inkoop.Boek(wie, id, eerste.DocumentId.Value, Invoer(null, new("Belnet B.V.", herkend.Iban, herkend.BtwNummer, null), "BN-1"));

        var tweede = await inkoop.Lees(wie, id, new MemoryStream(Belnet("BN-2", "2026-05-01")), "2.xml", null);
        var voorstel = (await inkoop.Verwerking(wie, id, tweede.DocumentId!.Value))!.Voorstel!;
        Assert.Equal(Documentstatus.TeVerwerken, tweede.Status);
        Assert.Equal(("BELNET", "4310", "Geleerd: vorige keuze"), (voorstel.Leverancier?.Relatie.Code, voorstel.Advies.Rekeningnummer, voorstel.Advies.Reden));
        var f = voorstel.Factuur!;
        Assert.True((await inkoop.Boek(wie, id, tweede.DocumentId.Value, new Factuurinvoer("BELNET", null, f.Factuurnummer, f.Datum, f.Vervaldatum, f.Omschrijving, f.Regels))).IsGelukt);

        var derde = await inkoop.Lees(wie, id, new MemoryStream(Belnet("BN-3", "2026-06-01")), "3.xml", null);

        Assert.True(derde.IsGeboekt, derde.Melding);
        Assert.Contains("automatisch geboekt", derde.Melding, StringComparison.Ordinal);
        Assert.Equal(3, (await app.Dienst<GrootboekDienst>().Posten(wie, id, "INK")).Count);
    }

    [Fact]
    public async Task Regel_boekt_de_eerste_factuur_van_een_bekende_leverancier_meteen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        await using (var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden))
        {
            db.Relaties.Add(new Handelsrelatie { Id = Guid.NewGuid(), AdministratieId = id, Code = "BELNET", Naam = "Belnet B.V.", Ibans = ["NL63TRIO0212345678"], BtwNummer = "NL123456782B01" });
            db.Regels.Add(new Automatiseringsregel { Id = Guid.NewGuid(), AdministratieId = id, Naam = "Belnet is telefoon", Rekeningnummer = "4310", BtwCode = "I21", Automatisch = true, Soort = Verwerkingssoort.Inkoopfactuur, NaamBevat = "Belnet", BedragVan = 10, BedragTot = 30 });
            await db.SaveChangesAsync();
        }

        var gelezen = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(Belnet()), "belnet.xml", null);

        Assert.True(gelezen.IsGeboekt, gelezen.Melding);
        Assert.Contains("Regel: Belnet is telefoon", gelezen.Melding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zelfde_bestand_of_zelfde_factuurnummer_wordt_niet_dubbel_geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var inkoop = app.Dienst<InkoopDienst>();
        var eerste = await inkoop.Lees(wie, id, new MemoryStream(Belnet()), "a.xml", null);
        await inkoop.Boek(wie, id, eerste.DocumentId!.Value, Invoer(null, new("Belnet B.V.", "NL63TRIO0212345678", "NL123456782B01", null), "BN-2026-0411"));

        var zelfdeBestand = await inkoop.Lees(wie, id, new MemoryStream(Belnet()), "a-kopie.xml", null);
        var andereKopie = await inkoop.Lees(wie, id, new MemoryStream(Belnet("BN2026-0411")), "b.xml", null);
        var nogmaals = await inkoop.Boek(wie, id, andereKopie.DocumentId!.Value, Invoer("BELNET", null, "BN2026-0411"));

        Assert.StartsWith("Dit bestand is al ingelezen op", zelfdeBestand.Melding, StringComparison.Ordinal);
        Assert.Contains("Lijkt al geboekt", andereKopie.Melding, StringComparison.Ordinal);
        Assert.Contains("Factuur BN2026-0411 van deze leverancier is al geboekt.", nogmaals.Fouten);
    }

    [Fact]
    public async Task Niet_herkenbaar_bestand_blijft_te_verwerken_met_een_melding()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();

        var foto = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream([0x89, 0x50, 0x4E, 0x47]), "bon.png", "image/png");
        var nep = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream("geen pdf"u8.ToArray()), "nep.pdf", "application/pdf");

        Assert.Equal(Documentstatus.TeVerwerken, foto.Status);
        Assert.Contains("niet herkend", foto.Melding, StringComparison.Ordinal);
        Assert.Contains("Het bestand is geen PDF.", nep.Melding, StringComparison.Ordinal);
        Assert.Equal(2, (await app.Dienst<InkoopDienst>().Lijst(wie, id, Documentstatus.TeVerwerken)).Count);
    }

    [Fact]
    public async Task Alleen_leden_met_het_recht_lezen_in_en_boeken()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var buiten = await app.Gebruiker("Bas Buiten");
        var inzien = await app.Gebruiker("Ina Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, beheerder);
        var inkoop = app.Dienst<InkoopDienst>();
        var gelezen = await inkoop.Lees(beheerder, id, new MemoryStream(Belnet()), "belnet.xml", null);

        await Assert.ThrowsAsync<GeenToegangException>(() => inkoop.Lees(buiten, id, new MemoryStream(Belnet("X")), "x.xml", null));
        await Assert.ThrowsAsync<GeenToegangException>(() => inkoop.Lees(inzien, id, new MemoryStream(Belnet("Y")), "y.xml", null));
        await Assert.ThrowsAsync<GeenToegangException>(() => inkoop.Verwerking(buiten, id, gelezen.DocumentId!.Value));
        await Assert.ThrowsAsync<GeenToegangException>(() => inkoop.Inhoud(buiten, id, gelezen.DocumentId!.Value));
        await Assert.ThrowsAsync<GeenToegangException>(() => inkoop.Boek(inzien, id, gelezen.DocumentId!.Value, Invoer(null, new("X", null, null, null), "1")));
        Assert.NotNull(await inkoop.Inhoud(inzien, id, gelezen.DocumentId!.Value));
        Assert.Null(await inkoop.Verwerking(beheerder, await app.Administratie("Andere B.V."), gelezen.DocumentId.Value));
    }

    [Fact]
    public void Code_voor_een_nieuwe_relatie_is_uniek()
    {
        Assert.Equal("BELNET", InkoopDienst.NieuweCode("Belnet B.V.", []));
        Assert.Equal("BELNET2", InkoopDienst.NieuweCode("Belnet Zakelijk", [new("BELNET", "Belnet B.V.", [])]));
        Assert.Equal("REL", InkoopDienst.NieuweCode("B.V.", []));
    }
}

/// <summary>Met een (nagebootste) virusscanner: besmet gaat in quarantaine, zonder scan wordt niets ingelezen.</summary>
public sealed class VirusscanFixture : AdministratieFixture
{
    public NepScanner Scanner { get; } = new();

    protected override void Diensten(IServiceCollection diensten) => diensten.AddSingleton<IVirusscanner>(Scanner);

    public sealed class NepScanner : IVirusscanner
    {
        public bool Bereikbaar { get; set; } = true;

        public Task<Scanresultaat> ScanAsync(Stream inhoud, CancellationToken annulering = default)
        {
            using var lezer = new StreamReader(inhoud);
            var tekst = lezer.ReadToEnd();
            return Task.FromResult(!Bereikbaar ? new Scanresultaat(Scanuitslag.NietGescand, "De virusscanner is niet bereikbaar.")
                : tekst.Contains("EICAR-TEST", StringComparison.Ordinal) ? new Scanresultaat(Scanuitslag.Besmet, "Eicar-Test-Signature")
                : new Scanresultaat(Scanuitslag.Schoon));
        }
    }
}

public sealed class InkoopVirusscanTests(VirusscanFixture app) : IClassFixture<VirusscanFixture>
{
    [Fact]
    public async Task Besmet_bestand_in_quarantaine_zonder_inhoud_en_onbereikbare_scanner_leest_niets_in()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var inkoop = app.Dienst<InkoopDienst>();

        var besmet = await inkoop.Lees(wie, id, new MemoryStream("%PDF-1.7 EICAR-TEST"u8.ToArray()), "virus.pdf", "application/pdf");
        app.Scanner.Bereikbaar = false;
        var ongescand = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);
        app.Scanner.Bereikbaar = true;
        var schoon = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);

        Assert.Equal(Documentstatus.Quarantaine, besmet.Status);
        Assert.Null(await inkoop.Inhoud(wie, id, besmet.DocumentId!.Value));
        Assert.Null(ongescand.DocumentId);
        Assert.Contains("niet ingelezen", ongescand.Melding, StringComparison.Ordinal);
        Assert.Equal(Documentstatus.TeVerwerken, schoon.Status);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.DocumentQuarantaine);
    }
}

/// <summary>Een ingelezen document bekijken via HTTP: alleen leden, met headers die de inhoud onschadelijk maken.</summary>
public sealed class DocumentEndpointTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Lid_krijgt_het_document_in_een_sandbox_buitenstaander_niet()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var gelezen = await app.Dienst<InkoopDienst>().Lees(beheerder, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", "application/xml");
        var pad = new Uri($"/a/{id}/documenten/{gelezen.DocumentId}", UriKind.Relative);
        var opties = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true };

        using var lid = app.CreateClient(opties);
        await Aanmelden.LogInMetCode(lid, "beheer@kantoor.test", AdministratieFixture.Wachtwoord, await app.Tweestap(beheerder), "/");
        var antwoord = await lid.GetAsync(pad);

        Assert.Equal(System.Net.HttpStatusCode.OK, antwoord.StatusCode);
        Assert.StartsWith("sandbox", antwoord.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("application/octet-stream", antwoord.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", antwoord.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(InkoopTests.Belnet(), await antwoord.Content.ReadAsByteArrayAsync());

        var buiten = await app.Gebruiker("Bea Buiten");
        using var vreemd = app.CreateClient(opties);
        await Aanmelden.LogIn(vreemd, await app.Email(buiten), AdministratieFixture.Wachtwoord, "/");
        var geweigerd = await vreemd.GetAsync(pad);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, geweigerd.StatusCode);
    }
}
