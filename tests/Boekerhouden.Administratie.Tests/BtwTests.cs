using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De btw-tabel van het kantoor: codes met hun soort en tarief, en tarieven door de tijd (eigen database).</summary>
public sealed class BtwTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Standaardtabel_staat_in_de_database()
    {
        var tabel = await app.Dienst<BtwDienst>().Tabel();

        Assert.Equal(BtwCodes.Definities.Select(c => c.Code), tabel.Codes.Select(c => c.Code).Take(BtwCodes.Definities.Count));
        Assert.Equal(21m, BtwCodes.Zoek(tabel.Op(new DateOnly(2026, 6, 1)), "I21")!.Percentage);
        Assert.Equal(("4b", true), (BtwCodes.Zoek(tabel.Codes, "IEU")!.Rubriek, BtwCodes.Zoek(tabel.Codes, "IEU")!.Voorbelasting));
    }

    [Fact]
    public async Task Nieuw_tarief_geldt_voor_facturen_vanaf_de_ingangsdatum()
    {
        var wie = await app.Beheerder();
        var btw = app.Dienst<BtwDienst>();
        Assert.True((await btw.VoegTariefToe(wie, Tarief.Hoog, new DateOnly(2027, 1, 1), 21.15m)).IsGelukt);
        var id = await app.Administratie();
        var inkoop = app.Dienst<InkoopDienst>();

        await Boek(wie, id, inkoop, "T-2026", new DateOnly(2026, 12, 31));
        await Boek(wie, id, inkoop, "T-2027", new DateOnly(2027, 1, 5));

        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.Equal(21m, (await grootboek.Proefbalans(wie, id, 2026)).Regels.Single(r => r.Rekening.Nummer == "1500").Saldo);
        Assert.Equal(21m + 21.15m, (await grootboek.Kaart(wie, id, "1500", new(2026, 1, 1), new(2027, 12, 31))).Eindsaldo);
        var tarieven = await btw.Tarieven();
        Assert.Contains(tarieven, t => t is { Tarief: Tarief.Hoog, Percentage: 21.15m });

        // Terug naar alleen 21%: het nieuwe tarief weer weg.
        Assert.True((await btw.VerwijderTarief(wie, tarieven.Single(t => t.Percentage == 21.15m).Id)).IsGelukt);
    }

    private static async Task Boek(Uitvoerder wie, Guid id, InkoopDienst inkoop, string nummer, DateOnly datum)
    {
        var gelezen = await inkoop.Lees(wie, id, new MemoryStream(InkoopTests.Belnet(nummer)), $"{nummer}.xml", null);
        var invoer = new Factuurinvoer(null, new NieuweRelatie($"Leverancier {nummer}", null, null, null), nummer, datum, datum.AddDays(14), "Papier",
            [new Factuurregel("Papier", "4300", "I21", 100m)]);
        var uitkomst = await inkoop.Boek(wie, id, gelezen.DocumentId!.Value, invoer);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }

    [Fact]
    public async Task Tarieven_en_codes_worden_gecontroleerd()
    {
        var wie = await app.Beheerder();
        var btw = app.Dienst<BtwDienst>();

        Assert.False((await btw.VoegTariefToe(wie, Tarief.Hoog, new DateOnly(2012, 10, 1), 22m)).IsGelukt);
        Assert.False((await btw.VoegTariefToe(wie, Tarief.Laag, new DateOnly(2030, 1, 1), 120m)).IsGelukt);
        Assert.False((await btw.VerwijderTarief(wie, (await btw.Tarieven()).Single(t => t.Tarief == Tarief.Nul).Id)).IsGelukt);
        Assert.False((await btw.VoegCodeToe(wie, "I21", "Dubbel", Btwsoort.InkoopVoorbelasting, Tarief.Hoog)).IsGelukt);
        Assert.False((await btw.VoegCodeToe(wie, "x y", "Fout", Btwsoort.InkoopVoorbelasting, Tarief.Hoog)).IsGelukt);

        Assert.True((await btw.VoegCodeToe(wie, "ieu9", "Inkoop binnen de EU laag", Btwsoort.InkoopBinnenEu, Tarief.Laag)).IsGelukt);
        Assert.True((await btw.WijzigCode(wie, "IEU9", "Inkoop EU laag tarief", actief: false)).IsGelukt);

        var code = BtwCodes.Zoek((await btw.Tabel()).Op(new DateOnly(2026, 1, 1)), "IEU9")!;
        Assert.Equal(("Inkoop EU laag tarief", false, 9m, "4b"), (code.Omschrijving, code.Actief, code.Percentage, code.Rubriek));
    }

    [Fact]
    public async Task Alleen_de_beheerder_wijzigt_de_btw_tabel()
    {
        var gewoon = await app.Gebruiker("Gewone Gebruiker");
        var btw = app.Dienst<BtwDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => btw.VoegTariefToe(gewoon, Tarief.Hoog, new DateOnly(2031, 1, 1), 22m));
        await Assert.ThrowsAsync<GeenToegangException>(() => btw.VerwijderTarief(gewoon, 1));
        await Assert.ThrowsAsync<GeenToegangException>(() => btw.VoegCodeToe(gewoon, "X1", "X", Btwsoort.GeenBtw, Tarief.Geen));
        await Assert.ThrowsAsync<GeenToegangException>(() => btw.WijzigCode(gewoon, "I21", "X", actief: true));
        Assert.NotEmpty((await btw.Tabel()).Codes);
    }
}
