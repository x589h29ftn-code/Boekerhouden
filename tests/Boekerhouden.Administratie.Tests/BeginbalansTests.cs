using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Documenten.Tests;

namespace Boekerhouden.Administratie.Tests;

/// <summary>De beginbalans uit de jaarrekening van vorig jaar: lezen, rekeningen voorstellen en boeken.</summary>
public sealed class BeginbalansTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Jaarrekening_lezen_stelt_rekeningen_en_datum_voor_en_boekt_de_beginbalans()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<BeginbalansDienst>();

        var gelezen = await dienst.Lees(wie, id, new MemoryStream(FictieveJaarrekening.Pdf()), "jaarrekening-2025.pdf");

        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        var voorstel = gelezen.Waarde!;
        Assert.Equal((new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 1)), (voorstel.Balansdatum, voorstel.Boekdatum));
        Assert.Equal(
            [("Inventaris", "0200"), ("Debiteuren", "1300"), ("Rekening-courant directie", "1560"), ("Liquide middelen", "1100"), ("Aandelenkapitaal", "0500"),
             ("Overige reserves", "0510"), ("Overige schulden", "0700"), ("Crediteuren", "1600"), ("Overlopende passiva", "1700")],
            voorstel.Regels.Select(r => (r.Post.Naam, r.Rekening)));
        // Debiteuren en crediteuren komen als één bedrag op de rekening: dat staat erbij.
        Assert.Equal(2, voorstel.Signalen.Count);

        var boek = await dienst.Boek(wie, id, voorstel.Boekdatum, [.. voorstel.Regels.Select(r => new Beginbalansinvoer(r.Rekening!, r.Post.Boekbedrag, r.Post.Naam))], voorstel.Bestandsnaam);

        Assert.True(boek.IsGelukt, string.Join("; ", boek.Fouten));
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal((1250m, -2000m, -3300m), (Saldo(proef, "1100"), Saldo(proef, "0700"), Saldo(proef, "0510")));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BeginbalansGeboekt && r.Details == "uit jaarrekening-2025.pdf; 9 rekeningen");
        var nogEens = await dienst.Lees(wie, id, new MemoryStream(FictieveJaarrekening.Pdf()), "jaarrekening-2025.pdf");
        Assert.Contains("Op 01-01-2026 staat al een beginbalans.", nogEens.Waarde!.Signalen);
    }

    [Fact]
    public async Task Een_beginbalans_die_niet_sluit_of_op_een_resultaatrekening_wordt_geweigerd()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<BeginbalansDienst>();

        var scheef = await dienst.Boek(wie, id, new(2026, 1, 1), [new("1100", 1250m), new("0500", -1240m)]);
        var resultaat = await dienst.Boek(wie, id, new(2026, 1, 1), [new("1100", 100m), new("4300", -100m)]);
        var leeg = await dienst.Boek(wie, id, new(2026, 1, 1), [new("", 100m, "Deelnemingen"), new("0500", -100m)]);
        var geenPdf = await dienst.Lees(wie, id, new MemoryStream("geen pdf"u8.ToArray()), "x.pdf");
        var geenBalans = await dienst.Lees(wie, id, new MemoryStream(FictiefAfschrift.OktoberPdf()), "afschrift.pdf");

        Assert.Equal(["De beginbalans is niet in balans: debet en credit verschillen € 10,00."], scheef.Fouten);
        Assert.Equal(["4300 Kantoorbenodigdheden is geen balansrekening."], resultaat.Fouten);
        Assert.Equal(["Kies een rekening voor Deelnemingen."], leeg.Fouten);
        Assert.Equal(["x.pdf is geen PDF."], geenPdf.Fouten);
        Assert.Contains("geen balans gevonden", geenBalans.Fouten[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alleen_het_kantoor_leest_en_boekt_een_beginbalans()
    {
        var beheerder = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<BeginbalansDienst>();
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, beheerder);
        var buiten = await app.Gebruiker("Buiten Staander");

        foreach (var wie in new[] { ondernemer, buiten })
        {
            await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees(wie, id, new MemoryStream(FictieveJaarrekening.Pdf()), "j.pdf"));
            await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Boek(wie, id, new(2026, 1, 1), [new("1100", 1m), new("0500", -1m)]));
        }
    }

    private static decimal Saldo(Boekhouding.Grootboek.Proefbalans proef, string rekening) =>
        proef.Regels.Single(r => r.Rekening.Nummer == rekening).Saldo;
}
